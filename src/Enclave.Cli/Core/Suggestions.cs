namespace Enclave.Cli.Core;

internal static class Suggestions
{
    // System.CommandLine's own typo corrections allow three edits whatever the length, which
    // suggests --os for --bogus (System.CommandLine 2.0.12, ParseErrorAction.MaxLevenshteinDistance).
    // One edit per three characters, up to three, keeps slips such as sytem, lst or --filtr and drops
    // words that only share a few letters.
    private const int MaxDistance = 3;

    // Null when no candidate is close enough.
    public static string? Closest(string typed, IEnumerable<string> candidates)
    {
        ArgumentNullException.ThrowIfNull(typed);
        ArgumentNullException.ThrowIfNull(candidates);

        var limit = Math.Clamp(typed.Length / 3, 1, MaxDistance);

        return candidates
            .Select(candidate => (Candidate: candidate, Distance: LevenshteinDistance(typed, candidate)))
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

    private static int LevenshteinDistance(string first, string second)
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
