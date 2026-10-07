using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// Enclave.Sdk.Api 1.0.4 has no partner API clients and no partner API base URL
// (proposed-cli-surface.md "Partner API", and item 8 of "Needs Enclave.Sdk.Api changes";
// src/Enclave.Cli/Partner/PartnerApi.cs). A partner customer command that would call the partner
// API therefore exits 1 with not_implemented, prints nothing on stdout and makes no call ("Errors
// and exit codes"). It parses and checks its command line before that point, so a bad argument
// exits 2 with invalid_argument whatever the partner API's state, and the command names, flags and
// argument rules can be tested before the partner clients exist.
//
// Every test here chooses a valid partner through ENCLAVE_PARTNER_ID, so a bad argument is the only
// error a case can have.
//
// A rejection alone does not show that a command checked its input, since an unknown command is
// rejected the same way. Each argument case therefore runs the corrected command in the same
// sandbox and expects not_implemented, which only a command that exists and accepted its arguments
// reports (the reason CliAssert.RejectedThenAcceptedAsync gives for the main API's commands).
//
// This class also holds the table of partner customer commands that PartnerContextTests and
// PartnerSafetyTests draw on, so the three fixtures cannot drift apart on which commands exist.
[Category(TestCategory.Pending)]
public class PartnerCommandTests
{
    private const string PartnerIdVariable = "ENCLAVE_PARTNER_ID";

    private static readonly string[] InvalidBillingMonths = ["0", "2", "6", "13", "48", "twelve"];

    // One valid command line for each partner customer verb in the commands tree
    // (proposed-cli-surface.md "Commands"), the customer given by name. Changes marks the verbs that
    // change something through the API, which take --dry-run ("Options on every command").
    // list-invites only reads, although the API asks for WriteCustomers to read invites (portal
    // CustomersController.cs, GET {customerId}/invites).
    internal static IReadOnlyList<CustomerCommand> Commands { get; } =
    [
        new("list", TakesCustomer: false, Changes: false, []),
        new("show", TakesCustomer: true, Changes: false, []),
        new("create", TakesCustomer: false, Changes: true, [TestData.NewCustomerName]),
        new("update", TakesCustomer: true, Changes: true, ["--systems", "30"]),
        new("convert", TakesCustomer: true, Changes: true, ["--billing-months", "12"]),
        new("list-admins", TakesCustomer: true, Changes: false, []),
        new("add-admin", TakesCustomer: true, Changes: true, ["--user-id", TestData.PartnerStaffAccountId]),
        new("remove-admin", TakesCustomer: true, Changes: true, ["--user", TestData.CustomerAdminEmail]),
        new("list-invites", TakesCustomer: true, Changes: false, []),
        new("invite", TakesCustomer: true, Changes: true, ["--email", TestData.CustomerInviteEmail]),
        new("cancel-invite", TakesCustomer: true, Changes: true, ["--email", TestData.CustomerInviteEmail]),
        new("enable-auto-sync", TakesCustomer: true, Changes: true, []),
        new("disable-auto-sync", TakesCustomer: true, Changes: true, []),
    ];

    [TestCaseSource(nameof(CommandsByName))]
    public async Task Every_partner_customer_command_reports_not_implemented_and_sends_no_request(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.NotImplemented(run, result);
    }

    // A customer is given by its name or by --org-id, its organisation ID, which is the only ID a
    // customer has (proposed-cli-surface.md "Commands"; portal CustomersController.cs:69 reads the
    // route's customer ID as an OrganisationGuid).
    [TestCaseSource(nameof(CommandsByOrgId))]
    public async Task Every_partner_customer_command_that_acts_on_a_customer_takes_it_by_org_id(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.NotImplemented(run, result);
    }

    // The partner examples of proposed-cli-surface.md ("Examples" 20, 34, 65, 66, 85 and 86), each
    // command written as the specification writes it.
    [TestCase("partner", "customer", "list")]
    [TestCase("partner", "customer", "remove-admin", "Globex Ltd", "--user", "alex@example.com")]
    [TestCase("partner", "customer", "add-admin", "Globex Ltd", "--user-id", "5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25")]
    [TestCase("partner", "customer", "enable-auto-sync", "Globex Ltd")]
    [TestCase("partner", "customer", "add-admin", "--org-id", "6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b", "--user-id", "5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25")]
    [TestCase("partner", "customer", "enable-auto-sync", "--org-id", "6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b")]
    [TestCase("partner", "customer", "invite", "Globex Ltd", "--email", "sam@globex.example")]
    [TestCase("partner", "customer", "create", "Initech", "--owner", "it@initech.example", "--domain", "initech.example", "--systems", "20", "--gateways", "1", "--auto-sync")]
    [TestCase("partner", "customer", "convert", "Initech", "--billing-months", "12")]
    [TestCase("partner", "customer", "convert", "--org-id", "8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6", "--billing-months", "12")]
    public async Task The_partner_examples_in_the_specification_report_not_implemented_and_send_no_request(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.NotImplemented(run, result);
    }

