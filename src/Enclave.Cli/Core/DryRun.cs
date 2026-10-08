using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Context;

namespace Enclave.Cli.Core;

/// <summary>
/// The changes a command run with --dry-run would send, captured in place of being sent, and the
/// organisation or partner they are for (proposed-cli-surface.md "Dry run").
/// </summary>
internal sealed class DryRun
{
    private readonly List<JsonObject> _requests = [];

    private readonly Lock _lock = new();

    /// <summary>
    /// The organisation the command acts in, once the command has resolved it.
    /// </summary>
    public OrganisationInUse? Organisation { get; set; }

    /// <summary>
    /// The partner a partner command acts for, once the command has resolved it.
    /// </summary>
    public PartnerInUse? Partner { get; set; }

    /// <summary>
    /// Records the request's method, URL and body, in the order the command sends them. The
    /// request's headers, the Authorization header that carries the token among them, are left out.
    /// </summary>
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

    /// <summary>
    /// The answer a captured change gets in place of the API's: 200 with the JSON body {}.
    /// </summary>
    // Every Enclave.Sdk.Api call that changes something either checks the status alone
    // (RemoveUserAsync, InviteUserAsync, CancelInviteAync), or reads the body into a model or a bulk
    // result and checks the result is not null (ClientBase.DeserialiseAsync and EnsureNotNull, as
    // PatchClient.ApplyAsync and CustomersClient.ReadAsync use them; Enclave.Sdk.Api 1.1.0). {} passes
    // both: it reads as a model whose fields hold their default values, or a bulk count of 0, since
    // no model has a required member (neither Enclave.Sdk.Api 1.1.0 nor Enclave.Sdk.Api.Data
    // 304.48.0 uses RequiredMemberAttribute or JsonRequiredAttribute). Every call then completes, so
    // a bulk command over 200 IDs goes on to show each of its calls.
    //
    // Nothing read from the answer is printed: the command's own output is dropped under --dry-run,
    // and the report holds the captured requests alone. No command builds a request from a change's
    // answer, with one exception: tag set sends its create after a 404 from its update, and under
    // --dry-run it reads the tag instead to choose between them (TagSetCommand).
    public static HttpResponseMessage Answer(HttpRequestMessage request) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("{}", Encoding.UTF8, MediaTypeNames.Application.Json),
        RequestMessage = request,
    };

    /// <summary>
    /// The --dry-run output: { "dryRun": true, "org": { "id", "name" }, "requests": [ { "method",
    /// "url", "body" } ] }, with "partner": { "id" } in place of "org" for a partner command. The
    /// organisation's name is null when it was given by ID, and org is null when the command
    /// resolved neither an organisation nor a partner.
    /// </summary>
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

    // Every body Enclave.Sdk.Api sends is JSON; anything else is shown as the text it is, so the
    // report holds what would have been sent.
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
