using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// system list (GET /org/{orgId}/systems) is the command that shows which organisation was used:
// the organisation ID is in the request path.
[Category(TestCategory.Pending)]
public class OrgContextTests
{
    private const string ThirdOrgName = "Globex Ltd";

    private static readonly Guid ThirdOrgId = new("e8a1f5c2-7d3b-4c69-a04e-6b2d9c1f8e53");

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    // The same path form as TestData.OrgPath: Enclave.Sdk.Api writes the organisation ID as 32 hex
    // digits (OrganisationGuid.ToString, Enclave.Sdk.Api.Data 304.48.0).
    private static readonly string OtherOrgSystems = $"/org/{TestData.OtherOrgId:N}/systems";

    private static readonly string[] OrgsLookupOnly = ["GET /account/orgs"];

    // Precedence is --org, then ENCLAVE_ORG, then the default org use saves, then the automatic
    // choice (proposal "Context"). Each case gives the higher source Globex and the lower one Acme,
    // and the token sees both organisations, so the automatic choice alone would exit 2. The setup
    // run of org use makes its own request, so only the requests after it are checked.
    [TestCase("--org over ENCLAVE_ORG")]
    [TestCase("--org over the saved default")]
    [TestCase("ENCLAVE_ORG over the saved default")]
    [TestCase("the saved default over the automatic choice")]
    public async Task Organisation_precedence_is_org_option_then_ENCLAVE_ORG_then_the_saved_default(string precedence)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        string[] args = ["system", "list"];

        switch (precedence)
        {
            case "--org over ENCLAVE_ORG":
                run.Environment["ENCLAVE_ORG"] = TestData.OrgId.ToString();
                args = ["system", "list", "--org", TestData.OtherOrgId.ToString()];
                break;
            case "--org over the saved default":
                run.Environment.Remove("ENCLAVE_ORG");
                await UseOrg(run, TestData.OrgId.ToString());
                args = ["system", "list", "--org", TestData.OtherOrgId.ToString()];
                break;
            case "ENCLAVE_ORG over the saved default":
                run.Environment.Remove("ENCLAVE_ORG");
                await UseOrg(run, TestData.OrgId.ToString());
                run.Environment["ENCLAVE_ORG"] = TestData.OtherOrgId.ToString();
                break;
            default:
                run.Environment.Remove("ENCLAVE_ORG");
                await UseOrg(run, TestData.OtherOrgId.ToString());
                break;
        }

