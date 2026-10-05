using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enclave.Cli.Tests.Contract;

// System.CommandLine's default parse-error action writes plain text and help and exits 1
// (ParseErrorAction, version 2.0.12). The proposal replaces it ("Errors and exit codes"): a parse
// error is one JSON error on stderr with code invalid_argument, nothing on stdout, exit 2, and any
// suggestion in "detail".
public class ParseErrorTests
{
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "PATCH", "DELETE"];

    public static IEnumerable<TestCaseData> MalformedCommands()
    {
        yield return Malformed("an unknown option", "system", "list", "--bogus");
        yield return Malformed("an unknown noun", "widget", "list");
        yield return Malformed("an unknown verb", "system", "frobnicate");
        yield return Malformed("an unexpected argument", "system", "list", "extra");
        yield return Malformed("a noun without a verb", "system");
        yield return Malformed("no command", []);
        yield return Malformed("a missing argument", "system", "show");
        yield return Malformed("a missing second argument", "partner", "customer", "admin", "add", TestData.OrgId.ToString());
        yield return Malformed("a missing required option", "key", "create");
        yield return Malformed("a non-numeric limit", "system", "list", "--limit", "many");
        yield return Malformed("an unknown enum value", "system", "list", "--sort", "Newest");
        yield return Malformed("an unknown output format", "system", "list", "-o", "yaml");

        // --dry-run and --yes exist only on commands that change something, --limit and --all only
        // on list, and --partner only on partner commands; elsewhere they are unknown options.
        yield return Malformed("dry run on a command that changes nothing", "system", "list", "--dry-run");
        yield return Malformed("yes on a command that changes nothing", "system", "show", "SYS7001", "--yes");
        yield return Malformed("all on a command other than list", "system", "show", "SYS7001", "--all");
        yield return Malformed("limit on a command other than list", "system", "show", "SYS7001", "--limit", "5");
        yield return Malformed("partner on a command outside partner", "system", "list", "--partner", TestData.PartnerId.ToString());
    }

    // The API answers every path these commands could reach, so a CLI that sends a request despite
    // the parse error gets a reply, and the request log shows the call.
    [TestCaseSource(nameof(MalformedCommands))]
    public async Task Parse_errors_exit_2_with_one_invalid_argument_error_and_no_help(string[] args)
    {
        using var run = CliRun.Start();
        StubEverything(run);

        var result = await run.RunAsync(args);

        AssertParseError(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Stderr, Does.Not.Contain("Usage:"));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // An agent that mistypes a name corrects it from the suggestion, with no further call to
    // `commands`.
    [TestCase("system lst", "list")]
    [TestCase("sytem list", "system")]
    [TestCase("system list --serch web", "--search")]
    [TestCase("dns zone shwo 7001", "show")]
    public async Task A_mistyped_name_carries_the_suggestion_in_detail(string command, string suggestion)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        StubEverything(run);

        var result = await run.RunAsync(command.Split(' '));

        var error = AssertParseError(result);
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("detail").GetString(), Does.Contain(suggestion));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // The error names what was wrong, so the caller can tell which argument to fix.
    [Test]
    public async Task An_unknown_option_is_named_in_detail()
    {
        using var run = CliRun.Start();
        StubEverything(run);

        var result = await run.RunAsync("system", "list", "--bogus");

        var error = AssertParseError(result);
        Assert.That(error.GetProperty("detail").GetString(), Does.Contain("--bogus"));
    }

    // Arguments are checked before the token is looked for: a malformed command is wrong whatever
    // the token, and reporting token_missing would send the agent to fix the wrong thing.
    [Test]
    public async Task Parse_errors_are_reported_before_the_token_is_looked_for()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG");

        var result = await run.RunAsync("system", "lst");

        AssertParseError(result);
        Assert.That(run.Requests, Is.Empty);
    }

    private static TestCaseData Malformed(string description, params string[] args) =>
        new TestCaseData(new object[] { args }).SetArgDisplayNames(description);

    private static void StubEverything(CliRun run)
    {
        foreach (var method in HttpMethods)
        {
            run.Api
                .Given(Request.Create().UsingMethod(method).WithPath(new WildcardMatcher("*")))
                .AtPriority(100)
                .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody(ApiJson.Page()));
        }
    }

    private static JsonElement AssertParseError(CliResult result)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
        });

        var error = result.Error;
        Assert.That(error.GetProperty("code").GetString(), Is.EqualTo("invalid_argument"));
        return error;
    }
}
