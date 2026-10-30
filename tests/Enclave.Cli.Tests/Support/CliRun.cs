using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using WireMock.Matchers;
using WireMock.Server;

namespace Enclave.Cli.Tests.Support;

// One test's sandbox for running the CLI in-process: fake main and partner APIs on loopback,
// files in memory, and the only environment, stdin and API URLs the CLI sees.
// Each fake answers the other API's routes (spec/cli-surface.md "Partner API") with 421, which the
// CLI reports as api_error, so a call sent to the wrong address fails whatever a test expects.
internal sealed class CliRun : IDisposable
{
    private const string PartnerRoutes = "/partner/";

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly string _root;

    // One reader for the whole run, so a read in any of its commands is known when RunAsync checks.
    private readonly TerminalStdin _terminalStdin = new();

    private CliRun(WireMockServer api, string apiUrl, WireMockServer partnerApi, string partnerApiUrl, string root)
    {
        Api = api;
        ApiBaseUrl = apiUrl;
        PartnerApi = partnerApi;
        PartnerApiBaseUrl = partnerApiUrl;
        DefaultPartnerApiUrl = PartnerApiUrl;
        _root = root;
        Home = Path.Combine(root, "home");
        WorkDirectory = Path.Combine(root, "work");
    }

    public WireMockServer Api { get; }

    // http://127.0.0.1:{port}, without a trailing slash.
    public string ApiBaseUrl { get; }

    public Uri ApiUrl => new(ApiBaseUrl);

    // The fake partner API. Stub and the other Stub methods send a path under /partner/ here.
    public WireMockServer PartnerApi { get; }

    // http://127.0.0.1:{port}, without a trailing slash.
    public string PartnerApiBaseUrl { get; }

    public Uri PartnerApiUrl => new(PartnerApiBaseUrl);

    // The default partner API URL the CLI receives. Null leaves Enclave.Sdk.Api's default,
    // production's. A run with any URL but the fake's must set NetworkHandler, so nothing reaches
    // the live partner API.
    public Uri? DefaultPartnerApiUrl { get; set; }

    // Replaces the network beneath the CLI's own handler (CliHost.CreateNetworkHandler), such as
    // with FailingNetwork.CreateHandler.
    public Func<HttpMessageHandler>? NetworkHandler { get; set; }

    // The user profile path the CLI receives. Nothing exists at it on disk; its files are in Files.
    // A test sets "" or a relative path for a profile path that names no home directory; Files
    // resolves a relative path against the current directory, as the disk does.
    public string Home { get; set; }

    // The path WriteFile puts input files under.
    public string WorkDirectory { get; }

    public MemoryFileStore Files { get; } = new();

    public string CredentialsPath => Path.Combine(Home, ".enclave", "credentials.json");

    public string CliConfigPath => Path.Combine(Home, ".enclave", "cli.json");

