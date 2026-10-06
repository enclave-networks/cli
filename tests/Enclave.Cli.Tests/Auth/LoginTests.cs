using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using WireMock;

namespace Enclave.Cli.Tests.Auth;

[Category(TestCategory.Pending)]
public class LoginTests
{
    private const string StdinToken = "stdin-token-51c2e8";

    private const string OldToken = "old-token-d40b77";

    private static readonly string[] OrgsLookupOnly = ["GET /account/orgs"];

    [Test]
    public async Task Login_reads_the_token_from_stdin_when_token_stdin_is_given()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");

        // A shell pipe ends the token with a newline, and a Windows pipe with \r\n. Neither is part
        // of the token, so the saved token and the header must not carry them.
        run.StdinText = StdinToken + "\r\n";
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login", "--token-stdin");

        var request = run.SingleRequest();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo("/account/orgs"));
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {StdinToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(StdinToken));
        });
    }

    // The proposal ("Login, logout and status") checks the token with one GetOrganisationsAsync call
    // before saving it, so a mistyped token is reported at login and never saved.
    [Test]
    public async Task Login_checks_the_token_from_ENCLAVE_TOKEN_with_one_organisations_request_and_saves_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login");

        var request = run.SingleRequest();

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo("/account/orgs"));
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(SavedToken(run), Is.EqualTo(TestData.Token));
        });
    }

    // --token-stdin is the caller asking for the token on stdin by name, so it decides which token
    // is saved even when ENCLAVE_TOKEN holds another.
    [Test]
    public async Task Login_takes_the_token_from_stdin_over_ENCLAVE_TOKEN_when_token_stdin_is_given()
    {
        using var run = CliRun.Start();
        run.StdinText = StdinToken + "\n";
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login", "--token-stdin");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {StdinToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(StdinToken));
        });
    }

    // credentials.json is the file Enclave.Sdk.Api reads (EnclaveClient.GetSettingsFile, version
    // 1.0.4), which needs both fields. With no file to keep a base URL from, login records the API
    // address that accepted the token.
    [Test]
    public async Task Login_writes_credentials_json_holding_the_token_and_the_api_base_url()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.True, "credentials.json was not written.");

        var credentials = ReadJson(run, run.CredentialsPath);

        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(credentials, "personalAccessToken").GetString(), Is.EqualTo(TestData.Token));
            Assert.That(new Uri(JsonAssert.Property(credentials, "baseUrl").GetString()!), Is.EqualTo(run.ApiUrl));
        });
    }

    // A credentials.json pointing at another API address (a staging or self-hosted API) keeps
    // pointing there after login: the proposal says login keeps an existing baseUrl. The second
    // fake API proves both that the check went to the saved address and that the address was kept.
    [Test]
    public async Task Login_keeps_the_base_url_already_in_credentials_json()
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.SaveCredentials(OldToken, otherUrl);

        var result = await run.RunAsync("login");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var credentials = ReadJson(run, run.CredentialsPath);
        var checks = otherApi.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The token was checked against the default API address.");
            Assert.That(checks.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(OrgsLookupOnly));
            Assert.That(new Uri(JsonAssert.Property(credentials, "baseUrl").GetString()!), Is.EqualTo(new Uri(otherUrl)));
            Assert.That(JsonAssert.Property(credentials, "personalAccessToken").GetString(), Is.EqualTo(TestData.Token));
        });
    }

    // The file holds a token that never expires (portal TokensApiController.cs:105 issues personal
    // access tokens with TimeSpan.MaxValue), so other local users must not be able to read it.
    // Login asks the file store to write the file private to the user, which is the same request on
    // every OS. The disk store's own tests prove that a private write creates the .enclave directory
    // as 0700 and the file as 0600 on Linux and macOS (Storage/DiskFileStoreTests.cs).
    [Test]
    public async Task Login_writes_credentials_json_private_to_the_user()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.True, "credentials.json was not written.");
        Assert.That(run.Files.IsPrivate(run.CredentialsPath), Is.True);
    }

    // login prints what org list prints, so the caller sees which organisations the token reaches
    // without a second call.
    [Test]
    public async Task Login_prints_the_organisations_the_token_can_see()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));

        var result = await run.RunAsync("login");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var items = JsonAssert.Property(result.StdoutJson, "items").EnumerateArray().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(item => Guid.Parse(JsonAssert.Property(item, "orgId").GetString()!)), Is.EquivalentTo(new[] { TestData.OrgId, TestData.OtherOrgId }));
            Assert.That(items.Select(item => JsonAssert.Property(item, "orgName").GetString()), Is.EquivalentTo(new[] { TestData.OrgName, TestData.OtherOrgName }));
            Assert.That(JsonAssert.Property(result.StdoutJson, "total").GetInt32(), Is.EqualTo(2));
        });
    }

    // The proposal ("Context") saves the default when the token sees one organisation. A later
    // command with no --org and no ENCLAVE_ORG then acts on it straight away; finding it by lookup
    // instead would show up as a GET /account/orgs before the command's own request.
    [Test]
    public async Task Login_saves_the_organisation_as_the_default_when_the_token_sees_exactly_one()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());

        var login = await run.RunAsync("login");
        Assert.That(login.ExitCode, Is.Zero, login.ToString());

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {TestData.OrgPath("systems")}" }));
        });
    }

    // With several organisations there is no single right default, so login saves none and a later
    // command with no organisation chosen asks the caller to choose.
    [Test]
    public async Task Login_saves_no_default_organisation_when_the_token_sees_several()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());

        var login = await run.RunAsync("login");
        Assert.That(login.ExitCode, Is.Zero, login.ToString());

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("no_org"));
            Assert.That(run.Calls(before), Is.EqualTo(OrgsLookupOnly));
        });
    }

    // A token the API refuses is not saved: a saved bad token would make every later command fail.
    // Enclave.Sdk.Api raises EnclaveApiException only for problem+json responses
    // (ProblemDetailsHttpMessageHandler.cs:20, version 1.0.4), so the plain-status cases, a proxy or
    // server answering without problem details, reach the CLI as HttpRequestException. A 403 means
    // the token lacks ReadOrgList, which the check needs.
    [TestCase(401, true, 3, "token_invalid")]
    [TestCase(401, false, 3, "token_invalid")]
    [TestCase(403, true, 4, "forbidden")]
    [TestCase(403, false, 4, "forbidden")]
    public async Task Login_exits_with_the_error_and_writes_no_credentials_file_when_the_api_refuses_the_token(
        int status, bool problemDetails, int exitCode, string code)
    {
        using var run = CliRun.Start();

        if (problemDetails)
        {
            run.StubProblem("GET", "/account/orgs", status, "Refused");
        }
        else
        {
            run.Stub("GET", "/account/orgs", status);
        }

        var result = await run.RunAsync("login");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo(code));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.SingleRequest().Path, Is.EqualTo("/account/orgs"));
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
        });
    }

    // A rejected token must not replace the saved one, which may still work.
    [Test]
    public async Task Login_leaves_an_existing_credentials_file_unchanged_when_the_api_rejects_the_token()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(OldToken);
        var before = run.Files.ReadText(run.CredentialsPath);
        run.StubProblem("GET", "/account/orgs", 401, "Unauthorized");

        var result = await run.RunAsync("login");

        var after = run.Files.ReadText(run.CredentialsPath);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(3), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("token_invalid"));
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(after, Is.EqualTo(before));
        });
    }

    // The CLI never prompts or waits (AGENTS.md "CLI contract"). Without --token-stdin, stdin is not
    // a token source, so text waiting there must not be read: a CLI that read it would find a valid
    // token in this test and log in.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Login_exits_2_naming_both_token_sources_when_neither_is_given(bool stdinIsTerminal)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.StdinIsTerminal = stdinIsTerminal;
        run.StdinText = StdinToken + "\n";
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain("--token-stdin").And.Contain("ENCLAVE_TOKEN"));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
        });
    }

    // Reading a terminal waits for someone to type, and the CLI never waits (proposal "Several IDs"
    // applies the same rule to "-"). The text on stdin is a valid token, so a CLI that read the
    // terminal anyway would log in and exit 0.
    [Test]
    public async Task Login_token_stdin_exits_2_without_reading_stdin_when_stdin_is_a_terminal()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.StdinIsTerminal = true;
        run.StdinText = StdinToken + "\n";
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login", "--token-stdin");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
        });
    }

    // An empty pipe (an unset variable in `echo "$TOKEN" | enclave-cli login --token-stdin`) gives
    // no token. Sending "Bearer " would turn a caller's mistake into a 401 and a misleading error.
    [Test]
    public async Task Login_token_stdin_exits_2_without_a_request_when_stdin_holds_no_token()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.StdinText = "\r\n";
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync("login", "--token-stdin");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(run.Requests, Is.Empty);
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
        });
    }

    private static string? SavedToken(CliRun run) =>
        run.Files.ReadText(run.CredentialsPath) is { } credentials
            ? JsonAssert.Property(JsonRead.Parse(credentials), "personalAccessToken").GetString()
            : null;

    private static JsonElement ReadJson(CliRun run, string path) =>
        JsonRead.Parse(run.Files.ReadText(path) ?? throw new AssertionException($"Expected a file at {path}."));
}
