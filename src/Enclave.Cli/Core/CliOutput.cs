using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

// stdout is held until the command succeeds, so a command that fails part way prints nothing there.
internal sealed class CliOutput
{
    private readonly TextWriter _stdout;

    private readonly TextWriter _stderr;

    private readonly List<object> _pending = [];

    private readonly bool _dropCommandOutput;

    // Under --dry-run the command's own output comes from answers the API never sent (DryRun.Answer),
    // so it is dropped and WriteDryRunAsync prints the report in its place (spec/cli-surface.md "Dry
    // run").
    public CliOutput(TextWriter stdout, TextWriter stderr, CliTrace trace, bool dryRun = false)
    {
        _stdout = stdout;
        _stderr = stderr;
        Trace = trace;
        _dropCommandOutput = dryRun;
    }

    public CliTrace Trace { get; }

    // Under --dry-run it prints nothing. T decides the properties written, whatever the value's
    // runtime type.
    public Task WriteAsync<T>(T value, CancellationToken cancellationToken = default) =>
        _dropCommandOutput ? Task.CompletedTask : AppendJsonAsync(value, cancellationToken);

    public Task WriteDryRunAsync(JsonObject report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        _pending.Clear();
        return AppendJsonAsync(report, cancellationToken);
    }

    public Task WriteListAsync<T>(ListKind kind, IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(items);

        return WriteAsync(new ListEnvelope<T>(kind.Name, items, items.Count), cancellationToken);
    }

    public Task WriteBulkAsync(BulkResult result, CancellationToken cancellationToken = default) =>
        WriteAsync(result, cancellationToken);

    // Only help prints text; every other output is JSON.
    public Task WriteTextAsync(string text)
    {
        if (!_dropCommandOutput)
        {
            _pending.Add(text);
        }

        return Task.CompletedTask;
    }

    // Call once the command has succeeded.
    public async Task FlushAsync()
    {
        var trace = Trace.ToJson();
        var text = new StringBuilder();

        foreach (var entry in _pending)
        {
            text.Append(entry is JsonObject document ? Print(document, trace) + "\n" : entry);
        }

        _pending.Clear();
        await _stdout.WriteAsync(text.ToString());
    }

    public async Task WriteErrorAsync(CliException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _pending.Clear();
        await _stderr.WriteLineAsync(Print(Describe(error), Trace.ToJson(error)));
    }

    // For an error before any command runs, which has no trace.
    public static string Format(CliException error) => Describe(error).ToJsonString(CliJson.Output);

    private static string Print(JsonObject document, JsonObject? trace)
    {
        if (trace is not null)
        {
            document["trace"] = trace.DeepClone();
        }

        return document.ToJsonString(CliJson.Output);
    }

    private static JsonObject Describe(CliException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        // errors stays empty when its one message is detail's, so the person reading the error
        // reads it once (spec/cli-surface.md "Errors and exit codes").
        var errors = new JsonObject();
        if (error.Errors.Values.SelectMany(messages => messages).ToArray() is not [var only] || only != error.Detail)
        {
            foreach (var (key, messages) in error.Errors)
            {
                errors[key] = new JsonArray(messages.Select(message => (JsonNode?)JsonValue.Create(message)).ToArray());
            }
        }

        var body = new JsonObject
        {
            ["code"] = error.Code.Code,
            ["status"] = error.Status,
            ["title"] = error.Title,
            ["detail"] = error.Detail,
            ["errors"] = errors,
        };

        if (error.Candidates is not null)
        {
            body["candidates"] = error.Candidates.DeepClone();
        }

        if (error.Requested is not null)
        {
            body["requested"] = error.Requested;
            body["affected"] = error.Affected;
        }

        return new JsonObject { ["error"] = body };
    }

    // Some Enclave.Sdk.Api models hold IAsyncEnumerable properties, which only the asynchronous
    // serializer writes, so the document is read back from its output to add the trace.
    private async Task AppendJsonAsync<T>(T value, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, CliJson.Output, cancellationToken);
        stream.Position = 0;

        _pending.Add(await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidOperationException($"{typeof(T).Name} is not written as a JSON object, so no trace can end it."));
    }

    private sealed record ListEnvelope<T>(string Kind, IReadOnlyCollection<T> Items, int Total);
}
