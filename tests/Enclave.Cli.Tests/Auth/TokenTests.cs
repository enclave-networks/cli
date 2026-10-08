using Enclave.Cli.Context;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using WireMock;

namespace Enclave.Cli.Tests.Auth;

// Where the token comes from, when it is checked, and that no command of this area prints it
// (proposed-cli-surface.md "Options on every command", "Errors and exit codes" and "Login, logout
// and status").
public class TokenTests
{
    private const string FileToken = "file-token-2b8e61";

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string OtherOrgSystems = TestData.OtherOrgPath("systems");

    // Commands of this area, each run with --verbose to the point where the CLI holds the token:
    // every request it sends carries the token, and the failures are ones the CLI reports after
    // the API received it, or after resolving the organisation. Arrange sets each one up.
    private static readonly string[] VerboseRuns =
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

    // The token comes from ENCLAVE_TOKEN, then credentials.json ("Options on every command"), so an
    // agent session can use its own token without replacing the one saved for the user.
    [Test]
    public async Task ENCLAVE_TOKEN_takes_precedence_over_credentials_json()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(FileToken);
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", OrgSystems, "system", "list");

        Assert.That(request.Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
    }

    // An empty ENCLAVE_TOKEN counts as unset ("Login, logout and status"), the value a CI job gets
    // when its secret is missing, so the saved file supplies the token then too.
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

