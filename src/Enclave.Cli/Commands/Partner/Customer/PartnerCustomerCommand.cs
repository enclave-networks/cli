using System.CommandLine;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;
using Enclave.Sdk.Api.Partner.Models;

namespace Enclave.Cli.Commands.Partner.Customer;

internal static class PartnerCustomerCommand
{
    // The partner portal's form defaults (portal AddNewCustomer.razor, CustomerCreateFormModel),
    // sent for a flag left out so that a change to the API's own defaults does not change what
    // create does.
    private const int DefaultSystems = 50;

    private const int DefaultGateways = 0;

    public static CliNoun Create()
    {
        var noun = new CliNoun("customer", "customers", "The partner's customers, their admins, invites and auto-sync.");

        noun.Add(List());
        noun.Add(Show());
        noun.Add(CreateCustomer());
        noun.Add(Update());
        noun.Add(Convert());
        noun.Add(ListAdmins());
        noun.Add(AddAdmin());
        noun.Add(RemoveAdmin());
        noun.Add(ListInvites());
        noun.Add(Invite());
        noun.Add(CancelInvite());
        noun.Add(AutoSync("enable-auto-sync", "Turn on auto-sync for the customer, which keeps the partner's staff as its admins.", (customers, id) => customers.EnableAutoSyncAsync(id)));
        noun.Add(AutoSync("disable-auto-sync", "Turn off auto-sync for the customer.", (customers, id) => customers.DisableAutoSyncAsync(id)));

        return noun;
    }

