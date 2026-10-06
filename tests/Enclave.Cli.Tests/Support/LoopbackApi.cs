using System.Net;
using System.Net.Sockets;
using WireMock.Logging;
using WireMock.Matchers;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using WireMock.Settings;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Starts fake APIs that listen on 127.0.0.1 only, and sets up their answers.
/// </summary>
internal static class LoopbackApi
{
    // WireMock.Net serves a request from the matching mapping with the lowest priority number, so a
    // stub that also matches the query wins over one that matches the path alone.
    internal const int SpecificPriority = 1;

    internal const int GeneralPriority = 10;

    /// <summary>
    /// Starts a fake API on a free loopback port and returns it with its URL,
    /// http://127.0.0.1:{port} without a trailing slash.
    /// </summary>
    public static (WireMockServer Api, string Url) Start()
    {
        var url = $"http://127.0.0.1:{FreeLoopbackPort()}";

        var api = WireMockServer.Start(new WireMockServerSettings
        {
            // Without Urls, WireMock.Net listens on every interface, which makes Windows Firewall
            // prompt to allow the test host and accepts connections from other machines. The tests
            // need loopback only.
            Urls = [url],

            // The SDK sends bulk revoke, decline and delete requests as DELETE with a JSON body
            // (Enclave.Sdk.Api 1.0.4, SystemsClient.RevokeSystemsAsync), so every method's body
            // is read and recorded.
            AllowBodyForAllHttpMethods = true,

            // One request at a time keeps the request log in the order the CLI sent them.
            HandleRequestsSynchronously = true,
            Logger = new WireMockNullLogger(),
        });

        return (api, url);
    }

    /// <summary>
    /// Answers requests with this method and URL path, whatever their query, with the given status
    /// and JSON body.
    /// </summary>
    public static void Stub(WireMockServer api, string method, string path, int status = 200, string? json = null) =>
        api.Given(Matching(method, path)).AtPriority(GeneralPriority)
            .RespondWith(Respond(status, json, "application/json"));

    internal static IRequestBuilder Matching(string method, string path) =>
        Request.Create().UsingMethod(method).WithPath(new ExactMatcher(path));

    internal static IResponseBuilder Respond(int status, string? json, string contentType)
    {
        var response = Response.Create().WithStatusCode(status);

        return json is null
            ? response
            : response.WithHeader("Content-Type", contentType).WithBody(json);
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
}
