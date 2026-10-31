using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Login, logout and status", "Environments". Each run is a person at a
// terminal (CliRun.AtTerminal), who answers through CliRun.Terminal.
public class LoginTests
{
    private const string TypedToken = "typed-token-51c2e8";

    private const string OldToken = "old-token-d40b77";

    private const string OrgLookup = "GET /account/orgs";

    private const string OtherApi = "https://other-api.example";

    private const string NoDefault = "No default";

    private static readonly string[] OrgLookupOnly = [OrgLookup];

    // Production's partner API address is left to Enclave.Sdk.Api's default.
    private static readonly string[] CredentialsFields = ["personalAccessToken", "apiBaseUrl", "defaultOrgID"];

    private static readonly string OtherOrgSystems = TestData.OtherOrgPath("systems");

    // Example 1. The check reports a mistyped token at login, before it is saved.
    [Test]
    public async Task Login_asks_for_a_token_checks_it_with_one_request_and_saves_it()
    {
        using var run = CliRun.Start();
        run.AtTerminal();
        run.AnswerLogin(TypedToken);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "login");

        Assert.Multiple(() =>
        {
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {TypedToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(TypedToken));
        });
    }

    // The question names where the token goes. The token stays off the screen, and out of any
    // recording of it.
    [Test]
    public async Task Login_asks_for_the_token_naming_the_api_host_and_shows_an_asterisk_for_each_character()
    {
        using var run = CliRun.Start();
        run.AtTerminal();
        run.AnswerLogin(TypedToken);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        var screen = run.Terminal.Output;
        Assert.Multiple(() =>
        {
            Assert.That(screen, Does.Contain($"{Question(run.ApiUrl)} {new string('*', TypedToken.Length)}"));
            Assert.That(screen, Does.Not.Contain(TypedToken));
            Assert.That(screen, Does.Not.Contain("[saved]"));
        });
    }

    // The questions go to the terminal, so stdout holds only the JSON ("Errors and exit codes"). The
    // list keeps the API's order, whatever order the picker showed. Enclave.Sdk.Api writes orgId as
    // 32 hex digits (Enclave.Sdk.Api.Data OrganisationGuid JSON converter).
    [Test]
    public async Task Login_prints_the_organisations_the_token_can_see_as_an_org_list_and_nothing_on_stderr()
    {
        using var run = CliRun.Start();
        BothOrgs.Stub(run);
        run.SaveCredentials(OldToken, defaultOrgId: TestData.OtherOrgId.ToString());
        run.AtTerminal();
        run.AnswerLogin(TypedToken);

        var result = await run.RunAsync("login");

        var items = CliAssert.List(result, "org");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "orgId"), Is.EqualTo($"{TestData.OrgId:N},{TestData.OtherOrgId:N}"));
            Assert.That(JsonRead.StringFieldList(items, "orgName"), Is.EqualTo($"{TestData.OrgName},{TestData.OtherOrgName}"));
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // Programs set ENCLAVE_CLI_PAT, and the error says so. stdin holds a token, so a CLI that read it
    // logs in; as a terminal, any read of it fails the run (CliRun.StdinIsTerminal).
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task Login_exits_3_with_token_missing_and_asks_nothing_unless_stdin_and_stderr_are_both_terminals(bool stdinIsTerminal, bool stderrIsTerminal)
    {
        using var run = CliRun.Start();
        run.AtTerminal();
        run.StdinIsTerminal = stdinIsTerminal;
        run.StderrIsTerminal = stderrIsTerminal;
        run.StdinText = TypedToken + "\n";
        run.AnswerLogin(TypedToken);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Rejected(run, result, "token_missing");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain("terminal").And.Contain("ENCLAVE_CLI_PAT"));
            Assert.That(run.Terminal.Output, Is.Empty);
            Assert.That(run.Files.Paths, Is.Empty);
        });
    }

    // Logging in again to change the default organisation needs no token. A file that names no
    // apiBaseUrl holds a token for production's address, where commands send it ("Options on every
    // command").
    [TestCase(true)]
    [TestCase(false)]
    public async Task Login_offers_the_token_saved_for_the_api_address_and_keeps_it_on_Enter(bool fileNamesTheAddress)
    {
        using var run = CliRun.Start();

        if (fileNamesTheAddress)
        {
            run.SaveCredentials(OldToken);
        }
        else
        {
            run.Files.WriteText(run.CredentialsPath, new JsonObject { ["personalAccessToken"] = OldToken }.ToJsonString(), privateToUser: true);
        }

        run.AtTerminal();
        run.AnswerLogin(string.Empty);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Terminal.Output, Does.Contain($"{Question(run.ApiUrl)} [saved]"));
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {OldToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(OldToken));
            Assert.That(JsonAssert.Property(JsonAssert.Property(result.StdoutTrace, "token"), "source").GetString(), Is.EqualTo(run.CredentialsPath));
        });
    }

    [Test]
    public async Task Login_replaces_the_saved_token_with_one_typed_at_the_question()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(OldToken);
        run.AtTerminal();
        run.AnswerLogin(TypedToken);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var request = await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", "login");

        Assert.Multiple(() =>
        {
            Assert.That(request.Authorization, Is.EqualTo($"Bearer {TypedToken}"));
            Assert.That(SavedToken(run), Is.EqualTo(TypedToken));
        });
    }

    // A token goes only to the environment it came with ("Options on every command"), so Enter
    // cannot send the token saved for another address to this one.
    [Test]
    public async Task Login_does_not_offer_a_token_saved_for_another_api_address()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(OldToken, OtherApi);
        var before = run.Files.ReadText(run.CredentialsPath);
        run.AtTerminal();
        run.AnswerLogin(string.Empty);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Rejected(run, result, "token_missing");
        Assert.Multiple(() =>
        {
            Assert.That(run.Terminal.Output, Does.Contain(Question(run.ApiUrl)).And.Not.Contain("[saved]"));
            Assert.That(run.Files.ReadText(run.CredentialsPath), Is.EqualTo(before));
        });
    }

    // Sending "Bearer " would turn a missing token into a 401 and a misleading token_invalid.
    [TestCase("")]
    [TestCase("   ")]
    public async Task Login_exits_3_with_token_missing_on_an_empty_answer_with_no_saved_token(string answer)
    {
        using var run = CliRun.Start();
        run.AtTerminal();
        run.AnswerLogin(answer);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Rejected(run, result, "token_missing");
        Assert.That(run.Files.Paths, Is.Empty);
    }

    // A refused token must not replace the saved one, which may be a working token, nor the saved
    // default. Enclave.Sdk.Api raises EnclaveApiException only for problem+json
    // (ProblemDetailsHttpMessageHandler.SendAsync), so the plain cases, a proxy answering without
    // problem details, reach the CLI as HttpRequestException.
    [TestCase(401, true, "token_invalid")]
    [TestCase(401, false, "token_invalid")]
    [TestCase(403, true, "forbidden")]
    [TestCase(403, false, "forbidden")]
    [TestCase(503, true, "transient")]
    public async Task Login_saves_nothing_and_shows_no_picker_when_the_check_fails(int status, bool problemDetails, string code)
    {
        using var run = CliRun.Start();
        run.SaveCredentials(OldToken, defaultOrgId: TestData.OtherOrgId.ToString());
        var before = run.Files.ReadText(run.CredentialsPath);
        run.AtTerminal();
        run.AnswerLogin(TypedToken);

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
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TypedToken}"));
            Assert.That(run.Files.ReadText(run.CredentialsPath), Is.EqualTo(before));
            Assert.That(run.Terminal.Output, Does.Not.Contain(NoDefault));
        });
    }

    // A partner's staff can see hundreds of organisations, so the saved default comes first, where
    // Enter keeps it.
    [Test]
    public async Task Login_lists_the_saved_default_first_and_No_default_last()
    {
        using var run = CliRun.Start();
        BothOrgs.Stub(run);
        run.SaveCredentials(OldToken, defaultOrgId: TestData.OtherOrgId.ToString());
        run.AtTerminal();
        run.AnswerLogin(TypedToken);

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        var screen = run.Terminal.Output;
        Assert.Multiple(() =>
        {
            Assert.That(screen.IndexOf(TestData.OtherOrgName, StringComparison.Ordinal), Is.GreaterThanOrEqualTo(0), screen);
            Assert.That(screen.IndexOf(TestData.OrgName, StringComparison.Ordinal), Is.GreaterThan(screen.IndexOf(TestData.OtherOrgName, StringComparison.Ordinal)), screen);
            Assert.That(screen.IndexOf(NoDefault, StringComparison.Ordinal), Is.GreaterThan(screen.IndexOf(TestData.OrgName, StringComparison.Ordinal)), screen);
            Assert.That(SavedOrgId(run), Is.EqualTo(TestData.OtherOrgId));
        });
    }

    // The saved default names an organisation this token does not see.
    [Test]
    public async Task Login_lists_the_organisations_in_the_api_s_order_when_the_token_does_not_see_the_saved_default()
    {
        using var run = CliRun.Start();
        BothOrgs.Stub(run);
        run.SaveCredentials(OldToken, defaultOrgId: Guid.NewGuid().ToString());
        run.AtTerminal();
        run.AnswerLogin(TypedToken);

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        var screen = run.Terminal.Output;
        Assert.Multiple(() =>
        {
            Assert.That(screen.IndexOf(TestData.OtherOrgName, StringComparison.Ordinal), Is.GreaterThan(screen.IndexOf(TestData.OrgName, StringComparison.Ordinal)), screen);
            Assert.That(SavedOrgId(run), Is.EqualTo(TestData.OrgId));
        });
    }

    // The default is saved as an ID, so a later command builds its client from it with no lookup
    // ("Context").
    [Test]
    public async Task Login_saves_the_organisation_picked_as_the_default_for_later_commands()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        BothOrgs.Stub(run);
        run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        run.AtTerminal();
        run.Type(TypedToken);
        run.Terminal.Input.PushKey(ConsoleKey.DownArrow);
        run.Terminal.Input.PushKey(ConsoleKey.Enter);

        CliAssert.Succeeded(await run.RunAsync("login"));

        Assert.That(SavedOrgId(run), Is.EqualTo(TestData.OtherOrgId));

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(before), Is.EqualTo(new[] { $"GET {OtherOrgSystems}" }));
            Assert.That(run.Requests[^1].Authorization, Is.EqualTo($"Bearer {TypedToken}"));
        });
    }

    // Type-to-search: typing part of a name moves the choice to the organisation that matches.
    [Test]
    public async Task Login_picks_the_organisation_whose_name_is_typed_at_the_picker()
    {
        using var run = CliRun.Start();
        BothOrgs.Stub(run);
        run.AtTerminal();
        run.Type(TypedToken);
        run.Terminal.Input.PushText("glo");
        run.Terminal.Input.PushKey(ConsoleKey.Enter);

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        Assert.That(SavedOrgId(run), Is.EqualTo(TestData.OtherOrgId));
    }

    // With no default and several organisations, a command given none exits 2 no_org, so it cannot
    // act on a customer by mistake.
    [Test]
    public async Task Login_with_No_default_removes_the_saved_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        BothOrgs.Stub(run);
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.SaveCredentials(OldToken, defaultOrgId: TestData.OrgId.ToString());
        run.AtTerminal();
        run.Type(TypedToken);
        run.Terminal.Input.PushKey(ConsoleKey.DownArrow);
        run.Terminal.Input.PushKey(ConsoleKey.DownArrow);
        run.Terminal.Input.PushKey(ConsoleKey.Enter);

        CliAssert.Succeeded(await run.RunAsync("login"));

        Assert.That(JsonRead.PropertyNames(SavedCredentials(run)), Does.Not.Contain("defaultOrgID"));

        var before = run.Requests.Count;
        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));
    }

    // There is nothing to pick from, and the saved default is no organisation the token sees.
    [Test]
    public async Task Login_shows_no_picker_and_removes_the_saved_organisation_when_the_token_sees_none()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs());
        run.SaveCredentials(OldToken, defaultOrgId: TestData.OrgId.ToString());
        run.AtTerminal();
        run.Type(TypedToken);

        var result = await run.RunAsync("login");

        var items = CliAssert.List(result, "org");
        Assert.Multiple(() =>
        {
            Assert.That(items.GetArrayLength(), Is.Zero);
            Assert.That(run.Terminal.Output, Does.Not.Contain(NoDefault));
            Assert.That(JsonRead.PropertyNames(SavedCredentials(run)), Does.Not.Contain("defaultOrgID"));
            Assert.That(SavedToken(run), Is.EqualTo(TypedToken));
        });
    }

    // login writes the addresses of the environment it logged in to ("Environments"). A
    // partnerApiBaseUrl already in the file went with the old login.
    [Test]
    public async Task Login_writes_the_token_and_production_s_api_address_to_credentials_json()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(OldToken, OtherApi, "https://other-partner-api.example");
        run.AtTerminal();
        run.AnswerLogin(TypedToken);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        var credentials = SavedCredentials(run);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(credentials), Is.EquivalentTo(CredentialsFields));
            Assert.That(JsonAssert.Property(credentials, "personalAccessToken").GetString(), Is.EqualTo(TypedToken));
            Assert.That(new Uri(JsonAssert.Property(credentials, "apiBaseUrl").GetString()!), Is.EqualTo(run.ApiUrl));
            Assert.That(SavedOrgId(run), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonRead.StringList(JsonAssert.Property(result.StdoutTrace, "saved")), Is.EqualTo(run.CredentialsPath));
        });
    }

    // The token does not expire (portal TokensApiController.Create issues it with TimeSpan.MaxValue),
    // so other local users must not read it. Storage/DiskFileStoreTests proves what a private write
    // does on disk.
    [Test]
    public async Task Login_writes_credentials_json_private_to_the_user()
    {
        using var run = CliRun.Start();
        run.AtTerminal();
        run.AnswerLogin(TypedToken);
        run.Stub("GET", "/account/orgs", json: OneOrg());

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        Assert.That(run.Files.IsPrivate(run.CredentialsPath), Is.True);
    }

    // Later commands send the token to the addresses saved beside it ("Environments").
    [Test]
    public async Task Login_staging_checks_the_token_at_staging_and_saves_both_of_staging_s_addresses()
    {
        using var run = CliRun.Start();
        using var staging = StagingFakes.Start(run);
        LoopbackApi.Stub(staging.Api, "GET", "/account/orgs", 200, OneOrg());
        run.Stub("GET", "/account/orgs", json: OneOrg());
        run.SaveCredentials(OldToken);
        run.AtTerminal();
        run.AnswerLogin(TypedToken);

        var result = await run.RunAsync("login", "--staging");

        var checks = LoopbackApi.Received(staging.Api);

        CliAssert.Succeeded(result);
        var credentials = SavedCredentials(run);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The token was checked at production's address.");
            Assert.That(checks.Select(request => request.Call), Is.EqualTo(OrgLookupOnly));
            Assert.That(checks.Select(request => request.Authorization), Is.EqualTo(new[] { $"Bearer {TypedToken}" }));
            Assert.That(LoopbackApi.Received(staging.PartnerApi), Is.Empty);
            Assert.That(run.Terminal.Output, Does.Contain(Question(run.StagingApiUrl)).And.Not.Contain("[saved]"));
            Assert.That(new Uri(JsonAssert.Property(credentials, "apiBaseUrl").GetString()!), Is.EqualTo(run.StagingApiUrl));
            Assert.That(new Uri(JsonAssert.Property(credentials, "partnerApiBaseUrl").GetString()!), Is.EqualTo(run.StagingPartnerApiUrl));
            Assert.That(JsonAssert.Property(result.StdoutTrace, "api").GetString(), Is.EqualTo(run.StagingApiUrl.AbsoluteUri));
        });
    }

    // login changes only local files ("Dry run"). The corrected run shows the rejection withheld the
    // question and the check.
    [Test]
    public async Task Login_does_not_take_dry_run()
    {
        using var run = CliRun.Start();
        run.AtTerminal();
        run.AnswerLogin(TypedToken);
        run.Stub("GET", "/account/orgs", json: OneOrg());
        string[] command = ["login"];

        CliAssert.Rejected(run, await run.RunAsync([.. command, "--dry-run"]));
        Assert.Multiple(() =>
        {
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
            Assert.That(run.Terminal.Output, Is.Empty);
        });

        await CliAssert.AcceptedAsync(run, "GET", "/account/orgs", command);
    }

    private static string OneOrg() => ApiJson.Orgs((TestData.OrgId, TestData.OrgName));

    // The port is part of the name only where it is not the scheme's default, as in the fakes'
    // addresses.
    private static string Question(Uri api) => $"Personal access token for {api.Authority}:";

    private static JsonElement SavedCredentials(CliRun run) =>
        JsonRead.Parse(run.Files.ReadText(run.CredentialsPath) ?? throw new AssertionException("login wrote no credentials.json."));

    private static string? SavedToken(CliRun run) =>
        JsonAssert.Property(SavedCredentials(run), "personalAccessToken").GetString();

    private static Guid SavedOrgId(CliRun run) =>
        Guid.Parse(JsonAssert.Property(SavedCredentials(run), "defaultOrgID").GetString()!, CultureInfo.InvariantCulture);
}
