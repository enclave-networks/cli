using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Partner.Customer;

/// <summary>
/// The `partner customer` noun: the partner's customers, their admins, invites and auto-sync
/// (proposed-cli-surface.md "Commands", "Partner API"). A customer is an organisation, given by
/// name or by --org-id, its organisation ID.
/// </summary>
internal static class PartnerCustomerCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("customer", "customers", "The partner's customers, their admins, invites and auto-sync.");

        noun.Add(List());
        noun.Add(Show());
        noun.Add(CreateCustomer());
        noun.Add(Update());
        noun.Add(Convert());
        noun.Add(ForCustomer("list-admins", "List the customer's admins.", changes: false));
        noun.Add(AddAdmin());
        noun.Add(RemoveAdmin());
        noun.Add(ForCustomer("list-invites", "List the customer's pending admin invites.", changes: false));
        noun.Add(Invite("invite", "Invite someone to be an admin of the customer."));
        noun.Add(Invite("cancel-invite", "Cancel a pending admin invite to the customer, given by its email address."));
        noun.Add(ForCustomer("enable-auto-sync", "Turn on auto-sync for the customer, which keeps the partner's staff as its admins.", changes: true));
        noun.Add(ForCustomer("disable-auto-sync", "Turn off auto-sync for the customer.", changes: true));

        return noun;
    }

    // Every partner customer command calls the partner API, for which Enclave.Sdk.Api 1.0.5 has no
    // clients and no base URL (proposed-cli-surface.md "Needs Enclave.Sdk.Api changes", item 8). It
    // checks its arguments, the token and the partner as the finished command will, then exits 1
    // with not_implemented, having made no call.
    private static async Task NotImplementedAsync(CliContext context)
    {
        _ = await context.GetPartnerAsync();

        throw CliErrors.NotImplemented("Partner customer commands need Enclave.Sdk.Api partner API clients and a partner API base URL, which Enclave.Sdk.Api 1.0.5 does not have. Nothing was sent.");
    }

    private static CliVerb List()
    {
        var verb = new CliVerb("list", "List the partner's customers.", CommandScope.Partner);
        verb.SetHandler(NotImplementedAsync);
        return verb;
    }

    private static CliVerb Show() => ForCustomer("show", "Show a customer.", changes: false);

    private static CliVerb CreateCustomer()
    {
        var verb = new CliVerb("create", "Create a customer, sending every field of the API's customer model, with the partner portal's values for the flags left out.", CommandScope.Partner, changes: true);
        verb.Add(CliArguments.Text("name", "The customer's name."));
        verb.Add(CliOptions.Text("--owner", "The email address of the user who will own the customer's organisation.", "email"));
        verb.Add(CliOptions.Text("--domain", "The customer's domain.", "domain"));
        verb.Add(CliOptions.Text("--contact", "The customer's contact name.", "name"));
        verb.Add(CliOptions.Number("--systems", "The number of systems to license, at least 2; 50 when left out.", minimum: 2));
        verb.Add(CliOptions.Number("--gateways", "The number of gateways to license; 0 when left out.", minimum: 0));
        verb.Add(CliOptions.Flag("--industry-discount", "Request the industry discount."));
        verb.Add(CliOptions.Flag("--hard-limit", "Refuse systems beyond the licensed number."));
        verb.Add(CliOptions.Flag("--auto-sync", "Keep the partner's staff as the customer's admins."));
        verb.SetHandler(NotImplementedAsync);
        return verb;
    }

    // An update sends only the fields given, so one with no change flag has nothing to send, and a
    // flag given with its --no- form leaves the field's new value unclear; both exit 2
    // (proposed-cli-surface.md "Details"). Auto-sync is not an update flag: it changes through
    // enable-auto-sync and disable-auto-sync, which have their own API routes ("Command options").
    private static CliVerb Update()
    {
        var verb = ForCustomer("update", "Change a customer's name, contact, licensed systems and gateways, discount request and hard limit.", changes: true);
        var name = verb.Add(CliOptions.Text("--name", "The customer's new name.", "name"));
        var contact = verb.Add(CliOptions.Text("--contact", "The customer's contact name.", "name"));
        var systems = verb.Add(CliOptions.Number("--systems", "The number of systems to license, at least 2.", minimum: 2));
        var gateways = verb.Add(CliOptions.Number("--gateways", "The number of gateways to license.", minimum: 0));
        var industryDiscount = verb.Add(CliOptions.Flag("--industry-discount", "Request the industry discount."));
        var noIndustryDiscount = verb.Add(CliOptions.Flag("--no-industry-discount", "Withdraw the industry discount request."));
        var hardLimit = verb.Add(CliOptions.Flag("--hard-limit", "Refuse systems beyond the licensed number."));
        var noHardLimit = verb.Add(CliOptions.Flag("--no-hard-limit", "Allow systems beyond the licensed number."));

        verb.AtLeastOne(name, contact, systems, gateways, industryDiscount, noIndustryDiscount, hardLimit, noHardLimit);
        verb.Exclusive(industryDiscount, noIndustryDiscount);
        verb.Exclusive(hardLimit, noHardLimit);
        return verb;
    }

    // --billing-months is required, so the command states the billing period it commits to; the
    // API's default is 1 month (proposed-cli-surface.md "Command options"; portal
    // ConvertCustomerModelValidator.cs:13-17).
    private static CliVerb Convert()
    {
        var verb = ForCustomer("convert", "Convert a customer from trial to paid, with the billing period it commits to.", changes: true);
        var billingMonths = verb.Add(CliOptions.Choice("--billing-months", "The billing period in months.", ("1", 1), ("12", 12), ("24", 24), ("36", 36)));
        verb.ExactlyOne(billingMonths);
        return verb;
    }

    // The account is given by ID only: it is a partner user, and listing partner users needs a
    // scope personal access tokens cannot carry, so an email address cannot be looked up
    // (proposed-cli-surface.md "Names and IDs").
    private static CliVerb AddAdmin()
    {
        var verb = ForCustomer("add-admin", "Make one of the partner's users an admin of the customer, given by account ID, which the partner portal shows.", changes: true);
        var userId = verb.Add(CliOptions.Id("--user-id", "The account ID of the partner's user.", IdFormats.Guid, "accountId"));
        verb.ExactlyOne(userId);
        return verb;
    }

    // An email address with an account ID contradict each other, as a name with its ID option does
    // (proposed-cli-surface.md "Details").
    private static CliVerb RemoveAdmin()
    {
        var verb = ForCustomer("remove-admin", "Remove an admin from the customer, given by email address or account ID.", changes: true);
        var user = verb.Add(CliOptions.Text("--user", "The admin's email address.", "email"));
        var userId = verb.Add(CliOptions.Id("--user-id", "The admin's account ID.", IdFormats.Guid, "accountId"));
        verb.ExactlyOne(user, userId);
        return verb;
    }

    private static CliVerb Invite(string name, string description)
    {
        var verb = ForCustomer(name, description, changes: true);
        var email = verb.Add(CliOptions.Text("--email", "The email address.", "email"));
        verb.ExactlyOne(email);
        return verb;
    }

    // A command that acts on one customer takes its name, or --org-id, its organisation ID, the
    // only ID a customer has (portal CustomersController.cs:69). It takes exactly one of them: a
    // name with its ID option contradict each other (proposed-cli-surface.md "Details"), and
    // ENCLAVE_ORG_ID and the saved organisation choose the organisation that organisation commands
    // act within, so they never stand in for the customer ("Context").
    private static CliVerb ForCustomer(string name, string description, bool changes)
    {
        var verb = new CliVerb(name, description, CommandScope.Partner, changes);
        var customer = verb.Add(CliArguments.OptionalText("customer", "The customer's name, matched whole and ignoring case."));
        var orgId = verb.Add(CliOptions.Id("--org-id", "The customer's organisation ID.", IdFormats.Guid, "orgId"));
        verb.ExactlyOne(customer, orgId);
        verb.SetHandler(NotImplementedAsync);
        return verb;
    }
}
