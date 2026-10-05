using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// Enclave.Sdk.Api 1.0.4 has no partner API clients (proposed-cli-surface.md, "Partner API" and item 9 of "Needs
// Enclave.Sdk.Api changes"), so a partner command that would call the partner API reports not_implemented. Before that
// call it parses its arguments, checks IDs, resolves the partner and checks --yes, so the command surface and its
// safety rules can be tested before the partner clients exist. The CLI never builds a request itself (AGENTS.md,
// "Architecture"), so no partner command may reach the fake API.
//
// This class also holds the table of partner commands that PartnerContextTests and PartnerSafetyTests draw on, so the
// three fixtures cannot drift apart on which commands exist or which need --yes.
public class PartnerCommandTests
{
    internal const string CustomerId = "6d1f4a2e-93b7-4c58-a0e2-5f8c71d3b9a4";

    internal const string AccountId = "b2e85c17-4f0a-4d6b-9c3e-1a7f28e64d05";

    // The partner API's invite IDs are strings (OrganisationInviteId, IdBackingType.String); the proposal leaves their
    // exact format to the partner clients, so the tests use letters and digits only.
    internal const string InviteId = "Q7Z2K9XW";

    internal const string Email = "alex@example.com";

    // Stands for the path of a JSON body file. RunAsync writes the file into the run's work directory and passes its
    // path, because each run has its own temporary directory.
    internal const string BodyFile = "<body-file>";

    internal const string PartnerVariable = "ENCLAVE_PARTNER";

    // Enclave.Sdk.Api 1.0.4 has no partner API create or patch models to take field names from, so the body is any
    // JSON object.
    private const string BodyJson = """{ "name": "Initech" }""";

    // One entry per command in the proposal's partner tree that calls the partner API, which is every partner command
    // except `partner use`. Partner and customer create and update take --from-file because their flags are chosen once
    // the partner API models exist (proposal, "Command options"). NeedsYes follows the proposal's "Confirmation" list.
    internal static IReadOnlyList<PartnerCommand> ApiCommands { get; } =
    [
        Read("partner list"),
        Read("partner show"),
        Change("partner update", "--from-file", BodyFile),
        Read("partner user list"),
        Change("partner user update", AccountId, "--from-file", BodyFile),
        Confirmed("partner user remove", AccountId),
        Read("partner invite list"),
        Confirmed("partner invite send", Email),
        Change("partner invite update", InviteId, "--from-file", BodyFile),
        Change("partner invite cancel", InviteId),
        Read("partner customer list"),
        Read("partner customer show", CustomerId),
        Change("partner customer create", "--from-file", BodyFile),
        Change("partner customer update", CustomerId, "--from-file", BodyFile),
        Confirmed("partner customer convert", CustomerId),
        Read("partner customer admin list", CustomerId),
        Confirmed("partner customer admin add", CustomerId, AccountId),
        Confirmed("partner customer admin remove", CustomerId, AccountId),
        Read("partner customer invite list", CustomerId),
        Confirmed("partner customer invite send", CustomerId, Email),
        Change("partner customer invite cancel", CustomerId, InviteId),
        Change("partner customer auto-sync enable", CustomerId),
        Change("partner customer auto-sync disable", CustomerId),
    ];

    [TestCaseSource(nameof(ApiCommandsAsConfirmed))]
    public async Task Partner_api_commands_report_not_implemented_and_send_no_request(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerVariable] = TestData.PartnerId.ToString();

        var result = await RunAsync(run, args);

