using Enclave.Cli.Context;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Options on every command", "Errors and exit codes", "Login, logout and
// status".
public class TokenTests
{
    private const string FileToken = "file-token-2b8e61";

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string OtherOrgSystems = TestData.OtherOrgPath("systems");

    // Each runs to a point where the CLI holds the token: a failure comes after the API received it,
    // or after resolving the organisation.
    private static readonly string[] TokenRuns =
    [
        "login",
        "login --token-stdin",
        "login with a refused token",
        "logout",
        "status with ENCLAVE_TOKEN",
        "status with credentials.json",
        "status with no organisation chosen",
        "status with a refused token",
        "org list",
        "org use by name",
        "org use --id",
        "an organisation looked up by name",
        "several organisations and none chosen",
    ];

    // An agent session can use its own token without replacing the one saved for the user.
    [Test]
    public async Task ENCLAVE_TOKEN_takes_precedence_over_credentials_json()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(FileToken);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", OrgSystems, "system", "list");

        Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
    }

    // An empty ENCLAVE_TOKEN is what a CI job gets when its secret is missing ("Login, logout and
    // status").
    [TestCase(false)]
    [TestCase(true)]
    public async Task Credentials_json_supplies_the_token_when_ENCLAVE_TOKEN_is_unset_or_empty(bool empty)
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
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", OrgSystems, "system", "list");

        Assert.That(request.Authorization, Is.EqualTo($"Bearer {FileToken}"));
    }

    // Sending "Bearer " would turn a missing secret into a 401 and token_invalid.
    [Test]
    public async Task An_empty_ENCLAVE_TOKEN_with_no_credentials_file_exits_3_with_token_missing()
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_TOKEN"] = string.Empty;
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result, "token_missing");
    }

    // The CLI reads the file itself and passes EnclaveClientOptions to Enclave.Sdk.Api, so
    // ENCLAVE_TOKEN keeps the file's baseUrl ("Login, logout and status").
    [TestCase(false)]
    [TestCase(true)]
    public async Task Credentials_json_base_url_is_the_api_address_whichever_source_supplies_the_token(bool tokenFromEnvironment)
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", OrgSystems, 200, ApiJson.Page());
        run.SaveCredentials(FileToken, otherUrl);

        if (!tokenFromEnvironment)
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
        }

        var result = await run.RunAsync("system", "list");

        var received = LoopbackApi.Received(otherApi);
        var expectedToken = tokenFromEnvironment ? TestData.Token : FileToken;

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The request went to the default API address.");
            Assert.That(received.Select(request => request.Call), Is.EqualTo(new[] { $"GET {OrgSystems}" }));
            Assert.That(received.Select(request => request.Authorization), Is.EqualTo(new[] { $"Bearer {expectedToken}" }));
        });
    }

    // Enclave.Sdk.Api builds the partner API's HttpClient when the EnclaveClient is made
    // (EnclaveClient.SetupPartnerHttpClient), so a bad address fails every command, partner or not.
    [TestCase("not a url")]
    [TestCase("ftp://partner-api.example")]
    public async Task A_partner_api_base_url_that_is_not_an_http_or_https_url_exits_2_naming_the_file(string partnerApiBaseUrl)
    {
        using var run = CliRun.Start();
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.SaveCredentials(FileToken, partnerApiBaseUrl: partnerApiBaseUrl);

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result);
        Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain(run.CredentialsPath).And.Contain("partnerApiBaseUrl"));
    }

    // Nothing is sent, the lookup a name makes included.
    [TestCase("system", "list")]
    [TestCase("system", "list", "--org", TestData.OrgName)]
    [TestCase("org", "list")]
    [TestCase("org", "use", TestData.OrgName)]
    [TestCase("status")]
    public async Task A_command_exits_3_with_token_missing_and_sends_nothing_when_there_is_no_token(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result, "token_missing");
    }

    // "Errors and exit codes". Each choice would otherwise give no_org, a lookup, or exit 2 with no
    // call. The choices that exit 2 need no client, so a CLI that chose before reading the token
    // reports them.
    [TestCaseSource(nameof(ContextChoices))]
    public async Task The_token_is_checked_before_the_organisation_and_the_partner(string[] args, string choice)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        switch (choice)
        {
            case "none chosen":
                break;
            case "ENCLAVE_ORG":
                run.Environment["ENCLAVE_ORG"] = TestData.OrgName;
                break;
            case "ENCLAVE_ORG with ENCLAVE_ORG_ID":
                run.Environment["ENCLAVE_ORG"] = TestData.OrgName;
                run.Environment["ENCLAVE_ORG_ID"] = TestData.OrgId.ToString();
                break;
            case "ENCLAVE_ORG_ID that is not a GUID":
                run.Environment["ENCLAVE_ORG_ID"] = "not-a-guid";
                break;
            case "ENCLAVE_PARTNER_ID that is not a GUID":
                run.Environment["ENCLAVE_PARTNER_ID"] = "12";
                break;
            case "cli.json that is not JSON":
                run.Files.WriteText(run.CliConfigPath, "{ not json", privateToUser: false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(choice), choice, "No arrangement for this choice.");
        }

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result, "token_missing");
    }

    // The caller fixes the command line first, and the error does not depend on the machine's
    // credentials ("Errors and exit codes").
    [TestCaseSource(nameof(BadArgumentRuns))]
    public async Task A_command_with_bad_arguments_fails_the_same_way_with_or_without_a_token(string[] args)
    {
        using var withToken = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");

        foreach (var run in new[] { withToken, withoutToken })
        {
            BothOrgs.Stub(run);
            run.Stub("GET", OrgSystems, json: ApiJson.Page());
            run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        }

        var first = await withToken.RunAsync(args);
        var second = await withoutToken.RunAsync(args);

        CliAssert.Rejected(withToken, first);
        CliAssert.Rejected(withoutToken, second);
        Assert.That(second.Error.GetRawText(), Is.EqualTo(first.Error.GetRawText()));
    }

    // A token given as an argument ends up in shell history, process listings, CI logs and agent
    // transcripts, and does not expire (portal TokensApiController.Create). ENCLAVE_TOKEN is unset
    // and no file is saved, so a CLI that took the option as a token source runs the command.
    [TestCase("login", "--token", TestData.Token)]
    [TestCase("status", "--token", TestData.Token)]
    [TestCase("org", "list", "--token", TestData.Token)]
    [TestCase("system", "list", "--token", TestData.Token)]
    public async Task Token_is_not_an_option_and_the_error_does_not_repeat_its_value(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Stderr, Does.Not.Contain(TestData.Token));
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
        });
    }

    // A printed token lands in CI logs and agent transcripts. The token is the same from every
    // source, so one check covers them all. A run that calls the API writes diagnostics ("Details"),
    // which the check covers.
    [TestCaseSource(nameof(TokenRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr(string name)
    {
        using var run = CliRun.Start();
        var command = Arrange(run, name);

        var result = await run.RunAsync(command.Args);

        var requests = run.Requests;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(command.ExitCode), result.ToString());
            Assert.That(result.Stdout, Does.Not.Contain(TestData.Token));
            Assert.That(result.Stderr, Does.Not.Contain(TestData.Token));

            if (command.SendsRequests)
            {
                Assert.That(requests, Is.Not.Empty);
                Assert.That(requests.Select(request => request.Authorization), Is.All.EqualTo($"Bearer {TestData.Token}"));
                Assert.That(result.Diagnostics, Is.Not.Empty, result.ToString());
            }
            else
            {
                Assert.That(requests, Is.Empty);
            }
        });
    }

    // ToString is what interpolation, an exception message or a diagnostic prints. Every value is
    // given, so a text that lists them all shows the token.
    [Test]
    public void Saved_credentials_leave_the_token_out_of_their_text()
    {
        var credentials = new StoredCredentials(TestData.Token, "https://api.example", "https://partner-api.example", true);

        Assert.That(credentials.ToString(), Does.Not.Contain(TestData.Token));
    }

    // Bad arguments of each kind in this area. status succeeds with no organisation or partner
    // chosen, so a CLI that read a blank ID as not given would print its status.
    private static IEnumerable<TestCaseData> BadArgumentRuns()
    {
        yield return BadArguments("system", "list", "--org-id", "12");
        yield return BadArguments("system", "list", "--org", TestData.OtherOrgName, "--org-id", TestData.OtherOrgId.ToString());
        yield return BadArguments("system", "list", "--no-such-option");
        yield return BadArguments("status", "--org-id", TestData.OrgName);
        yield return BadArguments("org", "use");
        yield return BadArguments("org", "use", "--id", "12");
        yield return BadArguments("org", "list", "--dry-run");
        yield return BadArguments("login", "--dry-run");
        yield return BadArguments("system", "list", "--org", string.Empty);
        yield return BadArguments("system", "list", "--org", "  ");
        yield return BadArguments("status", "--org", string.Empty);
        yield return BadArguments("status", "--org-id", string.Empty);
        yield return BadArguments("status", "--partner-id", " ");
        yield return BadArguments("commands", "system", "list", "--search-keys", "--org", " ");
        yield return BadArguments("org", "use", string.Empty);
        yield return BadArguments("org", "use", " \t ");
    }

    // status reads both the organisation and the partner; a partner command reads cli.json for its
    // partner.
    private static IEnumerable<TestCaseData> ContextChoices()
    {
        string[] systemList = ["system", "list"];
        string[] status = ["status"];
        string[] partnerList = ["partner", "customer", "list"];
        string[] organisationChoices = ["none chosen", "ENCLAVE_ORG", "ENCLAVE_ORG with ENCLAVE_ORG_ID", "ENCLAVE_ORG_ID that is not a GUID", "cli.json that is not JSON"];

        foreach (var choice in organisationChoices)
        {
            yield return ContextChoice(systemList, choice);
            yield return ContextChoice(status, choice);
        }

        yield return ContextChoice(status, "ENCLAVE_PARTNER_ID that is not a GUID");
        yield return ContextChoice(partnerList, "ENCLAVE_PARTNER_ID that is not a GUID");
        yield return ContextChoice(partnerList, "cli.json that is not JSON");
    }

    private static TestCaseData ContextChoice(string[] args, string choice) =>
        new TestCaseData(args, choice).SetArgDisplayNames(string.Join(" ", args), choice);

    // An empty or blank argument is shown quoted, so the cases with one have distinct names.
    private static TestCaseData BadArguments(params string[] args) =>
        new TestCaseData((object)args).SetArgDisplayNames(string.Join(" ", args.Select(arg => string.IsNullOrWhiteSpace(arg) ? $"'{arg}'" : arg)));

    private static CommandRun Arrange(CliRun run, string name)
    {
        var oneOrg = ApiJson.Orgs((TestData.OrgId, TestData.OrgName));

        switch (name)
        {
            case "login":
                run.Stub("GET", "/account/orgs", json: oneOrg);
                return Command(0, "login");
            case "login --token-stdin":
                run.Environment.Remove("ENCLAVE_TOKEN");
                run.StdinText = TestData.Token + "\n";
                run.Stub("GET", "/account/orgs", json: oneOrg);
                return Command(0, "login", "--token-stdin");
            case "login with a refused token":
                run.StubProblem("GET", "/account/orgs", 401, "Unauthorized");
                return Command(3, "login");
            case "logout":
                run.SaveCredentials(TestData.Token);
                return LocalCommand("logout");
            case "status with ENCLAVE_TOKEN":
                BothOrgs.Stub(run);
                return Command(0, "status");
            case "status with credentials.json":
                run.Environment.Remove("ENCLAVE_TOKEN");
                run.SaveCredentials(TestData.Token);
                BothOrgs.Stub(run);
                return Command(0, "status");
            case "status with no organisation chosen":
                run.Environment.Remove("ENCLAVE_ORG_ID");
                BothOrgs.Stub(run);
                return Command(0, "status");
            case "status with a refused token":
                run.StubProblem("GET", "/account/orgs", 401, "Unauthorized");
                return Command(3, "status");
            case "org list":
                BothOrgs.Stub(run);
                return Command(0, "org", "list");
            case "org use by name":
                BothOrgs.Stub(run);
                return Command(0, "org", "use", TestData.OtherOrgName);
            case "org use --id":
                BothOrgs.Stub(run);
                return Command(0, "org", "use", "--id", TestData.OtherOrgId.ToString());
            case "an organisation looked up by name":
                BothOrgs.Stub(run);
                run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
                return Command(0, "system", "list", "--org", TestData.OtherOrgName);
            case "several organisations and none chosen":
                run.Environment.Remove("ENCLAVE_ORG_ID");
                BothOrgs.Stub(run);
                return Command(2, "system", "list");
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "No arrangement for this command.");
        }
    }

    private static CommandRun Command(int exitCode, params string[] args) => new(args, exitCode, SendsRequests: true);

    private static CommandRun LocalCommand(params string[] args) => new(args, 0, SendsRequests: false);

    private sealed record CommandRun(string[] Args, int ExitCode, bool SendsRequests);
}
