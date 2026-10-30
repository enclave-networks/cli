using System.Globalization;
using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Gives an item by its description or by --id, and reads the candidates a name lookup error
/// carries (spec/cli-surface.md "Names and IDs").
/// </summary>
internal static class NameLookup
{
    /// <summary>
    /// The arguments that give an item by its description, or by --id.
    /// </summary>
    // Examples give an item both ways, by its description and by --id ("Names and IDs"), and the
    // command must send the same change either way.
    public static string[] Target(bool byDescription, string description, int id) =>
        byDescription ? [description] : ["--id", id.ToString(CultureInfo.InvariantCulture)];

    /// <summary>
    /// The IDs of the candidates a name lookup error carries, sorted and joined with commas.
    /// </summary>
    // The error carries candidates, an empty list for no match ("Errors and exit codes"), so a
    // missing or null list fails the test, and an empty one reads as "".
    public static string CandidateIds(JsonElement error)
    {
        if (!error.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            throw new AssertionException($"Expected the error to carry a candidates list: {error}");
        }

        return string.Join(",", candidates.EnumerateArray().Select(candidate => JsonAssert.Property(candidate, "id").GetInt32()).Order().Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }
}
