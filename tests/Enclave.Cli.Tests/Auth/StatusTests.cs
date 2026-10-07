using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// status prints { "token": { "source" }, "org": { "id", "name", "role", "source" }, "partner":
// { "id", "source" } } (proposed-cli-surface.md "Login, logout and status"). A source is the option
// or variable that made the choice, the path of the file it came from, or only-organisation when
// the token sees one organisation.
[Category(TestCategory.Pending)]
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

    // Example 36. status makes one GetOrganisationsAsync call ("Login, logout and status"), which
    // proves the token works and carries the organisation names and roles status reports.
    [Test]
    public async Task Status_makes_one_request_for_the_organisations_the_token_can_see()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "status");

        Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
    }

    // With an organisation and a partner in use, every part of the output is present, so its
    // properties can be checked exactly. CliRun sets ENCLAVE_ORG_ID to Acme, whose role is Owner.
    [Test]
    public async Task Status_prints_the_token_organisation_and_partner_with_exactly_their_fields()
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_PARTNER_ID"] = TestData.PartnerId.ToString();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var org = JsonAssert.Property(output, "org");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(StatusFields));
            Assert.That(JsonRead.PropertyNames(JsonAssert.Property(output, "token")), Is.EquivalentTo(TokenFields));
            Assert.That(JsonRead.PropertyNames(org), Is.EquivalentTo(OrgFields));
            Assert.That(JsonRead.PropertyNames(JsonAssert.Property(output, "partner")), Is.EquivalentTo(PartnerFields));
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OrgName));
            Assert.That(JsonAssert.Property(org, "role").GetString(), Is.EqualTo("Owner"));
            Assert.That(JsonAssert.Property(org, "source").GetString(), Is.EqualTo("ENCLAVE_ORG_ID"));
        });
    }

    // A saved credentials.json is present as well, so the reported source shows ENCLAVE_TOKEN came
    // first ("Options on every command").
    [Test]
    public async Task Status_reports_ENCLAVE_TOKEN_as_the_token_source_when_it_is_set()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(FileToken);
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(TokenSource(result), Is.EqualTo("ENCLAVE_TOKEN"));
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // The full path tells the caller which file holds the token, and it differs per user and OS. An
    // empty ENCLAVE_TOKEN counts as unset ("Login, logout and status"), so the file supplies the
    // token then too.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Status_reports_the_credentials_file_path_as_the_token_source_when_ENCLAVE_TOKEN_is_unset_or_empty(bool empty)
    {
        using var run = CliRun.Start();

        if (empty)
        {
            run.Environment["ENCLAVE_TOKEN"] = string.Empty;
        }
        else
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
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

    // Each case makes Globex the organisation in use through a different source ("Context"), with
    // Acme listed first and given another role, so a status that reported the first organisation
    // or its role fails. The option cases leave ENCLAVE_ORG_ID naming Acme, so the report shows the
    // option came first. status takes --org and --org-id ("Login, logout and status") and makes its
    // one call in every case: a name, and the lookup when nothing is chosen, are resolved from that
    // same response. The org use that saves a default makes its own request, so only the requests
    // after it are checked.
    [TestCase("--org-id")]
    [TestCase("--org")]
    [TestCase("ENCLAVE_ORG_ID")]
    [TestCase("ENCLAVE_ORG")]
    [TestCase("cli.json")]
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
            case "ENCLAVE_ORG_ID":
                run.Environment["ENCLAVE_ORG_ID"] = TestData.OtherOrgId.ToString();
                break;
            case "ENCLAVE_ORG":
                run.Environment.Remove("ENCLAVE_ORG_ID");
                run.Environment["ENCLAVE_ORG"] = "globex";
                break;
            case "cli.json":
                run.Environment.Remove("ENCLAVE_ORG_ID");
                CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", TestData.OtherOrgId.ToString()));
                break;
            default:
                run.Environment.Remove("ENCLAVE_ORG_ID");
                break;
        }

        var before = run.Requests.Count;
        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        var org = JsonAssert.Property(result.StdoutJson, "org");
        var source = JsonAssert.Property(org, "source").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OtherOrgName));
            Assert.That(JsonAssert.Property(org, "role").GetString(), Is.EqualTo("Admin"));
            Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));

            if (chosenBy == "cli.json")
            {
                Assert.That(Path.GetFullPath(source!), Is.EqualTo(Path.GetFullPath(run.CliConfigPath)));
            }
            else
            {
                Assert.That(source, Is.EqualTo(chosenBy));
            }
        });
    }

    // status describes what a command would use. With none chosen and several organisations there
    // is no organisation in use, and status reports that as a null org ("Login, logout and status")
    // where another command exits 2 with no_org.
    [Test]
    public async Task Status_reports_no_organisation_when_none_is_chosen_and_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutJson, "org").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
        });
    }

    // The partner is given by ID only ("Context": partners cannot be looked up with a personal
    // access token), so status reports the ID and where it came from and makes no call for it.
    // status takes --partner-id ("Login, logout and status"). In the --partner-id case
    // ENCLAVE_PARTNER_ID names another partner, so the report shows the option came first; in the
    // ENCLAVE_PARTNER_ID case partner use has saved another partner, so the report shows the
    // variable came before the saved default.
    [TestCase("--partner-id")]
    [TestCase("ENCLAVE_PARTNER_ID")]
    [TestCase("cli.json")]
    public async Task Status_reports_the_partner_in_use_and_where_that_choice_came_from(string chosenBy)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());
        string[] args = ["status"];

        switch (chosenBy)
        {
            case "--partner-id":
                run.Environment["ENCLAVE_PARTNER_ID"] = OtherPartnerId.ToString();
                args = [.. args, "--partner-id", TestData.PartnerId.ToString()];
                break;
            case "ENCLAVE_PARTNER_ID":
                CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", OtherPartnerId.ToString()));
                run.Environment["ENCLAVE_PARTNER_ID"] = TestData.PartnerId.ToString();
                break;
            default:
                CliAssert.Succeeded(await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString()));
                break;
        }

        var before = run.Requests.Count;
        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        var partner = JsonAssert.Property(result.StdoutJson, "partner");
        var source = JsonAssert.Property(partner, "source").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(JsonAssert.Property(partner, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.PartnerId));
            Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));

            if (chosenBy == "cli.json")
            {
                Assert.That(Path.GetFullPath(source!), Is.EqualTo(Path.GetFullPath(run.CliConfigPath)));
            }
            else
            {
                Assert.That(source, Is.EqualTo(chosenBy));
            }
        });
    }

    // Only partner commands need a partner ("Context"), so status succeeds without one and reports
    // a null partner ("Login, logout and status"). An empty ENCLAVE_PARTNER_ID counts as unset.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Status_reports_no_partner_when_none_is_chosen(bool emptyVariable)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        if (emptyVariable)
        {
            run.Environment["ENCLAVE_PARTNER_ID"] = string.Empty;
        }

        var result = await run.RunAsync("status");

        CliAssert.Succeeded(result);
        Assert.That(JsonAssert.Property(result.StdoutJson, "partner").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    // status checks the token with its one call, so a token the API refuses is reported with the
    // code for the status ("Errors and exit codes"); 403 means the token lacks ReadOrgList, which
    // GetOrganisationsAsync needs.
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

    // status is a read, so it does not take --dry-run ("Dry run"), and an option a command does not
    // take exits 2 ("Details"). The corrected run proves the rejection withheld the request.
    [Test]
    public async Task Status_does_not_take_dry_run()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());
        string[] command = ["status"];

        await CliAssert.RejectedThenAcceptedAsync(run, [.. command, "--dry-run"], command, "GET", "/account/orgs");
    }

    private static string? TokenSource(CliResult result) =>
        JsonAssert.Property(JsonAssert.Property(result.StdoutJson, "token"), "source").GetString();

    // GET /account/orgs in the form ApiJson.Orgs writes (portal QueryAccountOrgsResponseModel and
    // AccountOrganisationModel), with a different role per organisation, which ApiJson.Orgs does
    // not offer. The roles are UserOrganisationRole names (portal
    // Enclave.Configuration.Data/Enums/UserOrganisationRole.cs).
    private static string OrgsWithRoles() =>
        $$"""
        {"orgs":[
          {"orgId":"{{TestData.OrgId:N}}","orgName":"{{TestData.OrgName}}","role":"Owner","partnerAccess":false},
          {"orgId":"{{TestData.OtherOrgId:N}}","orgName":"{{TestData.OtherOrgName}}","role":"Admin","partnerAccess":true}
        ]}
        """;

    private static string OnlyGlobexAsAdmin() =>
        $$"""
        {"orgs":[
          {"orgId":"{{TestData.OtherOrgId:N}}","orgName":"{{TestData.OtherOrgName}}","role":"Admin","partnerAccess":true}
        ]}
        """;
}
