namespace Enclave.Cli.Core;

internal sealed record LabelledEntry(string Value, string? Label)
{
    /// <summary>
    /// A value given without "=" keeps the label of the existing entry with the same value
    /// (spec/cli-surface.md "Command options"). Values compare ordinally when comparer is null;
    /// countries compare ignoring case.
    /// </summary>
    public static IReadOnlyList<LabelledEntry> Keep(
        IReadOnlyList<LabelledValue> given,
        IEnumerable<LabelledEntry> existing,
        IEqualityComparer<string>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(given);
        ArgumentNullException.ThrowIfNull(existing);

        var values = comparer ?? StringComparer.Ordinal;
        var current = existing.ToArray();

        return given
            .Select(value => new LabelledEntry(
                value.Value,
                value.HasLabel ? value.Label : current.FirstOrDefault(entry => values.Equals(entry.Value, value.Value))?.Label))
            .ToArray();
    }

    public static IReadOnlyList<LabelledEntry> FromGiven(IReadOnlyList<LabelledValue>? given) =>
        given?.Select(value => new LabelledEntry(value.Value, value.Label)).ToArray() ?? [];
}
