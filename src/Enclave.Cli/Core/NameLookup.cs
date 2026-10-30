using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Api.Scaffolding.Pagination.Models;

namespace Enclave.Cli.Core;

// spec/cli-surface.md "Names and IDs"
internal static class NameLookup
{
    // Names match whole, ignoring case. A name that matches no item or several exits 2 with the
    // matches as candidates. Makes no call when names is empty. readPage must ask for disabled
    // items too, since enable takes a disabled item by name (spec/cli-surface.md "Filters").
    public static async Task<IReadOnlyList<TItem>> FindAsync<TItem, TId>(
        IReadOnlyList<string> names,
        Func<int, int, Task<PaginatedResponseModel<TItem>>> readPage,
        Func<TItem, string?> nameOf,
        Func<TItem, TId> idOf,
        string key,
        string noun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Count == 0)
        {
            return [];
        }

        var items = await Paging.ReadAllAsync(readPage, cancellationToken);
        return Match(names, items, nameOf, idOf, key, noun);
    }

    // As FindAsync, for a list already read.
    public static IReadOnlyList<TItem> Match<TItem, TId>(
        IReadOnlyList<string> names,
        IReadOnlyCollection<TItem> items,
        Func<TItem, string?> nameOf,
        Func<TItem, TId> idOf,
        string key,
        string noun)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameOf);
        ArgumentNullException.ThrowIfNull(idOf);

        return names
            .Select(name => One(
                name,
                items,
                nameOf,
                idOf,
                key,
                $"No {noun} is named \"{name}\".",
                $"\"{name}\" names more than one {noun}. Give the one meant with --id."))
            .ToArray();
    }

    // The one item whose name matches, ignoring case. No match, or several, exits 2 with noMatch
    // or several as the detail and the matches as candidates.
    public static TItem One<TItem, TId>(
        string name,
        IReadOnlyCollection<TItem> items,
        Func<TItem, string?> nameOf,
        Func<TItem, TId> idOf,
        string key,
        string noMatch,
        string several)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(nameOf);
        ArgumentNullException.ThrowIfNull(idOf);

        var matches = items.Where(item => string.Equals(nameOf(item), name, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (matches.Length == 1)
        {
            return matches[0];
        }

        var candidates = new JsonArray(matches
            .Select(item => (JsonNode?)new JsonObject
            {
                ["id"] = JsonSerializer.SerializeToNode(idOf(item), CliJson.Compact),
                ["name"] = nameOf(item),
            })
            .ToArray());

        throw CliErrors.NameNotUnique(key, matches.Length == 0 ? noMatch : several, candidates);
    }
}
