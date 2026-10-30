using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// spec/cli-surface.md "Context". The partner in use shows in the request path,
// /partner/{partnerId}/ (Enclave.Sdk.Api PartnerClient).
public class PartnerContextTests
{
    private const string PartnerIdOption = "--partner-id";

    private const string PartnerIdVariable = "ENCLAVE_PARTNER_ID";

    private const string PartnerUse = "partner use";

    private const string SavedDefault = "cli.json";

    private static readonly Guid OtherPartnerId = new("0c5e7a91-2b4d-4f86-b3a0-9d1e6c48f2b7");

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    private static readonly string[] CustomerListOnly = [$"GET {TestData.PartnerPath("customers")}"];

    // Naming both ways lets an agent recover without reading help. CliRun sets ENCLAVE_ORG_ID, which
    // must not stand in for the partner.
    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.CommandsByName))]
    public async Task Every_partner_customer_command_exits_2_no_partner_naming_the_partner_id_option_and_partner_use_when_no_partner_is_chosen(string[] args)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result, "no_partner");
        Assert.That(result.Error.ToString(), Does.Contain("--partner-id").And.Contain("partner use"));
    }

    [TestCase(PartnerIdOption)]
    [TestCase(PartnerIdVariable)]
    [TestCase(PartnerUse)]
    public async Task A_partner_chosen_by_any_one_source_is_the_partner_a_partner_command_calls_the_api_for(string source)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        var options = await ChooseTestPartnerAsync(run, source);

        var result = await run.RunAsync(["partner", "customer", "list", .. options]);

        PartnerApiFake.AssertSentToThePartnerApi(run, result);
    }

    // A personal access token cannot look partners up ("Partner API"). With no organisation chosen
    // and two visible to the token, a CLI that resolved the organisation would call the API. The
    // partner command afterwards shows the saved default is used.
    [Test]
    public async Task Partner_use_saves_the_partner_in_cli_json_and_prints_its_id_without_calling_the_api()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        var use = await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString());

        CliAssert.Succeeded(use);
        var printed = use.StdoutJson;
        var saved = Saved(run, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(printed), Is.EqualTo("id"), use.ToString());
            Assert.That(JsonRead.IdOf(printed), Is.EqualTo(TestData.PartnerId), use.ToString());
            Assert.That(JsonRead.PropertyNameList(saved), Is.EqualTo("id"), saved.ToString());
            Assert.That(JsonRead.IdOf(saved), Is.EqualTo(TestData.PartnerId), saved.ToString());
            Assert.That(use.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });

        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list"));
    }

    [Test]
    public async Task Partner_use_replaces_the_saved_partner()
    {
        using var run = CliRun.Start();

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", OtherPartnerId.ToString()));
        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));

        var saved = Saved(run, "partner");
        Assert.That(JsonRead.IdOf(saved), Is.EqualTo(TestData.PartnerId), saved.ToString());
    }

    // Looking partners up needs the ReadPartnerList scope, which personal access tokens cannot carry
    // ("Partner API").
    [TestCase("Acme Partners")]
    [TestCase("not-a-guid")]
    [TestCase("../x")]
    public async Task Partner_use_exits_2_and_saves_nothing_for_an_id_that_is_not_a_guid(string value)
    {
        using var run = CliRun.Start();

        CliAssert.Rejected(run, await run.RunAsync("partner", "use", "--id", value));
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.False);

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
    }

    // An ID is never a positional argument ("Commands").
    [TestCase("partner", "use", "9e2d4f61-3c8b-4a05-b7e9-1d6a0c5f2b48")]
    [TestCase("partner", "use")]
    public async Task Partner_use_takes_the_partner_only_through_the_id_option(params string[] args)
    {
        using var run = CliRun.Start();

        CliAssert.Rejected(run, await run.RunAsync(args));
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.False);

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
    }

    // The lower source holds a valid ID, so only a CLI that reads the option first sees the
    // malformed value. The second run shows the request goes to the option's partner.
    [TestCase(PartnerIdVariable)]
    [TestCase(PartnerUse)]
    public async Task The_partner_id_option_is_used_over_ENCLAVE_PARTNER_ID_and_the_saved_partner(string lowerSource)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        PartnerApiFake.StubCustomerList(run, OtherPartnerId);
        _ = await ChooseTestPartnerAsync(run, lowerSource);

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", "../x"));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list", "--partner-id", OtherPartnerId.ToString()), OtherPartnerId);
    }

    // The saved partner is valid, so only a CLI that reads ENCLAVE_PARTNER_ID first sees the
    // malformed value.
    [Test]
    public async Task ENCLAVE_PARTNER_ID_is_used_over_the_saved_partner()
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        PartnerApiFake.StubCustomerList(run, OtherPartnerId);
        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));

        run.Environment[PartnerIdVariable] = "not-a-guid";
        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"));

        run.Environment[PartnerIdVariable] = OtherPartnerId.ToString();
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list"), OtherPartnerId);
    }

    // "Details". The first run shows the malformed source fails the command when it is read.
    [TestCase(PartnerIdOption, PartnerIdVariable)]
    [TestCase(PartnerIdOption, SavedDefault)]
    [TestCase(PartnerIdVariable, SavedDefault)]
    public async Task A_malformed_partner_source_is_not_read_when_a_higher_one_is_given(string higher, string lower)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        SetMalformedPartner(run, lower);

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"));

        var options = await ChooseTestPartnerAsync(run, higher);
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(["partner", "customer", "list", .. options]));
    }

    // With no other source it leaves no partner chosen, and it does not hide the saved default
    // ("Login, logout and status").
    [Test]
    public async Task An_empty_ENCLAVE_PARTNER_ID_counts_as_unset()
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        run.Environment[PartnerIdVariable] = string.Empty;

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"), "no_partner");

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list"));
    }

    // "Errors and exit codes". Each step removes the error the step before reported, so each check
    // is shown ahead of the next.
    [TestCaseSource(nameof(ArgumentErrors))]
    public async Task Checks_run_in_the_order_arguments_token_partner_then_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubPartnerApi(run);
        run.Environment.Remove("ENCLAVE_TOKEN");

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.Rejected(run, await run.RunAsync(corrected), "token_missing");

        run.Environment["ENCLAVE_TOKEN"] = TestData.Token;
        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.Rejected(run, await run.RunAsync(corrected), "no_partner");

        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.CommandsByName))]
    public async Task Every_partner_customer_command_exits_3_token_missing_without_a_token(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result, "token_missing");
    }

    // --partner-id is an argument, so it is checked with the arguments, ahead of the token ("ID
    // checks").
    [Test]
    public async Task A_partner_id_option_that_is_not_a_guid_is_reported_before_a_missing_token()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", "../x"));
        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString()), "token_missing");
    }

    // With no organisation chosen the token sees two, so a CLI that resolved the organisation would
    // exit 2 no_org.
    [Test]
    public async Task A_partner_customer_command_needs_no_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        BothOrgs.Stub(run);
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(CustomerListOnly));
    }

    // ENCLAVE_ORG names an organisation the token does not see, so a lookup would fail the command.
    [Test]
    public async Task A_partner_customer_command_does_not_look_up_the_organisation_ENCLAVE_ORG_names()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Environment["ENCLAVE_ORG"] = "Initrode";
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        BothOrgs.Stub(run);
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(CustomerListOnly));
    }

    // A CLI that used the partner ID as the organisation would call /org/<partnerId>/systems.
    [TestCase(PartnerIdVariable)]
    [TestCase(PartnerUse)]
    public async Task A_partner_is_not_used_as_the_organisation(string source)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        _ = await ChooseTestPartnerAsync(run, source);
        var before = run.Requests.Count;

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));
    }

    [TestCase("dns", "show", "dns")]
    public async Task The_partner_id_option_is_unknown_to_an_organisation_command(string noun, string verb, string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));
        run.Stub("GET", TestData.OrgPath("dns"), json: ApiJson.DnsSummary());

        _ = await CliAssert.RejectedThenAcceptedAsync(run, [noun, verb, PartnerIdOption, TestData.PartnerId.ToString()], [noun, verb], "GET", TestData.OrgPath(pathSuffix));
    }

    [Test]
    public async Task A_saved_organisation_is_not_used_as_the_partner()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", TestData.OrgId.ToString()));
        var before = run.Requests.Count;

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Failed(result, "no_partner");
        Assert.That(run.Requests, Has.Count.EqualTo(before));
    }

    // The token sees two organisations, so if partner use lost the saved organisation, system list
    // would exit 2 no_org.
    [Test]
    public async Task Partner_use_keeps_the_saved_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));

        CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", TestData.OrgId.ToString()));
        var orgBefore = Compact(Saved(run, "org"));
        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
        var before = run.Requests.Count;
        var systems = await run.RunAsync("system", "list");

        CliAssert.Succeeded(systems);
        var org = Saved(run, "org");
        var partner = Saved(run, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(Compact(org), Is.EqualTo(orgBefore));
            Assert.That(JsonRead.IdOf(partner), Is.EqualTo(TestData.PartnerId), partner.ToString());
            Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {TestData.OrgPath("systems")}" }));
        });
    }

    // If org use lost the saved partner, the partner command would exit 2 no_partner.
    [Test]
    public async Task Org_use_keeps_the_saved_partner()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
        var partnerBefore = Compact(Saved(run, "partner"));
        CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", TestData.OtherOrgId.ToString()));
        var before = run.Requests.Count;
        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Succeeded(result);
        var partner = Saved(run, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(Compact(partner), Is.EqualTo(partnerBefore));
            Assert.That(run.Calls(before), Is.EqualTo(CustomerListOnly));
        });
    }

    // One bad argument of each kind.
    private static IEnumerable<TestCaseData> ArgumentErrors()
    {
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("convert", "--billing-months", "6"), PartnerCommandTests.ForCustomer("convert", "--billing-months", "12"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.Line("list", "--no-such-option"), PartnerCommandTests.Line("list"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("update"), PartnerCommandTests.ForCustomer("update", "--systems", "30"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("update", "--hard-limit", "--no-hard-limit"), PartnerCommandTests.ForCustomer("update", "--hard-limit"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("show", "--org-id", TestData.CustomerOrgId), PartnerCommandTests.ForCustomer("show"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("add-admin", "--user", TestData.CustomerAdminEmail), PartnerCommandTests.ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.Line("show", "--org-id", "../x"), PartnerCommandTests.Line("show", "--org-id", TestData.CustomerOrgId));
    }

    // Chooses the test partner through source and returns the options a partner command then needs,
    // which are empty for every source but --partner-id.
    private static async Task<string[]> ChooseTestPartnerAsync(CliRun run, string source)
    {
        var partnerId = TestData.PartnerId.ToString();

        switch (source)
        {
            case PartnerIdOption:
                return [PartnerIdOption, partnerId];

            case PartnerIdVariable:
                run.Environment[PartnerIdVariable] = partnerId;
                return [];

            case PartnerUse:
                CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", partnerId));
                return [];

            default:
                throw new ArgumentException($"\"{source}\" is not a way to choose a partner.", nameof(source));
        }
    }

    private static void SetMalformedPartner(CliRun run, string source)
    {
        switch (source)
        {
            case PartnerIdVariable:
                run.Environment[PartnerIdVariable] = "not-a-guid";
                break;

            // partner use rejects a malformed ID, so only a hand edit could save one.
            case SavedDefault:
                run.Files.WriteText(run.CliConfigPath, """{ "partner": { "id": "not-a-guid" } }""", privateToUser: false);
                break;

            default:
                throw new ArgumentException($"\"{source}\" is not a partner source that can hold a malformed ID.", nameof(source));
        }
    }

    // The named default in cli.json. Fails the test when the file or the default is missing.
    private static JsonElement Saved(CliRun run, string name)
    {
        var text = run.Files.ReadText(run.CliConfigPath);

        Assert.That(text, Is.Not.Null, $"Expected {run.CliConfigPath} to exist.");

        var settings = JsonRead.Parse(text!);

        Assert.That(settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(name, out _), Is.True, $"Expected \"{name}\" in cli.json: {text}");

        return settings.GetProperty(name);
    }

    // Whitespace is not part of a saved default, so a default rewritten with other formatting
    // compares equal.
    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);
}
