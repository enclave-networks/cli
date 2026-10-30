using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Login, logout and status".
public class LoginTests
{
    private const string StdinToken = "stdin-token-51c2e8";

    private const string OldToken = "old-token-d40b77";

    private const string OrgLookup = "GET /account/orgs";

    private const char ByteOrderMark = (char)0xFEFF;

    private static readonly string[] OrgLookupOnly = [OrgLookup];

    // The format Enclave.Sdk.Api reads (EnclaveClient.ReadCredentialsFile). login keeps a
    // partnerApiBaseUrl already there and never adds one.
    private static readonly string[] CredentialsFields = ["personalAccessToken", "baseUrl"];

    private static readonly string OtherOrgSystems = TestData.OtherOrgPath("systems");

    // Example 1 reads the token from a file, which ends in a newline; a Windows pipe ends it with
    // \r\n.
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

    // Windows PowerShell 5.1's Out-File -Encoding utf8 writes a byte order mark (Microsoft Learn,
    // about_Character_Encoding), and on Linux and macOS Console.In passes it on as U+FEFF
    // (dotnet/runtime release/10.0, ConsolePal.Unix.cs, GetOrCreateReader). StdinText is stdin as
    // that reader gives it.
    [Test]
    public async Task Login_token_stdin_checks_and_saves_the_token_without_the_byte_order_mark_before_it()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.StdinText = ByteOrderMark + StdinToken + "\n";
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "login", "--token-stdin");

        Assert.Multiple(() =>
        {
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {StdinToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(StdinToken));
        });
    }

    // The check reports a mistyped token at login, before it is saved.
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

    // With no file to keep a base URL from, login records the API address that accepted the token.
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

    // A credentials.json pointing at a staging or self-hosted API keeps pointing there after login
    // ("Login, logout and status").
    [Test]
    public async Task Login_keeps_the_base_url_already_in_credentials_json_and_checks_the_token_there()
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", "/account/orgs", 200, OneOrg());
        run.SaveCredentials(OldToken, otherUrl);

        var result = await run.RunAsync("login");

        var checks = LoopbackApi.Received(otherApi).Select(request => request.Call).ToArray();

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

    // A login that dropped it would send the next partner command to production ("Partner API").
    // No test calls the staging address.
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

    // The token does not expire (portal TokensApiController.Create issues it with TimeSpan.MaxValue),
    // so other local users must not read it. Storage/DiskFileStoreTests proves what a private write
    // does on disk.
    [Test]
    public async Task Login_writes_credentials_json_private_to_the_user()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        Assert.That(run.Files.IsPrivate(run.CredentialsPath), Is.True);
    }

    // The caller learns which organisations the token reaches without a second call.
    // Enclave.Sdk.Api writes orgId as 32 hex digits (Enclave.Sdk.Api.Data OrganisationGuid JSON
    // converter).
    [Test]
    public async Task Login_prints_the_organisations_the_token_can_see_as_an_org_list()
    {
        using var run = CliRun.Start();
        BothOrgs.Stub(run);

        var result = await run.RunAsync("login");

        var items = CliAssert.List(result, "org");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "orgId"), Is.EqualTo($"{TestData.OrgId:N},{TestData.OtherOrgId:N}"));
            Assert.That(JsonRead.StringFieldList(items, "orgName"), Is.EqualTo($"{TestData.OrgName},{TestData.OtherOrgName}"));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
        });
    }

    // The one organisation is Globex, so a later command that used the test organisation acts on
    // Acme, and one that looked the organisation up shows GET /account/orgs ("Context").
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

    // A later command with no organisation chosen shows whether a default was saved ("Context").
    [Test]
    public async Task Login_saves_no_default_organisation_when_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());

        CliAssert.Succeeded(await run.RunAsync("login"));

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));
    }

    // A default chosen with org use survives a new login ("Login, logout and status"). A login that
    // cleared it would leave the later command to look the organisations up and exit 2.
    [Test]
    public async Task Login_leaves_the_saved_default_as_it_is_when_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
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

    // A saved bad token would fail every later command. Enclave.Sdk.Api raises EnclaveApiException
    // only for problem+json (ProblemDetailsHttpMessageHandler.SendAsync), so the plain cases, a
    // proxy answering without problem details, reach the CLI as HttpRequestException.
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

    // Without --token-stdin, stdin is no token source: redirected, it holds a valid token, so a CLI
    // that read it logs in; as a terminal, any read fails the run (CliRun.StdinIsTerminal).
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

    // Reading a terminal waits for someone to type, and login never prompts. Any read of a terminal
    // stdin fails the run (CliRun.StdinIsTerminal).
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

    // Empty stdin comes from an unset variable in `echo "$TOKEN" | enclave-cli login --token-stdin`.
    // Sending "Bearer " would turn that into a 401 and a misleading token_invalid.
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

    // login changes only local files ("Dry run"). The corrected run shows the rejection withheld the
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

    private static JsonElement SavedCredentials(CliRun run) =>
        JsonRead.Parse(run.Files.ReadText(run.CredentialsPath) ?? throw new AssertionException("login wrote no credentials.json."));

    private static string? SavedToken(CliRun run) =>
        JsonAssert.Property(SavedCredentials(run), "personalAccessToken").GetString();
}
