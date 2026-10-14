namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The terms of a search text, as the API reads them.
/// </summary>
internal static class SearchTerms
{
    /// <summary>
    /// The terms of a search text, sorted, or none when it is null.
    /// </summary>
    // The API splits the search text at whitespace into terms and applies each, whatever their
    // order (portal BaseSearchKeyService.ParseSearchString and GetTokens), so the order of the terms
    // is not part of the requirement and they are compared sorted; each term is the exact search
    // text the "Filters" table gives.
    public static string[] Of(string? search) =>
        (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
}
