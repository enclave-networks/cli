namespace Enclave.Cli.Core;

/// <summary>
/// An entry of a labelled list as a command sends it: the value (a range, country, ACL or subnet)
/// and its label, or null for none.
/// </summary>
internal sealed record LabelledEntry(string Value, string? Label)
{
    /// <summary>
    /// The entries a --set- flag of labelled values sends: each value given, in order, with the label
    /// given after "=", an empty label as none, and a value given without "=" keeping the label of the
    /// entry the item has with the same value. A new value without a label has none
    /// (proposed-cli-surface.md "Command options": a flag that replaces a list keeps the label of every
    /// entry that stays).
    /// </summary>
    /// <param name="given">The flag's values.</param>
    /// <param name="existing">The item's entries, read first.</param>
    /// <param name="comparer">How values compare; ordinal when null. Countries compare ignoring case.</param>
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

    /// <summary>
    /// The entries a create sends: each value with the label given, or none.
    /// </summary>
    public static IReadOnlyList<LabelledEntry> FromGiven(IReadOnlyList<LabelledValue>? given) =>
        given?.Select(value => new LabelledEntry(value.Value, value.Label)).ToArray() ?? [];
}
