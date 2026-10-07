using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// System.CommandLine's default parse-error action writes plain text and help and exits 1
// (ParseErrorAction, version 2.0.12). The CLI replaces it (proposed-cli-surface.md "Errors and exit
// codes"): a parse error is one JSON error on stderr with code invalid_argument, nothing on stdout,
// exit 2, and any suggestion in "detail". A command line with several problems gives one error
// object, whose detail names the first problem and whose errors lists them all. CliAssert.Rejected
// checks the one line on stderr, the code, the exit code, the empty stdout and that no request was
// made.
public class ParseErrorTests
{
    private const string Secret = "s3cret-value";

    private static readonly string[] BogusAndWrong = ["--bogus", "--wrong"];

    public static IEnumerable<TestCaseData> MalformedCommands()
    {
        yield return Malformed("an unknown option", "system", "list", "--bogus");
        yield return Malformed("an unknown noun", "widget", "list");
        yield return Malformed("an unknown verb", "system", "frobnicate");
        yield return Malformed("an unexpected argument", "system", "list", "extra");
        yield return Malformed("a noun without a verb", "system");
        yield return Malformed("a missing argument", "system", "show");
        yield return Malformed("a missing argument after a hyphenated verb", "dns", "create-zone");
        yield return Malformed("an option without its value", "system", "list", "--filter");
        yield return Malformed("a number option given a word", "key", "create", "ci runners", "--uses", "many");
    }

    // Each case is a command line the specification no longer has, with the command that replaced
    // it, which proves the rejection came from the old form and not from a command that does not
    // run at all. The old forms are the sub-nouns `dns zone`, `dns record` and `org user`, which
    // became hyphenated verbs, the `pending` noun, which became `system --pending`, and `log list`,
    // which became `log` ("Commands", "Changes to AGENTS.md").
    public static IEnumerable<TestCaseData> ReplacedCommands()
    {
        yield return Replaced("dns zone list", "dns list-zones", "GET", TestData.OrgPath("dns/zones"), ApiJson.Page(ApiJson.Zone(4, "internal")));
        yield return Replaced("dns record list", "dns list-hostnames", "GET", TestData.OrgPath("dns/records"), ApiJson.Page(ApiJson.Record(7, "db")));
        yield return Replaced("org user list", "org list-users", "GET", TestData.OrgPath("users"), ApiJson.Users((TestData.OtherOrgId, "sam@acme.example")));
        yield return Replaced("pending list", "system list --pending", "GET", TestData.OrgPath("unapproved-systems"), ApiJson.Page(ApiJson.PendingSystem("ABCDE")));
        yield return Replaced("log list", "log", "GET", TestData.OrgPath("logs"), ApiJson.Page(ApiJson.Log("System ABCDE enrolled")));
    }

    // Options the CLI does not have, each with the command corrected. Output is always JSON, so
    // there is no -o or --output ("Output"); lists read every page, so there is no --all, and only
    // log has --limit; --filter replaced --search; the token never goes on the command line
    // ("Options on every command"); no command asks for confirmation, so there is no --yes; every
    // field has a flag, so there is no --from-file or --template ("Changes to AGENTS.md"). An option
    // a command does not take, such as --dry-run on a read or --partner-id on an organisation
    // command, is unknown to it ("Details").
    public static IEnumerable<TestCaseData> OptionsTheCliDoesNotHave()
    {
        var systems = TestData.OrgPath("systems");
        var systemsPage = ApiJson.Page(ApiJson.System("ABCDE"));

        yield return Replaced("system list -o json", "system list", "GET", systems, systemsPage);
        yield return Replaced("system list -o table", "system list", "GET", systems, systemsPage);
        yield return Replaced("system list --output json", "system list", "GET", systems, systemsPage);
        yield return Replaced("system show ABCDE -o json", "system show ABCDE", "GET", TestData.OrgPath("systems/ABCDE"), ApiJson.System("ABCDE"));
        yield return Replaced("system list --all", "system list", "GET", systems, systemsPage);
        yield return Replaced("system list --limit 5", "system list", "GET", systems, systemsPage);
        yield return Replaced("system list --search web", "system list --filter web", "GET", systems, systemsPage);
        yield return Replaced("system list --token not-a-token", "system list", "GET", systems, systemsPage);
        yield return Replaced("system list --dry-run", "system list", "GET", systems, systemsPage);
        yield return Replaced($"system list --partner-id {TestData.PartnerId}", "system list", "GET", systems, systemsPage);
        yield return Replaced("policy delete --id 17 --yes", "policy delete --id 17", "DELETE", TestData.OrgPath("policies"), ApiJson.Bulk("policiesDeleted", 1));
        yield return Replaced("policy update --id 42 --from-file policy.json", "policy update --id 42 --notes Reviewed", "PATCH", TestData.OrgPath("policies/42"), ApiJson.Policy(42));
        yield return Replaced("policy update --id 42 --template", "policy update --id 42 --notes Reviewed", "PATCH", TestData.OrgPath("policies/42"), ApiJson.Policy(42));
    }

