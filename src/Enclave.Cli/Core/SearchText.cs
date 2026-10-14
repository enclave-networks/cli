namespace Enclave.Cli.Core;

/// <summary>
/// Builds a list's search text: the --filter text as typed, then each search-key flag as the API's
/// search syntax, key:value (proposed-cli-surface.md "Filters").
/// </summary>
internal sealed class SearchText
{
    private readonly List<string> _terms = [];

    /// <summary>
    /// Starts with the --filter text, sent as typed so that it takes the API's own search syntax;
    /// nothing when it is null. Throws <see cref="ArgumentException"/> for a blank filter.
    /// </summary>
    // A blank filter or an empty list filters by nothing, and adding no term for it would list every
    // item, so the options a list filters by refuse it as they parse (CliOptions.Filter, TagFilter)
    // and one reaching here is a fault in the command.
    public SearchText(string? filter)
    {
        if (filter is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filter);
            _terms.Add(filter);
        }
    }

    /// <summary>
    /// Adds key:value, or nothing when the value is null. A value with a space is quoted,
    /// key:"build agents".
    /// </summary>
    public SearchText Add(string key, string? value)
    {
        if (value is not null)
        {
            _terms.Add(value.Contains(' ', StringComparison.Ordinal) ? $"{key}:\"{value}\"" : $"{key}:{value}");
        }

        return this;
    }

    /// <summary>
    /// Adds key:a,b for a list such as --tag a,b, which the API reads as items having every value
    /// given; nothing when the list is null. Throws <see cref="ArgumentException"/> for an empty
    /// list.
    /// </summary>
    public SearchText Add(string key, IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return this;
        }

        return values.Count > 0
            ? Add(key, string.Join(',', values))
            : throw new ArgumentException($"The list for the search key {key} holds no value.", nameof(values));
    }

    /// <summary>
    /// Adds key:true when the flag is given, as --gateway adds gateway:true.
    /// </summary>
    public SearchText AddFlag(string key, bool given) => given ? Add(key, "true") : this;

    /// <summary>
    /// The search text, or null when there is none, so the list call sends no search.
    /// </summary>
    public string? ToSearchTerm() => _terms.Count == 0 ? null : string.Join(' ', _terms);
}
