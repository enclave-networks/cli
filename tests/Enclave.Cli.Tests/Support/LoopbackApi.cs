using System.Globalization;
using System.Web;
using WireMock;
using WireMock.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;
using WireMock.Types;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Starts fake APIs that listen on 127.0.0.1 only, sets up their answers, and reads the requests
/// they received. CliRun starts one for the main API and one for the partner API.
/// </summary>
internal static class LoopbackApi
{
    // WireMock.Net serves a request from the matching mapping with the lowest priority number, so a
    // stub that also matches the query wins over one that matches the path alone.
    internal const int SpecificPriority = 1;

    internal const int GeneralPriority = 10;

    // Below every stub, so it answers only what no stub does.
    internal const int MisdirectedPriority = 100;

    internal const string JsonContentType = "application/json";

    // Enclave.Sdk.Api 1.1.0 throws EnclaveApiException only for this media type
    // (Handlers/ProblemDetailsHttpMessageHandler.cs:29); any other error body reaches the caller as
    // HttpRequestException.
    internal const string ProblemContentType = "application/problem+json";

    // 421 Misdirected Request: "the request was directed at a server that is unable or unwilling to
    // produce an authoritative response for the target URI" (RFC 9110 section 15.5.20).
    private const int MisdirectedRequest = 421;

    /// <summary>
    /// Starts a fake API on a loopback port the OS chooses, and returns it with its URL,
    /// http://127.0.0.1:{port} without a trailing slash.
    /// </summary>
    public static (WireMockServer Api, string Url) Start()
    {
        var api = WireMockServer.Start(new WireMockServerSettings
        {
            // Without Urls, WireMock.Net listens on every interface, which makes Windows Firewall
            // prompt to allow the test host and accepts connections from other machines. The tests
            // need loopback only. Port 0 has the OS choose a free port as the listener binds it, so
            // no other process can take the port between the choice and the bind; WireMock.Net
            // reports the port it bound in Ports (WireMock.Net 2.18.0, checked below).
            Urls = ["http://127.0.0.1:0"],

            // Enclave.Sdk.Api sends bulk revoke, decline and delete requests as DELETE with a JSON body
            // (Enclave.Sdk.Api 1.1.0, SystemsClient.RevokeSystemsAsync), so every method's body
            // is read and recorded.
            AllowBodyForAllHttpMethods = true,

            // One request at a time keeps the request log in the order the CLI sent them.
            HandleRequestsSynchronously = true,
            Logger = new WireMockNullLogger(),
        });

        if (api.Ports.Count != 1 || api.Ports[0] == 0)
        {
            api.Stop();
            api.Dispose();
            throw new InvalidOperationException($"The fake API reported the ports [{string.Join(", ", api.Ports)}]; expected the one port the OS chose.");
        }

        return (api, string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{api.Ports[0]}"));
    }

    /// <summary>
    /// Answers requests with this method and URL path, whatever their query, with the given status
    /// and JSON body.
    /// </summary>
    public static void Stub(WireMockServer api, string method, string path, int status = 200, string? json = null) =>
        api.Given(Matching(method, path)).AtPriority(GeneralPriority)
            .RespondWith(Respond(status, json, JsonContentType));

    /// <summary>
    /// The requests a fake API received, in the order it received them.
    /// </summary>
    public static IReadOnlyList<RecordedRequest> Received(WireMockServer api) =>
        Log(api).Select(Record).ToArray();

    /// <summary>
    /// Answers every request whose path <paramref name="path"/> accepts, and that no stub answers,
    /// with 421 Misdirected Request and a problem details body carrying <paramref name="detail"/>.
    /// </summary>
    internal static void Misdirect(WireMockServer api, IStringMatcher path, string detail) =>
        api.Given(Request.Create().WithPath(path)).AtPriority(MisdirectedPriority)
            .RespondWith(Respond(MisdirectedRequest, ApiJson.Problem(MisdirectedRequest, "Misdirected Request", detail), ProblemContentType));

    /// <summary>
    /// The messages a fake API received, in the order it received them.
    /// </summary>
    internal static IEnumerable<IRequestMessage> Log(WireMockServer api) =>
        api.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>();

    internal static RecordedRequest Record(IRequestMessage request)
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

    internal static IRequestBuilder Matching(string method, string path) =>
        Request.Create().UsingMethod(method).WithPath(new ExactMatcher(path));

    /// <summary>
    /// Matches a request for this page of a list: page=<paramref name="page"/>, or no page parameter
    /// for page 0.
    /// </summary>
    // The API reads a missing page parameter as page 0 (portal PaginatedRequestModel.Page), and
    // Enclave.Sdk.Api 1.1.0 leaves the parameter out when it is given no page number
    // (SystemsClient.BuildQueryString and the other list clients). A parameter that is not a whole
    // number matches no page.
    internal static IRequestBuilder MatchingPage(string path, int page) =>
        Matching("GET", path).WithParam(query => RequestedPage(query) == page);

    internal static IResponseBuilder Respond(int status, string? json, string contentType)
    {
        var response = Response.Create().WithStatusCode(status);

        return json is null
            ? response
            : response.WithHeader("Content-Type", contentType).WithBody(json);
    }

    private static int? RequestedPage(IDictionary<string, WireMockList<string>>? query)
    {
        if (query is null || !query.TryGetValue("page", out var values) || values.Count == 0)
        {
            return 0;
        }

        return values.Count == 1 && int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            ? page
            : null;
    }
}
