namespace Enclave.Cli.Core;

/// <summary>
/// Checks a list option whose list the API refuses when it holds a value twice, so the command
/// exits 2 before any call (proposed-cli-surface.md "Command options"), and tells the value that
/// clears a repeated --set- flag's list from the values that replace it. The policy, DNS, key, tag
/// and trust commands use it.
/// </summary>
internal static class ListValues
{
    /// <summary>
    /// Whether a repeated --set- flag with a label, such as --set-allow-ip, was given "" or only
    /// white space alone, which clears its list. The option refuses a blank value in any other
    /// place (<see cref="CliOptions.Labelled"/>).
    /// </summary>
    // A --set- list flag given "" clears its list, and only white space reads as "" does
    // (proposed-cli-surface.md "Details"). A value with "=" has a label, so it is an entry.
    public static bool Clears(IReadOnlyList<LabelledValue>? values) =>
        values is [{ HasLabel: false } value] && string.IsNullOrWhiteSpace(value.Value);

    /// <summary>
    /// Whether a repeated --set- flag without labels, such as --set-gateway or --set-claim, was given
    /// "" or only white space alone, which clears its list.
    /// </summary>
    public static bool Clears(IReadOnlyList<string>? values) =>
        values is [var value] && string.IsNullOrWhiteSpace(value);

    /// <summary>
    /// Exits 2 when the option's values hold one value twice, compared with the comparer given, or
    /// the default comparer when it is null. An option not given, whose values are null, passes.
    /// </summary>
    /// <typeparam name="T">The value type: a tag, name or ID.</typeparam>
    // The error does not repeat the value, as no option's parse error does (CliOptions), since a
    // value can be a token passed by mistake.
    public static void CheckNoRepeats<T>(string option, IReadOnlyList<T>? values, IEqualityComparer<T>? comparer = null)
    {
        if (values is not null && values.Distinct(comparer).Count() < values.Count)
        {
            throw CliErrors.InvalidArgument(option, $"{option} gives a value more than once, and the API refuses a list that holds a value twice.");
        }
    }
}
