using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Api.Scaffolding.Pagination.Models;

namespace Enclave.Cli.Core;

/// <summary>
/// Finds items by name: a key, policy or trust requirement by its description, a zone by its name,
/// and so on (proposed-cli-surface.md "Names and IDs").
/// </summary>
internal static class NameLookup
{
    /// <summary>
    /// Reads the whole list once and returns, for each name in order, the one item whose name
    /// matches it whole, ignoring case. A name that matches no item, or several, exits 2 with
    /// invalid_argument and the matches as candidates, { "id", "name" }, an empty list for no match.
    /// Makes no call when <paramref name="names"/> is empty.
    /// </summary>
    /// <typeparam name="TItem">The list's item model.</typeparam>
    /// <typeparam name="TId">The item's ID type.</typeparam>
    /// <param name="names">The names given.</param>
    /// <param name="readPage">Reads one page of the list, asking for disabled items too
    /// (include_disabled), since enable takes a disabled item by name ("Filters"), as in
    /// <c>(page, perPage) => client.Policies.GetPoliciesAsync(includeDisabled: true, pageNumber: page, perPage: perPage)</c>.</param>
    /// <param name="nameOf">The item's name, such as PolicyModel.Description.</param>
    /// <param name="idOf">The item's ID, written into the candidates.</param>
    /// <param name="key">The option or argument the names came from, for errors.</param>
    /// <param name="noun">What the items are, for messages: "policy".</param>
    /// <param name="cancellationToken">Stops reading.</param>
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

    /// <summary>
    /// As <see cref="FindAsync"/>, for a list already read, such as one Enclave.Sdk.Api returns
    /// whole (GetOrganisationUsersAsync).
    /// </summary>
    /// <typeparam name="TItem">The list's item model.</typeparam>
    /// <typeparam name="TId">The item's ID type.</typeparam>
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

        var found = new List<TItem>(names.Count);

        foreach (var name in names)
        {
            var matches = items.Where(item => string.Equals(nameOf(item), name, StringComparison.OrdinalIgnoreCase)).ToArray();

            if (matches.Length == 1)
            {
                found.Add(matches[0]);
                continue;
            }

            var candidates = new JsonArray(matches
                .Select(item => (JsonNode?)new JsonObject
                {
                    ["id"] = JsonSerializer.SerializeToNode(idOf(item), CliJson.Compact),
                    ["name"] = nameOf(item),
                })
                .ToArray());

            var message = matches.Length == 0
                ? $"No {noun} is named \"{name}\"."
                : $"\"{name}\" names more than one {noun}. Give the one meant with --id.";

            throw CliErrors.NameNotUnique(key, message, candidates);
        }

        return found;
    }
}