    // A value given to an option the CLI does not know could be a token passed by mistake, so no
    // error repeats it ("Errors and exit codes"). --token is the likeliest mistake; the = form puts
    // the value in the same argument as the option; --verbose adds diagnostics, which must not
    // repeat it either; and a second problem after it must not bring it into errors.
    public static IEnumerable<TestCaseData> UnknownOptionsWithValues()
    {
        yield return Malformed("--token value", "system", "list", "--token", Secret);
        yield return Malformed("--token=value", "system", "list", $"--token={Secret}");
        yield return Malformed("--token value --verbose", "system", "list", "--token", Secret, "--verbose");
        yield return Malformed("--api-key value", "system", "list", "--api-key", Secret);
        yield return Malformed("--password value --bogus", "system", "list", "--password", Secret, "--bogus");
    }

    // The API answers no request here, and the fake API records a request whether or not a stub
    // answers it, so a CLI that sends one despite the parse error shows in the request log.
    [TestCaseSource(nameof(MalformedCommands))]
    public async Task A_parse_error_exits_2_with_one_invalid_argument_error_naming_the_problem(string[] args)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
        Assert.That(result.Error.GetProperty("detail").GetString(), Is.Not.Null.And.Not.Empty, result.ToString());
    }

    // Two unknown options make two problems; the agent fixes both from one error, and reads the
    // first from detail. errors has the API's shape, an object keyed by the option with a list of
    // messages each, so an agent reads it the same way whether the API or the CLI wrote it ("Errors
    // and exit codes"). An object's keys have no order, so they are compared as a set.
    [Test]
    public async Task A_command_line_with_several_problems_gives_one_error_whose_detail_names_the_first_and_whose_errors_lists_them_all()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("system", "list", "--bogus", "--wrong");

        CliAssert.Rejected(run, result);
        var error = result.Error;
        var errors = error.GetProperty("errors");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("detail").GetString(), Does.Contain("--bogus").And.Not.Contain("--wrong"));
            Assert.That(errors.ValueKind, Is.EqualTo(JsonValueKind.Object), errors.ToString());
        });
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(errors), Is.EquivalentTo(BogusAndWrong), errors.ToString());

            foreach (var option in errors.EnumerateObject())
            {
                Assert.That(JsonAssert.Strings(option.Value), Is.Not.Empty.And.All.Not.Empty, errors.ToString());
            }
        });
    }

    [TestCaseSource(nameof(UnknownOptionsWithValues))]
    public async Task An_error_never_repeats_the_value_given_to_an_unknown_option(string[] args)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(CliAssert.ExitCodes["invalid_argument"]), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.Stderr, Is.Not.Empty);
            Assert.That(result.Stderr, Does.Not.Contain(Secret));
        });
    }

    [TestCaseSource(nameof(ReplacedCommands))]
    public async Task A_command_line_the_command_tree_no_longer_has_is_a_parse_error(string[] rejected, string[] corrected, string method, string path, string json)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: json);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, corrected, method, path);
    }

    [TestCaseSource(nameof(OptionsTheCliDoesNotHave))]
    public async Task An_option_the_command_does_not_take_is_a_parse_error(string[] rejected, string[] corrected, string method, string path, string json)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: json);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, corrected, method, path);
    }

    // The error names what was wrong, so the caller can tell which argument to fix.
    [Test]
    public async Task A_parse_error_names_the_unknown_option_in_detail()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("system", "list", "--bogus");

        CliAssert.Rejected(run, result);
        Assert.That(result.Error.GetProperty("detail").GetString(), Does.Contain("--bogus"));
    }

    // An agent that mistypes a name corrects it from the suggestion, with no call to `commands`.
    // System.CommandLine suggests the names closest to an unmatched token (its typo corrections,
    // version 2.0.12), and the CLI carries them in detail. The mistyped name is the first problem
    // on each command line, so detail is about it.
    [TestCase("system lst", "list")]
    [TestCase("sytem", "system")]
    [TestCase("system list --filtr", "--filter")]
    [TestCase("dns list-zone", "list-zones")]
    public async Task A_mistyped_name_carries_the_suggestion_in_detail(string command, string suggestion)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();

        var result = await run.RunAsync(command.Split(' '));

        CliAssert.Rejected(run, result);
        Assert.That(result.Error.GetProperty("detail").GetString(), Does.Contain(suggestion));
    }

    // Checks run in this order: arguments, the token, the organisation, then the call ("Errors and
    // exit codes"). A command with bad arguments fails the same way with or without a token and an
    // organisation, and with neither chosen the CLI makes no organisation lookup.
    [TestCase("system lst")]
    [TestCase("system list --bogus")]
    [TestCase("system list --bogus --wrong")]
    public async Task A_parse_error_is_the_same_with_or_without_a_token_or_organisation(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var withContext = CliRun.Start();
        using var withoutContext = CliRun.Start();
        withoutContext.Environment.Remove("ENCLAVE_TOKEN");
        withoutContext.Environment.Remove("ENCLAVE_ORG_ID");

        var withResult = await withContext.RunAsync(command.Split(' '));
        var withoutResult = await withoutContext.RunAsync(command.Split(' '));

        CliAssert.Rejected(withContext, withResult);
        CliAssert.Rejected(withoutContext, withoutResult);
        Assert.That(withoutResult.Stderr, Is.EqualTo(withResult.Stderr));
    }

    private static TestCaseData Malformed(string description, params string[] args) =>
        new TestCaseData(new object[] { args }).SetArgDisplayNames(description);

    private static TestCaseData Replaced(string rejected, string corrected, string method, string path, string json) =>
        new TestCaseData(rejected.Split(' '), corrected.Split(' '), method, path, json).SetArgDisplayNames(rejected);
}
