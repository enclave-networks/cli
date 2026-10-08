using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using WireMock.Matchers;
using WireMock.Server;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// One test's sandbox for running the CLI in-process: a fake Enclave API and a fake partner API,
/// each on its own loopback address, files held in memory under a user profile path and a working
/// directory path, and the only environment variables, stdin and API URLs the CLI sees.
/// </summary>
// The partner API is a separate service on its own host, with every route under
// /partner/{partnerId}/, and the main API has no route there (proposed-cli-surface.md "Partner
// API"). Each stub goes to the fake that serves its path, and each fake answers the other API's
// routes with 421 Misdirected Request, which the CLI reports as api_error, so a call sent to the
// wrong address fails the command whatever answer a test expects.
internal sealed class CliRun : IDisposable
{
    private const string PartnerRoutes = "/partner/";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly string _root;

    private CliRun(WireMockServer api, string apiUrl, WireMockServer partnerApi, string partnerApiUrl, string root)
    {
        Api = api;
        ApiBaseUrl = apiUrl;
        PartnerApi = partnerApi;
        PartnerApiBaseUrl = partnerApiUrl;
        _root = root;
        Home = Path.Combine(root, "home");
        WorkDirectory = Path.Combine(root, "work");
    }

    /// <summary>
    /// The fake main API.
    /// </summary>
    public WireMockServer Api { get; }

    /// <summary>
    /// The URL of the fake API, http://127.0.0.1:{port} without a trailing slash.
    /// </summary>
    public string ApiBaseUrl { get; }

    /// <summary>
    /// The URL of the fake API, which the CLI receives as its default API URL.
    /// </summary>
    public Uri ApiUrl => new(ApiBaseUrl);

    /// <summary>
    /// The fake partner API. <see cref="Stub"/> and the other Stub methods put a stub for a path
    /// under /partner/ here, such as TestData.PartnerPath gives.
    /// </summary>
    public WireMockServer PartnerApi { get; }

    /// <summary>
    /// The URL of the fake partner API, http://127.0.0.1:{port} without a trailing slash, another
    /// port from <see cref="ApiBaseUrl"/>.
    /// </summary>
    public string PartnerApiBaseUrl { get; }

    /// <summary>
    /// The URL of the fake partner API, which the CLI receives as its default partner API URL.
    /// </summary>
    public Uri PartnerApiUrl => new(PartnerApiBaseUrl);

    /// <summary>
    /// The user profile path the CLI receives; ~/.enclave lives here. Nothing exists at it on disk:
    /// the files under it are in <see cref="Files"/>.
    /// </summary>
    public string Home { get; }

    /// <summary>
    /// The path WriteFile puts input files under, in <see cref="Files"/>.
    /// </summary>
    public string WorkDirectory { get; }

    /// <summary>
    /// The CLI's files. Tests set up starting files and check what the CLI wrote here.
    /// </summary>
    public MemoryFileStore Files { get; } = new();

    public string CredentialsPath => Path.Combine(Home, ".enclave", "credentials.json");

    public string CliConfigPath => Path.Combine(Home, ".enclave", "cli.json");

