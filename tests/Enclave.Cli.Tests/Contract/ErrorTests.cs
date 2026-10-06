using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Enclave.Cli.Tests.Contract;

// An agent decides what to do next from the exit code and the "code" of the one JSON error on
// stderr, so each API failure maps to a fixed exit code and code (proposal "Errors and exit codes").
//
// On every error stdout is empty and stderr holds exactly one JSON line with an "error" object
// (CliAssert.Failed checks both), so a caller never parses a partial result.
[Category(TestCategory.Pending)]
public class ErrorTests
{
    private const string SearchError = "The search term must be 100 characters or fewer.";

    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string[] SearchErrors = [SearchError];

    public static IEnumerable<TestCaseData> SingleIdCommands()
    {
        yield return SingleId("system show SYS7001", "systems/SYS7001");
        yield return SingleId("pending show SYS7001", "unapproved-systems/SYS7001");
        yield return SingleId("key show 7001", "enrolment-keys/7001");
        yield return SingleId("policy show 7001", "policies/7001");
        yield return SingleId("tag show web", "tags/web");
        yield return SingleId("trust show 7001", "trust-requirements/7001");
        yield return SingleId("dns zone show 7001", "dns/zones/7001");
        yield return SingleId("dns record show 7001", "dns/records/7001");
    }

    // Enclave.Sdk.Api throws EnclaveApiException for application/problem+json responses
    // (Handlers/ProblemDetailsHttpMessageHandler.cs:20, version 1.0.4). The error carries the
    // problem's status, title and detail through, so the caller sees what the API said.
    [TestCase(401, "token_invalid", 3)]
    [TestCase(403, "forbidden", 4)]
    [TestCase(429, "transient", 7)]
    [TestCase(500, "transient", 7)]
    [TestCase(502, "transient", 7)]
    [TestCase(503, "transient", 7)]
    [TestCase(400, "api_error", 1)]
    [TestCase(409, "api_error", 1)]
    public async Task Problem_responses_exit_with_the_code_for_their_status_and_carry_the_problem_through(int status, string code, int exitCode)
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", SystemsPath, status, "Problem title", "Problem detail");

        var result = await run.RunAsync("system", "list");

        AssertSentOneGet(run, SystemsPath);
        var error = CliAssert.Failed(result, exitCode, code);
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(status));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("Problem title"));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo("Problem detail"));
        });
    }

    // not_found means the ID is unknown, so a 404 on a command given one ID exits 5 (proposal
    // "Several IDs": single-ID commands exit 5 for an unknown ID).
    [TestCaseSource(nameof(SingleIdCommands))]
    public async Task A_problem_404_on_a_single_id_command_exits_5_with_not_found(string command, string path)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.StubProblem("GET", path, 404, "Not Found", "No such item.");

        var result = await run.RunAsync(command.Split(' '));

        AssertSentOneGet(run, path);
        var error = CliAssert.Failed(result, 5, "not_found");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("Not Found"));
        });
    }

    // Validation failures list the offending fields in the problem's "errors" (RFC 9457 extension
    // member, read by Enclave.Sdk.Api into ProblemDetails.Errors). The CLI passes them through with
    // their keys unchanged, so an agent can fix the named field.
    [Test]
    public async Task A_problem_400_exits_1_with_api_error_carrying_status_title_detail_and_errors()
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
                ["Search"] = new JsonArray(SearchError),
            },
        };
        StubRaw(run, SystemsPath, 400, "application/problem+json", problem.ToJsonString());

        var result = await run.RunAsync("system", "list");

        AssertSentOneGet(run, SystemsPath);
        var error = CliAssert.Failed(result, 1, "api_error");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(400));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("One or more validation errors occurred."));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo("The search term is too long."));
            Assert.That(JsonAssert.Strings(error.GetProperty("errors").GetProperty("Search")), Is.EqualTo(SearchErrors));
        });
    }

    // A response that is not problem+json reaches the CLI as HttpRequestException with a status code
    // (Enclave.Sdk.Api 1.0.4 throws EnclaveApiException only for problem+json,
    // ProblemDetailsHttpMessageHandler.cs:20). A proxy in front of the API answers this way, and the
    // proposal maps these statuses to the same exit codes ("Errors and exit codes").
    [TestCase(401, "token_invalid", 3)]
    [TestCase(403, "forbidden", 4)]
    [TestCase(429, "transient", 7)]
    [TestCase(500, "transient", 7)]
    [TestCase(502, "transient", 7)]
    [TestCase(503, "transient", 7)]
    [TestCase(400, "api_error", 1)]
    public async Task Plain_error_responses_exit_with_the_same_code_as_problem_responses(int status, string code, int exitCode)
    {
        using var run = CliRun.Start();
        StubRaw(run, SystemsPath, status, "text/html", "<html><body>Error</body></html>");

        var result = await run.RunAsync("system", "list");

        AssertSentOneGet(run, SystemsPath);
        CliAssert.Failed(result, exitCode, code);
    }

    [Test]
    public async Task A_plain_404_on_a_single_id_command_exits_5_with_not_found()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/SYS7001");
        StubRaw(run, path, 404, "text/html", "<html><body>Not Found</body></html>");

        var result = await run.RunAsync("system", "show", "SYS7001");

        AssertSentOneGet(run, path);
        CliAssert.Failed(result, 5, "not_found");
    }

    // A connection failure is transient: retrying can succeed once the API is reachable. Stopping
    // the fake API leaves its loopback port closed, so the connection is refused.
    [Test]
    public async Task An_unreachable_api_exits_7_with_transient()
    {
        using var run = CliRun.Start();
        run.Api.Stop();

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, 7, "transient");
    }

    private static TestCaseData SingleId(string command, string pathSuffix) =>
        new TestCaseData(command, TestData.OrgPath(pathSuffix)).SetArgDisplayNames(command);

    private static void StubRaw(CliRun run, string path, int status, string contentType, string body) =>
        run.Api
            .Given(Request.Create().UsingGet().WithPath(path))
            .RespondWith(Response.Create().WithStatusCode(status).WithHeader("Content-Type", contentType).WithBody(body));

    private static void AssertSentOneGet(CliRun run, string path)
    {
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(path));
        });
    }
}
