namespace Enclave.Cli.Tests.Support;

internal static class SearchTerms
{
    /// <summary>
    /// The terms of a search text, sorted, or none when it is null.
    /// </summary>
    // The API applies the terms of different keys in any order (portal
    // BaseSearchKeyService.ParseSearchString). A key given twice is the exception: a key that reads
    // one value, such as state, reads the first (AddOrUpdateQueryDetails).
    public static string[] Of(string? search) =>
        (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
}