        AssertNotImplemented(run, result);
    }

    // --dry-run prints the request Enclave.Sdk.Api would build (proposal, "Dry run"). With no partner clients there is
    // no request to print, so a dry run reports not_implemented too, and sends nothing.
    [TestCaseSource(nameof(ApiCommandsThatChangeSomethingUnderDryRun))]
    public async Task Partner_api_commands_that_change_something_report_not_implemented_under_dry_run(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerVariable] = TestData.PartnerId.ToString();

        var result = await RunAsync(run, args);

        AssertNotImplemented(run, result);
    }

    // `create --template` prints the create model and `update <id> --template` reads the resource's current values
    // (proposal, "Create and update"). For partner resources both need the partner API models and clients.
    [TestCase("partner", "update", "--template")]
    [TestCase("partner", "user", "update", AccountId, "--template")]
    [TestCase("partner", "invite", "update", InviteId, "--template")]
    [TestCase("partner", "customer", "create", "--template")]
    [TestCase("partner", "customer", "update", CustomerId, "--template")]
    public async Task Partner_create_and_update_templates_report_not_implemented(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        AssertNotImplemented(run, result);
    }

    // Nouns are singular and a plural name is a hidden alias for it (proposal, "Shape and naming"), so an agent that
    // guesses the plural reaches the same command.
    [TestCase("partners", "list")]
    [TestCase("partner", "customers", "list")]
    [TestCase("partners", "customers", "list")]
    [TestCase("partner", "users", "list")]
    [TestCase("partner", "invites", "list")]
    [TestCase("partner", "customers", "show", CustomerId)]
    [TestCase("partner", "customer", "admins", "list", CustomerId)]
    [TestCase("partner", "customer", "invites", "list", CustomerId)]
    public async Task Plural_partner_nouns_run_the_singular_command(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        AssertNotImplemented(run, result);
    }

    // A misspelt noun is a parse error. This test keeps the plural tests above meaningful: a CLI that reported
    // not_implemented for any words after `partner` would pass them.
    [Test]
    public async Task An_unknown_partner_noun_exits_2_invalid_argument()
    {
        using var run = CliRun.Start();
        run.Environment[PartnerVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync("partner", "customerz", "list");

        AssertError(run, result, 2, "invalid_argument");
    }

    internal static IEnumerable<TestCaseData> ApiCommandsAsConfirmed() =>
        ApiCommands.Select(command => Case(command, command.NeedsYes ? ["--yes"] : []));

    internal static IEnumerable<TestCaseData> ApiCommandsThatNeedAPartner() =>
        ApiCommands
            .Where(command => command.NeedsPartner)
            .Select(command => Case(command, command.NeedsYes ? ["--yes"] : []));

    internal static IEnumerable<TestCaseData> ApiCommandsThatNeedYes() =>
        ApiCommands.Where(command => command.NeedsYes).Select(command => Case(command));

    internal static TestCaseData Case(PartnerCommand command, params string[] options)
    {
        string[] args = [.. command.Name.Split(' '), .. command.Arguments, .. options];

        // The cast selects the TestCaseData(object) constructor, which passes the array as the test's one argument;
        // the params object[] constructor would take each string as a separate argument.
        return new TestCaseData((object)args).SetArgDisplayNames(string.Join(' ', args.Select(Display)));
    }

    internal static Task<CliResult> RunAsync(CliRun run, string[] args)
    {
        var resolved = args.Select(arg => arg == BodyFile ? run.WriteFile("body.json", BodyJson) : arg).ToArray();

        return run.RunAsync(resolved);
    }

    internal static void AssertNotImplemented(CliRun run, CliResult result) =>
        AssertError(run, result, 1, "not_implemented");

    internal static void AssertError(CliRun run, CliResult result, int exitCode, string code)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(ErrorCode(result), Is.EqualTo(code));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    internal static string? ErrorCode(CliResult result) => JsonAssert.Property(result.Error, "code").GetString();

    private static IEnumerable<TestCaseData> ApiCommandsThatChangeSomethingUnderDryRun() =>
        ApiCommands
            .Where(command => command.Changes)
            .Select(command => Case(command, command.NeedsYes ? ["--dry-run", "--yes"] : ["--dry-run"]));

    private static PartnerCommand Read(string name, params string[] arguments) => new(name, arguments, false, false);

    private static PartnerCommand Change(string name, params string[] arguments) => new(name, arguments, true, false);

    private static PartnerCommand Confirmed(string name, params string[] arguments) => new(name, arguments, true, true);

    // Test names show placeholders for the fixed IDs so that each case reads as the command line it runs.
    private static string Display(string arg) => arg switch
    {
        CustomerId => "<customerId>",
        AccountId => "<accountId>",
        InviteId => "<inviteId>",
        Email => "<email>",
        _ when arg == TestData.PartnerId.ToString() => "<partnerId>",
        _ => arg,
    };

    internal sealed record PartnerCommand(string Name, IReadOnlyList<string> Arguments, bool Changes, bool NeedsYes)
    {
        // `partner list` lists the partners the token can see, so it is the one partner API command that acts
        // without a partner chosen.
        public bool NeedsPartner => Name != "partner list";
    }
}