    private static CliVerb List()
    {
        var verb = new CliVerb("list", "List the partner's customers.", CommandScope.Partner);

        verb.SetHandler(async context =>
        {
            var partner = await context.GetPartnerAsync();
            var customers = await CustomerLookup.ReadAllAsync(partner.Client.Customers, context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Customer, customers, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb Show()
    {
        var (verb, customer) = ForCustomer("show", "Show a customer.", changes: false);

        SetHandler(verb, customer, (context, customers, id) => PrintAsync(context, () => customers.GetAsync(id)));
        return verb;
    }

    private static CliVerb CreateCustomer()
    {
        var verb = new CliVerb("create", "Create a customer, sending every field of the API's customer model, with the partner portal's values for the flags left out.", CommandScope.Partner, changes: true);
        var name = verb.Add(CliArguments.Text("name", "The customer's name."));
        var owner = EmailOption(verb, "--owner", "The email address of the user who will own the customer's organisation.");
        var domain = CreateTextOption(verb, "--domain", "The customer's domain.", "domain", "a domain name");
        var contact = CreateTextOption(verb, "--contact", "The customer's contact name.", "name", "a contact name");
        var systems = verb.Add(CliOptions.Number("--systems", "The number of systems to license, at least 2; 50 when left out.", minimum: 2));
        var gateways = verb.Add(CliOptions.Number("--gateways", "The number of gateways to license; 0 when left out.", minimum: 0));
        var industryDiscount = verb.Add(CliOptions.Flag("--industry-discount", "Request the industry discount."));
        var hardLimit = verb.Add(CliOptions.Flag("--hard-limit", "Refuse systems beyond the licensed number."));
        var autoSync = verb.Add(CliOptions.Flag("--auto-sync", "Keep the partner's staff as the customer's admins."));
        verb.RefuseBlank(name, context => context.Get(name), "a customer name");

        verb.SetHandler(async context =>
        {
            // The owner, domain and contact have no portal default. Left out, they go as null,
            // since Enclave.Sdk.Api writes every property (ClientBase.CreateJsonContent).
            var model = new CustomerCreateModel
            {
                Name = context.Get(name)!,
                OwnerEmail = context.Get(owner),
                Domain = context.Get(domain),
                ContactName = context.Get(contact),
                InitialSystemsCount = context.Get(systems) ?? DefaultSystems,
                InitialGatewaysCount = context.Get(gateways) ?? DefaultGateways,
                IndustryDiscount = context.Get(industryDiscount),
                HardLimit = context.Get(hardLimit),
                AdminAutoSyncIsEnabled = context.Get(autoSync),
            };

            var partner = await context.GetPartnerAsync();
            var customer = await partner.Client.Customers.CreateAsync(model);

            await context.Output.WriteAsync(customer, context.CancellationToken);
        });

        return verb;
    }

    // Auto-sync has its own API routes, so it changes through enable-auto-sync and
    // disable-auto-sync (spec/cli-surface.md "Command options").
    private static CliVerb Update()
    {
        var (verb, customer) = ForCustomer("update", "Change a customer's name, contact, licensed systems and gateways, discount request and hard limit.", changes: true);
        var name = verb.Add(CliOptions.Text("--name", "The customer's new name.", "name"));
        var contact = verb.Add(CliOptions.Text("--contact", "The customer's contact name.", "name"));
        var systems = verb.Add(CliOptions.Number("--systems", "The number of systems to license, at least 2.", minimum: 2));
        var gateways = verb.Add(CliOptions.Number("--gateways", "The number of gateways to license.", minimum: 0));
        var industryDiscount = verb.Add(CliOptions.Flag("--industry-discount", "Request the industry discount."));
        var noIndustryDiscount = verb.Add(CliOptions.Flag("--no-industry-discount", "Withdraw the industry discount request."));
        var hardLimit = verb.Add(CliOptions.Flag("--hard-limit", "Refuse systems beyond the licensed number."));
        var noHardLimit = verb.Add(CliOptions.Flag("--no-hard-limit", "Allow systems beyond the licensed number."));
        verb.RefuseBlank(name, context => context.Get(name), "a customer name");

        verb.AtLeastOne(name, contact, systems, gateways, industryDiscount, noIndustryDiscount, hardLimit, noHardLimit);
        verb.Exclusive(industryDiscount, noIndustryDiscount);
        verb.Exclusive(hardLimit, noHardLimit);

        SetHandler(verb, customer, (context, customers, id) =>
        {
            var patch = customers.Update(id);

            if (context.Get(name) is { } newName)
            {
                patch.Set(model => model.Name, newName);
            }

            if (context.Get(contact) is { } contactName)
            {
                patch.Set(model => model.ContactName, contactName);
            }

            if (context.Get(systems) is { } systemCount)
            {
                patch.Set(model => model.LicensedAgentsCount, systemCount);
            }

            if (context.Get(gateways) is { } gatewayCount)
            {
                patch.Set(model => model.LicensedGatewaysCount, gatewayCount);
            }

            if (context.Get(industryDiscount) || context.Get(noIndustryDiscount))
            {
                patch.Set(model => model.IndustryDiscount, context.Get(industryDiscount));
            }

            if (context.Get(hardLimit) || context.Get(noHardLimit))
            {
                patch.Set(model => model.EnableHardLimit, context.Get(hardLimit));
            }

            return PrintAsync(context, patch.ApplyAsync);
        });

        return verb;
    }

    // --billing-months is required so the command states the billing period it commits to; the
    // API's default is 1 month (portal ConvertCustomerModel.BillingPeriodMonths).
    private static CliVerb Convert()
    {
        var (verb, customer) = ForCustomer("convert", "Convert a customer from trial to paid, with the billing period it commits to.", changes: true);
        var billingMonths = verb.Add(CliOptions.Choice("--billing-months", "The billing period in months.", ("1", 1), ("12", 12), ("24", 24), ("36", 36)));
        verb.ExactlyOne(billingMonths);

        SetHandler(verb, customer, (context, customers, id) => PrintAsync(context, () => customers.ConvertAsync(id, context.Get(billingMonths)!.Value)));
        return verb;
    }

    private static CliVerb ListAdmins()
    {
        var (verb, customer) = ForCustomer("list-admins", "List the customer's admins.", changes: false);

        SetHandler(verb, customer, async (context, customers, id) =>
        {
            // Not paged: one response holds every owner and admin (Enclave.Sdk.Api
            // CustomersClient.GetAdminsAsync).
            var admins = await SingleItem.CallAsync(() => customers.GetAdminsAsync(id));

            await context.Output.WriteListAsync(ListKind.Admin, admins, context.CancellationToken);
        });

        return verb;
    }

    // By ID only: listing partner users needs a scope personal access tokens cannot carry, so an
    // email address cannot be looked up (spec/cli-surface.md "Names and IDs").
    private static CliVerb AddAdmin()
    {
        var (verb, customer) = ForCustomer("add-admin", "Make one of the partner's users an admin of the customer, given by account ID, which the partner portal shows.", changes: true);
        var userId = verb.Add(CliOptions.Id("--user-id", "The account ID of the partner's user.", IdFormats.Guid, "accountId"));
        verb.ExactlyOne(userId);

        SetHandler(verb, customer, (context, customers, id) => PrintAsync(context, () => customers.AddAdminAsync(id, AccountGuid.FromGuid(context.Get(userId)!.Value))));
        return verb;
    }

    private static CliVerb RemoveAdmin()
    {
        var (verb, customer) = ForCustomer("remove-admin", "Remove an admin from the customer, given by email address or account ID.", changes: true);
        var user = EmailOption(verb, "--user", "The admin's email address.");
        var userId = verb.Add(CliOptions.Id("--user-id", "The admin's account ID.", IdFormats.Guid, "accountId"));
        verb.ExactlyOne(user, userId);

        SetHandler(verb, customer, async (context, customers, id) =>
        {
            var accountId = context.Get(userId) is { } guid
                ? AccountGuid.FromGuid(guid)
                : await CustomerLookup.AdminIdAsync(customers, id, context.Get(user)!, user.Name);

            await PrintAsync(context, () => customers.RemoveAdminAsync(id, accountId));
        });

        return verb;
    }

    private static CliVerb ListInvites()
    {
        var (verb, customer) = ForCustomer("list-invites", "List the customer's pending admin invites.", changes: false);

        SetHandler(verb, customer, async (context, customers, id) =>
        {
            // Not paged: one response holds every invite (Enclave.Sdk.Api
            // CustomersClient.GetPendingInvitesAsync).
            var invites = await SingleItem.CallAsync(() => customers.GetPendingInvitesAsync(id));

            await context.Output.WriteListAsync(ListKind.Invite, invites, context.CancellationToken);
        });

        return verb;
    }

    private static CliVerb Invite()
    {
        var (verb, customer, email) = ForInvite("invite", "Invite someone to be an admin of the customer.");

        SetHandler(verb, customer, (context, customers, id) => PrintAsync(context, () => customers.InviteAdminAsync(id, context.Get(email)!)));
        return verb;
    }

    private static CliVerb CancelInvite()
    {
        var (verb, customer, email) = ForInvite("cancel-invite", "Cancel a pending admin invite to the customer, given by its email address.");

        SetHandler(verb, customer, async (context, customers, id) =>
        {
            var inviteId = await CustomerLookup.InviteIdAsync(customers, id, context.Get(email)!, email.Name);

            await PrintAsync(context, () => customers.CancelInviteAsync(id, inviteId));
        });

        return verb;
    }

    // For a customer the partner does not have, the auto-sync routes answer 204 with no body where
    // the other customer routes answer 404, and Enclave.Sdk.Api throws InvalidOperationException
    // (CustomersClient.SetAutoSyncAsync). It exits 5, as an unknown customer does elsewhere.
    private static CliVerb AutoSync(string name, string description, Func<ICustomersClient, OrganisationGuid, Task<CustomerModel>> call)
    {
        var (verb, customer) = ForCustomer(name, description, changes: true);

        SetHandler(verb, customer, (context, customers, id) => PrintAsync(context, async () =>
        {
            try
            {
                return await call(customers, id);
            }
            catch (InvalidOperationException exception)
            {
                throw new CliException(ErrorCode.NotFound, $"The partner has no customer {id}.", innerException: exception);
            }
        }));

        return verb;
    }

    private static (CliVerb Verb, CustomerChoice Customer, Option<string?> Email) ForInvite(string name, string description)
    {
        var (verb, customer) = ForCustomer(name, description, changes: true);
        var email = EmailOption(verb, "--email", "The email address.");
        verb.ExactlyOne(email);
        return (verb, customer, email);
    }

    // A blank address names no admin, invite or owner, so it exits 2 before the token and any call
    // (spec/cli-surface.md "Details").
    private static Option<string?> EmailOption(CliVerb verb, string name, string description)
    {
        var option = verb.Add(CliOptions.Text(name, description, "email"));
        verb.RefuseBlank(option, "an email address");
        return option;
    }

    // A blank domain or contact names nothing, and the API would store it (portal
    // CustomerCreateModelValidator, CreateCustomerHandler.GetResponseAsync), so it exits 2 before
    // the token and any call (spec/cli-surface.md "Details").
    private static Option<string?> CreateTextOption(CliVerb verb, string name, string description, string valueName, string takes)
    {
        var option = verb.Add(CliOptions.Text(name, description, valueName));
        verb.RefuseBlank(option, takes);
        return option;
    }

    // A customer's only ID is its organisation ID (portal CustomersController), hence --org-id.
    // ENCLAVE_ORG_ID and the saved organisation choose the organisation that organisation commands
    // act within, so they never stand in for the customer (spec/cli-surface.md "Context").
    private static (CliVerb Verb, CustomerChoice Customer) ForCustomer(string name, string description, bool changes)
    {
        var verb = new CliVerb(name, description, CommandScope.Partner, changes);
        var customer = verb.Add(CliArguments.OptionalText("customer", "The customer's name, matched whole and ignoring case."));
        var orgId = verb.Add(CliOptions.Id("--org-id", "The customer's organisation ID.", IdFormats.Guid, "orgId"));
        verb.ExactlyOne(customer, orgId);
        verb.RefuseBlank(customer, context => context.Get(customer), "a customer name");
        return (verb, new CustomerChoice(customer, orgId));
    }

    // GetPartnerAsync checks the token and the partner with no call, so it goes before the customer
    // lookup to keep the order of checks (spec/cli-surface.md "Errors and exit codes").
    private static void SetHandler(CliVerb verb, CustomerChoice customer, Func<CliContext, ICustomersClient, OrganisationGuid, Task> handler) =>
        verb.SetHandler(async context =>
        {
            var partner = await context.GetPartnerAsync();
            var customers = partner.Client.Customers;

            var customerId = context.Get(customer.OrgId) is { } orgId
                ? OrganisationGuid.FromGuid(orgId)
                : await CustomerLookup.IdAsync(customers, context.Get(customer.Name)!, customer.Name.Name, context.CancellationToken);

            await handler(context, customers, customerId);
        });

    // Every call on one customer is a single-ID call: a 404 means the API does not know the
    // customer, or the admin or invite named (spec/cli-surface.md "Several IDs").
    private static async Task PrintAsync<T>(CliContext context, Func<Task<T>> call)
    {
        var model = await SingleItem.CallAsync(call);

        await context.Output.WriteAsync(model, context.CancellationToken);
    }

    private sealed record CustomerChoice(Argument<string?> Name, Option<Guid?> OrgId);
}
