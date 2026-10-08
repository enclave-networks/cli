using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// Every ID is checked before any call, and one bad ID exits 2 naming it (proposed-cli-surface.md
// "ID checks"). Organisation IDs (a partner's customers included), account IDs and partner IDs are
// GUIDs. Enclave.Sdk.Api 1.1.0 puts customer and account IDs into the partner API's paths as their
// typed IDs write them (CustomersClient.CustomerRoute), so a value that is not a GUID never reaches
// a path; "../x" is the value that matters most, since a path holding it unchecked would send the
// request to another resource. A rejection alone does not show the command exists, so each ID case
// runs the corrected command in the same sandbox and expects its request at the partner API.
//
// --dry-run prints the change a partner customer command would send, with the partner in place of
// the organisation, and sends none of it ("Dry run"). No command prints the token ("Login, logout
// and status").
public class PartnerSafetyTests
{
    // None is a GUID: a path that leaves the customer's route, plain text, a number, a customer name
    // given to the ID option (the option decides how a value is read, and the CLI never guesses its
    // type, "Commands"), and a GUID with a path after it.
    private static readonly string[] OrgIdsThatAreNotGuids = ["../x", "not-a-guid", "42", TestData.CustomerName, TestData.CustomerOrgId + "/../x"];

    // As above, with an admin's email address given to --user-id, which takes only a GUID; the email
    // address goes to --user ("Commands").
    private static readonly string[] AccountIdsThatAreNotGuids = ["../x", "not-a-guid", "42", TestData.CustomerAdminEmail, TestData.PartnerStaffAccountId + "/../x"];

    private static readonly string[] PartnerIdsThatAreNotGuids = ["../x", "Acme Partners"];

    private static readonly string[] AdminVerbs = ["add-admin", "remove-admin"];

    // The dry-run report of a partner command: "partner" takes the place of "org" ("Dry run").
    private static readonly string[] DryRunFields = ["dryRun", "partner", "requests"];

    [TestCaseSource(nameof(CustomerOrgIdsThatAreNotGuids))]
    public async Task A_customer_org_id_that_is_not_a_guid_exits_2_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    [TestCaseSource(nameof(UserIdsThatAreNotGuids))]
    public async Task A_user_id_that_is_not_a_guid_exits_2_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    // No other source chooses a partner here, so the option's value is the partner in use. A CLI
    // that treated a malformed value as no value would report no_partner.
    [TestCaseSource(nameof(PartnerIdOptionsThatAreNotGuids))]
    public async Task A_partner_id_option_that_is_not_a_guid_exits_2_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubPartnerApi(run);

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    [TestCase("../x")]
    [TestCase("not-a-guid")]
    [TestCase("Acme Partners")]
    public async Task An_ENCLAVE_PARTNER_ID_that_is_not_a_guid_exits_2_before_the_partner_api(string value)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        run.Environment["ENCLAVE_PARTNER_ID"] = value;
        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"));