    // An empty ENCLAVE_TOKEN with no saved file leaves no token, so the CLI reports token_missing
    // before any call. Sending "Bearer " would turn a missing secret into a 401 and token_invalid.
    [Test]
    public async Task An_empty_ENCLAVE_TOKEN_with_no_credentials_file_exits_3_with_token_missing()
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_TOKEN"] = string.Empty;
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result, "token_missing");
    }

    // credentials.json's baseUrl is the API address, and ENCLAVE_TOKEN keeps it ("Login, logout and
    // status": the CLI reads the file itself and passes EnclaveClientOptions to Enclave.Sdk.Api). A
    // second fake API at another address shows where the request went; the default API receives
    // nothing.
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

        var received = otherApi.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>().ToArray();
        var expectedToken = tokenFromEnvironment ? TestData.Token : FileToken;

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The request went to the default API address.");
            Assert.That(received.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[] { $"GET {OrgSystems}" }));
            Assert.That(received.Select(Authorization), Is.EqualTo(new[] { $"Bearer {expectedToken}" }));
        });
    }

    // Enclave.Sdk.Api builds the partner API's HttpClient when the EnclaveClient is made, from
    // new Uri(PartnerApiBaseUrl) (EnclaveClient.SetupPartnerHttpClient, Enclave.Sdk.Api 1.1.0), so
    // an address that is not a URL fails every command. The CLI reports it as a bad setting naming
    // the file, as it does a bad baseUrl, before any call.
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

    // With no token there is nothing to authenticate with, so the CLI reports token_missing (exit 3,
    // "Errors and exit codes") before any call, including the lookup a name or no choice of
    // organisation makes. The commands cover the organisation commands and status, and a command
    // that acts in an organisation given by ID and by name.
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

    // Checks run in this order: arguments, the token, the organisation, then the call ("Errors and
    // exit codes"). With no token and an organisation choice that would end in no_org (none chosen,
    // several organisations) or a lookup (a name in ENCLAVE_ORG), the CLI reports token_missing and
    // makes no lookup.
    [TestCase("none chosen")]
    [TestCase("ENCLAVE_ORG")]
    public async Task The_token_is_checked_before_the_organisation(string choice)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", OrgSystems, json: ApiJson.Page());

        if (choice == "ENCLAVE_ORG")
        {
            run.Environment["ENCLAVE_ORG"] = TestData.OrgName;
        }

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result, "token_missing");
    }

    // Arguments are checked before the token, so a command with bad arguments fails the same way
    // with or without a token ("Errors and exit codes"): the caller fixes the command line first,
    // and the error does not depend on the machine's credentials. Each case is run with the token
    // and without one, and both runs must give the same error and send nothing.
    [TestCaseSource(nameof(BadArgumentRuns))]
    public async Task A_command_with_bad_arguments_fails_the_same_way_with_or_without_a_token(string[] args)
    {
        using var withToken = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");

        foreach (var run in new[] { withToken, withoutToken })
        {
            run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
            run.Stub("GET", OrgSystems, json: ApiJson.Page());
            run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
        }

        var first = await withToken.RunAsync(args);
        var second = await withoutToken.RunAsync(args);

        CliAssert.Rejected(withToken, first);
        CliAssert.Rejected(withoutToken, second);
        Assert.That(second.Error.GetRawText(), Is.EqualTo(first.Error.GetRawText()));
    }

    // There is no --token option ("Options on every command"): a token given as an argument ends up
    // in shell history, process listings, CI logs and agent transcripts, and personal access tokens
    // do not expire (portal Enclave.Accounts/Controllers/Api/TokensApiController.cs:105). The option
    // is a parse error, exit 2, and the error never repeats the value given to an unknown option,
    // since that value can be a token ("Errors and exit codes"). ENCLAVE_TOKEN is unset and no file
    // is saved, so a CLI that took the option as a token source runs the command.
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

    // No command prints the token, under --verbose either ("Login, logout and status"); a printed
    // token lands in CI logs and agent transcripts, and personal access tokens do not expire. The
    // token is the same in every case, whether it comes from ENCLAVE_TOKEN, stdin or the file, so
    // one check covers every source.
    [TestCaseSource(nameof(VerboseRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_under_verbose(string name)
    {
        using var run = CliRun.Start();
        var command = Arrange(run, name);

        var result = await run.RunAsync([.. command.Args, "--verbose"]);

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
            }
            else
            {
                Assert.That(requests, Is.Empty);
            }
        });
    }

    // A type's ToString is what string interpolation, an exception message, a debugger or a
    // diagnostic prints of it, so the saved credentials leave the token out of their text: no
    // command prints the token ("Login, logout and status"). The base URLs and the token are all
    // given, so a text that lists every value shows the token.
    [Test]
    public void Saved_credentials_leave_the_token_out_of_their_text()
    {
        var credentials = new StoredCredentials(TestData.Token, "https://api.example", "https://partner-api.example", true);

        Assert.That(credentials.ToString(), Does.Not.Contain(TestData.Token));
    }

    // Bad arguments of each kind this area has: an ID that is not a GUID, a name with its ID option,
    // an unknown option, a missing argument, and an option the command does not take ("Details").
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
    }

    private static TestCaseData BadArguments(params string[] args) =>
        new TestCaseData((object)args).SetArgDisplayNames(string.Join(" ", args));

    // Stubs what each command needs to reach its outcome, and returns its arguments and exit code.
    private static CommandRun Arrange(CliRun run, string name)
    {
        var oneOrg = ApiJson.Orgs((TestData.OrgId, TestData.OrgName));
        var bothOrgs = ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName));

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
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                return Command(0, "status");
            case "status with credentials.json":
                run.Environment.Remove("ENCLAVE_TOKEN");
                run.SaveCredentials(TestData.Token);
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                return Command(0, "status");
            case "status with no organisation chosen":
                run.Environment.Remove("ENCLAVE_ORG_ID");
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                return Command(0, "status");
            case "status with a refused token":
                run.StubProblem("GET", "/account/orgs", 401, "Unauthorized");
                return Command(3, "status");
            case "org list":
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                return Command(0, "org", "list");
            case "org use by name":
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                return Command(0, "org", "use", TestData.OtherOrgName);
            case "org use --id":
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                return Command(0, "org", "use", "--id", TestData.OtherOrgId.ToString());
            case "an organisation looked up by name":
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                run.Stub("GET", OtherOrgSystems, json: ApiJson.Page());
                return Command(0, "system", "list", "--org", TestData.OtherOrgName);
            case "several organisations and none chosen":
                run.Environment.Remove("ENCLAVE_ORG_ID");
                run.Stub("GET", "/account/orgs", json: bothOrgs);
                return Command(2, "system", "list");
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "No arrangement for this command.");
        }
    }

    private static CommandRun Command(int exitCode, params string[] args) => new(args, exitCode, SendsRequests: true);

    private static CommandRun LocalCommand(params string[] args) => new(args, 0, SendsRequests: false);

    private static string? Authorization(IRequestMessage request) =>
        request.Headers?
            .Where(header => string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            .Select(header => string.Join(", ", header.Value))
            .FirstOrDefault();

    private sealed record CommandRun(string[] Args, int ExitCode, bool SendsRequests);
}
