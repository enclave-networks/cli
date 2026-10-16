namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The terms of a search text, as the API reads them.
/// </summary>
internal static class SearchTerms
{
    /// <summary>
    /// The terms of a search text, sorted, or none when it is null.
    /// </summary>
    // The API splits the search text at whitespace into terms and applies the terms of different
    // keys whatever their order (portal BaseSearchKeyService.ParseSearchString and GetTokens), so
    // for a search that gives each key once the order of the terms is not part of the requirement
    // and they are compared sorted; each term is the exact search text the "Filters" table gives. A
    // key given twice adds its second values after the first (AddOrUpdateQueryDetails), and a key
    // that reads one value, such as state, reads the first, so there the order matters.
    public static string[] Of(string? search) =>
        (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
}