    // Each create flag of proposed-cli-surface.md "Command options" is given alone, so a flag the
    // CLI lacks shows as the one case that fails. 2 systems and 0 gateways are the smallest counts
    // the API accepts (the create table; portal CustomerCreateModelValidator.cs).
    [TestCase("--owner", "it@initech.example")]
    [TestCase("--domain", "initech.example")]
    [TestCase("--contact", "Peter Gibbons")]
    [TestCase("--systems", "2")]
    [TestCase("--gateways", "0")]
    [TestCase("--industry-discount")]
    [TestCase("--hard-limit")]
    [TestCase("--auto-sync")]
    public async Task Create_takes_each_flag_the_specification_lists(params string[] options)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(["partner", "customer", "create", TestData.NewCustomerName, .. options]);

        CliAssert.NotImplemented(run, result);
    }

    // Each update flag of proposed-cli-surface.md "Command options" is given alone. --systems sets
    // LicensedAgentsCount, at least 2, and --gateways sets LicensedGatewaysCount (portal
    // CustomerPatchModelValidator.cs:14-16).
    [TestCase("--name", "Globex Limited")]
    [TestCase("--contact", "Jo Bloggs")]
    [TestCase("--systems", "2")]
    [TestCase("--gateways", "0")]
    [TestCase("--industry-discount")]
    [TestCase("--no-industry-discount")]
    [TestCase("--hard-limit")]
    [TestCase("--no-hard-limit")]
    public async Task Update_takes_each_flag_the_specification_lists(params string[] options)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(["partner", "customer", "update", TestData.CustomerName, .. options]);

        CliAssert.NotImplemented(run, result);
    }

    // The billing periods the API accepts (proposed-cli-surface.md "Command options"; portal
    // ConvertCustomerModelValidator.cs:13-17).
    [TestCase("1")]
    [TestCase("12")]
    [TestCase("24")]
    [TestCase("36")]
    public async Task Convert_takes_each_billing_period_the_api_accepts(string months)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync("partner", "customer", "convert", TestData.CustomerName, "--billing-months", months);

        CliAssert.NotImplemented(run, result);
    }

    // remove-admin takes the admin's email address, looked up in the customer's admins, or the
    // account ID (proposed-cli-surface.md "Names and IDs").
    [TestCase("--user", TestData.CustomerAdminEmail)]
    [TestCase("--user-id", TestData.PartnerStaffAccountId)]
    public async Task Remove_admin_takes_the_admin_by_email_or_by_account_id(string option, string value)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync("partner", "customer", "remove-admin", TestData.CustomerName, option, value);

        CliAssert.NotImplemented(run, result);
    }

    // Nouns are singular, and a plural name is a hidden alias for one (proposed-cli-surface.md
    // "Shape and naming"), so an agent that guesses the plural reaches the same command.
    [TestCase("partners", "customer", "list")]
    [TestCase("partner", "customers", "list")]
    [TestCase("partners", "customers", "list")]
    [TestCase("partner", "customers", "show", TestData.CustomerName)]
    [TestCase("partner", "customers", "add-admin", TestData.CustomerName, "--user-id", TestData.PartnerStaffAccountId)]
    public async Task Plural_nouns_run_the_singular_partner_customer_command(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.NotImplemented(run, result);
    }

    // A CLI that answered not_implemented to any words after "partner" would pass the test above.
    // The alias runs the singular command, so it applies that command's argument checks as well.
    [Test]
    public async Task A_plural_noun_applies_the_argument_checks_of_the_singular_command()
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        CliAssert.Rejected(run, await run.RunAsync("partner", "customers", "convert", TestData.CustomerName, "--billing-months", "6"));
        CliAssert.NotImplemented(run, await run.RunAsync("partner", "customers", "convert", TestData.CustomerName, "--billing-months", "12"));
    }

    [TestCase("partner", "customerz", "list")]
    [TestCase("partner", "customer", "lst")]
    public async Task An_unknown_partner_noun_or_verb_exits_2_invalid_argument(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
    }

    // The partner's own properties, users and invites, and listing partners, need scopes a personal
    // access token cannot carry, and the partner API's countries, referlink and customer
    // oldest-version routes serve the partner portal's screens; the CLI has no commands for them
    // (proposed-cli-surface.md "Left out", "Partner API"). A noun's own parts take hyphenated verbs
    // in place of another level ("Shape and naming"), so the customer admin, invite and auto-sync
    // sub-nouns do not exist either. Each of these is an unknown command.
    [TestCase("partner", "list")]
    [TestCase("partner", "show")]
    [TestCase("partner", "update", "--name", "Acme Partners")]
    [TestCase("partner", "list-users")]
    [TestCase("partner", "remove-user", "--user-id", TestData.PartnerStaffAccountId)]
    [TestCase("partner", "list-invites")]
    [TestCase("partner", "invite", "--email", TestData.CustomerInviteEmail)]
    [TestCase("partner", "cancel-invite", "--email", TestData.CustomerInviteEmail)]
    [TestCase("partner", "user", "list")]
    [TestCase("partner", "invite", "list")]
    [TestCase("partner", "countries")]
    [TestCase("partner", "referlink")]
    [TestCase("partner", "customer", "oldest-version", TestData.CustomerName)]
    [TestCase("partner", "customer", "admin", "list", TestData.CustomerName)]
    [TestCase("partner", "customer", "admin", "add", TestData.CustomerName, TestData.PartnerStaffAccountId)]
    [TestCase("partner", "customer", "invite", "list", TestData.CustomerName)]
    [TestCase("partner", "customer", "invite", "send", TestData.CustomerName, TestData.CustomerInviteEmail)]
    [TestCase("partner", "customer", "auto-sync", "enable", TestData.CustomerName)]
    public async Task Partner_commands_the_specification_leaves_out_exit_2_invalid_argument(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
    }

    [TestCaseSource(nameof(BadArguments))]
    public async Task A_bad_argument_exits_2_invalid_argument_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    // A command that acts on a customer needs one, by name or by --org-id (proposed-cli-surface.md
    // "Commands"). CliRun sets ENCLAVE_ORG_ID, which chooses the organisation that organisation
    // commands act within; partner commands use the partner ("Context"), so it does not stand in
    // for the customer.
    [TestCaseSource(nameof(CommandsWithoutTheCustomer))]
    public async Task A_customer_command_without_a_customer_exits_2_invalid_argument(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    // A name with its ID option contradict each other and exit 2 (proposed-cli-surface.md
    // "Details"): the command cannot tell which customer is meant.
    [TestCaseSource(nameof(CommandsWithNameAndOrgId))]
    public async Task A_customer_name_with_org_id_exits_2_invalid_argument(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    // --dry-run applies to commands that change something through the API, and an option a command
    // does not take is unknown to it and exits 2 (proposed-cli-surface.md "Options on every
    // command", "Details").
    [TestCaseSource(nameof(ReadsWithDryRun))]
    public async Task Dry_run_is_an_unknown_option_on_a_partner_customer_command_that_only_reads(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    internal static IEnumerable<TestCaseData> CommandsByName() =>
        Commands.Select(command => Case(command.ByName()));

    internal static IEnumerable<TestCaseData> CommandsThatChangeSomething() =>
        Commands.Where(command => command.Changes).Select(command => Case(command.ByName()));

    // The cast selects the TestCaseData(object) constructor, which passes the array as the test's one
    // argument; the params object[] constructor would take each string as a separate argument.
    internal static TestCaseData Case(string[] args) =>
        new TestCaseData((object)args).SetArgDisplayNames(Display(args));

    internal static TestCaseData Pair(string[] rejected, string[] corrected) =>
        new TestCaseData(rejected, corrected).SetArgDisplayNames(Display(rejected));

    internal static string[] Line(string verb, params string[] rest) => ["partner", "customer", verb, .. rest];

    internal static string[] ForCustomer(string verb, params string[] rest) => ["partner", "customer", verb, TestData.CustomerName, .. rest];

    private static IEnumerable<TestCaseData> CommandsByOrgId() =>
        Commands.Where(command => command.TakesCustomer).Select(command => Case(command.ByOrgId(TestData.CustomerOrgId)));

    private static IEnumerable<TestCaseData> CommandsWithoutTheCustomer() =>
        Commands.Where(command => command.TakesCustomer).Select(command => Pair(command.WithoutCustomer(), command.ByName()));

    private static IEnumerable<TestCaseData> CommandsWithNameAndOrgId() =>
        Commands.Where(command => command.TakesCustomer).Select(command => Pair([.. command.ByName(), "--org-id", TestData.CustomerOrgId], command.ByName()));

    private static IEnumerable<TestCaseData> ReadsWithDryRun() =>
        Commands.Where(command => !command.Changes).Select(command => Pair([.. command.ByName(), "--dry-run"], command.ByName()));

    private static IEnumerable<TestCaseData> BadArguments()
    {
        // convert requires --billing-months, one of the periods the API accepts, so the command
        // states the billing period it commits to (proposed-cli-surface.md "Command options").
        yield return Pair(ForCustomer("convert"), ForCustomer("convert", "--billing-months", "12"));

        foreach (var months in InvalidBillingMonths)
        {
            yield return Pair(ForCustomer("convert", "--billing-months", months), ForCustomer("convert", "--billing-months", "12"));
        }

        // create requires the new customer's name, and --systems is at least 2 (the create table of
        // "Command options"). Counts are whole numbers.
        yield return Pair(Line("create"), Line("create", TestData.NewCustomerName));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "1"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "0"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "two"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--gateways", "one"), Line("create", TestData.NewCustomerName, "--gateways", "1"));

        // update --systems is at least 2 ("Command options"). Auto-sync changes only through
        // enable-auto-sync and disable-auto-sync, which have their own API routes, and the owner is
        // set at create only, so neither is an update flag.
        yield return Pair(ForCustomer("update", "--systems", "1"), ForCustomer("update", "--systems", "2"));
        yield return Pair(ForCustomer("update", "--systems", "30", "--auto-sync"), ForCustomer("update", "--systems", "30"));
        yield return Pair(ForCustomer("update", "--systems", "30", "--owner", "it@initech.example"), ForCustomer("update", "--systems", "30"));

        // An update with no change flag, and flags that contradict each other, exit 2 ("Details").
        yield return Pair(ForCustomer("update"), ForCustomer("update", "--systems", "30"));
        yield return Pair(ForCustomer("update", "--industry-discount", "--no-industry-discount"), ForCustomer("update", "--industry-discount"));
        yield return Pair(ForCustomer("update", "--hard-limit", "--no-hard-limit"), ForCustomer("update", "--hard-limit"));
        yield return Pair(ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail, "--user-id", TestData.PartnerStaffAccountId), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));

        // add-admin takes the account by --user-id only: the account is a partner user, and listing
        // partner users needs a scope personal access tokens cannot carry ("Names and IDs").
        yield return Pair(ForCustomer("add-admin", "--user", TestData.CustomerAdminEmail), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return Pair(ForCustomer("add-admin"), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));

        // The admin to remove and the address to invite or cancel are part of each command
        // ("Commands"); without them the command has nothing to act on.
        yield return Pair(ForCustomer("remove-admin"), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
        yield return Pair(ForCustomer("invite"), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
        yield return Pair(ForCustomer("cancel-invite"), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));

        // Anything a partner customer command needs besides the customer is a named option
        // ("Commands"), so a second positional argument is an error.
        yield return Pair(ForCustomer("add-admin", TestData.PartnerStaffAccountId), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return Pair(ForCustomer("remove-admin", TestData.CustomerAdminEmail), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
        yield return Pair(ForCustomer("invite", TestData.CustomerInviteEmail), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
        yield return Pair(ForCustomer("cancel-invite", TestData.CustomerInviteEmail), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));

        // --yes, --from-file, --template, -o, --all and --token are removed ("Changes to AGENTS.md"),
        // --limit stays on log only, and partner customer list takes no filter options ("Command
        // options").
        yield return Pair(ForCustomer("convert", "--billing-months", "12", "--yes"), ForCustomer("convert", "--billing-months", "12"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--from-file", "customer.json"), Line("create", TestData.NewCustomerName));
        yield return Pair(Line("create", TestData.NewCustomerName, "--template"), Line("create", TestData.NewCustomerName));
        yield return Pair(Line("list", "-o", "json"), Line("list"));
        yield return Pair(Line("list", "--all"), Line("list"));
        yield return Pair(Line("list", "--limit", "10"), Line("list"));
        yield return Pair(Line("list", "--search", "Globex"), Line("list"));
        yield return Pair(Line("list", "--token", "abc123"), Line("list"));

        yield return Pair(ForCustomer("show", "--no-such-option"), ForCustomer("show"));
    }

    // Test names show each case as the command line it runs, with placeholders for the fixed IDs.
    private static string Display(string[] args) => string.Join(' ', args.Select(DisplayArg));

    private static string DisplayArg(string arg) => arg switch
    {
        TestData.CustomerOrgId => "<orgId>",
        TestData.PartnerStaffAccountId => "<accountId>",
        _ when arg == TestData.PartnerId.ToString() => "<partnerId>",
        _ when arg.Contains(' ', StringComparison.Ordinal) => $"'{arg}'",
        _ => arg,
    };

    internal sealed record CustomerCommand(string Verb, bool TakesCustomer, bool Changes, IReadOnlyList<string> Options)
    {
        public string[] ByName() => TakesCustomer ? Build(TestData.CustomerName) : Build();

        public string[] ByOrgId(string orgId) => Build("--org-id", orgId);

        public string[] WithoutCustomer() => Build();

        private string[] Build(params string[] customer) => ["partner", "customer", Verb, .. customer, .. Options];
    }
}
