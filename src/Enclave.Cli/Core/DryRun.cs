using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Context;

namespace Enclave.Cli.Core;

// spec/cli-surface.md "Dry run"
internal sealed class DryRun
{
    private readonly List<JsonObject> _requests = [];

    private readonly Lock _lock = new();

    public OrganisationInUse? Organisation { get; set; }

    public PartnerInUse? Partner { get; set; }

    // The headers are left out, so the token in the Authorization header stays out of the report.
    public async Task CaptureAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        var captured = new JsonObject
        {
            ["method"] = request.Method.Method,
            ["url"] = request.RequestUri?.AbsoluteUri,
            ["body"] = Parse(body),
        };

        lock (_lock)
        {
            _requests.Add(captured);
        }
    }

    // Every Enclave.Sdk.Api change call fails on an empty body or JSON null (ClientBase.EnsureNotNull,
    // OrganisationScopedClient.ReadModelAsync). {} reads as a model with default values, or a bulk
    // count of 0, since no model has a required member, so every call completes and a bulk command
    // over 200 IDs shows each of its calls. Nothing read from the answer is printed, and no command
    // builds a request from it; tag set, which would, reads the tag instead (TagSetCommand).
    public static HttpResponseMessage Answer(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JsonObject().ToJsonString(), Encoding.UTF8, MediaTypeNames.Application.Json),
        RequestMessage = request,
    };

    // The organisation's name is null when it was given by ID.
    public JsonObject Report()
    {
        JsonArray requests;

        lock (_lock)
        {
            requests = new JsonArray(_requests.Select(request => (JsonNode?)request.DeepClone()).ToArray());
        }

        var report = new JsonObject { ["dryRun"] = true };

        if (Organisation is null && Partner is { } partner)
        {
            report["partner"] = new JsonObject { ["id"] = partner.Id.ToString("D") };
        }
        else
        {
            report["org"] = Organisation is { } org ? new JsonObject { ["id"] = org.Id.ToString(), ["name"] = org.Name } : null;
        }

        report["requests"] = requests;
        return report;
    }

    // Every body Enclave.Sdk.Api sends is JSON; any other is shown as text, so the report holds what
    // would have been sent.
    private static JsonNode? Parse(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return JsonValue.Create(body);
        }
    }
}