        var before = run.Requests.Count;
        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {OtherOrgSystems}" }));
        });
    }

    // With nothing chosen, the command looks the organisations up (one extra call) and uses the only
    // one, so a token limited to one organisation needs no setup.
    [Test]
    public async Task Automatic_choice_uses_the_only_organisation_the_token_sees()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.Calls(), Is.EqualTo(new[] { "GET /account/orgs", $"GET {OrgSystems}" }));
        });
    }

    // Guessing among several organisations could change the wrong one. The error carries every
    // { id, name } so the caller can choose without another call (proposal "Context").
    [Test]
    public async Task Automatic_choice_exits_2_with_no_org_and_the_organisations_when_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("no_org"));
            Assert.That(Candidates(result), Is.EquivalentTo(new[] { (TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName) }));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Calls(), Is.EqualTo(OrgsLookupOnly));
        });
    }

    // An ID needs no lookup: the command builds the organisation client from it directly (proposal
    // "Calls per command": only a name or no choice adds a call).
    [TestCase("--org")]
    [TestCase("ENCLAVE_ORG")]
    public async Task An_organisation_id_makes_no_lookup(string source)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        string[] args = ["system", "list"];

        if (source == "--org")
        {
            run.Environment.Remove("ENCLAVE_ORG");
            args = ["system", "list", "--org", TestData.OtherOrgId.ToString()];
        }
        else
        {
            run.Environment["ENCLAVE_ORG"] = TestData.OtherOrgId.ToString();
        }

        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {OtherOrgSystems}" }));
        });
    }

    // Names match exactly, ignoring case (proposal "Context"). "Globex Ltd" starts with and contains
    // "Globex", so a prefix or substring match would find two organisations and exit 2.
    [TestCase("--org")]
    [TestCase("ENCLAVE_ORG")]
    public async Task An_organisation_name_is_looked_up_and_matched_exactly_ignoring_case(string source)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs(
            (TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName), (ThirdOrgId, ThirdOrgName)));
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        string[] args = ["system", "list"];

        if (source == "--org")
        {
            run.Environment.Remove("ENCLAVE_ORG");
            args = ["system", "list", "--org", "gLoBeX"];
        }
        else
        {
            run.Environment["ENCLAVE_ORG"] = "gLoBeX";
        }

        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.Calls(), Is.EqualTo(new[] { "GET /account/orgs", $"GET {OtherOrgSystems}" }));
        });
    }

    // Organisation names are free text, so two can differ only in case. The candidates are the
    // matching organisations only, which lets the caller pick by ID.
    [Test]
    public async Task An_organisation_name_matching_several_organisations_exits_2_with_the_candidates()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs(
            (TestData.OrgId, "Acme"), (TestData.OtherOrgId, "ACME"), (ThirdOrgId, ThirdOrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list", "--org", "acme");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("no_org"));
            Assert.That(Candidates(result), Is.EquivalentTo(new[] { (TestData.OrgId, "Acme"), (TestData.OtherOrgId, "ACME") }));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Calls(), Is.EqualTo(OrgsLookupOnly));
        });
    }

    // A name that matches nothing leaves no organisation chosen, the same state as several
    // organisations and no choice, so it is reported the same way with every organisation the
    // token sees.
    [Test]
    public async Task An_organisation_name_matching_no_organisation_exits_2_with_the_organisations_the_token_sees()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync("system", "list", "--org", "Initech");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("no_org"));
            Assert.That(Candidates(result), Is.EquivalentTo(new[] { (TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName) }));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Calls(), Is.EqualTo(OrgsLookupOnly));
        });
    }

    // org use saves the default as ID and name, and later commands build the organisation client
    // from the saved ID with no lookup (proposal "Context"). The token sees two organisations, so a
    // later command that ignored the default would exit 2.
    [TestCase("id")]
    [TestCase("Globex")]
    [TestCase("GLOBEX")]
    public async Task Org_use_saves_the_default_so_later_commands_use_it_with_no_lookup(string choice)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        StubBothOrgsAndTheirSystems(run);

        await UseOrg(run, choice == "id" ? TestData.OtherOrgId.ToString() : choice);

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {OtherOrgSystems}" }));
        });
    }

    // org use prints the organisation it saved as the API describes it (AccountOrganisationModel),
    // so a caller that chose by name learns the ID.
    [Test]
    public async Task Org_use_prints_the_organisation_it_saves()
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync("org", "use", "globex");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var org = result.StdoutJson;

        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(JsonAssert.Property(org, "orgId").GetString()!), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(JsonAssert.Property(org, "orgName").GetString(), Is.EqualTo(TestData.OtherOrgName));
        });
    }

    // A name org use cannot resolve to one organisation saves nothing, so the previous state stands
    // and the caller sees the candidates.
    [Test]
    public async Task Org_use_with_an_ambiguous_name_exits_2_and_saves_no_default()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, "Acme"), (TestData.OtherOrgId, "ACME")));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("org", "use", "acme");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("no_org"));
            Assert.That(Candidates(result), Is.EquivalentTo(new[] { (TestData.OrgId, "Acme"), (TestData.OtherOrgId, "ACME") }));
            Assert.That(File.Exists(run.CliConfigPath), Is.False);
        });
    }

    // org list needs no organisation chosen: it is how a caller finds one. It runs with several
    // organisations and none chosen, where an organisation-scoped command would exit 2.
    [Test]
    public async Task Org_list_prints_the_organisations_the_token_sees()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync("org", "list");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var items = JsonAssert.Property(result.StdoutJson, "items").EnumerateArray().ToArray();
        var request = run.SingleRequest();

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => Guid.Parse(JsonAssert.Property(item, "orgId").GetString()!)), Is.EquivalentTo(new[] { TestData.OrgId, TestData.OtherOrgId }));
            Assert.That(items.Select(item => JsonAssert.Property(item, "orgName").GetString()), Is.EquivalentTo(new[] { TestData.OrgName, TestData.OtherOrgName }));
            Assert.That(JsonAssert.Property(result.StdoutJson, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(result.StdoutJson, "truncated").ValueKind, Is.EqualTo(JsonValueKind.False));
            Assert.That($"{request.Method} {request.Path}", Is.EqualTo("GET /account/orgs"));
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // A lookup needs the token's ReadOrgList scope (proposal "Context"). A token without it gets
    // 403, which is exit 4 forbidden, and the command's own request is not sent.
    [TestCase("automatic choice")]
    [TestCase("name")]
    public async Task An_organisation_lookup_refused_with_403_exits_4_with_forbidden(string lookup)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.StubProblem("GET", "/account/orgs", 403, "Forbidden");
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync(lookup == "name" ? ["system", "list", "--org", TestData.OrgName] : ["system", "list"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(4), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("forbidden"));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Calls(), Is.EqualTo(OrgsLookupOnly));
        });
    }

    private static void StubBothOrgsAndTheirSystems(CliRun run)
    {
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
    }

    private static async Task UseOrg(CliRun run, string idOrName)
    {
        var result = await run.RunAsync("org", "use", idOrName);
        Assert.That(result.ExitCode, Is.Zero, $"org use {idOrName} failed: {result}");
    }

    // The { id, name } list a no_org error carries in error.orgs.
    private static (Guid Id, string? Name)[] Candidates(CliResult result) =>
        JsonAssert.Property(result.Error, "orgs").EnumerateArray()
            .Select(org => (Guid.Parse(JsonAssert.Property(org, "id").GetString()!), JsonAssert.Property(org, "name").GetString()))
            .ToArray();
}
