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
    /// nothing when it is null or empty.
    /// </summary>
    public SearchText(string? filter)
    {
        if (!string.IsNullOrEmpty(filter))
        {
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
    /// given; nothing when the list is null or empty.
    /// </summary>
    public SearchText Add(string key, IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? Add(key, string.Join(',', values)) : this;

    /// <summary>
    /// Adds key:true when the flag is given, as --gateway adds gateway:true.
    /// </summary>
    public SearchText AddFlag(string key, bool given) => given ? Add(key, "true") : this;

    /// <summary>
    /// The search text, or null when there is none, so the list call sends no search.
    /// </summary>
    public string? ToSearchTerm() => _terms.Count == 0 ? null : string.Join(' ', _terms);
}
