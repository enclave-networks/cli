namespace Enclave.Cli.Core;

/// <summary>
/// Checks a list option whose list the API refuses when it holds a value twice, so the command
/// exits 2 before any call (proposed-cli-surface.md "Command options"). The policy, DNS and tag
/// commands use it.
/// </summary>
internal static class ListValues
{
    /// <summary>
    /// Exits 2 when the option's values hold one value twice, compared with the comparer given, or
    /// the default comparer when it is null. An option not given, whose values are null, passes.
    /// </summary>
    /// <typeparam name="T">The value type: a tag, name or ID.</typeparam>
    // The error does not repeat the value, as no CLI error does (CliOptions), since a value can be
    // a token passed by mistake.
    public static void CheckNoRepeats<T>(string option, IReadOnlyList<T>? values, IEqualityComparer<T>? comparer = null)
    {
        if (values is not null && values.Distinct(comparer).Count() < values.Count)
        {
            throw CliErrors.InvalidArgument(option, $"{option} gives a value more than once, and the API refuses a list that holds a value twice.");
        }
    }
}
