using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// Every ID is checked before any call, and one bad ID exits 2 (proposed-cli-surface.md "ID
// checks"). Organisation IDs (a partner's customers included), account IDs and partner IDs are
// GUIDs. Enclave.Sdk.Api 1.0.4 puts IDs into URL paths unescaped (for example
// UnapprovedSystemsClient.cs:95) and .NET resolves ".." when it combines the path with the base
// address, so "../x" is the value that matters most: unchecked, it sends the request to another
// resource. A command that passes the checks reports not_implemented (Enclave.Sdk.Api 1.0.4 has no
// partner clients), and each ID case runs the corrected command in the same sandbox to show that,
// since a rejection alone does not show the command exists.
//
// No command prints the token ("Login, logout and status"). A partner customer command exits with
// not_implemented under --dry-run too, since there is no partner client to build the request
// ("Dry run").
[Category(TestCategory.Pending)]
public class PartnerSafetyTests
{
    private const string PartnerIdVariable = "ENCLAVE_PARTNER_ID";

    // None is a GUID: a path that leaves the customer's route, plain text, a number, a customer name
    // given to the ID option (the option decides how a value is read, and the CLI never guesses its
    // type, "Commands"), and a GUID with a path after it.
    private static readonly string[] OrgIdsThatAreNotGuids = ["../x", "not-a-guid", "42", TestData.CustomerName, TestData.CustomerOrgId + "/../x"];

    // As above, with an admin's email address given to --user-id, which takes only a GUID; the email
    // address goes to --user ("Commands").
    private static readonly string[] AccountIdsThatAreNotGuids = ["../x", "not-a-guid", "42", TestData.CustomerAdminEmail, TestData.PartnerStaffAccountId + "/../x"];

    private static readonly string[] PartnerIdsThatAreNotGuids = ["../x", "Acme Partners"];

    private static readonly string[] AdminVerbs = ["add-admin", "remove-admin"];

    [TestCaseSource(nameof(CustomerOrgIdsThatAreNotGuids))]
    public async Task A_customer_org_id_that_is_not_a_guid_exits_2_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    [TestCaseSource(nameof(UserIdsThatAreNotGuids))]
    public async Task A_user_id_that_is_not_a_guid_exits_2_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    // No other source chooses a partner here, so the option's value is the partner in use. A CLI
    // that treated a malformed value as no value would report no_partner.
    [TestCaseSource(nameof(PartnerIdOptionsThatAreNotGuids))]
    public async Task A_partner_id_option_that_is_not_a_guid_exits_2_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    [TestCase("../x")]
    [TestCase("not-a-guid")]
    [TestCase("Acme Partners")]
    public async Task An_ENCLAVE_PARTNER_ID_that_is_not_a_guid_exits_2_before_the_partner_api(string value)
    {
        using var run = CliRun.Start();

        run.Environment[PartnerIdVariable] = value;
        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"));

        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        CliAssert.NotImplemented(run, await run.RunAsync("partner", "customer", "list"));
    }

    // A dry run sends nothing, and a partner customer command has no request to print, so it
    // reports not_implemented as it does without --dry-run ("Dry run").
    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.CommandsThatChangeSomething))]
    public async Task A_dry_run_of_a_partner_customer_command_reports_not_implemented_and_sends_nothing(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync([.. args, "--dry-run"]);

        CliAssert.NotImplemented(run, result);
    }

    // No command prints the token, under --verbose and --dry-run either (proposed-cli-surface.md
    // "Login, logout and status"): a printed token lands in CI logs and agent transcripts, and
    // personal access tokens do not expire (portal Enclave.Accounts TokensApiController.cs:105).
    // Each token test also requires the outcome its command must have, so a command that printed
    // nothing because it did nothing does not pass.
    [TestCaseSource(nameof(CustomerCommandRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_a_partner_customer_command(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        TokenAssert.Absent(result);
        CliAssert.NotImplemented(run, result);
    }

    // --verbose adds diagnostics to stderr, so the not_implemented error need not be the only line
    // there.
    [TestCaseSource(nameof(CustomerCommandRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_a_partner_customer_command_under_verbose(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync([.. args, "--verbose"]);

        TokenAssert.Absent(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), result.ToString());
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(StderrErrorCodes(result), Does.Contain("not_implemented"), result.ToString());
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // partner use changes only cli.json, so it has no dry run. The partner command run afterwards,
    // with no other partner source, shows the default was saved.
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_partner_use(bool verbose)
    {
        using var run = CliRun.Start();
        string[] use = ["partner", "use", "--id", TestData.PartnerId.ToString()];
        string[] args = verbose ? [.. use, "--verbose"] : use;

        var result = await run.RunAsync(args);

        TokenAssert.Absent(result);
        CliAssert.Succeeded(result);
        Assert.That(run.Requests, Is.Empty);

        CliAssert.NotImplemented(run, await run.RunAsync("partner", "customer", "list"));
    }

    // There is no --token option ("Options on every command"), and an error never repeats the value
    // given to an unknown option, since that value could be a token passed by mistake ("Errors and
    // exit codes").
    [Test]
    public async Task A_token_given_to_an_unknown_option_is_not_repeated_in_the_error()
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync("partner", "customer", "list", "--token", TestData.Token);

        TokenAssert.Absent(result);
        CliAssert.Rejected(run, result);
    }

    private static IEnumerable<TestCaseData> CustomerOrgIdsThatAreNotGuids() =>
        from command in PartnerCommandTests.Commands
        where command.TakesCustomer
        from value in OrgIdsThatAreNotGuids
        select PartnerCommandTests.Pair(command.ByOrgId(value), command.ByOrgId(TestData.CustomerOrgId));

    private static IEnumerable<TestCaseData> UserIdsThatAreNotGuids() =>
        from verb in AdminVerbs
        from value in AccountIdsThatAreNotGuids
        select PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer(verb, "--user-id", value), PartnerCommandTests.ForCustomer(verb, "--user-id", TestData.PartnerStaffAccountId));

    private static IEnumerable<TestCaseData> PartnerIdOptionsThatAreNotGuids() =>
        from command in PartnerCommandTests.Commands
        from value in PartnerIdsThatAreNotGuids
        select PartnerCommandTests.Pair([.. command.ByName(), "--partner-id", value], [.. command.ByName(), "--partner-id", TestData.PartnerId.ToString()]);

    // Each partner customer command alone and, where it changes something through the API, under
    // --dry-run.
    private static IEnumerable<TestCaseData> CustomerCommandRuns()
    {
        foreach (var command in PartnerCommandTests.Commands)
        {
            yield return PartnerCommandTests.Case(command.ByName());

            if (command.Changes)
            {
                yield return PartnerCommandTests.Case([.. command.ByName(), "--dry-run"]);
            }
        }
    }

    // The error codes of the stderr lines that are CLI errors, { "error": { "code", ... } }. The form
    // of --verbose diagnostics is not part of the CLI contract, so lines that are not JSON are
    // skipped.
    private static string[] StderrErrorCodes(CliResult result)
    {
        var codes = new List<string>();

        foreach (var line in result.Stderr.Split('\n'))
        {
            JsonElement json;

            try
            {
                json = JsonRead.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String)
            {
                codes.Add(code.GetString()!);
            }
        }

        return [.. codes];
    }
}
