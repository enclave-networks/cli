using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Core;

// spec/cli-surface.md "Several IDs"
internal static class Items
{
    // For a command whose arguments are names and whose IDs go after --id; declare the two
    // exclusive. Exits 2 when nothing is given.
    public static async Task<ItemSelection<TId>> ReadAsync<TId>(
        CliContext context,
        Argument<IReadOnlyList<string>> names,
        Option<IReadOnlyList<TId>?> ids,
        ListKind kind,
        IdFormat<TId> format)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(ids);

        var given = context.Get(names) ?? [];

        if (IsStdin(given))
        {
            return new ItemSelection<TId>(await FromStdinAsync(context, kind, format), []);
        }

        if (context.Get(ids) is { } idList)
        {
            // --id "" reads as an empty list (CliOptions.Pieces), which names nothing. Only "-" with
            // an empty list succeeds with nothing to do, so a pipeline fed by an empty list succeeds
            // (spec/cli-surface.md "Several IDs").
            return idList.Count > 0
                ? new ItemSelection<TId>(idList.Distinct().ToArray(), [])
                : throw CliErrors.InvalidArgument(ids.Name, $"{ids.Name} holds no ID. Give at least one ID after {ids.Name}, a <{names.Name}>, or \"{CliArguments.Stdin}\" to read a list from stdin.");
        }

        if (given.Count == 0)
        {
            throw CliErrors.InvalidArgument(names.Name, $"Give at least one <{names.Name}>, IDs after {ids.Name}, or \"{CliArguments.Stdin}\" to read a list from stdin.");
        }

        return new ItemSelection<TId>([], given);
    }

    // For a command whose arguments are IDs: systems, and tags. Exits 2 when nothing is given.
    // Duplicates are compared in the form the format reads, so a system ID given in two cases is
    // one ID (IdFormats.System).
    public static async Task<IReadOnlyList<string>> ReadIdsAsync(
        CliContext context,
        Argument<IReadOnlyList<string>> argument,
        ListKind kind,
        IdFormat<string> format)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(argument);
        ArgumentNullException.ThrowIfNull(format);

        var given = context.Get(argument) ?? [];

        if (IsStdin(given))
        {
            return await FromStdinAsync(context, kind, format);
        }

        if (given.Count == 0)
        {
            throw CliErrors.InvalidArgument(argument.Name, $"Give at least one <{argument.Name}>, or \"{CliArguments.Stdin}\" to read a list from stdin.");
        }

        var ids = new List<string>(given.Count);

        foreach (var text in given)
        {
            if (!format.TryParse(text, out var id))
            {
                throw CliErrors.InvalidArgument(argument.Name, $"Each <{argument.Name}> is {format.Describe}.");
            }

            ids.Add(id);
        }

        return ids.Distinct(StringComparer.Ordinal).ToArray();
    }

    // Exits 2 when stdin is a terminal, is not a list of kind, or holds an ID not in format.
    public static async Task<IReadOnlyList<TId>> FromStdinAsync<TId>(CliContext context, ListKind kind, IdFormat<TId> format)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(format);

        var key = CliArguments.Stdin;
        var expected = $"stdin to hold a list printed by an enclave-cli list command of kind \"{kind.Name}\"";

        // The CLI never waits for input (AGENTS.md "CLI contract"), and reading a terminal waits for
        // someone to type.
        if (context.Host.StdinIsTerminal)
        {
            throw CliErrors.InvalidArgument(key, $"\"{CliArguments.Stdin}\" reads a list from stdin, and stdin is a terminal. Pipe a list into the command.");
        }

        var text = await context.Host.Stdin.ReadToEndAsync(context.CancellationToken);
        JsonObject list;

        try
        {
            list = JsonNode.Parse(text) as JsonObject ?? throw CliErrors.InvalidArgument(key, $"\"{CliArguments.Stdin}\" expects {expected}.");
        }
        catch (JsonException)
        {
            throw CliErrors.InvalidArgument(key, $"\"{CliArguments.Stdin}\" expects {expected}; stdin is not JSON.");
        }

        if (list["kind"] is not JsonValue kindValue || !kindValue.TryGetValue(out string? listKind) || list["items"] is not JsonArray items)
        {
            throw CliErrors.InvalidArgument(key, $"\"{CliArguments.Stdin}\" expects {expected}; stdin holds JSON without a kind and an items array.");
        }

        if (!string.Equals(listKind, kind.Name, StringComparison.Ordinal))
        {
            throw CliErrors.InvalidArgument(key, $"\"{CliArguments.Stdin}\" expects {expected}; stdin holds a list of kind \"{listKind}\".");
        }

        var ids = new List<TId>(items.Count);

        foreach (var item in items)
        {
            var id = kind.IdField is { } field && item is JsonObject obj ? Text(obj[field]) : null;

            if (id is null || !format.TryParse(id, out var value))
            {
                throw CliErrors.InvalidArgument(key, $"\"{CliArguments.Stdin}\" expects {expected}; item {ids.Count + 1} has no {kind.IdField} that is {format.Describe}.");
            }

            ids.Add(value);
        }

        return ids.Distinct().ToArray();
    }

    private static bool IsStdin(IReadOnlyList<string> given) => given is [CliArguments.Stdin];

    // An integer ID is a number in a list, and is read from its JSON text, so 1.5 or a number beyond
    // 32 bits fails the ID check.
    private static string? Text(JsonNode? node) => node switch
    {
        JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),
        JsonValue value when value.GetValueKind() == JsonValueKind.Number => value.ToJsonString(),
        _ => null,
    };
}
