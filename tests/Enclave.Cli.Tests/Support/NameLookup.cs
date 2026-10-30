using System.Globalization;
using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

internal static class NameLookup
{
    /// <summary>
    /// The arguments that give an item by its description, or by --id. A command must send the
    /// same change either way (spec/cli-surface.md "Names and IDs").
    /// </summary>
    public static string[] Target(bool byDescription, string description, int id) =>
        byDescription ? [description] : ["--id", id.ToString(CultureInfo.InvariantCulture)];

    /// <summary>
    /// The IDs of the candidates a name lookup error carries, sorted and joined with commas.
    /// </summary>
    // The error carries candidates even when there are none (spec/cli-surface.md "Errors and exit
    // codes"), so a missing list fails the test.
    public static string CandidateIds(JsonElement error)
    {
        if (!error.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            throw new AssertionException($"Expected the error to carry a candidates list: {error}");
        }

        return string.Join(",", candidates.EnumerateArray().Select(candidate => JsonAssert.Property(candidate, "id").GetInt32()).Order().Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }
}
