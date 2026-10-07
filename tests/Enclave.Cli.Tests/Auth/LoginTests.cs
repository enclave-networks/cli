using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using WireMock;

namespace Enclave.Cli.Tests.Auth;

// login checks a personal access token with one GetOrganisationsAsync call, saves it in
// credentials.json, prints the organisations the token can see, and never prompts
// (proposed-cli-surface.md "Login, logout and status").
public class LoginTests
{
    private const string StdinToken = "stdin-token-51c2e8";

    private const string OldToken = "old-token-d40b77";

    private const string OrgLookup = "GET /account/orgs";

    private static readonly string[] OrgLookupOnly = [OrgLookup];

    // The format Enclave.Sdk.Api reads (EnclaveClient.ReadCredentialsFile, version 1.1.0), which the
    // CLI must not change (AGENTS.md "CLI contract"). The file can also hold partnerApiBaseUrl, which
    // login keeps when it is there and never adds.
    private static readonly string[] CredentialsFields = ["personalAccessToken", "baseUrl"];

    private static readonly string OtherOrgSystems = $"/org/{TestData.OtherOrgId:N}/systems";

    // Example 1 reads the token from a file, which ends in a newline, and a Windows pipe ends it
    // with \r\n. login reads the token without the trailing newline, so neither the Authorization
    // header nor the saved token carries it.
    [Test]
    public async Task Login_token_stdin_checks_the_token_read_from_stdin_without_its_trailing_newline_and_saves_it()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.StdinText = StdinToken + "\r\n";
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "login", "--token-stdin");

        Assert.Multiple(() =>
        {
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {StdinToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(StdinToken));
        });
    }

    // With ENCLAVE_TOKEN set, login saves that token after one check, so a mistyped token is
    // reported at login and never saved.
    [Test]
    public async Task Login_checks_the_token_from_ENCLAVE_TOKEN_with_one_request_and_saves_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "login");

        Assert.Multiple(() =>
        {
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(SavedToken(run), Is.EqualTo(TestData.Token));
        });
    }

    // --token-stdin wins over ENCLAVE_TOKEN ("Login, logout and status"), so the token on stdin is
    // the one checked and saved when ENCLAVE_TOKEN holds another.
    [Test]
    public async Task Login_token_stdin_takes_the_token_from_stdin_when_ENCLAVE_TOKEN_is_also_set()
    {
        using var run = CliRun.Start();
        run.StdinText = StdinToken + "\n";
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "login", "--token-stdin");

        Assert.Multiple(() =>
        {
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {StdinToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(StdinToken));
        });
    }

    // credentials.json holds personalAccessToken and baseUrl, the file Enclave.Sdk.Api reads. With
    // no file to keep a base URL from, login records the API address that accepted the token.
    [Test]
    public async Task Login_writes_credentials_json_holding_the_token_and_the_api_base_url()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        var credentials = SavedCredentials(run);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(credentials), Is.EquivalentTo(CredentialsFields));
            Assert.That(JsonAssert.Property(credentials, "personalAccessToken").GetString(), Is.EqualTo(TestData.Token));
            Assert.That(new Uri(JsonAssert.Property(credentials, "baseUrl").GetString()!), Is.EqualTo(run.ApiUrl));
        });
    }

    // login keeps an existing baseUrl, and ENCLAVE_TOKEN keeps the file's baseUrl ("Login, logout
    // and status"), so a credentials.json pointing at another API address (a staging or self-hosted
    // API) points there after login too. The second fake API proves the check went to the saved
    // address, and the file proves the address was kept while the token was replaced.
    [Test]
    public async Task Login_keeps_the_base_url_already_in_credentials_json_and_checks_the_token_there()
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", "/account/orgs", 200, OneOrg());
        run.SaveCredentials(OldToken, otherUrl);

        var result = await run.RunAsync("login");

        var checks = otherApi.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>().Select(request => $"{request.Method} {request.Path}").ToArray();

        CliAssert.Succeeded(result);
        var credentials = SavedCredentials(run);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The token was checked against the default API address.");
            Assert.That(checks, Is.EqualTo(OrgLookupOnly));
            Assert.That(new Uri(JsonAssert.Property(credentials, "baseUrl").GetString()!), Is.EqualTo(new Uri(otherUrl)));
            Assert.That(JsonAssert.Property(credentials, "personalAccessToken").GetString(), Is.EqualTo(TestData.Token));
        });
    }

    // credentials.json can hold partnerApiBaseUrl beside baseUrl, and staging means setting both
    // there (proposed-cli-surface.md "Partner API"), so login keeps it as it keeps baseUrl: a login
    // that rewrote the file without it would send the next partner command to production. The
    // address is staging's, which no test calls.
    [Test]
    public async Task Login_keeps_the_partner_api_base_url_already_in_credentials_json()
    {
        const string StagingPartnerApi = "https://staging-partner-api.enclave.io";
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OneOrg());
        run.SaveCredentials(OldToken, partnerApiBaseUrl: StagingPartnerApi);

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        var credentials = SavedCredentials(run);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(JsonAssert.Property(credentials, "partnerApiBaseUrl").GetString(), Is.EqualTo(StagingPartnerApi));
            Assert.That(new Uri(JsonAssert.Property(credentials, "baseUrl").GetString()!), Is.EqualTo(run.ApiUrl));
            Assert.That(JsonAssert.Property(credentials, "personalAccessToken").GetString(), Is.EqualTo(TestData.Token));
        });
    }

    // The file holds a token that does not expire (portal
    // Enclave.Accounts/Controllers/Api/TokensApiController.cs:105 issues personal access tokens with
    // TimeSpan.MaxValue), so other local users must not read it. login asks the file store for a
    // private write (AGENTS.md "Code"), the same request on every OS; Storage/DiskFileStoreTests.cs
    // proves that a private write makes ~/.enclave 0700 and the file 0600 on Linux and macOS.
    [Test]
    public async Task Login_writes_credentials_json_private_to_the_user()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        Assert.That(run.Files.IsPrivate(run.CredentialsPath), Is.True);
    }

    // login prints the organisations the token can see as an org list ("Login, logout and status",
    // "Output"), so the caller learns which organisations the token reaches without a second call.
    // The items are AccountOrganisationModel unchanged, and Enclave.Sdk.Api writes orgId as 32 hex
    // digits (OrganisationGuid JSON converter, Enclave.Sdk.Api.Data 304.48.0).
    [Test]
    public async Task Login_prints_the_organisations_the_token_can_see_as_an_org_list()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));

        var result = await run.RunAsync("login");

        var items = CliAssert.List(result, "org");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "orgId"), Is.EqualTo($"{TestData.OrgId:N},{TestData.OtherOrgId:N}"));
            Assert.That(JsonRead.StringFieldList(items, "orgName"), Is.EqualTo($"{TestData.OrgName},{TestData.OtherOrgName}"));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
        });
    }

    // login saves the default when the token sees exactly one organisation, from the response to its
    // one check, as { "org": { "id", "name" } } in cli.json ("Login, logout and status"). A later
    // command with no organisation chosen then builds the organisation client from the saved ID
    // with no lookup ("Context"). The one organisation is Globex, so a command that used the test
    // organisation acts on Acme, and one that looked the organisation up shows a GET /account/orgs
    // before its own request.
    [Test]
    public async Task Login_saves_the_organisation_as_the_default_when_the_token_sees_exactly_one()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());

        var login = await run.RunAsync("login");

        CliAssert.Succeeded(login);
        var org = JsonAssert.Property(JsonRead.Parse(run.Files.ReadText(run.CliConfigPath) ?? throw new AssertionException("login saved no cli.json.")), "org");
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OtherOrgName));
        });

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {OtherOrgSystems}" }));
    }

    // With several organisations there is no single default, so login saves none, and a later
    // command with no organisation chosen looks them up and asks the caller to choose ("Context").
    [Test]
    public async Task Login_saves_no_default_organisation_when_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Stub("GET", "/account/orgs", json: BothOrgs());
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());

        CliAssert.Succeeded(await run.RunAsync("login"));

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));
    }

    // When the token sees several organisations login leaves the saved default as it is ("Login,
    // logout and status"), so a default chosen with org use survives a new login. The default is
    // Globex and the token also sees Acme, so a login that rewrote the default changes cli.json,
    // and one that cleared it leaves the later command to look the organisations up and exit 2.
    [Test]
    public async Task Login_leaves_the_saved_default_as_it_is_when_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Stub("GET", "/account/orgs", json: BothOrgs());
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        CliAssert.Succeeded(await run.RunAsync("org", "use", "--id", TestData.OtherOrgId.ToString()));
        var savedDefault = run.Files.ReadText(run.CliConfigPath);

        CliAssert.Succeeded(await run.RunAsync("login"));

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Files.ReadText(run.CliConfigPath), Is.EqualTo(savedDefault));
            Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {OtherOrgSystems}" }));
        });
    }

    // login writes credentials.json after its check passes, so a token the API refuses is never
    // saved: a saved bad token makes every later command fail. Enclave.Sdk.Api raises
    // EnclaveApiException only for problem+json responses
    // (Handlers/ProblemDetailsHttpMessageHandler.cs:29, version 1.1.0), so the plain cases, a proxy
    // or server answering without problem details, reach the CLI as HttpRequestException, which
    // the CLI maps to the same codes ("Errors and exit codes"). 403 means the token lacks
    // ReadOrgList, which the check needs.
    [TestCase(401, true, "token_invalid")]
    [TestCase(401, false, "token_invalid")]
    [TestCase(403, true, "forbidden")]
    [TestCase(403, false, "forbidden")]
    [TestCase(503, true, "transient")]
    public async Task Login_writes_no_credentials_file_when_the_check_fails(int status, bool problemDetails, string code)
    {
        using var run = CliRun.Start();

        if (problemDetails)
        {
            run.StubProblem("GET", "/account/orgs", status, "Refused");
        }
        else
        {
            run.StubRaw("GET", "/account/orgs", status, "text/html", "<html><body>Refused</body></html>");
        }

        var result = await run.RunAsync("login");

        CliAssert.Failed(result, code);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo("/account/orgs"));
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
        });
    }

    // A refused token must not replace the saved one, which may be a working token.
    [Test]
    public async Task Login_leaves_an_existing_credentials_file_unchanged_when_the_api_refuses_the_token()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(OldToken);
        var before = run.Files.ReadText(run.CredentialsPath);
        run.StubProblem("GET", "/account/orgs", 401, "Unauthorized");

        var result = await run.RunAsync("login");

        CliAssert.Failed(result, "token_invalid");
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(run.Files.ReadText(run.CredentialsPath), Is.EqualTo(before));
        });
    }

    // login never prompts, and with no token it exits 3 token_missing; an empty ENCLAVE_TOKEN counts
    // as unset ("Login, logout and status"). Without --token-stdin, stdin is not a token source:
    // stdin holds a valid token in every case, so a CLI that read it, or waited on a terminal, logs
    // in.
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public async Task Login_with_no_token_exits_3_with_token_missing_and_leaves_stdin_unread(bool stdinIsTerminal, bool emptyVariable)
    {
        using var run = CliRun.Start();

        if (emptyVariable)
        {
            run.Environment["ENCLAVE_TOKEN"] = string.Empty;
        }
        else
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
        }

        run.StdinIsTerminal = stdinIsTerminal;
        run.StdinText = StdinToken + "\n";
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Rejected(run, result, "token_missing");
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
    }

    // Reading a terminal waits for someone to type, and login never prompts: --token-stdin with
    // stdin a terminal exits 2 ("Login, logout and status"). The text on stdin is a valid token, so
    // a CLI that read the terminal anyway logs in.
    [Test]
    public async Task Login_token_stdin_exits_2_without_reading_stdin_when_stdin_is_a_terminal()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.StdinIsTerminal = true;
        run.StdinText = StdinToken + "\n";
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login", "--token-stdin");

        CliAssert.Rejected(run, result);
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
    }

    // Empty stdin (an unset variable in `echo "$TOKEN" | enclave-cli login --token-stdin`) gives no
    // token and exits 3 token_missing ("Login, logout and status"); a newline alone is the empty
    // token with its trailing newline. Sending "Bearer " would turn the caller's mistake into a 401
    // and a misleading token_invalid.
    [TestCase("")]
    [TestCase("\n")]
    [TestCase("\r\n")]
    public async Task Login_token_stdin_exits_3_with_token_missing_when_stdin_holds_no_token(string stdin)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.StdinText = stdin;
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login", "--token-stdin");

        CliAssert.Rejected(run, result, "token_missing");
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
    }

    // login changes only local files, so it does not take --dry-run ("Dry run"), and an option a
    // command does not take exits 2 ("Details"). The corrected run proves the rejection withheld the
    // check.
    [Test]
    public async Task Login_does_not_take_dry_run()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OneOrg());
        string[] command = ["login"];

        CliAssert.Rejected(run, await run.RunAsync([.. command, "--dry-run"]));
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);

        await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", command);
    }

    private static string OneOrg() => ApiJson.Orgs((TestData.OrgId, TestData.OrgName));

    private static string BothOrgs() =>
        ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName));

    private static JsonElement SavedCredentials(CliRun run) =>
        JsonRead.Parse(run.Files.ReadText(run.CredentialsPath) ?? throw new AssertionException("login wrote no credentials.json."));

    private static string? SavedToken(CliRun run) =>
        JsonAssert.Property(SavedCredentials(run), "personalAccessToken").GetString();
}