        run.Environment["ENCLAVE_PARTNER_ID"] = TestData.PartnerId.ToString();
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list"));
    }

    // A dry run prints the change as Enclave.Sdk.Api builds it, for the partner, and sends none of
    // it. The reads the change depends on are sent, as without --dry-run: the customer lookup for a
    // customer given by name, and the admins or invites lookup for an admin or invite given by email
    // address (proposed-cli-surface.md "Dry run"). Each command's change is one call.
    [TestCaseSource(nameof(ChangesWithDryRun))]
    public async Task A_dry_run_of_a_partner_customer_command_prints_its_change_for_the_partner_and_sends_only_the_reads_it_depends_on(string[] args, string[] reads, string change)
    {
        ArgumentNullException.ThrowIfNull(change);
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync([.. args, "--dry-run"]);

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var requests = JsonAssert.Property(output, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var url = new Uri(JsonAssert.Property(requests[0], "url").GetString()!);
        var partner = JsonAssert.Property(output, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(DryRunFields), result.ToString());
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(JsonRead.PropertyNameList(partner), Is.EqualTo("id"));
            Assert.That(JsonRead.IdOf(partner), Is.EqualTo(TestData.PartnerId));
            Assert.That($"{JsonAssert.Property(requests[0], "method").GetString()} {url.AbsolutePath}", Is.EqualTo(change));
            Assert.That(url.GetLeftPart(UriPartial.Authority), Is.EqualTo(run.PartnerApiUrl.GetLeftPart(UriPartial.Authority)));
            Assert.That(run.Calls(), Is.EqualTo(reads));
        });
    }

    // The report shows the body each change would send, every create field among them ("Create and
    // update": --dry-run shows every value sent), and "body": null for a request with none ("Dry
    // run").
    [TestCase("create Initech", "initialSystemsCount", "50")]
    [TestCase("create Initech", "initialGatewaysCount", "0")]
    [TestCase("create Initech", "adminAutoSyncIsEnabled", "false")]
    [TestCase("update --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --systems 30", "LicensedAgentsCount", "30")]
    [TestCase("convert --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --billing-months 24", "billingPeriodMonths", "24")]
    [TestCase("invite --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --email sam@globex.example", "emailAddress", "\"sam@globex.example\"")]
    [TestCase("add-admin --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --user-id 5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25", null, null)]
    [TestCase("remove-admin --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --user alex@example.com", null, null)]
    [TestCase("cancel-invite --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --email sam@globex.example", null, null)]
    [TestCase("enable-auto-sync --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b", null, null)]
    [TestCase("disable-auto-sync --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b", null, null)]
    public async Task A_dry_run_shows_the_body_the_change_would_send(string commandLine, string? field, string? json)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(["partner", "customer", .. commandLine.Split(' '), "--dry-run"]);

        CliAssert.Succeeded(result);
        var requests = JsonAssert.Property(result.StdoutJson, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var body = JsonAssert.Property(requests[0], "body");

        if (field is null)
        {
            Assert.That(body.ValueKind, Is.EqualTo(JsonValueKind.Null), result.ToString());
        }
        else
        {
            Assert.That(JsonAssert.Property(body, field).GetRawText(), Is.EqualTo(json), result.ToString());
        }
    }

    // No command prints the token, under --verbose and --dry-run either (proposed-cli-surface.md
    // "Login, logout and status"): a printed token lands in CI logs and agent transcripts, and
    // personal access tokens do not expire (portal Enclave.Accounts TokensApiController.cs:105).
    // Each token test also requires the command to succeed, so a command that printed nothing
    // because it did nothing does not pass, and to send exactly the requests the command table
    // gives, each carrying the token, which shows the CLI held the token it left out. create under
    // --dry-run is the one run that sends no request: it reads nothing first, and its change is
    // withheld ("Dry run"). The report it prints is what shows it ran.
    [TestCaseSource(nameof(CustomerCommandRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_a_partner_customer_command(string[] args, string[] calls)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        TokenAssert.Absent(result);
        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(calls), result.ToString());
            Assert.That(run.Requests.Select(request => request.Authorization), Is.All.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // --verbose adds diagnostics to stderr, each request's method and URL among them ("Details"), so
    // stderr is not empty, and it holds no error.
    [TestCaseSource(nameof(CustomerCommandRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_a_partner_customer_command_under_verbose(string[] args, string[] calls)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync([.. args, "--verbose"]);

        TokenAssert.Absent(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(result.Stderr, Is.Not.Empty);
            Assert.That(StderrErrorCodes(result), Is.Empty, result.ToString());
            Assert.That(run.Calls(), Is.EqualTo(calls), result.ToString());
            Assert.That(run.Requests.Select(request => request.Authorization), Is.All.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // partner use changes only cli.json, so it has no dry run. The partner command run afterwards,
    // with no other partner source, shows the default was saved.
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_partner_use(bool verbose)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        string[] use = ["partner", "use", "--id", TestData.PartnerId.ToString()];
        string[] args = verbose ? [.. use, "--verbose"] : use;

        var result = await run.RunAsync(args);

        TokenAssert.Absent(result);
        CliAssert.Succeeded(result);
        Assert.That(run.Requests, Is.Empty);

        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list"));
    }

    // There is no --token option ("Options on every command"), and an error never repeats the value
    // given to an unknown option, since that value could be a token passed by mistake ("Errors and
    // exit codes").
    [Test]
    public async Task A_token_given_to_an_unknown_option_is_not_repeated_in_the_error()
    {
        using var run = PartnerApiFake.StartWithPartner();

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

    // Each change, with the customer given by name and by --org-id: the reads it makes, and the
    // change it would send as "METHOD path".
    private static IEnumerable<TestCaseData> ChangesWithDryRun()
    {
        foreach (var command in PartnerCommandTests.Commands.Where(command => command.Changes))
        {
            yield return DryRunCase(command.ByName(), command.Calls(byName: true));

            if (command.TakesCustomer)
            {
                yield return DryRunCase(command.ByOrgId(TestData.CustomerOrgId), command.Calls(byName: false));
            }
        }
    }

    private static TestCaseData DryRunCase(string[] args, string[] calls) =>
        new TestCaseData(args, calls[..^1], calls[^1]).SetArgDisplayNames(string.Join(' ', args));

    // Each partner customer command alone and, where it changes something through the API, under
    // --dry-run, with the calls it sends: all of them alone, and under --dry-run the reads before
    // its change.
    private static IEnumerable<TestCaseData> CustomerCommandRuns()
    {
        foreach (var command in PartnerCommandTests.Commands)
        {
            var calls = command.Calls(byName: true);

            yield return Run(command.ByName(), calls);

            if (command.Changes)
            {
                yield return Run([.. command.ByName(), "--dry-run"], calls[..^1]);
            }
        }
    }

    private static TestCaseData Run(string[] args, string[] calls) =>
        new TestCaseData(args, calls).SetArgDisplayNames(string.Join(' ', args));

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