    /// <summary>
    /// The only environment the CLI sees. A missing key or a null value is an unset variable.
    /// </summary>
    // ENCLAVE_ORG_ID names the organisation by ID, so the CLI builds the organisation client from it
    // and makes no lookup call (proposed-cli-surface.md "Context"); a test's requests are then the
    // command's own. ENCLAVE_ORG takes a name, never an ID.
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ENCLAVE_TOKEN"] = TestData.Token,
        ["ENCLAVE_ORG_ID"] = TestData.OrgId.ToString(),
    };

    /// <summary>
    /// What the CLI reads from stdin, such as a list from <see cref="CliList"/>.
    /// </summary>
    public string StdinText { get; set; } = string.Empty;

    /// <summary>
    /// Whether the CLI sees stdin as a terminal, where a command given "-" must exit 2 without
    /// reading it.
    /// </summary>
    public bool StdinIsTerminal { get; set; }

    /// <summary>
    /// The clock and local time zone the CLI sees. A test whose result depends on the time sets a
    /// <see cref="FixedTimeProvider"/> here.
    /// </summary>
    // The system clock is the default so that a test that does not depend on the time sees the CLI
    // behave as it does outside tests.
    public TimeProvider Time { get; set; } = TimeProvider.System;

    /// <summary>
    /// Every request the two fake APIs received, main and partner, in the order they received them.
    /// </summary>
    // The CLI sends one request at a time and waits for its answer, so each request reaches a fake
    // after the one before it was answered, and ordering by the time each fake received a request
    // (RequestMessage.DateTime, from DateTime.UtcNow, WireMock.Net 2.18.0) gives the order sent. A
    // clock step backwards between two requests to different fakes would swap them; the order
    // between the two APIs matters only across commands, since one command calls one API, so that
    // is accepted.
    public IReadOnlyList<RecordedRequest> Requests =>
        LoopbackApi.Log(Api).Concat(LoopbackApi.Log(PartnerApi))
            .OrderBy(request => request.DateTime)
            .Select(LoopbackApi.Record)
            .ToArray();

    /// <summary>
    /// The requests the fake main API received, in the order it received them.
    /// </summary>
    public IReadOnlyList<RecordedRequest> ApiRequests => LoopbackApi.Received(Api);

    /// <summary>
    /// The requests the fake partner API received, in the order it received them.
    /// </summary>
    public IReadOnlyList<RecordedRequest> PartnerApiRequests => LoopbackApi.Received(PartnerApi);

    public static CliRun Start()
    {
        var (api, apiUrl) = LoopbackApi.Start();
        var (partnerApi, partnerApiUrl) = LoopbackApi.Start();

        LoopbackApi.Misdirect(api, new RegexMatcher("^" + PartnerRoutes), "This is the main API's address; partner API routes are served at the partner API's address.");
        LoopbackApi.Misdirect(partnerApi, new RegexMatcher(MatchBehaviour.RejectOnMatch, "^" + PartnerRoutes), "This is the partner API's address; it serves only routes under /partner/.");

        var root = Path.Combine(Path.GetTempPath(), "enclave-cli-tests", Guid.NewGuid().ToString("N"));

        return new CliRun(api, apiUrl, partnerApi, partnerApiUrl, root);
    }

    /// <summary>
    /// The host RunAsync gives the CLI: this run's environment, home directory, stdin, in-memory
    /// files, clock, and the fake APIs' URLs as the default API URL and the default partner API URL,
    /// so a run can never reach the live API or the live partner API.
    /// </summary>
    public CliHost CreateHost() => new()
    {
        GetEnvironmentVariable = name => Environment.TryGetValue(name, out var value) ? value : null,
        HomeDirectory = Home,
        Stdin = new StringReader(StdinText),
        StdinIsTerminal = StdinIsTerminal,
        DefaultApiUrl = ApiUrl,
        DefaultPartnerApiUrl = PartnerApiUrl,
        Files = Files,
        Time = Time,
    };

    public async Task<CliResult> RunAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await Program.CreateRootCommand(CreateHost())
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr });

        // The test's code after RunAsync must not run inside the CLI's catch blocks, where .NET
        // 10.0.0 and 10.0.1 lose the failures it throws.
        //
        // When an API call fails, the CLI's task completes on the thread that received the answer,
        // and each awaiting async method resumes there inline, from inside the catch block of the
        // method it awaited (the compiler-generated MoveNext calls SetException in its catch), so
        // the test would resume inside those catch blocks. In .NET 10.0.0 and 10.0.1, an exception
        // thrown there from a finally block that was entered without an exception is taken for a
        // collided unwind, and the catch blocks of the methods that called it are skipped, the
        // test's own among them (https://github.com/dotnet/runtime/issues/121578, fixed in 10.0.2
        // by https://github.com/dotnet/runtime/pull/121626). Assert.Multiple and EnterMultipleScope
        // throw their failures from Dispose in such a finally block, so the failure escapes the test
        // and ends the test host. DOTNET_TieredCompilation=0 hides this only for a finally block the
        // JIT copies onto the normal exit path, which it does only when optimizing and never for a
        // finally block that cannot exit normally (dotnet/runtime
        // docs/design/coreclr/jit/finally-optimizations.md), so it does not avoid the defect.
        //
        // Task.Yield queues the rest of this method, and the test with it, as a new work item
        // (https://learn.microsoft.com/dotnet/api/system.threading.tasks.task.yield), which starts
        // with no exception being caught. The cost is one thread pool hop per run.
        await Task.Yield();

        // Home and WorkDirectory exist only in memory, so anything on disk under this run's root
        // came from the CLI going around CliHost.Files.
        if (Directory.Exists(_root))
        {
            throw new AssertionException($"The CLI wrote to the disk under {_root}; every file it reads or writes must go through CliHost.Files.");
        }

        return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Answers requests with this method and URL path, whatever their query, with the given status
    /// and JSON body.
    /// </summary>
    public void Stub(string method, string path, int status = 200, string? json = null) =>
        LoopbackApi.Stub(FakeFor(path), method, path, status, json);

    /// <summary>
    /// Answers requests with this method, URL path and query parameter value. These stubs take
    /// precedence over <see cref="Stub"/> for the same path, so a test can serve each page of a
    /// list separately.
    /// </summary>
    public void StubWithQuery(string method, string path, string parameter, string value, int status = 200, string? json = null) =>
        FakeFor(path).Given(LoopbackApi.Matching(method, path).WithParam(parameter, new ExactMatcher(value))).AtPriority(LoopbackApi.SpecificPriority)
            .RespondWith(LoopbackApi.Respond(status, json, LoopbackApi.JsonContentType));

    /// <summary>
    /// Answers requests with this method and URL path with an RFC 9457 problem details body, the
    /// form the Enclave API reports errors in.
    /// </summary>
    public void StubProblem(string method, string path, int status, string title, string? detail = null) =>
        FakeFor(path).Given(LoopbackApi.Matching(method, path)).AtPriority(LoopbackApi.GeneralPriority)
            .RespondWith(LoopbackApi.Respond(status, ApiJson.Problem(status, title, detail), LoopbackApi.ProblemContentType));

    /// <summary>
    /// Answers requests with this method and URL path with a body of any content type, such as the
    /// HTML error page a proxy in front of the API sends.
    /// </summary>
    public void StubRaw(string method, string path, int status, string contentType, string body) =>
        FakeFor(path).Given(LoopbackApi.Matching(method, path)).AtPriority(LoopbackApi.GeneralPriority)
            .RespondWith(LoopbackApi.Respond(status, body, contentType));

    /// <summary>
    /// Serves <paramref name="items"/> as a list at this path, <paramref name="pageSize"/> items to a
    /// page, whatever per_page a request asks for. A GET for page n gets page n, as ApiJson.PageAt
    /// writes it, and a GET without a page parameter gets page 0. The page after the last gets an
    /// empty page with no nextPage, as the API answers it; a GET for any later page matches no
    /// stub, and WireMock.Net answers 404.
    /// </summary>
    // Each page is a separate stub matched on the page parameter, so a CLI that asks for the wrong
    // page receives the wrong items, and a CLI that reads past the last page shows in the requests.
    public void StubPages(string path, int pageSize, params string[] items)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);

        var lastPage = items.Length == 0 ? 0 : (items.Length - 1) / pageSize;

        for (var page = 0; page <= lastPage + 1; page++)
        {
            var pageItems = items.Skip(page * pageSize).Take(pageSize).ToArray();

            FakeFor(path).Given(LoopbackApi.MatchingPage(path, page)).AtPriority(LoopbackApi.GeneralPriority)
                .RespondWith(LoopbackApi.Respond(200, ApiJson.PageAt(page, pageSize, items.Length, pageItems), LoopbackApi.JsonContentType));
        }
    }

    /// <summary>
    /// Answers a GET for one page of the list at this path with a problem details body, in place of
    /// the page <see cref="StubPages"/> serves; a GET without a page parameter asks for page 0.
    /// </summary>
    public void StubPageProblem(string path, int page, int status, string title, string? detail = null) =>
        FakeFor(path).Given(LoopbackApi.MatchingPage(path, page)).AtPriority(LoopbackApi.SpecificPriority)
            .RespondWith(LoopbackApi.Respond(status, ApiJson.Problem(status, title, detail), LoopbackApi.ProblemContentType));

    /// <summary>
    /// Answers successive requests with this method and URL path, whatever their query, with the
    /// given responses in order; the last response answers every request after it. A 2xx response
    /// is sent as application/json and any other as application/problem+json, the form the Enclave
    /// API reports errors in (build the body with ApiJson.Problem).
    /// </summary>
    // A WireMock.Net scenario starts with no state, and each time one of its mappings answers, the
    // scenario takes that mapping's next state, so mapping i answers only the (i + 1)th request. A
    // mapping with no next state puts the scenario back to the start (observed with WireMock.Net
    // 2.18.0, where the fourth call of HarnessTests' StubBulk test got the first answer again), so
    // the final mapping sets its own state again and keeps answering. One response needs no
    // scenario. The scenario name is unique, so two sequences never share a state.
    public void StubSequence(string method, string path, params (int Status, string Json)[] responses)
    {
        ArgumentOutOfRangeException.ThrowIfZero(responses.Length);

        var scenario = Guid.NewGuid().ToString("N");

        for (var i = 0; i < responses.Length; i++)
        {
            var (status, json) = responses[i];
            var contentType = status is >= 200 and < 300 ? LoopbackApi.JsonContentType : LoopbackApi.ProblemContentType;
            var mapping = FakeFor(path).Given(LoopbackApi.Matching(method, path)).AtPriority(LoopbackApi.GeneralPriority);

            if (responses.Length > 1)
            {
                mapping = mapping.InScenario(scenario);

                if (i > 0)
                {
                    mapping = mapping.WhenStateIs(i);
                }

                mapping = mapping.WillSetStateTo(Math.Min(i + 1, responses.Length - 1));
            }

            mapping.RespondWith(LoopbackApi.Respond(status, json, contentType));
        }
    }

    /// <summary>
    /// Answers successive calls to a bulk endpoint with a bulk result, { "<paramref name="resultField"/>": n },
    /// the first call with the first count, the second with the second, and so on; the last count
    /// answers every call after it. Each call's body is in <see cref="RequestsTo"/>.
    /// </summary>
    public void StubBulk(string method, string path, string resultField, params int[] affected) =>
        StubSequence(method, path, affected.Select(count => (200, ApiJson.Bulk(resultField, count))).ToArray());

    /// <summary>
    /// The requests the fake API received after the first <paramref name="skip"/>, each as
    /// "METHOD path", in the order it received them.
    /// </summary>
    public string[] Calls(int skip = 0) =>
        Requests.Skip(skip).Select(request => request.Call).ToArray();

    /// <summary>
    /// The requests the fake API received with this method and URL path, in the order it received
    /// them.
    /// </summary>
    public IReadOnlyList<RecordedRequest> RequestsTo(string method, string path) =>
        Requests
            .Where(request => string.Equals(request.Method, method, StringComparison.Ordinal) && string.Equals(request.Path, path, StringComparison.Ordinal))
            .ToArray();

    /// <summary>
    /// The page numbers of the GET requests for this path, in the order the fake API received them,
    /// joined with commas, such as "0,1,2"; a request without a page parameter counts as page 0.
    /// </summary>
    public string PagesRequested(string path) =>
        string.Join(",", RequestsTo("GET", path).Select(request => request.PageNumber.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Asserts the fake API received exactly one request, and returns it.
    /// </summary>
    public RecordedRequest SingleRequest()
    {
        var requests = Requests;

        if (requests.Count != 1)
        {
            throw new AssertionException(
                $"Expected exactly one API request, found {requests.Count}:{System.Environment.NewLine}{string.Join(System.Environment.NewLine, requests)}");
        }

        return requests[0];
    }

    /// <summary>
    /// Puts a file under the work directory in <see cref="Files"/> and returns its full path.
    /// </summary>
    public string WriteFile(string name, string content)
    {
        var path = Path.Combine(WorkDirectory, name);
        Files.WriteText(path, content, privateToUser: false);
        return path;
    }

    /// <summary>
    /// Puts ~/.enclave/credentials.json in <see cref="Files"/>, private to the user as login writes
    /// it, in the format Enclave.Sdk.Api reads (EnclaveClient.ReadCredentialsFile, version 1.1.0). The
    /// base URL defaults to the fake API's. The file holds partnerApiBaseUrl only when one is given.
    /// </summary>
    public void SaveCredentials(string token, string? baseUrl = null, string? partnerApiBaseUrl = null)
    {
        var credentials = new JsonObject
        {
            ["personalAccessToken"] = token,
            ["baseUrl"] = baseUrl ?? ApiBaseUrl,
        };

        if (partnerApiBaseUrl is not null)
        {
            credentials["partnerApiBaseUrl"] = partnerApiBaseUrl;
        }

        Files.WriteText(CredentialsPath, credentials.ToJsonString(IndentedJson), privateToUser: true);
    }

    public void Dispose()
    {
        Api.Stop();
        Api.Dispose();
        PartnerApi.Stop();
        PartnerApi.Dispose();

        // A directory here means the CLI wrote to the disk, which RunAsync reports; it is removed
        // so the failure leaves nothing behind.
        if (!Directory.Exists(_root))
        {
            return;
        }

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file the CLI left open keeps the directory on Windows. The directory is under the
            // system temp directory, so leaving it behind costs disk space only.
        }
        catch (UnauthorizedAccessException)
        {
            // As above, for a read-only file or a directory the CLI restricted.
        }
    }

    private WireMockServer FakeFor(string path) =>
        path.StartsWith(PartnerRoutes, StringComparison.Ordinal) ? PartnerApi : Api;
}
