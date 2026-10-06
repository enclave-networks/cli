using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// The partner is chosen by --partner, then ENCLAVE_PARTNER, then the default `partner use` saves in
// ~/.enclave/cli.json, separately from the organisation (proposal, "Context"). Partner commands report not_implemented
// whichever partner they use, so the tests observe the choice through `status`, which reports the partner in use and
// where it came from, and through whether a partner command gets past the context check.
[Category(TestCategory.Pending)]
public class PartnerContextTests
{
    private static readonly Guid OtherPartnerId = new("0c5e7a91-2b4d-4f86-b3a0-9d1e6c48f2b7");

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.ApiCommandsThatNeedAPartner))]
    public async Task Partner_commands_exit_2_no_partner_naming_the_partner_option_and_partner_use_when_no_partner_is_chosen(
        string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);

        var result = await PartnerCommandTests.RunAsync(run, args);

        PartnerCommandTests.AssertError(run, result, 2, "no_partner");

        // The error names both ways to choose a partner, so an agent can recover without reading help.
        Assert.That(result.Error.ToString(), Does.Contain("--partner").And.Contain("partner use"));
    }

    // `partner list` is how a partner's staff find their partner IDs, so it cannot need a partner chosen first.
    [Test]
    public async Task Partner_list_needs_no_partner()
    {
        using var run = CliRun.Start();
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);

        var result = await run.RunAsync("partner", "list");

        PartnerCommandTests.AssertNotImplemented(run, result);
    }

    // Each source on its own is enough to choose a partner: with nothing else set, the command gets past the context
    // check to the partner API.
    [TestCase("--partner")]
    [TestCase(PartnerCommandTests.PartnerVariable)]
    [TestCase("partner use")]
    public async Task A_partner_chosen_by_any_one_source_lets_partner_commands_past_the_context_check(string source)
    {
        using var run = CliRun.Start();
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);
        var partnerId = TestData.PartnerId.ToString();
        string[] args = ["partner", "customer", "list"];

        switch (source)
        {
            case "--partner":
                args = [.. args, "--partner", partnerId];
                break;
            case PartnerCommandTests.PartnerVariable:
                run.Environment[PartnerCommandTests.PartnerVariable] = partnerId;
                break;
            default:
                var use = await run.RunAsync("partner", "use", partnerId);
                Assert.That(use.ExitCode, Is.Zero, use.ToString());
                break;
        }

        var result = await run.RunAsync(args);

        PartnerCommandTests.AssertNotImplemented(run, result);
    }

    // `partner use` only records an ID, so it needs no API call; partners cannot be looked up with a personal access
    // token in any case (proposal, "Partner API"). status reports a saved choice with the path of the file that holds
    // it, as it does for the organisation.
    [Test]
    public async Task Partner_use_saves_the_default_partner_without_calling_the_api()
    {
        using var run = CliRun.Start();
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);

        var use = await run.RunAsync("partner", "use", TestData.PartnerId.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(use.ExitCode, Is.Zero, use.ToString());
            Assert.That(use.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });

        StubOrgs(run);
        var status = await run.RunAsync("status");

        Assert.That(status.ExitCode, Is.Zero, status.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(StatusPartnerId(status), Is.EqualTo(TestData.PartnerId));
            Assert.That(Path.GetFullPath(StatusPartnerSource(status)!), Is.EqualTo(Path.GetFullPath(run.CliConfigPath)));
        });
    }

    // Looking partners up by name needs the ReadPartnerList scope, which personal access tokens cannot carry
    // (proposal, "Partner API"), so `partner use` takes an ID only. A rejected value saves nothing, which the partner
    // command run afterwards shows by finding no partner.
    [TestCase("Acme Partners")]
    [TestCase("not-a-guid")]
    [TestCase("../x")]
    public async Task Partner_use_exits_2_and_saves_nothing_for_a_value_that_is_not_a_partner_id(string value)
    {
        using var run = CliRun.Start();
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);

        var use = await run.RunAsync("partner", "use", value);

        PartnerCommandTests.AssertError(run, use, 2, "invalid_argument");

        var after = await run.RunAsync("partner", "customer", "list");

        Assert.That(PartnerCommandTests.ErrorCode(after), Is.EqualTo("no_partner"), after.ToString());
    }

    // ENCLAVE_PARTNER names another partner, so the report shows that --partner won.
    [Test]
    public async Task The_partner_option_takes_precedence_over_ENCLAVE_PARTNER()
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = OtherPartnerId.ToString();
        StubOrgs(run);

        var status = await run.RunAsync("status", "--partner", TestData.PartnerId.ToString());

        Assert.That(status.ExitCode, Is.Zero, status.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(StatusPartnerId(status), Is.EqualTo(TestData.PartnerId));
            Assert.That(StatusPartnerSource(status), Is.EqualTo("--partner"));
        });
    }

    // ENCLAVE_PARTNER lets each agent session fix its partner without writing the shared cli.json; the proposal gives
    // this reason for ENCLAVE_ORG, and the partner follows the same precedence.
    [Test]
    public async Task ENCLAVE_PARTNER_takes_precedence_over_the_saved_default_partner()
    {
        using var run = CliRun.Start();
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);
        var use = await run.RunAsync("partner", "use", OtherPartnerId.ToString());
        Assert.That(use.ExitCode, Is.Zero, use.ToString());

        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();
        StubOrgs(run);

        var status = await run.RunAsync("status");

        Assert.That(status.ExitCode, Is.Zero, status.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(StatusPartnerId(status), Is.EqualTo(TestData.PartnerId));
            Assert.That(StatusPartnerSource(status), Is.EqualTo(PartnerCommandTests.PartnerVariable));
        });
    }

    // A source that a higher one overrides is not consulted, so a stale or malformed ENCLAVE_PARTNER in an agent's
    // environment does not stop a command that names its partner with --partner.
    [Test]
    public async Task A_partner_command_given_the_partner_option_ignores_a_malformed_ENCLAVE_PARTNER()
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = "not-a-guid";

        var result = await run.RunAsync("partner", "customer", "list", "--partner", TestData.PartnerId.ToString());

        PartnerCommandTests.AssertNotImplemented(run, result);
    }

    // The organisation and the partner are chosen separately (proposal, "Context"). The organisation comes from the
    // saved default and the token sees two organisations, so if `partner use` lost the saved organisation, `system list`
    // would have to look organisations up and exit 2 no_org.
    [Test]
    public async Task Choosing_a_partner_leaves_the_saved_organisation_in_use()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);
        StubTwoOrgs(run);
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page(ApiJson.System("ABCDE")));

        var orgUse = await run.RunAsync("org", "use", TestData.OrgId.ToString());
        var partnerUse = await run.RunAsync("partner", "use", TestData.PartnerId.ToString());
        var before = run.Requests.Count;
        var systems = await run.RunAsync("system", "list");
        var systemListRequests = run.Calls(before);

        Assert.Multiple(() =>
        {
            Assert.That(orgUse.ExitCode, Is.Zero, orgUse.ToString());
            Assert.That(partnerUse.ExitCode, Is.Zero, partnerUse.ToString());
            Assert.That(systems.ExitCode, Is.Zero, systems.ToString());
            Assert.That(systemListRequests, Is.EqualTo(new[] { $"GET {TestData.OrgPath("systems")}" }));
        });
    }

    // The partner is saved first and the organisation after it, so if `org use` lost the saved partner, the partner
    // command would exit 2 no_partner.
    [Test]
    public async Task Choosing_an_organisation_leaves_the_saved_partner_in_use()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);
        StubTwoOrgs(run);

        var partnerUse = await run.RunAsync("partner", "use", TestData.PartnerId.ToString());
        var orgUse = await run.RunAsync("org", "use", TestData.OtherOrgId.ToString());
        var requestsBefore = run.Requests.Count;
        var result = await run.RunAsync("partner", "customer", "list");

        Assert.Multiple(() =>
        {
            Assert.That(partnerUse.ExitCode, Is.Zero, partnerUse.ToString());
            Assert.That(orgUse.ExitCode, Is.Zero, orgUse.ToString());
            Assert.That(result.ExitCode, Is.EqualTo(1), result.ToString());
            Assert.That(PartnerCommandTests.ErrorCode(result), Is.EqualTo("not_implemented"));
            Assert.That(run.Requests, Has.Count.EqualTo(requestsBefore));
        });
    }

    // Choosing a partner does not choose an organisation: with a partner saved and no organisation chosen, a command
    // that acts within an organisation looks organisations up as it would with no partner, and with two visible exits
    // 2 no_org. A CLI that used the partner ID as an organisation would call /org/<partnerId>/systems.
    [Test]
    public async Task A_saved_partner_is_not_used_as_the_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Environment.Remove(PartnerCommandTests.PartnerVariable);
        StubTwoOrgs(run);

        var partnerUse = await run.RunAsync("partner", "use", TestData.PartnerId.ToString());
        var systems = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(partnerUse.ExitCode, Is.Zero, partnerUse.ToString());
            Assert.That(systems.ExitCode, Is.EqualTo(2), systems.ToString());
            Assert.That(PartnerCommandTests.ErrorCode(systems), Is.EqualTo("no_org"));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
        });
    }

    // status makes one GetOrganisationsAsync call to report the role in the organisation (proposal, "Login, logout and
    // status").
    private static void StubOrgs(CliRun run) =>
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

    private static void StubTwoOrgs(CliRun run)
    {
        var orgs = ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName));

        run.Stub("GET", "/account/orgs", json: orgs);
    }

    private static JsonElement StatusPartner(CliResult status) => JsonAssert.Property(status.StdoutJson, "partner");

    private static Guid StatusPartnerId(CliResult status) =>
        Guid.Parse(JsonAssert.Property(StatusPartner(status), "id").GetString()!);

    private static string? StatusPartnerSource(CliResult status) =>
        JsonAssert.Property(StatusPartner(status), "source").GetString();
}
