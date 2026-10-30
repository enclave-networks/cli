namespace Enclave.Cli.Core;

internal static class ListValues
{
    // A --set- list flag given only "" or white space clears its list (spec/cli-surface.md
    // "Details"). A value with "=" has a label, so it is an entry.
    public static bool Clears(IReadOnlyList<LabelledValue>? values) =>
        values is [{ HasLabel: false } value] && string.IsNullOrWhiteSpace(value.Value);

    public static bool Clears(IReadOnlyList<string>? values) =>
        values is [var value] && string.IsNullOrWhiteSpace(value);

    // Exits 2 when the values hold one twice, which the API refuses (spec/cli-surface.md "Command
    // options"). Null passes.
    // The error does not repeat the value, which can be a token passed by mistake (CliOptions).
    public static void CheckNoRepeats<T>(string option, IReadOnlyList<T>? values, IEqualityComparer<T>? comparer = null)
    {
        if (values is not null && values.Distinct(comparer).Count() < values.Count)
        {
            throw CliErrors.InvalidArgument(option, $"{option} gives a value more than once, and the API refuses a list that holds a value twice.");
        }
    }
}
