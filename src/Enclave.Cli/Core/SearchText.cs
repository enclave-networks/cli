namespace Enclave.Cli.Core;

// spec/cli-surface.md "Filters"
internal sealed class SearchText
{
    private readonly List<string> _terms = [];

    // Adding no term for a blank filter or empty list would list every item, so the options refuse
    // them as they parse (CliOptions.Filter, TagFilter), and one reaching here is a fault in the
    // command.
    public SearchText(string? filter)
    {
        if (filter is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filter);
            _terms.Add(filter);
        }
    }

    public SearchText Add(string key, string? value)
    {
        if (value is not null)
        {
            _terms.Add(value.Contains(' ', StringComparison.Ordinal) ? $"{key}:\"{value}\"" : $"{key}:{value}");
        }

        return this;
    }

    // The API reads key:a,b as items having every value given.
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

    public SearchText AddFlag(string key, bool given) => given ? Add(key, "true") : this;

    /// <summary>
    /// Null when there is no term, so the list call sends no search.
    /// </summary>
    public string? ToSearchTerm() => _terms.Count == 0 ? null : string.Join(' ', _terms);
}