    // The only environment the CLI sees. A missing key or a null value is an unset variable.
    // ENCLAVE_ORG_ID needs no lookup call (spec/cli-surface.md "Context"), so a test's requests are
    // the command's own.
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ENCLAVE_TOKEN"] = TestData.Token,
        ["ENCLAVE_ORG_ID"] = TestData.OrgId.ToString(),
    };

    // What the CLI reads from stdin while StdinIsTerminal is not set.
    public string StdinText { get; set; } = string.Empty;

    // While set, any read of stdin fails the run.
    public bool StdinIsTerminal { get; set; }

    // A test whose result depends on the time sets a FixedTimeProvider here.
    public TimeProvider Time { get; set; } = TimeProvider.System;

    // Every request the two fake APIs received, in the order received.
    // The CLI sends one request at a time, so ordering by receive time (RequestMessage.DateTime,
    // WireMock.Net 2.18.0) gives the order sent. A clock step backwards could swap requests to the
    // two fakes; one command calls one API, so only the order across commands is at risk, which is
    // accepted.
    public IReadOnlyList<RecordedRequest> Requests =>
        LoopbackApi.Log(Api).Concat(LoopbackApi.Log(PartnerApi))
            .OrderBy(request => request.DateTime)
            .Select(LoopbackApi.Record)
            .ToArray();

    public IReadOnlyList<RecordedRequest> ApiRequests => LoopbackApi.Received(Api);

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

    // The host RunAsync gives the CLI. Its default API URLs are the fakes', so a run cannot reach
    // the live APIs.
    public CliHost CreateHost()
    {
        if (DefaultPartnerApiUrl != PartnerApiUrl && NetworkHandler is null)
        {
            throw new InvalidOperationException("A run whose default partner API URL is not the fake partner API's must set NetworkHandler, so nothing it sends reaches the live partner API.");
        }

        return new()
        {
            GetEnvironmentVariable = name => Environment.TryGetValue(name, out var value) ? value : null,
            HomeDirectory = Home,
            Stdin = StdinIsTerminal ? _terminalStdin : new StringReader(StdinText),
            StdinIsTerminal = StdinIsTerminal,
            DefaultApiUrl = ApiUrl,
            DefaultPartnerApiUrl = DefaultPartnerApiUrl,
            Files = Files,
            Time = Time,
            CreateNetworkHandler = NetworkHandler,
        };
    }

    public async Task<CliResult> RunAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await Program.CreateRootCommand(CreateHost())
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr });

        // When an API call fails, the awaiting methods, the test among them, resume inline inside
        // the CLI's catch blocks. On .NET 10.0.0 and 10.0.1, a failure Assert.Multiple then throws
        // from its finally block skips the callers' catch blocks and ends the test host
        // (https://github.com/dotnet/runtime/issues/121578, fixed in 10.0.2). Task.Yield resumes
        // the test as a new work item, outside any catch block, for one thread pool hop per run.
        await Task.Yield();

        // Home and WorkDirectory exist only in memory, so anything on disk under this run's root
        // came from the CLI going around CliHost.Files.
        if (Directory.Exists(_root))
        {
            throw new AssertionException($"The CLI wrote to the disk under {_root}; every file it reads or writes must go through CliHost.Files.");
        }

        if (_terminalStdin.WasRead)
        {
            throw new AssertionException("The CLI read stdin while it was a terminal; a command must decide from StdinIsTerminal without reading it.");
        }

        return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    // Answers this method and path, whatever the query, with a JSON body.
    public void Stub(string method, string path, int status = 200, string? json = null) =>
        LoopbackApi.Stub(FakeFor(path), method, path, status, json);

    // Takes precedence over Stub for the same path.
    public void StubWithQuery(string method, string path, string parameter, string value, int status = 200, string? json = null) =>
        FakeFor(path).Given(LoopbackApi.Matching(method, path).WithParam(parameter, new ExactMatcher(value))).AtPriority(LoopbackApi.SpecificPriority)
            .RespondWith(LoopbackApi.Respond(status, json, LoopbackApi.JsonContentType));

    // Answers with an RFC 9457 problem details body, the form the Enclave API reports errors in.
    public void StubProblem(string method, string path, int status, string title, string? detail = null) =>
        FakeFor(path).Given(LoopbackApi.Matching(method, path)).AtPriority(LoopbackApi.GeneralPriority)
            .RespondWith(LoopbackApi.Respond(status, ApiJson.Problem(status, title, detail), LoopbackApi.ProblemContentType));

    // Answers with a body of any content type, such as a proxy's HTML error page.
    public void StubRaw(string method, string path, int status, string contentType, string body) =>
        FakeFor(path).Given(LoopbackApi.Matching(method, path)).AtPriority(LoopbackApi.GeneralPriority)
            .RespondWith(LoopbackApi.Respond(status, body, contentType));

    // Serves items as a list, pageSize to a page whatever per_page a request asks for. No page
    // parameter means page 0. The page after the last is empty, as the API answers it; any later
    // page gets WireMock.Net's 404.
    // A stub per page means a CLI that asks for the wrong page receives the wrong items.
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

    // Answers one page with a problem details body, in place of the page StubPages serves.
    public void StubPageProblem(string path, int page, int status, string title, string? detail = null) =>
        FakeFor(path).Given(LoopbackApi.MatchingPage(path, page)).AtPriority(LoopbackApi.SpecificPriority)
            .RespondWith(LoopbackApi.Respond(status, ApiJson.Problem(status, title, detail), LoopbackApi.ProblemContentType));

    // Answers successive requests with the responses in order; the last answers every request
    // after it. A 2xx response is sent as application/json, any other as application/problem+json
    // (build it with ApiJson.Problem).
    // A mapping with no next state puts a WireMock.Net scenario back to the start (WireMock.Net
    // 2.18.0), so the final mapping sets its own state again to keep answering.
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

    // Answers successive bulk calls with { "resultField": n }, one count per call; the last count
    // answers every call after it.
    public void StubBulk(string method, string path, string resultField, params int[] affected) =>
        StubSequence(method, path, affected.Select(count => (200, ApiJson.Bulk(resultField, count))).ToArray());

    // The requests after the first skip, each as "METHOD path".
    public string[] Calls(int skip = 0) =>
        Requests.Skip(skip).Select(request => request.Call).ToArray();

    public IReadOnlyList<RecordedRequest> RequestsTo(string method, string path) =>
        Requests
            .Where(request => string.Equals(request.Method, method, StringComparison.Ordinal) && string.Equals(request.Path, path, StringComparison.Ordinal))
            .ToArray();

    // The page numbers of the GET requests for this path, such as "0,1,2"; no page parameter
    // counts as page 0.
    public string PagesRequested(string path) =>
        string.Join(",", RequestsTo("GET", path).Select(request => request.PageNumber.ToString(CultureInfo.InvariantCulture)));

    // Asserts the fake API received exactly one request, and returns it.
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

    // Puts a file under WorkDirectory and returns its full path.
    public string WriteFile(string name, string content)
    {
        var path = Path.Combine(WorkDirectory, name);
        Files.WriteText(path, content, privateToUser: false);
        return path;
    }

    // Puts ~/.enclave/credentials.json in Files as login writes it, in the format Enclave.Sdk.Api
    // reads (EnclaveClient.ReadCredentialsFile). The base URL defaults to the fake API's;
    // partnerApiBaseUrl is written only when given.
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
        _terminalStdin.Dispose();

        // RunAsync reports a directory here as a failure; it is removed so the failure leaves
        // nothing behind.
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
            // A file the CLI left open keeps the directory on Windows. It is under the system temp
            // directory, so leaving it costs disk space only.
        }
        catch (UnauthorizedAccessException)
        {
            // As above, for a read-only file or a directory the CLI restricted.
        }
    }

    private WireMockServer FakeFor(string path) =>
        path.StartsWith(PartnerRoutes, StringComparison.Ordinal) ? PartnerApi : Api;
}
