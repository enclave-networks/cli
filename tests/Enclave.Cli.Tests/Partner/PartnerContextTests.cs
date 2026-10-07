using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// The partner is chosen by --partner-id, then ENCLAVE_PARTNER_ID, then the default `partner use
// --id` saves in ~/.enclave/cli.json. The organisation is chosen separately: partner commands use
// the partner, and every other command that acts within an organisation uses the organisation
// (proposed-cli-surface.md "Context"). A partner customer command that has a partner and a token
// reports not_implemented (Enclave.Sdk.Api 1.0.4 has no partner clients), so the tests observe the
// choice through whether a command gets past the context check, which source's value the ID check
// sees, and what cli.json holds.
[Category(TestCategory.Pending)]
public class PartnerContextTests
{
    private const string PartnerIdOption = "--partner-id";

    private const string PartnerIdVariable = "ENCLAVE_PARTNER_ID";

    private const string PartnerUse = "partner use";

    private const string SavedDefault = "cli.json";

    private static readonly Guid OtherPartnerId = new("0c5e7a91-2b4d-4f86-b3a0-9d1e6c48f2b7");

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    // With no partner chosen, a partner command exits 2 with an error naming both ways to choose
    // one ("Context"), so an agent can recover without reading help. CliRun sets ENCLAVE_ORG_ID, and
    // the organisation does not stand in for the partner.
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
    public async Task A_partner_chosen_by_any_one_source_takes_a_partner_command_past_the_context_check(string source)
    {
        using var run = CliRun.Start();
        var options = await ChooseTestPartnerAsync(run, source);

        var result = await run.RunAsync(["partner", "customer", "list", .. options]);

        CliAssert.NotImplemented(run, result);
    }

    // partner use records an ID and makes no API call: a personal access token cannot look partners
    // up ("Partner API"). It saves { "partner": { "id" } } in cli.json and prints { "id" } ("Login,
    // logout and status"). It needs no organisation either, so with none chosen and two visible to
    // the token it still makes no call. The partner command run afterwards shows the saved default
    // is the one used.
    [Test]
    public async Task Partner_use_saves_the_partner_in_cli_json_and_prints_its_id_without_calling_the_api()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        StubTwoOrgs(run);

