using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Context". system list's path shows which organisation a command used, and
// the requests before it show whether the CLI looked it up.
public class OrgContextTests
{
    private const string OrgLookup = "GET /account/orgs";

    private const string ThirdOrgName = "Globex Ltd";

    private static readonly Guid ThirdOrgId = new("e8a1f5c2-7d3b-4c69-a04e-6b2d9c1f8e53");

    private static readonly string[] OrgLookupOnly = [OrgLookup];

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string OtherOrgSystems = TestData.OtherOrgPath("systems");

    // Enclave.Sdk.Api writes the organisation ID as 32 hex digits (Enclave.Sdk.Api.Data
    // OrganisationGuid.ToString).
    private static readonly string ThirdOrgSystems = $"/org/{ThirdOrgId:N}/systems";

    public static IEnumerable<TestCaseData> DefaultsThatAreNotOrganisationIds()
    {
        yield return SavedAs("not-a-guid");
        yield return SavedAs("00000000-0000-0000-0000-000000000000");
        yield return SavedAs(12);
        yield return SavedAs(new JsonArray());
        yield return SavedAs(new JsonObject { ["id"] = "3f2a9c1e-7b4d-4e8a-9c61-2d5b8e0f4a17" });
    }

    // The higher source names Globex and the lower Acme, and the token sees both. A name costs one
    // lookup and an ID none ("Calls per command"). The org use that saves a default makes its own
    // request, so only the requests after it are checked.
    [TestCase("--org-id over ENCLAVE_CLI_ORG_ID", false)]
    [TestCase("--org over ENCLAVE_CLI_ORG_ID", true)]
    [TestCase("--org-id over ENCLAVE_CLI_ORG", false)]
    [TestCase("--org over ENCLAVE_CLI_ORG", true)]
    [TestCase("--org-id over the saved default", false)]
    [TestCase("--org over the saved default", true)]
    [TestCase("ENCLAVE_CLI_ORG_ID over the saved default", false)]
    [TestCase("ENCLAVE_CLI_ORG over the saved default", true)]
    [TestCase("the saved default over the lookup", false)]
    public async Task Organisation_precedence_is_the_option_then_the_environment_then_the_saved_default(string precedence, bool lookup)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        string[] args = ["system", "list"];

