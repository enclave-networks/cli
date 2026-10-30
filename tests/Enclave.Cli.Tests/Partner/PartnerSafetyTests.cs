using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// spec/cli-surface.md "ID checks", "Dry run", "Login, logout and status". "../x" matters most:
// unchecked in a path, it sends the request to another resource. Each ID case runs the corrected
// command after the rejected one, since an unknown command is rejected the same way.
public class PartnerSafetyTests
{
    // The customer name checks that the option decides how a value is read ("Commands").
    private static readonly string[] OrgIdsThatAreNotGuids = ["../x", "not-a-guid", "42", TestData.CustomerName, TestData.CustomerOrgId + "/../x"];

    // The email address belongs to --user ("Commands").
    private static readonly string[] AccountIdsThatAreNotGuids = ["../x", "not-a-guid", "42", TestData.CustomerAdminEmail, TestData.PartnerStaffAccountId + "/../x"];

    private static readonly string[] PartnerIdsThatAreNotGuids = ["../x", "Acme Partners"];

    private static readonly string[] AdminVerbs = ["add-admin", "remove-admin"];

    // "partner" takes the place of "org" ("Dry run").
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

    // No other source chooses a partner, so a CLI that treated a malformed value as no value would
    // report no_partner.
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

    // The reads are the customer lookup and the admins or invites lookup, which the change depends
    // on ("Dry run").
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

    // "body" is null for a request with none ("Dry run").
    [TestCase("create Initech", "initialSystemsCount", 50)]
    [TestCase("create Initech", "initialGatewaysCount", 0)]
    [TestCase("create Initech", "adminAutoSyncIsEnabled", false)]
    [TestCase("update --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --systems 30", "LicensedAgentsCount", 30)]
    [TestCase("convert --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --billing-months 24", "billingPeriodMonths", 24)]
    [TestCase("invite --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --email sam@globex.example", "emailAddress", "sam@globex.example")]
    [TestCase("add-admin --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --user-id 5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25", null, null)]
    [TestCase("remove-admin --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --user alex@example.com", null, null)]
    [TestCase("cancel-invite --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b --email sam@globex.example", null, null)]
    [TestCase("enable-auto-sync --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b", null, null)]
    [TestCase("disable-auto-sync --org-id 6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b", null, null)]
    public async Task A_dry_run_shows_the_body_the_change_would_send(string commandLine, string? field, object? value)
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
            Assert.That(JsonAssert.Property(body, field).GetRawText(), Is.EqualTo(JsonSerializer.Serialize(value)), result.ToString());
        }
    }

    // A printed token lands in CI logs and agent transcripts, and does not expire (portal
    // TokensApiController.Create). Each run must succeed and send its requests with the token, so a
    // command that printed nothing because it did nothing does not pass. create under --dry-run
    // sends nothing; its report shows it ran. stderr holds the diagnostics, each request among them
    // ("Details").
    [TestCaseSource(nameof(CustomerCommandRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_a_partner_customer_command(string[] args, string[] calls, string? withheldChange)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        TokenAssert.Absent(result);
        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Not.Empty, result.ToString());
            Assert.That(run.Calls(), Is.EqualTo(calls), result.ToString());
            Assert.That(run.Requests.Select(request => request.Authorization), Is.All.EqualTo($"Bearer {TestData.Token}"));
        });
        AssertReportedChange(result, withheldChange);
    }

    // partner use has no dry run. The partner command afterwards shows the default was saved.
    [Test]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_partner_use()
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        var result = await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString());

        TokenAssert.Absent(result);
        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics, Is.Not.Empty, result.ToString());
            Assert.That(run.Requests, Is.Empty);
        });

        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list"));
    }

    // The value of an unknown option could be a token passed by mistake ("Errors and exit codes").
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

    // Each change with the customer by name and by --org-id: its reads, and its change as "METHOD
    // path".
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

    // Each command alone, and under --dry-run where it changes something: the calls it sends, and
    // the change it withholds as "METHOD path".
    private static IEnumerable<TestCaseData> CustomerCommandRuns()
    {
        foreach (var command in PartnerCommandTests.Commands)
        {
            var calls = command.Calls(byName: true);

            yield return Run(command.ByName(), calls, null);

            if (command.Changes)
            {
                yield return Run([.. command.ByName(), "--dry-run"], calls[..^1], calls[^1]);
            }
        }
    }

    private static TestCaseData Run(string[] args, string[] calls, string? withheldChange) =>
        new TestCaseData(args, calls, withheldChange).SetArgDisplayNames(string.Join(' ', args));

    // A run without --dry-run prints the command's own output, which has no report.
    private static void AssertReportedChange(CliResult result, string? withheldChange)
    {
        if (withheldChange is null)
        {
            return;
        }

        var output = result.StdoutJson;
        var requests = JsonAssert.Property(output, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var url = new Uri(JsonAssert.Property(requests[0], "url").GetString()!);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True, result.ToString());
            Assert.That($"{JsonAssert.Property(requests[0], "method").GetString()} {url.AbsolutePath}", Is.EqualTo(withheldChange), result.ToString());
        });
    }
}
