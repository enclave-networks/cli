using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using NUnit.Framework;
using WireMock;
using WireMock.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// One test's sandbox for running the CLI in-process: a fake Enclave API on loopback, an empty
/// user profile directory, a working directory for input files, and the only environment
/// variables, stdin and API URL the CLI sees.
/// </summary>
internal sealed class CliRun : IDisposable
{
    // WireMock.Net serves a request from the matching mapping with the lowest priority number, so a
    // stub that also matches the query wins over one that matches the path alone.
    private const int SpecificPriority = 1;

    private const int GeneralPriority = 10;

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private readonly string _root;

    private CliRun(WireMockServer api, string apiUrl, string root)
    {
        Api = api;
        ApiBaseUrl = apiUrl;
        _root = root;
        Home = Path.Combine(root, "home");
        WorkDirectory = Path.Combine(root, "work");

        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(WorkDirectory);
    }

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
    /// The temporary user profile directory; ~/.enclave lives here.
    /// </summary>
    public string Home { get; }

    /// <summary>
    /// The temporary directory WriteFile writes into.
    /// </summary>
    public string WorkDirectory { get; }

    public string CredentialsPath => Path.Combine(Home, ".enclave", "credentials.json");

    public string CliConfigPath => Path.Combine(Home, ".enclave", "cli.json");

    /// <summary>
    /// The only environment the CLI sees. A missing key or a null value is an unset variable.
    /// </summary>
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["ENCLAVE_TOKEN"] = TestData.Token,
        ["ENCLAVE_ORG"] = TestData.OrgId.ToString(),
    };

    public string StdinText { get; set; } = string.Empty;

    public bool StdinIsTerminal { get; set; }

    /// <summary>
    /// Every request the fake API received, in the order it received them.
    /// </summary>
    public IReadOnlyList<RecordedRequest> Requests =>
        Api.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>().Select(Record).ToArray();

    public static CliRun Start()
    {
        var apiUrl = $"http://127.0.0.1:{FreeLoopbackPort()}";

        var api = WireMockServer.Start(new WireMockServerSettings
        {
            // Without Urls, WireMock.Net listens on every interface, which makes Windows Firewall
            // prompt to allow the test host and accepts connections from other machines. The tests
            // need loopback only.
            Urls = [apiUrl],

            // The SDK sends bulk revoke, decline and delete requests as DELETE with a JSON body
            // (Enclave.Sdk.Api 1.0.4, SystemsClient.RevokeSystemsAsync), so every method's body
            // is read and recorded.
            AllowBodyForAllHttpMethods = true,

            // One request at a time keeps the request log in the order the CLI sent them.
            HandleRequestsSynchronously = true,
            Logger = new WireMockNullLogger(),
        });

        var root = Path.Combine(Path.GetTempPath(), "enclave-cli-tests", Guid.NewGuid().ToString("N"));

        return new CliRun(api, apiUrl, root);
    }

    /// <summary>
    /// The host RunAsync gives the CLI: this run's environment, home directory, stdin and the fake
    /// API's URL as the default API URL, so a run can never reach the live API.
    /// </summary>
    public CliHost CreateHost() => new()
    {
        GetEnvironmentVariable = name => Environment.TryGetValue(name, out var value) ? value : null,
        HomeDirectory = Home,
        Stdin = new StringReader(StdinText),
        StdinIsTerminal = StdinIsTerminal,
        DefaultApiUrl = ApiUrl,
    };

    public async Task<CliResult> RunAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = await Program.CreateRootCommand(CreateHost())
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr });

        return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Answers requests with this method and URL path, whatever their query, with the given status
    /// and JSON body.
    /// </summary>
    public void Stub(string method, string path, int status = 200, string? json = null) =>
        Api.Given(Matching(method, path)).AtPriority(GeneralPriority)
            .RespondWith(Respond(status, json, "application/json"));

    /// <summary>
    /// Answers requests with this method, URL path and query parameter value. These stubs take
    /// precedence over <see cref="Stub"/> for the same path, so a test can serve each page of a
    /// list separately.
    /// </summary>
    public void StubWithQuery(string method, string path, string parameter, string value, int status = 200, string? json = null) =>
        Api.Given(Matching(method, path).WithParam(parameter, new ExactMatcher(value))).AtPriority(SpecificPriority)
            .RespondWith(Respond(status, json, "application/json"));

    /// <summary>
    /// Answers requests with this method and URL path with an RFC 9457 problem details body, the
    /// form the Enclave API reports errors in.
    /// </summary>
    public void StubProblem(string method, string path, int status, string title, string? detail = null)
    {
        var problem = new JsonObject
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
        };

        Api.Given(Matching(method, path)).AtPriority(GeneralPriority)
            .RespondWith(Respond(status, problem.ToJsonString(), "application/problem+json"));
    }

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
    /// Writes a file into the work directory and returns its full path.
    /// </summary>
    public string WriteFile(string name, string content)
    {
        var path = Path.Combine(WorkDirectory, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Writes ~/.enclave/credentials.json in the format Enclave.Sdk.Api reads
    /// (EnclaveClient.GetSettingsFile, version 1.0.4). The base URL defaults to the fake API's.
    /// </summary>
    public void SaveCredentials(string token, string? baseUrl = null)
    {
        var credentials = new JsonObject
        {
            ["personalAccessToken"] = token,
            ["baseUrl"] = baseUrl ?? ApiBaseUrl,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(CredentialsPath)!);
        File.WriteAllText(CredentialsPath, credentials.ToJsonString(IndentedJson));
    }

    public void Dispose()
    {
        Api.Stop();
        Api.Dispose();

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

    // Binding port 0 makes the OS choose a free port. The listener is stopped before WireMock.Net
    // binds the port, so another process could take it in between; on loopback with ports from the
    // ephemeral range that is unlikely, and a clash fails the test at Start.
    private static int FreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static IRequestBuilder Matching(string method, string path) =>
        Request.Create().UsingMethod(method).WithPath(new ExactMatcher(path));

    private static IResponseBuilder Respond(int status, string? json, string contentType)
    {
        var response = Response.Create().WithStatusCode(status);

        return json is null
            ? response
            : response.WithHeader("Content-Type", contentType).WithBody(json);
    }

    private static RecordedRequest Record(IRequestMessage request)
    {
        // WireMock.Net's own query parse can split one value into several
        // (WireMockServerSettings.QueryParameterMultipleValueSupport, default All, version 2.18.0),
        // so the raw query is parsed here and each value kept whole.
        var parsed = HttpUtility.ParseQueryString(request.RawQuery ?? string.Empty);
        var query = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in parsed.AllKeys)
        {
            if (key is not null)
            {
                query[key] = parsed[key] ?? string.Empty;
            }
        }

        string? authorization = null;

        if (request.Headers is not null)
        {
            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    authorization = string.Join(", ", header.Value);
                }
            }
        }

        return new RecordedRequest(request.Method, request.Path, query, request.Body, authorization);
    }
}
