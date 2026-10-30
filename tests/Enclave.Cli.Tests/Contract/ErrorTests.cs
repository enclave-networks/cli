using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// An agent decides what to do next from the exit code and the error's "code", so each API failure
// maps to fixed ones (spec/cli-surface.md "Errors and exit codes"). CliAssert.Failed also checks
// that stdout is empty and stderr holds one JSON error.
public class ErrorTests
{
    private const string ValidationError = "The search term must be 100 characters or fewer.";

    private const string CancelInvite = "org cancel-invite sam@acme.example";

    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string[] ErrorOnly = ["error"];

    private static readonly string[] ErrorFields = ["code", "status", "title", "detail", "errors"];

    private static readonly string[] ValidationErrors = [ValidationError];

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    private static readonly string[] IdOptionOnly = ["--id"];

    // Each item is an ID, or an invite's email address, which the API cancels by (Enclave.Sdk.Api
    // OrganisationScopedClient.CancelInviteAync), so no lookup comes first and the failing request
    // is the command's own ("Several IDs").
    public static IEnumerable<TestCaseData> SingleIdCommands()
    {
        yield return SingleId("system show ABCDE", "GET", "systems/ABCDE");
        yield return SingleId("system show ABCDE --pending", "GET", "unapproved-systems/ABCDE");
        yield return SingleId("key show --id 12", "GET", "enrolment-keys/12");
        yield return SingleId("policy show --id 7", "GET", "policies/7");
        yield return SingleId("tag show web", "GET", "tags/web");
        yield return SingleId("trust show --id 5", "GET", "trust-requirements/5");
        yield return SingleId("dns show-zone --id 4", "GET", "dns/zones/4");
        yield return SingleId("dns show-hostname --id 7", "GET", "dns/records/7");
        yield return SingleId("policy update --id 7 --notes Reviewed", "PATCH", "policies/7");
        yield return SingleId("policy enable --id 7 --for 8h", "PUT", "policies/7/enable-until");
        yield return SingleId("dns delete-zone --id 4", "DELETE", "dns/zones/4");
        yield return SingleId(CancelInvite, "DELETE", "invites");
    }

    // Each is wrong before any call: a bad ID ("ID checks"), a value outside the option's set
    // ("Options on every command"), and two options that contradict each other ("Details").
    public static IEnumerable<TestCaseData> BadArguments()
    {
        yield return CommandCases.Described("a bad ID", "system", "show", "../systems/ABCDE");
        yield return CommandCases.Described("a value outside the set", "system", "list", "--sort", "newest");
        yield return CommandCases.Described("--for with --until", "policy", "enable", "--id", "42", "--for", "8h", "--until", "2099-01-01T00:00:00Z");
        yield return CommandCases.Described("--id with no IDs", "policy", "delete", "--id", ",");
    }

    // None of these values holds an ID.
    public static IEnumerable<TestCaseData> EmptyIdOptions() =>
        BulkCommand.All
            .Where(command => command.TakesIdOption)
            .SelectMany(command => new[] { string.Empty, ",", " , " }
                .Select(value => new TestCaseData(command, value).SetArgDisplayNames(command.Name, $"--id \"{value}\"")));

    // A list is not a single-ID command, so its 404 is api_error. errors is an object even when the
    // problem has none, so a caller reads every error in one shape (AGENTS.md "CLI contract").
    [TestCase(401, "token_invalid")]
    [TestCase(403, "forbidden")]
    [TestCase(429, "transient")]
    [TestCase(500, "transient")]
    [TestCase(502, "transient")]
    [TestCase(503, "transient")]
    [TestCase(504, "transient")]
    [TestCase(400, "api_error")]
    [TestCase(404, "api_error")]
    [TestCase(409, "api_error")]
    [TestCase(422, "api_error")]
    public async Task A_problem_response_exits_with_the_code_for_its_status_and_carries_the_problem_through(int status, string code)
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", SystemsPath, status, "Problem title", "Problem detail");

        var result = await run.RunAsync("system", "list");

