namespace Enclave.Cli.Core;

/// <summary>
/// Finds the known name closest to a mistyped one.
/// </summary>
internal static class Suggestions
{
    // System.CommandLine's own typo corrections allow three edits whatever the length
    // (ParseErrorAction, MaxLevenshteinDistance, version 2.0.12), which suggests --os for --bogus.
    // One edit per three characters, up to three, keeps the suggestions for slips such as sytem,
    // lst or --filtr and drops those for words that only share a few letters.
    private const int MaxDistance = 3;

    /// <summary>
    /// The candidate with the smallest edit distance from <paramref name="typed"/>, within one edit
    /// per three characters typed (at least one, at most three), preferring the longer common prefix
    /// on a tie; null when none is that close.
    /// </summary>
    public static string? Closest(string typed, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(candidates);

        var limit = Math.Clamp(typed.Length / 3, 1, MaxDistance);

        return candidates
            .Select(candidate => (Candidate: candidate, Distance: Distance(typed, candidate)))
            .Where(match => match.Distance <= limit)
            .OrderBy(match => match.Distance)
            .ThenByDescending(match => CommonPrefix(typed, match.Candidate))
            .Select(match => match.Candidate)
            .FirstOrDefault();
    }

    private static int CommonPrefix(string first, string second)
    {
        var length = 0;

        while (length < first.Length && length < second.Length && first[length] == second[length])
        {
            length++;
        }

        return length;
    }

    // Levenshtein distance, keeping two rows of the matrix.
    private static int Distance(string first, string second)
    {
        var previous = new int[second.Length + 1];
        var current = new int[second.Length + 1];

        for (var j = 0; j <= second.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= first.Length; i++)
        {
            current[0] = i;

            for (var j = 1; j <= second.Length; j++)
            {
                var substitution = previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[second.Length];
    }
}