        switch (precedence)
        {
            case "--org-id over ENCLAVE_CLI_ORG_ID":
                run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OrgId.ToString();
                args = [.. args, "--org-id", TestData.OtherOrgId.ToString()];
                break;
            case "--org over ENCLAVE_CLI_ORG_ID":
                run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OrgId.ToString();
                args = [.. args, "--org", TestData.OtherOrgName];
                break;
            case "--org-id over ENCLAVE_CLI_ORG":
                run.Environment["ENCLAVE_CLI_ORG"] = TestData.OrgName;
                args = [.. args, "--org-id", TestData.OtherOrgId.ToString()];
                break;
            case "--org over ENCLAVE_CLI_ORG":
                run.Environment["ENCLAVE_CLI_ORG"] = TestData.OrgName;
                args = [.. args, "--org", TestData.OtherOrgName];
                break;
            case "--org-id over the saved default":
                await SaveDefaultAsync(run, TestData.OrgId);
                args = [.. args, "--org-id", TestData.OtherOrgId.ToString()];
                break;
            case "--org over the saved default":
                await SaveDefaultAsync(run, TestData.OrgId);
                args = [.. args, "--org", TestData.OtherOrgName];
                break;
            case "ENCLAVE_CLI_ORG_ID over the saved default":
                await SaveDefaultAsync(run, TestData.OrgId);
                run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OtherOrgId.ToString();
                break;
            case "ENCLAVE_CLI_ORG over the saved default":
                await SaveDefaultAsync(run, TestData.OrgId);
                run.Environment["ENCLAVE_CLI_ORG"] = TestData.OtherOrgName;
                break;
            case "the saved default over the lookup":
                await SaveDefaultAsync(run, TestData.OtherOrgId);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(precedence), precedence, "No arrangement for this case.");
        }

        var before = run.Requests.Count;
        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.That(
            run.Calls(before),
            Is.EqualTo(lookup ? new[] { OrgLookup, $"GET {OtherOrgSystems}" } : new[] { $"GET {OtherOrgSystems}" }));
    }

    // "Details". Each lower source exits 2 when it is read.
    [TestCase("ENCLAVE_CLI_ORG_ID under --org-id")]
    [TestCase("ENCLAVE_CLI_ORG_ID under --org")]
    [TestCase("the saved default under ENCLAVE_CLI_ORG_ID")]
    public async Task A_malformed_context_source_below_the_one_given_does_not_fail_the_command(string arrangement)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        string[] args = ["system", "list"];
        string[] expected = [$"GET {OtherOrgSystems}"];

        switch (arrangement)
        {
            case "ENCLAVE_CLI_ORG_ID under --org-id":
                run.Environment["ENCLAVE_CLI_ORG_ID"] = "not-a-guid";
                args = [.. args, "--org-id", TestData.OtherOrgId.ToString()];
                break;
            case "ENCLAVE_CLI_ORG_ID under --org":
                run.Environment["ENCLAVE_CLI_ORG_ID"] = "not-a-guid";
                args = [.. args, "--org", TestData.OtherOrgName];
                expected = [OrgLookup, $"GET {OtherOrgSystems}"];
                break;
            default:
                run.SaveLogin(defaultOrgId: "not-a-guid");
                run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OtherOrgId.ToString();
                break;
        }

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(expected));
    }

    // A command that read past a malformed default would act somewhere the caller did not choose.
    // The token sees one organisation, so a CLI that skipped the default would use it and succeed.
    [TestCaseSource(nameof(DefaultsThatAreNotOrganisationIds))]
    public async Task A_saved_default_that_is_not_an_organisation_id_exits_2_naming_credentials_json(string credentials)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_PAT");
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Files.WriteText(run.CredentialsPath, credentials, privateToUser: true);

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result);
        Assert.That(result.Error.GetProperty("detail").GetString(), Does.Contain(run.CredentialsPath));
    }

    // "Details". Both name Globex, so a CLI that picked either one would succeed.
    [TestCase("options")]
    [TestCase("environment")]
    public async Task An_organisation_name_given_with_an_organisation_id_exits_2(string where)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);

        if (where == "options")
        {
            CliAssert.Rejected(run, await run.RunAsync("system", "list", "--org", TestData.OtherOrgName, "--org-id", TestData.OtherOrgId.ToString()));
            await CliAssert.AcceptedAsync(run, "GET", OtherOrgSystems, "system", "list", "--org-id", TestData.OtherOrgId.ToString());
            return;
        }

        run.Environment["ENCLAVE_CLI_ORG"] = TestData.OtherOrgName;
        run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OtherOrgId.ToString();

        CliAssert.Rejected(run, await run.RunAsync("system", "list"));

        run.Environment.Remove("ENCLAVE_CLI_ORG");
        await CliAssert.AcceptedAsync(run, "GET", OtherOrgSystems, "system", "list");
    }

    // An empty ENCLAVE_CLI_ORG_ID falls through to the saved default, and an empty variable beside the
    // other is no conflict ("Login, logout and status").
    [TestCase("empty ENCLAVE_CLI_ORG_ID with a saved default", false)]
    [TestCase("empty ENCLAVE_CLI_ORG beside ENCLAVE_CLI_ORG_ID", false)]
    [TestCase("empty ENCLAVE_CLI_ORG_ID beside ENCLAVE_CLI_ORG", true)]
    public async Task An_empty_organisation_variable_counts_as_unset(string arrangement, bool lookup)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");

        switch (arrangement)
        {
            case "empty ENCLAVE_CLI_ORG_ID with a saved default":
                await SaveDefaultAsync(run, TestData.OtherOrgId);
                run.Environment["ENCLAVE_CLI_ORG_ID"] = string.Empty;
                break;
            case "empty ENCLAVE_CLI_ORG beside ENCLAVE_CLI_ORG_ID":
                run.Environment["ENCLAVE_CLI_ORG"] = string.Empty;
                run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OtherOrgId.ToString();
                break;
            default:
                run.Environment["ENCLAVE_CLI_ORG_ID"] = string.Empty;
                run.Environment["ENCLAVE_CLI_ORG"] = TestData.OtherOrgName;
                break;
        }

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        Assert.That(
            run.Calls(before),
            Is.EqualTo(lookup ? new[] { OrgLookup, $"GET {OtherOrgSystems}" } : new[] { $"GET {OtherOrgSystems}" }));
    }

    // Example 2. The saved default is Globex, so a CLI that used it acts on Globex. The filter flags
    // are the example's.
    [TestCase("ENCLAVE_CLI_ORG")]
    [TestCase("ENCLAVE_CLI_ORG_ID")]
    public async Task An_organisation_fixed_in_the_environment_is_used_and_the_saved_default_is_left_unchanged(string variable)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        await SaveDefaultAsync(run, TestData.OtherOrgId);
        var savedDefault = run.Files.ReadText(run.CredentialsPath);
        var byName = variable == "ENCLAVE_CLI_ORG";
        run.Environment[variable] = byName ? TestData.OrgName : TestData.OrgId.ToString();
        var before = run.Requests.Count;

        var result = await run.RunAsync("system", "list", "--tag", "build", "--os", "linux", "--state", "connected");

        CliAssert.Succeeded(result);
        Assert.That(
            run.Calls(before),
            Is.EqualTo(byName ? new[] { OrgLookup, $"GET {OrgSystems}" } : new[] { $"GET {OrgSystems}" }));

        var search = run.RequestsTo("GET", OrgSystems)[0].QueryValue("search");

        Assert.Multiple(() =>
        {
            Assert.That(search, Does.Contain("tags:build"));
            Assert.That(search, Does.Contain("os:Linux"));
            Assert.That(search, Does.Contain("state:connected"));
            Assert.That(run.Files.ReadText(run.CredentialsPath), Is.EqualTo(savedDefault));
        });
    }

    // Example 18, first form. The organisation lookup comes first because the policy lookup is a
    // call within it ("Calls per command").
    [Test]
    public async Task A_change_given_an_organisation_name_acts_in_that_organisation_whatever_the_saved_default_is()
    {
        using var run = CliRun.Start();
        var policies = TestData.OrgPath("policies");
        var disable = TestData.OrgPath("policies/disable");
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        BothOrgs.Stub(run);
        run.StubPages(policies, 200, ApiJson.Policy(42, "web to db"), ApiJson.Policy(7, "admins"));
        run.Stub("PUT", disable, json: ApiJson.Bulk("policiesUpdated", 1));
        await SaveDefaultAsync(run, TestData.OtherOrgId);
        var before = run.Requests.Count;

        var result = await run.RunAsync("policy", "disable", "web to db", "--org", TestData.OrgName);

        CliAssert.Bulk(result, requested: 1, affected: 1);
        Assert.That(run.Calls(before), Is.EqualTo(new[] { OrgLookup, $"GET {policies}", $"PUT {disable}" }));
        Assert.That(string.Join(",", run.RequestsTo("PUT", disable)[0].BodyIds("policyIds")), Is.EqualTo("42"));
    }

    // Example 18, second form.
    [Test]
    public async Task A_change_given_an_organisation_id_acts_in_that_organisation_with_no_lookup_whatever_the_saved_default_is()
    {
        using var run = CliRun.Start();
        var disable = TestData.OrgPath("policies/disable");
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        BothOrgs.Stub(run);
        run.Stub("PUT", disable, json: ApiJson.Bulk("policiesUpdated", 1));
        await SaveDefaultAsync(run, TestData.OtherOrgId);
        var before = run.Requests.Count;

        var result = await run.RunAsync("policy", "disable", "--id", "42", "--org-id", TestData.OrgId.ToString());

        CliAssert.Bulk(result, requested: 1, affected: 1);
        Assert.That(run.Calls(before), Is.EqualTo(new[] { $"PUT {disable}" }));
        Assert.That(string.Join(",", run.RequestsTo("PUT", disable)[0].BodyIds("policyIds")), Is.EqualTo("42"));
    }

    // The only organisation is Globex, so a CLI that used the test organisation acts on Acme.
    [Test]
    public async Task With_no_organisation_chosen_the_only_organisation_the_token_sees_is_used()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(new[] { OrgLookup, $"GET {OtherOrgSystems}" }));
    }

    // Guessing among several could change the wrong one. The candidates let the caller choose
    // without another call ("Errors and exit codes").
    [Test]
    public async Task With_no_organisation_chosen_several_organisations_exit_2_with_no_org_and_the_organisations_as_candidates()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync("system", "list");

        var error = CliAssert.Failed(result, "no_org");
        Assert.Multiple(() =>
        {
            Assert.That(Candidates(error), Is.EqualTo(Pairs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName))));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
        });
    }

    // "Globex Ltd" starts with "Globex", so a prefix or substring match finds two and exits 2.
    [TestCase("--org")]
    [TestCase("ENCLAVE_CLI_ORG")]
    public async Task An_organisation_name_matches_the_whole_name_ignoring_case(string source)
    {
        using var run = CliRun.Start();
        var orgs = ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName), (ThirdOrgId, ThirdOrgName));
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Stub("GET", "/account/orgs", json: orgs);
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        run.Stub("GET", ThirdOrgSystems, json: ApiJson.Page());

        var result = await RunWithOrgNameAsync(run, source, "gLoBeX");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(new[] { OrgLookup, $"GET {OtherOrgSystems}" }));
    }

    // Organisation names are free text, so two can differ only in case. Only the matches are
    // candidates, so the caller can pick one by ID ("Errors and exit codes").
    [TestCase("--org")]
    [TestCase("ENCLAVE_CLI_ORG")]
    [TestCase("org use")]
    public async Task An_organisation_name_matching_several_organisations_exits_2_with_the_matches_as_candidates(string source)
    {
        using var run = CliRun.Start();
        var orgs = ApiJson.Orgs((TestData.OrgId, "Acme"), (TestData.OtherOrgId, "ACME"), (ThirdOrgId, ThirdOrgName));
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Stub("GET", "/account/orgs", json: orgs);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());

        var result = await RunWithOrgNameAsync(run, source, "acme");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(Candidates(error), Is.EqualTo(Pairs((TestData.OrgId, "Acme"), (TestData.OtherOrgId, "ACME"))));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(SavedDefault(run), Is.Null);
        });
    }

    // no_org is for an organisation not chosen at all, so a name that found nothing is not_found
    // ("Errors and exit codes").
    [TestCase("--org")]
    [TestCase("ENCLAVE_CLI_ORG")]
    [TestCase("org use")]
    public async Task An_organisation_name_matching_no_organisation_exits_5_with_no_candidates(string source)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        StubBothOrgsAndTheirSystems(run);

        var result = await RunWithOrgNameAsync(run, source, "Initech");

        var error = CliAssert.Failed(result, "not_found");
        Assert.Multiple(() =>
        {
            Assert.That(error.TryGetProperty("candidates", out _), Is.False, error.ToString());
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(SavedDefault(run), Is.Null);
        });
    }

    // ENCLAVE_CLI_ORG_ID names Acme, so a CLI that read --org "" as no --org acts on Acme, and one that
    // passed the name on looks it up.
    [TestCase("--org", "")]
    [TestCase("--org", "   ")]
    [TestCase("org use", "")]
    [TestCase("org use", " \t ")]
    public async Task A_blank_organisation_name_exits_2_invalid_argument_before_the_lookup(string source, string name)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        var key = source == "org use" ? "name" : "--org";

        var result = await RunWithOrgNameAsync(run, source, name);

        CliAssert.Rejected(run, result);
        Assert.Multiple(() =>
        {
            CliAssert.Problems(result.Error, key);
            Assert.That(SavedDefault(run), Is.Null);
        });

        CliAssert.Succeeded(await RunWithOrgNameAsync(run, source, TestData.OtherOrgName));
        Assert.That(run.Calls()[0], Is.EqualTo(OrgLookup));
    }

    // The option decides how a value is read ("Shape and naming"), and an organisation can be named
    // "12".
    [Test]
    public async Task Org_option_reads_a_number_as_an_organisation_name()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (ThirdOrgId, "12")));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", ThirdOrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list", "--org", "12");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(new[] { OrgLookup, $"GET {ThirdOrgSystems}" }));
    }

    // The third organisation is named with Globex's ID, so a CLI that guessed the value was an ID
    // acts on Globex ("Shape and naming").
    [TestCase("--org")]
    [TestCase("ENCLAVE_CLI_ORG")]
    [TestCase("org use")]
    public async Task A_guid_given_as_an_organisation_name_is_matched_against_names(string source)
    {
        using var run = CliRun.Start();
        var name = TestData.OtherOrgId.ToString();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OtherOrgId, TestData.OtherOrgName), (ThirdOrgId, name)));
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        run.Stub("GET", ThirdOrgSystems, json: ApiJson.Page());
        string[] args = ["system", "list"];
        string[] expected = [OrgLookup, $"GET {ThirdOrgSystems}"];

        switch (source)
        {
            case "--org":
                args = [.. args, "--org", name];
                break;
            case "ENCLAVE_CLI_ORG":
                run.Environment["ENCLAVE_CLI_ORG"] = name;
                break;
            default:
                run.SaveLogin();
                CliAssert.Succeeded(await run.RunAsync("org", "use", name));
                expected = [$"GET {ThirdOrgSystems}"];
                break;
        }

        var before = run.Requests.Count;
        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(before), Is.EqualTo(expected));
    }

    // The token sees organisations named "Acme" and "12", so a CLI that fell back to a name lookup
    // sends a request ("ID checks").
    [TestCase("12")]
    [TestCase("Acme")]
    [TestCase("../systems")]
    public async Task Org_id_option_takes_a_guid_only(string value)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (ThirdOrgId, "12")));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        run.Stub("GET", ThirdOrgSystems, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--org-id", value],
            ["system", "list", "--org-id", TestData.OtherOrgId.ToString()],
            "GET",
            OtherOrgSystems);
    }

    // A name in ENCLAVE_CLI_ORG_ID is refused without a lookup, although the token sees an organisation
    // named "12" ("ID checks").
    [TestCase("Acme")]
    [TestCase("12")]
    public async Task ENCLAVE_CLI_ORG_ID_takes_a_guid_only(string value)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (ThirdOrgId, "12")));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        run.Stub("GET", ThirdOrgSystems, json: ApiJson.Page());
        run.Environment["ENCLAVE_CLI_ORG_ID"] = value;

        CliAssert.Rejected(run, await run.RunAsync("system", "list"));

        run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OtherOrgId.ToString();
        await CliAssert.AcceptedAsync(run, "GET", OtherOrgSystems, "system", "list");
    }

    // A lookup needs the token's ReadOrgList scope ("Context").
    [TestCase("no organisation chosen")]
    [TestCase("--org")]
    [TestCase("ENCLAVE_CLI_ORG")]
    public async Task An_organisation_lookup_refused_with_403_exits_4_with_forbidden(string lookup)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.StubProblem("GET", "/account/orgs", 403, "Forbidden");
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = lookup == "no organisation chosen"
            ? await run.RunAsync("system", "list")
            : await RunWithOrgNameAsync(run, lookup, TestData.OrgName);

        CliAssert.Failed(result, "forbidden");
        Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
    }

    // Example 1. org use --id makes one lookup, for the name ("Login, logout and status"). The token
    // sees two organisations, so a later command that ignored the default exits 2.
    [TestCase("Globex")]
    [TestCase("GLOBEX")]
    [TestCase("--id")]
    public async Task Org_use_saves_the_default_so_later_commands_use_it_with_no_lookup(string choice)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.SaveLogin();
        StubBothOrgsAndTheirSystems(run);

        var use = await run.RunAsync(choice == "--id" ? ["org", "use", "--id", TestData.OtherOrgId.ToString()] : ["org", "use", choice]);

        CliAssert.Succeeded(use);
        Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {OtherOrgSystems}" }));
    }

    // The file keeps everything else, a field another tool wrote included, and is readable by the user
    // only, since it holds the token ("Login, logout and status").
    [TestCase("globex")]
    [TestCase("--id")]
    public async Task Org_use_saves_the_organisation_id_as_defaultOrgID_and_leaves_the_rest_of_credentials_json(string choice)
    {
        using var run = CliRun.Start();
        StubBothOrgsAndTheirSystems(run);
        run.Environment.Remove("ENCLAVE_CLI_PAT");
        var saved = new JsonObject
        {
            ["personalAccessToken"] = TestData.Token,
            ["apiBaseUrl"] = run.ApiBaseUrl,
            ["partnerApiBaseUrl"] = run.PartnerApiBaseUrl,
            ["defaultOrgID"] = TestData.OrgId.ToString(),
            ["otherTool"] = new JsonObject { ["setting"] = 1 },
        };
        run.Files.WriteText(run.CredentialsPath, saved.ToJsonString(), privateToUser: false);

        var result = await run.RunAsync(choice == "--id" ? ["org", "use", "--id", TestData.OtherOrgId.ToString()] : ["org", "use", choice]);

        CliAssert.Succeeded(result);
        Assert.That(SavedDefault(run), Is.EqualTo(TestData.OtherOrgId));
        var file = JsonNode.Parse(run.Files.ReadText(run.CredentialsPath)!)!.AsObject();
        file.Remove("defaultOrgID");
        saved.Remove("defaultOrgID");
        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(file, saved), Is.True, file.ToJsonString());
            Assert.That(run.Files.IsPrivate(run.CredentialsPath), Is.True);
            Assert.That(JsonRead.StringList(JsonAssert.Property(result.StdoutTrace, "saved")), Is.EqualTo(run.CredentialsPath));
        });
    }

    // A caller that chose by name learns the ID, and one that chose by ID learns the name.
    // Enclave.Sdk.Api writes orgId as 32 hex digits (Enclave.Sdk.Api.Data OrganisationGuid JSON
    // converter).
    [TestCase("globex")]
    [TestCase("--id")]
    public async Task Org_use_prints_the_organisation_it_saves(string choice)
    {
        using var run = CliRun.Start();
        run.SaveLogin();
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync(choice == "--id" ? ["org", "use", "--id", TestData.OtherOrgId.ToString()] : ["org", "use", choice]);

        CliAssert.Succeeded(result);
        var org = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(org, "orgId").GetString(), Is.EqualTo(TestData.OtherOrgId.ToString("N")));
            Assert.That(JsonAssert.Property(org, "orgName").GetString(), Is.EqualTo(TestData.OtherOrgName));
        });
    }

    // org use --id makes one lookup ("Login, logout and status"). The ID is a valid GUID, so the
    // refusal comes from the lookup.
    [Test]
    public async Task Org_use_id_exits_5_and_saves_nothing_for_an_organisation_the_token_does_not_see()
    {
        using var run = CliRun.Start();
        run.SaveLogin();
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync("org", "use", "--id", ThirdOrgId.ToString());

        CliAssert.Failed(result, "not_found");
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(SavedDefault(run), Is.Null);
        });
    }

    [TestCase("12")]
    [TestCase("Globex")]
    public async Task Org_use_id_takes_a_guid_only_and_saves_nothing_for_another_value(string value)
    {
        using var run = CliRun.Start();
        run.SaveLogin();
        StubBothOrgsAndTheirSystems(run);

        CliAssert.Rejected(run, await run.RunAsync("org", "use", "--id", value));
        Assert.That(SavedDefault(run), Is.Null);

        await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "org", "use", "--id", TestData.OtherOrgId.ToString());
    }

    // "Commands", "Details".
    [TestCase(false)]
    [TestCase(true)]
    public async Task Org_use_takes_exactly_one_of_a_name_or_id(bool both)
    {
        using var run = CliRun.Start();
        run.SaveLogin();
        StubBothOrgsAndTheirSystems(run);
        string[] args = both ? ["org", "use", TestData.OtherOrgName, "--id", TestData.OtherOrgId.ToString()] : ["org", "use"];

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
        Assert.That(SavedDefault(run), Is.Null);

        await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "org", "use", TestData.OtherOrgName);
    }

    // org use changes only a local file and org list only reads ("Dry run").
    [TestCase("use")]
    [TestCase("list")]
    public async Task Org_use_and_org_list_do_not_take_dry_run(string verb)
    {
        using var run = CliRun.Start();
        run.SaveLogin();
        StubBothOrgsAndTheirSystems(run);
        string[] command = verb == "use" ? ["org", "use", TestData.OtherOrgName] : ["org", "list"];

        await CliAssert.RejectedThenAcceptedAsync(run, [.. command, "--dry-run"], command, "GET", "/account/orgs");
    }

    // org list is how a caller finds an organisation, so it needs none chosen.
    [Test]
    public async Task Org_list_prints_the_organisations_the_token_sees_with_none_chosen()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        StubBothOrgsAndTheirSystems(run);

        var result = await run.RunAsync("org", "list");

        var items = CliAssert.List(result, "org");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "orgId"), Is.EqualTo($"{TestData.OrgId:N},{TestData.OtherOrgId:N}"));
            Assert.That(JsonRead.StringFieldList(items, "orgName"), Is.EqualTo($"{TestData.OrgName},{TestData.OtherOrgName}"));
            Assert.That($"{request.Method} {request.Path}", Is.EqualTo(OrgLookup));
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // credentials.json with the test token and the value given as defaultOrgID. With no apiBaseUrl
    // the token goes to production's address, which is the fake API's.
    private static TestCaseData SavedAs(JsonNode value)
    {
        var credentials = new JsonObject { ["personalAccessToken"] = TestData.Token, ["defaultOrgID"] = value }.ToJsonString();
        return new TestCaseData(credentials).SetArgDisplayNames(value.ToJsonString());
    }

    private static void StubBothOrgsAndTheirSystems(CliRun run)
    {
        BothOrgs.Stub(run);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
    }

    private static Task<CliResult> RunWithOrgNameAsync(CliRun run, string source, string name)
    {
        switch (source)
        {
            case "--org":
                return run.RunAsync("system", "list", "--org", name);
            case "ENCLAVE_CLI_ORG":
                run.Environment["ENCLAVE_CLI_ORG"] = name;
                return run.RunAsync("system", "list");
            default:
                run.SaveLogin();
                return run.RunAsync("org", "use", name);
        }
    }

    // As org use saves it, after a login.
    private static async Task SaveDefaultAsync(CliRun run, Guid orgId)
    {
        run.SaveLogin();
        CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", orgId.ToString()));
        Assert.That(SavedDefault(run), Is.EqualTo(orgId), "org use saved no default.");
    }

    // The defaultOrgID in credentials.json, or null when there is none.
    private static Guid? SavedDefault(CliRun run) =>
        run.Files.ReadText(run.CredentialsPath) is { } text && JsonRead.Parse(text).TryGetProperty("defaultOrgID", out var id)
            ? Guid.Parse(id.GetString()!, CultureInfo.InvariantCulture)
            : null;

    // Written "<32 hex digits>=<name>" and sorted, so the comparison holds whatever GUID form and
    // order the CLI uses.
    private static string Candidates(JsonElement error)
    {
        if (!error.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            throw new AssertionException($"Expected a \"candidates\" array in the error: {error}");
        }

        return string.Join(", ", candidates.EnumerateArray().Select(Candidate).Order(StringComparer.Ordinal));
    }

    private static string Candidate(JsonElement candidate) =>
        Pair(Guid.Parse(JsonAssert.Property(candidate, "id").GetString()!, CultureInfo.InvariantCulture), JsonAssert.Property(candidate, "name").GetString()!);

    private static string Pairs(params (Guid Id, string Name)[] orgs) =>
        string.Join(", ", orgs.Select(org => Pair(org.Id, org.Name)).Order(StringComparer.Ordinal));

    private static string Pair(Guid id, string name) => $"{id:N}={name}";
}