        AssertSentOne(run, "GET", SystemsPath);
        var error = CliAssert.Failed(result, code);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(result.StderrJson[0]), Is.EqualTo(ErrorOnly));
            Assert.That(JsonRead.PropertyNames(error), Is.EquivalentTo(ErrorFields));
            Assert.That(error.GetProperty("errors").ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(JsonRead.PropertyNames(error.GetProperty("errors")), Is.Empty);
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(status));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("Problem title"));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo("Problem detail"));
        });
    }

    // not_found tells the caller the item it named does not exist ("Several IDs").
    [TestCaseSource(nameof(SingleIdCommands))]
    public async Task A_problem_404_on_a_single_id_command_exits_5_with_not_found(string command, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.StubProblem(method, path, 404, "Not Found", "No such item.");

        var result = await run.RunAsync(command.Split(' '));

        AssertSentOne(run, method, path);
        var error = CliAssert.Failed(result, "not_found");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("Not Found"));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo("No such item."));
        });
    }

    // "errors" is an RFC 9457 extension member that Enclave.Sdk.Api reads into
    // ProblemDetails.Errors. Its keys pass through unchanged, so an agent can fix the named field.
    [Test]
    public async Task A_validation_problem_exits_1_with_api_error_carrying_the_errors_through()
    {
        using var run = CliRun.Start();
        var problem = new JsonObject
        {
            ["type"] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            ["title"] = "One or more validation errors occurred.",
            ["status"] = 400,
            ["detail"] = "The search term is too long.",
            ["errors"] = new JsonObject
            {
                ["Search"] = new JsonArray(ValidationError),
            },
        };
        run.StubRaw("GET", SystemsPath, 400, "application/problem+json", problem.ToJsonString());

        var result = await run.RunAsync("system", "list");

        AssertSentOne(run, "GET", SystemsPath);
        var error = CliAssert.Failed(result, "api_error");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(400));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("One or more validation errors occurred."));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo("The search term is too long."));
            Assert.That(JsonAssert.Strings(error.GetProperty("errors").GetProperty("Search")), Is.EqualTo(ValidationErrors));
        });
    }

    // A proxy in front of the API answers this way. Enclave.Sdk.Api throws EnclaveApiException
    // only for problem+json (ProblemDetailsHttpMessageHandler), so these reach the CLI as
    // HttpRequestException.
    [TestCase(401, "text/plain", "Unauthorized", "token_invalid")]
    [TestCase(401, "application/json", """{ "message": "Unauthorized" }""", "token_invalid")]
    [TestCase(403, "text/plain", "Forbidden", "forbidden")]
    [TestCase(429, "text/plain", "Too Many Requests", "transient")]
    [TestCase(500, "text/html", "<html><body><h1>500 Internal Server Error</h1></body></html>", "transient")]
    [TestCase(502, "text/html", "<html><body><h1>502 Bad Gateway</h1></body></html>", "transient")]
    [TestCase(503, "text/html", "<html><body><h1>503 Service Unavailable</h1></body></html>", "transient")]
    [TestCase(504, "text/html", "<html><body><h1>504 Gateway Timeout</h1></body></html>", "transient")]
    [TestCase(400, "text/plain", "Bad Request", "api_error")]
    [TestCase(404, "text/html", "<html><body><h1>404 Not Found</h1></body></html>", "api_error")]
    public async Task A_response_that_is_not_problem_details_exits_with_the_code_for_its_status(int status, string contentType, string body, string code)
    {
        using var run = CliRun.Start();
        run.StubRaw("GET", SystemsPath, status, contentType, body);

        var result = await run.RunAsync("system", "list");

        AssertSentOne(run, "GET", SystemsPath);
        CliAssert.Failed(result, code);
    }

    [TestCaseSource(nameof(SingleIdCommands))]
    public async Task A_404_that_is_not_problem_details_on_a_single_id_command_exits_5_with_not_found(string command, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.StubRaw(method, path, 404, "text/html", "<html><body><h1>404 Not Found</h1></body></html>");

        var result = await run.RunAsync(command.Split(' '));

        AssertSentOne(run, method, path);
        CliAssert.Failed(result, "not_found");
    }

    // A connection failure is transient: retrying can succeed once the API is reachable. Stopping
    // the fake API leaves its loopback port closed, so the connection is refused.
    [Test]
    public async Task An_unreachable_api_exits_6_with_transient()
    {
        using var run = CliRun.Start();
        run.Api.Stop();

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "transient");
    }

    // HttpClient.Timeout throws TaskCanceledException with an inner TimeoutException (.NET 5 and
    // later, https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.timeout). A loopback
    // fake cannot keep HttpClient waiting 100 seconds, so the network beneath the CLI's handler
    // throws it. detail sets a timeout apart from the other transient failures.
    [Test]
    public async Task A_request_that_times_out_exits_6_with_transient()
    {
        using var run = CliRun.Start();
        var network = new FailingNetwork(() => new TaskCanceledException(
            "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
            new TimeoutException("The operation was canceled.")));
        run.NetworkHandler = network.CreateHandler;

        var result = await run.RunAsync("system", "list");

        var error = CliAssert.Failed(result, "transient");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo("The Enclave API did not answer in time."));
            Assert.That(network.Calls, Is.EqualTo(new[] { $"GET {SystemsPath}" }));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // The fake promises 1000 bytes of body, sends 10, then ends the connection with a FIN (a server
    // that stops part-way) or a RST (a dropped connection). Enclave.Sdk.Api reads a page with
    // HttpClient.GetFromJsonAsync, which reads the body after the headers arrive
    // (HttpCompletionOption.ResponseHeadersRead), so this failure escapes the HttpRequestException
    // that SendAsync throws.
    [TestCase("ends early")]
    [TestCase("is reset")]
    public async Task A_connection_that_fails_while_the_response_body_is_read_exits_6_with_transient(string connection)
    {
        using var run = CliRun.Start();
        using var api = new BrokenBodyApi(reset: connection == "is reset");
        run.SaveCredentials(TestData.Token, baseUrl: api.Url);

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "transient");
        Assert.That(api.Requests, Is.EqualTo(new[] { $"GET {SystemsPath}" }));
    }

    // Only "-" with an empty list succeeds with nothing to do ("Several IDs"). The corrected run
    // shows the command and option exist and the rejection withheld the bulk call.
    [TestCaseSource(nameof(EmptyIdOptions))]
    public async Task A_bulk_command_given_an_id_option_with_no_ids_exits_2_and_sends_nothing(BulkCommand command, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var run = CliRun.Start();
        command.StubBulk(run, 1);

        var rejected = await run.RunAsync([.. command.Name.Split(' '), "--id", value]);

        CliAssert.Rejected(run, rejected);
        Assert.That(JsonRead.PropertyNames(rejected.Error.GetProperty("errors")), Is.EqualTo(IdOptionOnly));

        var accepted = await run.RunAsync(command.Args(command.Ids(1)));

        CliAssert.Bulk(accepted, requested: 1, affected: 1);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(command.Path));
    }

    // Arguments are checked before the token and the organisation ("Errors and exit codes").
    [TestCaseSource(nameof(BadArguments))]
    public async Task Bad_arguments_fail_the_same_way_with_or_without_a_token_and_organisation(string[] args)
    {
        using var withContext = CliRun.Start();
        using var withoutContext = CliRun.Start();
        withContext.Time = TestData.Clock;
        withoutContext.Time = TestData.Clock;
        withoutContext.Environment.Remove("ENCLAVE_TOKEN");
        withoutContext.Environment.Remove("ENCLAVE_ORG_ID");

        var withResult = await withContext.RunAsync(args);
        var withoutResult = await withoutContext.RunAsync(args);

        CliAssert.Rejected(withContext, withResult);
        CliAssert.Rejected(withoutContext, withoutResult);
        Assert.That(withoutResult.Stderr, Is.EqualTo(withResult.Stderr));
    }

    // With no organisation chosen the CLI would look the token's organisations up, which needs the
    // token, so the missing token is reported first and nothing is sent.
    [Test]
    public async Task A_missing_token_is_reported_before_the_organisation_is_chosen()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG_ID");

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result, "token_missing");
    }

    // Checks run in the order arguments, token, partner, call ("Errors and exit codes").
    [Test]
    public async Task A_missing_token_is_reported_before_the_partner_is_chosen()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Rejected(run, result, "token_missing");
    }

    [Test]
    public async Task A_partner_that_was_not_chosen_is_reported_before_the_call()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Rejected(run, result, "no_partner");
    }

    // The token sees two organisations and none is chosen ("Context").
    [Test]
    public async Task An_organisation_that_was_not_chosen_is_reported_before_the_call()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        BothOrgs.Stub(run);
        run.StubPages(SystemsPath, 200, ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
    }

    private static TestCaseData SingleId(string command, string method, string pathSuffix) =>
        new TestCaseData(command, method, TestData.OrgPath(pathSuffix)).SetArgDisplayNames(command);

    private static void AssertSentOne(CliRun run, string method, string path)
    {
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
        });
    }
}
