using System.Text.Json.Nodes;
using Enclave.Cli.Context;
using Enclave.Sdk.Api.Exceptions;

namespace Enclave.Cli.Core;

// It takes the token's source and each request's method and URL only, which keeps the token and the
// Authorization header out.
internal sealed class CliTrace
{
    private readonly List<string> _requests = [];

    private readonly List<string> _saved = [];

    private readonly Lock _lock = new();

    public string? TokenSource { get; set; }

    public Uri? Api { get; set; }

    public Uri? PartnerApi { get; set; }

    public OrganisationInUse? Organisation { get; set; }

    public PartnerInUse? Partner { get; set; }

    // Call before the request is sent, so a request that fails is listed.
    public void Sent(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        lock (_lock)
        {
            _requests.Add($"{request.Method} {request.RequestUri?.AbsoluteUri}");
        }
    }

    public void Saved(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        _saved.Add(path);
    }

    public JsonObject? ToJson(CliException? error = null)
    {
        var trace = new JsonObject();

        if (TokenSource is { } tokenSource)
        {
            trace["token"] = new JsonObject { ["source"] = tokenSource };
        }

        if (Api is { } api)
        {
            trace["api"] = api.AbsoluteUri;
        }

        if (PartnerApi is { } partnerApi)
        {
            trace["partnerApi"] = partnerApi.AbsoluteUri;
        }

        if (Organisation is { } org)
        {
            trace["org"] = new JsonObject { ["id"] = org.Id.ToString(), ["name"] = org.Name, ["source"] = org.Source };
        }

        if (Partner is { } partner)
        {
            trace["partner"] = new JsonObject { ["id"] = partner.Id.ToString("D"), ["source"] = partner.Source };
        }

        lock (_lock)
        {
            if (_requests.Count > 0)
            {
                trace["requests"] = new JsonArray(_requests.Select(request => (JsonNode?)request).ToArray());
            }
        }

        if (_saved.Count > 0)
        {
            trace["saved"] = new JsonArray(_saved.Select(path => (JsonNode?)path).ToArray());
        }

        // The API's problem details say what went wrong, so they need no cause.
        if (error?.InnerException is { } cause and not EnclaveApiException { ProblemDetails: not null })
        {
            trace["cause"] = $"{cause.GetType().FullName}: {cause.Message}";
        }

        return trace.Count == 0 ? null : trace;
    }
}
