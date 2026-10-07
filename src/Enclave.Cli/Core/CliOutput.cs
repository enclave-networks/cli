using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

/// <summary>
/// What a command prints. stdout is held until the command succeeds, so a command that fails part
/// way prints nothing there; errors and --verbose diagnostics go to stderr as they happen.
/// </summary>
internal sealed class CliOutput
{
    private readonly TextWriter _stdout;

    private readonly TextWriter _stderr;

    private readonly StringBuilder _pending = new();

    private readonly bool _dropCommandOutput;

    /// <summary>
    /// The output of one command. With <paramref name="dryRun"/> set, everything the command prints
    /// on stdout is dropped, and <see cref="WriteDryRunAsync"/> prints the dry-run report in its place.
    /// </summary>
    // Under --dry-run each change is answered by the CLI itself (DryRun.Answer), so the model or
    // count a command would print comes from that answer, which the API never sent. Dropping it
    // unread keeps it out of stdout, and spares writing a model whose fields hold default values.
    public CliOutput(TextWriter stdout, TextWriter stderr, bool verbose, bool dryRun = false)
    {
        _stdout = stdout;
        _stderr = stderr;
        IsVerbose = verbose;
        _dropCommandOutput = dryRun;
    }

    public bool IsVerbose { get; }

    /// <summary>
    /// Prints an Enclave.Sdk.Api model, or any value, as JSON. Under --dry-run it prints nothing.
    /// </summary>
    /// <typeparam name="T">The value's type, which decides the properties written.</typeparam>
    public Task WriteAsync<T>(T value, CancellationToken cancellationToken = default) =>
        _dropCommandOutput ? Task.CompletedTask : AppendJsonAsync(value, cancellationToken);

    /// <summary>
    /// Prints the --dry-run report, { "dryRun", "org", "requests" }, as the command's only output.
    /// </summary>
    public Task WriteDryRunAsync(JsonObject report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        _pending.Clear();
        return AppendJsonAsync(report, cancellationToken);
    }

    /// <summary>
    /// Prints a list in its envelope, { "kind", "items", "total" }, with total the number of items.
    /// </summary>
    /// <typeparam name="T">The item model.</typeparam>
    public Task WriteListAsync<T>(ListKind kind, IReadOnlyCollection<T> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(items);

        return WriteAsync(new ListEnvelope<T>(kind.Name, items, items.Count), cancellationToken);
    }

    /// <summary>
    /// Prints { "requested", "affected" }, the output of a command that takes several items.
    /// </summary>
    public Task WriteBulkAsync(BulkResult result, CancellationToken cancellationToken = default) =>
        WriteAsync(result, cancellationToken);

    /// <summary>
    /// Prints {}, the output of a call whose API response has no body.
    /// </summary>
    public Task WriteNoBodyAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(new JsonObject(), cancellationToken);

    /// <summary>
    /// Prints text as it is. Only help prints text; every other output is JSON.
    /// </summary>
    public Task WriteTextAsync(string text)
    {
        if (!_dropCommandOutput)
        {
            _pending.Append(text);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a diagnostic line on stderr when --verbose is given. Callers never pass the token.
    /// </summary>
    public void Verbose(string message)
    {
        if (IsVerbose)
        {
            _stderr.WriteLine(new JsonObject { ["verbose"] = message }.ToJsonString(CliJson.Compact));
        }
    }

    /// <summary>
    /// Writes the held stdout. Called once the command has succeeded.
    /// </summary>
    public async Task FlushAsync()
    {
        await _stdout.WriteAsync(_pending.ToString());
        _pending.Clear();
    }

    /// <summary>
    /// Drops the held stdout and writes the error as one JSON line on stderr.
    /// </summary>
    public async Task WriteErrorAsync(CliException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        _pending.Clear();
        await _stderr.WriteLineAsync(Describe(error).ToJsonString(CliJson.Compact));
    }

    /// <summary>
    /// The error as the CLI writes it: { "error": { "code", "status", "title", "detail", "errors" } },
    /// with candidates, requested and affected when the error carries them.
    /// </summary>
    public static JsonObject Describe(CliException error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var errors = new JsonObject();
        foreach (var (key, messages) in error.Errors)
        {
            errors[key] = new JsonArray(messages.Select(message => (JsonNode?)JsonValue.Create(message)).ToArray());
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

    // SerializeAsync, because some Enclave.Sdk.Api models hold IAsyncEnumerable properties, which
    // only the asynchronous serializer writes (System.Text.Json, JsonSerializer.Serialize throws
    // NotSupportedException for them).
    private async Task AppendJsonAsync<T>(T value, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, CliJson.Output, cancellationToken);
        _pending.Append(Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length)).Append('\n');
    }

    private sealed record ListEnvelope<T>(string Kind, IReadOnlyCollection<T> Items, int Total);
}
