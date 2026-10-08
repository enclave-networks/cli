using System.CommandLine;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Clients.Interfaces;
using Enclave.Sdk.Api.Partner.Models;

namespace Enclave.Cli.Commands.Partner.Customer;

/// <summary>
/// The `partner customer` noun: the partner's customers, their admins, invites and auto-sync
/// (proposed-cli-surface.md "Commands", "Partner API"). A customer is an organisation, given by
/// name or by --org-id, its organisation ID.
/// </summary>
// Every call goes through Enclave.Sdk.Api 1.1.0's partner client (IPartnerClient.Customers), one
// call per command. A customer given by name, an admin given by email address and an invite given
// by email address each add one read (proposed-cli-surface.md "Calls per command").
internal static class PartnerCustomerCommand
{
    // The partner portal's values for a new customer's counts when the form is left as it opens
    // (portal Enclave.Partner.Portal.Client/Pages/AddNewCustomer.razor:315-345), which the CLI sends
    // for a flag left out, so a change to the API's own defaults does not change what create does
    // (proposed-cli-surface.md "Command options", the create table).
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
        var owner = verb.Add(CliOptions.Text("--owner", "The email address of the user who will own the customer's organisation.", "email"));
        var domain = verb.Add(CliOptions.Text("--domain", "The customer's domain.", "domain"));
        var contact = verb.Add(CliOptions.Text("--contact", "The customer's contact name.", "name"));
        var systems = verb.Add(CliOptions.Number("--systems", "The number of systems to license, at least 2; 50 when left out.", minimum: 2));
        var gateways = verb.Add(CliOptions.Number("--gateways", "The number of gateways to license; 0 when left out.", minimum: 0));
        var industryDiscount = verb.Add(CliOptions.Flag("--industry-discount", "Request the industry discount."));
        var hardLimit = verb.Add(CliOptions.Flag("--hard-limit", "Refuse systems beyond the licensed number."));
        var autoSync = verb.Add(CliOptions.Flag("--auto-sync", "Keep the partner's staff as the customer's admins."));
        RefuseBlankName(verb, name, context => context.Get(name));

        verb.SetHandler(async context =>
        {
            // The owner, domain and contact have no portal default and are sent without a value
            // when left out; Enclave.Sdk.Api 1.1.0 writes every property of the model, so they go as
            // null (ClientBase.CreateJsonContent).
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

    // An update sends only the fields given, so one with no change flag has nothing to send, and a
    // flag given with its --no- form leaves the field's new value unclear; both exit 2
    // (proposed-cli-surface.md "Details"). Auto-sync is not an update flag: it changes through
    // enable-auto-sync and disable-auto-sync, which have their own API routes ("Command options").
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
        RefuseBlankName(verb, name, context => context.Get(name));

        verb.AtLeastOne(name, contact, systems, gateways, industryDiscount, noIndustryDiscount, hardLimit, noHardLimit);
        verb.Exclusive(industryDiscount, noIndustryDiscount);
        verb.Exclusive(hardLimit, noHardLimit);

        // --systems sets LicensedAgentsCount and --gateways LicensedGatewaysCount (portal
        // CustomerPatchModelValidator.cs:14-16, proposed-cli-surface.md "Command options").
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

    // --billing-months is required, so the command states the billing period it commits to; the
    // API's default is 1 month (proposed-cli-surface.md "Command options"; portal
    // ConvertCustomerModelValidator.cs:13-17).
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
            // GetAdminsAsync returns every owner and admin in one response
            // (CustomersClient.GetAdminsAsync, Enclave.Sdk.Api 1.1.0), so there are no pages to read.
            var admins = await SingleItem.CallAsync(() => customers.GetAdminsAsync(id));

            await context.Output.WriteListAsync(ListKind.Admin, admins, context.CancellationToken);
        });

