using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

public class StatusTests
{
    private const string FileToken = "file-token-2b8e61";

    private static readonly Guid OtherPartnerId = new("4d7c1e93-8a2b-4f60-9e15-c0b3a7d26f84");

    private static readonly string[] OrgsLookupOnly = ["GET /account/orgs"];

    // The proposal ("Login, logout and status") gives status one GetOrganisationsAsync call: it
    // proves the token works and gives the organisation names and roles status reports.
    [Test]
    public async Task Status_makes_one_request_for_the_organisations_the_token_can_see()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        var request = run.SingleRequest();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo("/account/orgs"));
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // A saved credentials.json is present as well, so the reported source shows ENCLAVE_TOKEN won
    // over it.
    [Test]
    public async Task Status_reports_ENCLAVE_TOKEN_as_the_token_source_when_it_is_set()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(FileToken);
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(TokenSource(result), Is.EqualTo("ENCLAVE_TOKEN"));
        });
    }

    // The full path tells the caller which file to delete or replace, and differs per user and OS.
    [Test]
    public async Task Status_reports_the_credentials_file_path_as_the_token_source_when_ENCLAVE_TOKEN_is_unset()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.SaveCredentials(FileToken);
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(Path.GetFullPath(TokenSource(result)!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {FileToken}"));
        });
    }

    // Each case makes Globex the organisation in use through a different source, with Acme listed
    // first and given another role, so a status that reported the first organisation or its role
    // fails. Status makes its one call in every case; a name or the automatic choice is resolved
    // from that same response.
    [TestCase("--org with an ID", "--org")]
    [TestCase("--org with a name", "--org")]
    [TestCase("ENCLAVE_ORG", "ENCLAVE_ORG")]
    [TestCase("default saved by org use", "cli.json")]
    [TestCase("only organisation", "automatic")]
    public async Task Status_reports_the_organisation_in_use_where_that_choice_came_from_and_your_role(string chosenBy, string source)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: chosenBy == "only organisation" ? OnlyGlobexAsAdmin() : OrgsWithRoles());
        string[] args = ["status"];

        switch (chosenBy)
        {
            case "--org with an ID":
                args = ["status", "--org", TestData.OtherOrgId.ToString()];
                break;
            case "--org with a name":
                args = ["status", "--org", "globex"];
                break;
            case "ENCLAVE_ORG":
                run.Environment["ENCLAVE_ORG"] = TestData.OtherOrgId.ToString();
                break;
            case "default saved by org use":
                run.Environment.Remove("ENCLAVE_ORG");
                var use = await run.RunAsync("org", "use", TestData.OtherOrgId.ToString());
                Assert.That(use.ExitCode, Is.Zero, use.ToString());
                break;
            default:
                run.Environment.Remove("ENCLAVE_ORG");
                break;
        }

        var before = run.Requests.Count;
        var result = await run.RunAsync(args);

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var org = JsonAssert.Property(result.StdoutJson, "org");
        var expectedSource = source == "cli.json" ? Path.GetFullPath(run.CliConfigPath) : source;

        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OtherOrgName));
            Assert.That(JsonAssert.Property(org, "role").GetString(), Is.EqualTo("Admin"));
            Assert.That(
                source == "cli.json" ? Path.GetFullPath(JsonAssert.Property(org, "source").GetString()!) : JsonAssert.Property(org, "source").GetString(),
                Is.EqualTo(expectedSource));
            Assert.That(run.Requests.Skip(before).Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(OrgsLookupOnly));
        });
    }

    // Status describes the state; with several organisations and none chosen there is no
    // organisation in use, which is a valid answer. Other commands report that state as no_org.
    [Test]
    public async Task Status_reports_no_organisation_when_the_token_sees_several_and_none_is_chosen()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(JsonAssert.Property(result.StdoutJson, "org").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    // The partner is given by ID only (proposal "Context": partners cannot be looked up with a
    // personal access token), so status reports the ID and its source. In the --partner case
    // ENCLAVE_PARTNER names another partner, so the report shows --partner won.
    [TestCase("--partner")]
    [TestCase("ENCLAVE_PARTNER")]
    public async Task Status_reports_the_partner_in_use_and_where_that_choice_came_from(string source)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());
        string[] args = ["status"];

        if (source == "--partner")
        {
            run.Environment["ENCLAVE_PARTNER"] = OtherPartnerId.ToString();
            args = ["status", "--partner", TestData.PartnerId.ToString()];
        }
        else
        {
            run.Environment["ENCLAVE_PARTNER"] = TestData.PartnerId.ToString();
        }

        var result = await run.RunAsync(args);

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var partner = JsonAssert.Property(result.StdoutJson, "partner");

        Assert.Multiple(() =>
        {
            Assert.That(Guid.Parse(JsonAssert.Property(partner, "id").GetString()!), Is.EqualTo(TestData.PartnerId));
            Assert.That(JsonAssert.Property(partner, "source").GetString(), Is.EqualTo(source));
        });
    }

    [Test]
    public async Task Status_reports_no_partner_when_none_is_chosen()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync("status");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(JsonAssert.Property(result.StdoutJson, "partner").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    // Status is where a caller looks to check its credentials, so it is the command most likely to
    // be asked to show the token. The request assertion proves status ran with the token in hand.
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Status_never_prints_the_token(bool tokenFromFile, bool verbose)
    {
        using var run = CliRun.Start();

        if (tokenFromFile)
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
            run.SaveCredentials(TestData.Token);
        }

        run.Stub("GET", "/account/orgs", json: OrgsWithRoles());

        var result = await run.RunAsync(verbose ? ["status", "--verbose"] : ["status"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(result.Stdout, Does.Not.Contain(TestData.Token));
            Assert.That(result.Stderr, Does.Not.Contain(TestData.Token));
        });
    }

    [Test]
    public async Task Status_exits_3_with_token_invalid_when_the_api_rejects_the_token()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", "/account/orgs", 401, "Unauthorized");

        var result = await run.RunAsync("status");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(3), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("token_invalid"));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.SingleRequest().Path, Is.EqualTo("/account/orgs"));
        });
    }

    private static string? TokenSource(CliResult result) =>
        JsonAssert.Property(JsonAssert.Property(result.StdoutJson, "token"), "source").GetString();

    // GET /account/orgs in the form ApiJson.Orgs writes (portal QueryAccountOrgsResponseModel and
    // AccountOrganisationModel), with a different role per organisation, which ApiJson.Orgs does
    // not offer.
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