        var use = await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString());

        CliAssert.Succeeded(use);
        var printed = use.StdoutJson;
        var saved = Saved(run, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(printed), Is.EqualTo("id"), use.ToString());
            Assert.That(IdOf(printed), Is.EqualTo(TestData.PartnerId), use.ToString());
            Assert.That(JsonRead.PropertyNameList(saved), Is.EqualTo("id"), saved.ToString());
            Assert.That(IdOf(saved), Is.EqualTo(TestData.PartnerId), saved.ToString());
            Assert.That(use.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });

        CliAssert.NotImplemented(run, await run.RunAsync("partner", "customer", "list"));
    }

    // The saved default is one partner, so a second partner use replaces the first.
    [Test]
    public async Task Partner_use_replaces_the_saved_partner()
    {
        using var run = CliRun.Start();

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", OtherPartnerId.ToString()));
        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));

        var saved = Saved(run, "partner");
        Assert.That(IdOf(saved), Is.EqualTo(TestData.PartnerId), saved.ToString());
    }

    // A partner is given by its ID, a GUID ("Context", "ID checks"): looking partners up needs the
    // ReadPartnerList scope, which personal access tokens cannot carry ("Partner API"). A rejected
    // value saves nothing, and the corrected run shows the command exists.
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

    // An ID is never a positional argument ("Commands": partner use --id <partnerId>), and without
    // one partner use has nothing to save.
    [TestCase("partner", "use", "9e2d4f61-3c8b-4a05-b7e9-1d6a0c5f2b48")]
    [TestCase("partner", "use")]
    public async Task Partner_use_takes_the_partner_only_through_the_id_option(params string[] args)
    {
        using var run = CliRun.Start();

        CliAssert.Rejected(run, await run.RunAsync(args));
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.False);

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
    }

    // partner use changes only cli.json, and the commands that change only local files do not take
    // --dry-run ("Dry run"), so the option is unknown to it and exits 2 ("Details").
    [Test]
    public async Task Partner_use_does_not_take_dry_run()
    {
        using var run = CliRun.Start();

        CliAssert.Rejected(run, await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString(), "--dry-run"));
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.False);

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
    }

    // The plural noun is a hidden alias for the singular ("Shape and naming").
    [Test]
    public async Task Partners_use_saves_the_partner_as_partner_use_does()
    {
        using var run = CliRun.Start();

        var use = await run.RunAsync("partners", "use", "--id", TestData.PartnerId.ToString());

        CliAssert.Succeeded(use);
        var saved = Saved(run, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(IdOf(saved), Is.EqualTo(TestData.PartnerId), saved.ToString());
        });
    }

    // --partner-id comes first ("Context"), so its value is the partner in use and the value the ID
    // check sees. The lower source holds a valid ID, which a CLI that preferred it would use and go
    // on to report not_implemented. The corrected run shows the option exists.
    [TestCase(PartnerIdVariable)]
    [TestCase(PartnerUse)]
    public async Task The_partner_id_option_is_used_over_ENCLAVE_PARTNER_ID_and_the_saved_partner(string lowerSource)
    {
        using var run = CliRun.Start();
        _ = await ChooseTestPartnerAsync(run, lowerSource);

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", "../x"));
        CliAssert.NotImplemented(run, await run.RunAsync("partner", "customer", "list", "--partner-id", OtherPartnerId.ToString()));
    }

    // ENCLAVE_PARTNER_ID comes before the saved default ("Context"), which lets each agent session
    // fix its partner without writing the shared cli.json. The saved partner is valid, so only a CLI
    // that reads ENCLAVE_PARTNER_ID first sees the malformed value.
    [Test]
    public async Task ENCLAVE_PARTNER_ID_is_used_over_the_saved_partner()
    {
        using var run = CliRun.Start();
        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));

        run.Environment[PartnerIdVariable] = "not-a-guid";
        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"));

        run.Environment[PartnerIdVariable] = OtherPartnerId.ToString();
        CliAssert.NotImplemented(run, await run.RunAsync("partner", "customer", "list"));
    }

    // A context source lower in the precedence is not read when a higher one is given, so a
    // malformed lower source does not fail the command ("Details"). The first run shows the
    // malformed source fails the command when it is the source read.
    [TestCase(PartnerIdOption, PartnerIdVariable)]
    [TestCase(PartnerIdOption, SavedDefault)]
    [TestCase(PartnerIdVariable, SavedDefault)]
    public async Task A_malformed_partner_source_is_not_read_when_a_higher_one_is_given(string higher, string lower)
    {
        using var run = CliRun.Start();
        SetMalformedPartner(run, lower);

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"));

        var options = await ChooseTestPartnerAsync(run, higher);
        CliAssert.NotImplemented(run, await run.RunAsync(["partner", "customer", "list", .. options]));
    }

    // An empty environment variable counts as unset ("Login, logout and status"): with no other
    // source it leaves no partner chosen, and it does not hide the saved default.
    [Test]
    public async Task An_empty_ENCLAVE_PARTNER_ID_counts_as_unset()
    {
        using var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = string.Empty;

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"), "no_partner");

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
        CliAssert.NotImplemented(run, await run.RunAsync("partner", "customer", "list"));
    }

    // Checks run in this order: arguments (exit 2), the token (3), the partner (2), then the call,
    // and a command with bad arguments fails the same way with or without a token ("Errors and exit
    // codes"). Each run removes the error the run before it reported, so each check is shown
    // reporting ahead of the next one in the order.
    [TestCaseSource(nameof(ArgumentErrors))]
    public async Task Checks_run_in_the_order_arguments_token_partner_then_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.Rejected(run, await run.RunAsync(corrected), "token_missing");

        run.Environment["ENCLAVE_TOKEN"] = TestData.Token;
        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.Rejected(run, await run.RunAsync(corrected), "no_partner");

        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        CliAssert.NotImplemented(run, await run.RunAsync(corrected));
    }

    // The token is checked before the call, so a partner customer command that has its partner but
    // no token exits 3 and does not reach not_implemented ("Errors and exit codes").
    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.CommandsByName))]
    public async Task Every_partner_customer_command_exits_3_token_missing_without_a_token(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result, "token_missing");
    }

    // --partner-id is an argument, and every ID is checked before any call ("ID checks"), so a
    // malformed one is reported with the arguments, ahead of the missing token.
    [Test]
    public async Task A_partner_id_option_that_is_not_a_guid_is_reported_before_a_missing_token()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", "../x"));
        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString()), "token_missing");
    }

    // Partner commands use the partner and never the organisation ("Context"). With no organisation
    // chosen the token sees two, so a CLI that resolved the organisation would call /account/orgs
    // and exit 2 no_org.
    [Test]
    public async Task A_partner_customer_command_needs_no_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        StubTwoOrgs(run);

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.NotImplemented(run, result);
    }

    // ENCLAVE_ORG names an organisation the token does not see, which stops any command that looks
    // the organisation up; a partner command never does.
    [Test]
    public async Task A_partner_customer_command_does_not_look_up_the_organisation_ENCLAVE_ORG_names()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Environment["ENCLAVE_ORG"] = "Initrode";
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        StubTwoOrgs(run);

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.NotImplemented(run, result);
    }

    // Choosing a partner does not choose an organisation: with no organisation chosen, an
    // organisation command looks the token's organisations up and, with two, exits 2 no_org
    // ("Context"). A CLI that used the partner ID as the organisation would call
    // /org/<partnerId>/systems.
    [TestCase(PartnerIdVariable)]
    [TestCase(PartnerUse)]
    public async Task A_partner_is_not_used_as_the_organisation(string source)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        StubTwoOrgs(run);
        _ = await ChooseTestPartnerAsync(run, source);
        var before = run.Requests.Count;

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));
    }

    // --partner-id applies to partner commands only, and an option a command does not take is
    // unknown to it and exits 2 ("Options on every command", "Details"). The corrected run shows
    // the command exists and sends its request.
    [TestCase("system", "list", "systems")]
    [TestCase("dns", "show", "dns")]
    public async Task The_partner_id_option_is_unknown_to_an_organisation_command(string noun, string verb, string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));
        run.Stub("GET", TestData.OrgPath("dns"), json: ApiJson.DnsSummary());

        _ = await CliAssert.RejectedThenAcceptedAsync(run, [noun, verb, PartnerIdOption, TestData.PartnerId.ToString()], [noun, verb], "GET", TestData.OrgPath(pathSuffix));
    }

    // A saved organisation does not choose a partner either, so the partner command finds none.
    [Test]
    public async Task A_saved_organisation_is_not_used_as_the_partner()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        StubTwoOrgs(run);
        CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", TestData.OrgId.ToString()));
        var before = run.Requests.Count;

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Failed(result, "no_partner");
        Assert.That(run.Requests, Has.Count.EqualTo(before));
    }

    // org use and partner use save their defaults side by side in cli.json, and each leaves the
    // other's as it is ("Login, logout and status"). The token sees two organisations, so if
    // partner use lost the saved organisation, system list would look organisations up and exit 2
    // no_org; with the saved ID it makes no lookup call ("Context").
    [Test]
    public async Task Partner_use_keeps_the_saved_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        StubTwoOrgs(run);
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
            Assert.That(IdOf(partner), Is.EqualTo(TestData.PartnerId), partner.ToString());
            Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {TestData.OrgPath("systems")}" }));
        });
    }

    // The partner is saved first and the organisation after it, so if org use lost the saved
    // partner, the partner command would exit 2 no_partner.
    [Test]
    public async Task Org_use_keeps_the_saved_partner()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        StubTwoOrgs(run);

        CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
        var partnerBefore = Compact(Saved(run, "partner"));
        CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", TestData.OtherOrgId.ToString()));
        var before = run.Requests.Count;
        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Failed(result, "not_implemented");
        var partner = Saved(run, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(Compact(partner), Is.EqualTo(partnerBefore));
            Assert.That(run.Requests, Has.Count.EqualTo(before));
        });
    }

    // One bad argument of each kind: a value the command does not accept, an unknown option, an
    // update with no change, contradicting flags, a name with its ID option, an email address given
    // where only an account ID is taken, and a malformed ID.
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

    /// <summary>
    /// Chooses the test partner through <paramref name="source"/> and returns the options a partner
    /// command then needs: --partner-id for that source, none for the others.
    /// </summary>
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

            // partner use rejects a malformed ID, so the file is written in the layout partner use
            // saves ("Login, logout and status") with a value only a hand edit could put there.
            case SavedDefault:
                run.Files.WriteText(run.CliConfigPath, """{ "partner": { "id": "not-a-guid" } }""", privateToUser: false);
                break;

            default:
                throw new ArgumentException($"\"{source}\" is not a partner source that can hold a malformed ID.", nameof(source));
        }
    }

    // A command that acts within an organisation and has none chosen makes one lookup call, and
    // with several organisations exits 2 no_org ("Context").
    private static void StubTwoOrgs(CliRun run) =>
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));

    /// <summary>
    /// The named default in cli.json, which holds { "org": { "id", "name" }, "partner": { "id" } }
    /// ("Login, logout and status"). Fails the test when the file or the default is missing.
    /// </summary>
    private static JsonElement Saved(CliRun run, string name)
    {
        var text = run.Files.ReadText(run.CliConfigPath);

        Assert.That(text, Is.Not.Null, $"Expected {run.CliConfigPath} to exist.");

        var settings = JsonRead.Parse(text!);

        Assert.That(settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty(name, out _), Is.True, $"Expected \"{name}\" in cli.json: {text}");

        return settings.GetProperty(name);
    }

    // The ID is compared as a GUID, so the check holds whichever GUID form the CLI writes.
    private static Guid? IdOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("id", out var id)
        && id.ValueKind == JsonValueKind.String
        && Guid.TryParse(id.GetString(), CultureInfo.InvariantCulture, out var guid)
            ? guid
            : null;

    // Whitespace is not part of a saved default, so a default rewritten with other formatting
    // compares equal.
    private static string Compact(JsonElement element) => JsonSerializer.Serialize(element);
}