        return verb;
    }

    // The account is given by ID only: it is a partner user, and listing partner users needs a
    // scope personal access tokens cannot carry, so an email address cannot be looked up
    // (proposed-cli-surface.md "Names and IDs").
    private static CliVerb AddAdmin()
    {
        var (verb, customer) = ForCustomer("add-admin", "Make one of the partner's users an admin of the customer, given by account ID, which the partner portal shows.", changes: true);
        var userId = verb.Add(CliOptions.Id("--user-id", "The account ID of the partner's user.", IdFormats.Guid, "accountId"));
        verb.ExactlyOne(userId);

        SetHandler(verb, customer, (context, customers, id) => PrintAsync(context, () => customers.AddAdminAsync(id, AccountGuid.FromGuid(context.Get(userId)!.Value))));
        return verb;
    }

    // An email address with an account ID contradict each other, as a name with its ID option does
    // (proposed-cli-surface.md "Details").
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
            // GetPendingInvitesAsync returns every invite in one response
            // (CustomersClient.GetPendingInvitesAsync, Enclave.Sdk.Api 1.1.0), so there are no pages
            // to read.
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

    // The partner API cancels an invite by its ID, so the address is looked up in the customer's
    // invites (proposed-cli-surface.md "Partner API").
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
    // the other customer routes answer 404, and Enclave.Sdk.Api reports it by throwing
    // InvalidOperationException (ICustomersClient.EnableAutoSyncAsync and DisableAutoSyncAsync,
    // CustomersClient.SetAutoSyncAsync, 1.1.0). That is the unknown customer a single-ID command
    // exits 5 for (proposed-cli-surface.md "Several IDs").
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

    // An empty or blank address names no admin or invite, so it is refused with the other argument
    // checks, before the token and any call (proposed-cli-surface.md "Errors and exit codes").
    // Passed on, it would cost the customer lookup and then match nothing, or reach
    // ICustomersClient.InviteAdminAsync, which throws ArgumentException for a blank address
    // (Enclave.Sdk.Api 1.1.0).
    private static Option<string?> EmailOption(CliVerb verb, string name, string description)
    {
        var option = verb.Add(CliOptions.Text(name, description, "email"));

        verb.Check(context =>
        {
            if (context.Get(option) is { } value && string.IsNullOrWhiteSpace(value))
            {
                throw CliErrors.InvalidArgument(CliVerb.KeyOf(option), $"{option.Name} takes an email address, and the value given is blank.");
            }
        });

        return option;
    }

    // A command that acts on one customer takes its name, or --org-id, its organisation ID, the
    // only ID a customer has (portal CustomersController.cs:69). It takes exactly one of them: a
    // name with its ID option contradict each other (proposed-cli-surface.md "Details"), and
    // ENCLAVE_ORG_ID and the saved organisation choose the organisation that organisation commands
    // act within, so they never stand in for the customer ("Context").
    private static (CliVerb Verb, CustomerChoice Customer) ForCustomer(string name, string description, bool changes)
    {
        var verb = new CliVerb(name, description, CommandScope.Partner, changes);
        var customer = verb.Add(CliArguments.OptionalText("customer", "The customer's name, matched whole and ignoring case."));
        var orgId = verb.Add(CliOptions.Id("--org-id", "The customer's organisation ID.", IdFormats.Guid, "orgId"));
        verb.ExactlyOne(customer, orgId);
        RefuseBlankName(verb, customer, context => context.Get(customer));
        return (verb, new CustomerChoice(customer, orgId));
    }

    // An empty or blank name names no customer, so it is refused with the other argument checks,
    // before the token and any call (proposed-cli-surface.md "Errors and exit codes"). Passed on,
    // the name of the customer a command acts on would cost the customer lookup and then match
    // nothing, and a name for create or update --name would reach the API, which refuses it (portal
    // Enclave.Partner.Api/Modules/CustomerManagement/Customers/Validators/CustomerCreateModelValidator.cs:19
    // and CustomerPatchModelValidator.cs:10, NotEmpty, which fails a string that is empty or white
    // space: FluentValidation 11.9.0, NotEmptyValidator.IsValid).
    private static void RefuseBlankName(CliVerb verb, Symbol symbol, Func<CliContext, string?> value) =>
        verb.Check(context =>
        {
            if (value(context) is { } name && string.IsNullOrWhiteSpace(name))
            {
                throw CliErrors.InvalidArgument(CliVerb.KeyOf(symbol), $"{CliVerb.Describe(symbol)} takes a customer name, and the value given is blank.");
            }
        });

    // Checks run in the order arguments, token, partner, then the call (proposed-cli-surface.md
    // "Errors and exit codes"). The declared checks run before the handler, and GetPartnerAsync
    // checks the token and the partner with no call, so it comes before the customer lookup, which
    // is the first call a command given a customer name makes.
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
    // customer, or the admin or invite named, and exits 5 ("Several IDs"). Every customer route
    // answers with a model, which the command prints unchanged.
    private static async Task PrintAsync<T>(CliContext context, Func<Task<T>> call)
    {
        var model = await SingleItem.CallAsync(call);

        await context.Output.WriteAsync(model, context.CancellationToken);
    }

    private sealed record CustomerChoice(Argument<string?> Name, Option<Guid?> OrgId);
}
