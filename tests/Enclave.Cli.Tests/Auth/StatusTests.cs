using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Login, logout and status".
public class StatusTests
{
    private const string FileToken = "file-token-2b8e61";

    private const string OrgLookup = "GET /account/orgs";

    private static readonly Guid OtherPartnerId = new("4d7c1e93-8a2b-4f60-9e15-c0b3a7d26f84");

    private static readonly string[] OrgLookupOnly = [OrgLookup];

    private static readonly string[] StatusFields = ["token", "org", "partner"];

    private static readonly string[] TokenFields = ["source"];

    private static readonly string[] OrgFields = ["id", "name", "role", "source"];

    private static readonly string[] PartnerFields = ["id", "source"];

    // Example 36. The one call proves the token works and carries the names and roles status
    // reports.
    [Test]
    public async Task Status_makes_one_request_for_the_organisations_the_token_can_see()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "status");

        Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
    }

    // With an organisation and a partner in use every part is present. CliRun sets ENCLAVE_CLI_ORG_ID to
    // Acme, whose role is Owner.
    [Test]
    public async Task Status_prints_the_token_organisation_and_partner_with_exactly_their_fields()
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_CLI_PARTNER_ID"] = TestData.PartnerId.ToString();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var org = JsonAssert.Property(output, "org");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNamesBeforeTrace(output), Is.EquivalentTo(StatusFields));
            Assert.That(JsonRead.PropertyNames(JsonAssert.Property(output, "token")), Is.EquivalentTo(TokenFields));
            Assert.That(JsonRead.PropertyNames(org), Is.EquivalentTo(OrgFields));
            Assert.That(JsonRead.PropertyNames(JsonAssert.Property(output, "partner")), Is.EquivalentTo(PartnerFields));
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OrgName));
            Assert.That(JsonAssert.Property(org, "role").GetString(), Is.EqualTo("Owner"));
            Assert.That(JsonAssert.Property(org, "source").GetString(), Is.EqualTo("ENCLAVE_CLI_ORG_ID"));
        });
    }

    // A saved credentials.json is present too, so the source shows ENCLAVE_CLI_PAT came first.
    [Test]
    public async Task Status_reports_ENCLAVE_CLI_PAT_as_the_token_source_when_it_is_set()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(FileToken);
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(TokenSource(result), Is.EqualTo("ENCLAVE_CLI_PAT"));
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // The full path tells the caller which file holds the token, which differs per user and OS.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Status_reports_the_credentials_file_path_as_the_token_source_when_ENCLAVE_CLI_PAT_is_unset_or_empty(bool empty)
    {
        using var run = CliRun.Start();

        if (empty)
        {
            run.Environment["ENCLAVE_CLI_PAT"] = string.Empty;
        }
        else
        {
            run.Environment.Remove("ENCLAVE_CLI_PAT");
        }

        run.SaveCredentials(FileToken);
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFullPath(TokenSource(result)!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {FileToken}"));
        });
    }

    // Acme is listed first with another role, so a status that reported the first organisation
    // fails. The option cases leave ENCLAVE_CLI_ORG_ID naming Acme. A name, the saved default, which
    // has no name, and the lookup when nothing is chosen, are resolved from status's one call.
    [TestCase("--org-id")]
    [TestCase("--org")]
    [TestCase("ENCLAVE_CLI_ORG_ID")]
    [TestCase("ENCLAVE_CLI_ORG")]
    [TestCase("credentials.json")]
    [TestCase("only-organisation")]
    public async Task Status_reports_the_organisation_in_use_where_that_choice_came_from_and_your_role(string chosenBy)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: chosenBy == "only-organisation" ? OnlyGlobexAsAdmin() : OrgsWithRoles());
        string[] args = ["status"];

        switch (chosenBy)
        {
            case "--org-id":
                args = [.. args, "--org-id", TestData.OtherOrgId.ToString()];
                break;
            case "--org":
                args = [.. args, "--org", "globex"];
                break;
            case "ENCLAVE_CLI_ORG_ID":
                run.Environment["ENCLAVE_CLI_ORG_ID"] = TestData.OtherOrgId.ToString();
                break;
            case "ENCLAVE_CLI_ORG":
                run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
                run.Environment["ENCLAVE_CLI_ORG"] = "globex";
                break;
            case "credentials.json":
                run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
                run.SaveLogin(defaultOrgId: TestData.OtherOrgId.ToString());
                break;
            default:
                run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
                break;
        }

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        var org = JsonAssert.Property(result.StdoutJson, "org");
        var source = JsonAssert.Property(org, "source").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OtherOrgName));
            Assert.That(JsonAssert.Property(org, "role").GetString(), Is.EqualTo("Admin"));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));

            if (chosenBy == "credentials.json")
            {
                Assert.That(Path.GetFullPath(source!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            }
            else
            {
                Assert.That(source, Is.EqualTo(chosenBy));
            }
        });
    }

    // status describes what a command would use, and reports a null org where another command exits
    // 2 no_org.
    [Test]
    public async Task Status_reports_no_organisation_when_none_is_chosen_and_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutJson, "org").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
        });
    }

    // Partners cannot be looked up with a personal access token, so status makes no call for one
    // ("Context"). With --partner-id, the variable names another partner.
    [TestCase("--partner-id")]
    [TestCase("ENCLAVE_CLI_PARTNER_ID")]
    public async Task Status_reports_the_partner_in_use_and_where_that_choice_came_from(string chosenBy)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());
        string[] args = ["status"];

        if (chosenBy == "--partner-id")
        {
            run.Environment["ENCLAVE_CLI_PARTNER_ID"] = OtherPartnerId.ToString();
            args = [.. args, "--partner-id", TestData.PartnerId.ToString()];
        }
        else
        {
            run.Environment["ENCLAVE_CLI_PARTNER_ID"] = TestData.PartnerId.ToString();
        }

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        var partner = JsonAssert.Property(result.StdoutJson, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(JsonAssert.Property(partner, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.PartnerId));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(JsonAssert.Property(partner, "source").GetString(), Is.EqualTo(chosenBy));
        });
    }

    // Only partner commands need a partner ("Context"). An empty ENCLAVE_CLI_PARTNER_ID counts as unset.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Status_reports_no_partner_when_none_is_chosen(bool emptyVariable)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        if (emptyVariable)
        {
            run.Environment["ENCLAVE_CLI_PARTNER_ID"] = string.Empty;
        }

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        Assert.That(JsonAssert.Property(result.StdoutJson, "partner").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    // 403 means the token lacks ReadOrgList, which GetOrganisationsAsync needs.
    [TestCase(401, "token_invalid")]
    [TestCase(403, "forbidden")]
    public async Task Status_reports_a_token_the_api_refuses(int status, string code)
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", "/account/orgs", status, "Refused");

        var result = await run.RunAsync("status");

        CliAssert.Failed(result, code);
        Assert.That(run.SingleRequest().Path, Is.EqualTo("/account/orgs"));
    }

    // status chooses the organisation and partner as any command does ("Errors and exit codes",
    // "Details").
    [TestCase("ENCLAVE_CLI_ORG with ENCLAVE_CLI_ORG_ID")]
    [TestCase("ENCLAVE_CLI_ORG_ID that is not a GUID")]
    [TestCase("ENCLAVE_CLI_PARTNER_ID that is not a GUID")]
    [TestCase("a saved default that is not an organisation ID")]
    public async Task Status_exits_2_before_its_call_when_the_organisation_or_partner_cannot_be_chosen(string arrangement)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        switch (arrangement)
        {
            case "ENCLAVE_CLI_ORG with ENCLAVE_CLI_ORG_ID":
                run.Environment["ENCLAVE_CLI_ORG"] = TestData.OtherOrgName;
                break;
            case "ENCLAVE_CLI_ORG_ID that is not a GUID":
                run.Environment["ENCLAVE_CLI_ORG_ID"] = "not-a-guid";
                break;
            case "ENCLAVE_CLI_PARTNER_ID that is not a GUID":
                run.Environment["ENCLAVE_CLI_PARTNER_ID"] = "12";
                break;
            default:
                run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
                run.SaveLogin(defaultOrgId: "not-a-guid");
                break;
        }

        var result = await run.RunAsync("status");

        CliAssert.Rejected(run, result);
    }

    private static string? TokenSource(CliResult result) =>
        JsonAssert.Property(JsonAssert.Property(result.StdoutJson, "token"), "source").GetString();

    private static string OrgsWithRoles() =>
        ApiJson.OrgsOf(ApiJson.Org(TestData.OrgId, TestData.OrgName), GlobexAsAdmin());

    private static string OnlyGlobexAsAdmin() => ApiJson.OrgsOf(GlobexAsAdmin());

    private static string GlobexAsAdmin() =>
        ApiJson.Org(TestData.OtherOrgId, TestData.OtherOrgName, "Admin", partnerAccess: true);
}
