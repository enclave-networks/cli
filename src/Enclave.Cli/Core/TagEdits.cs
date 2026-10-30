namespace Enclave.Cli.Core;

// The patch models take the whole tag list (portal SystemPatchModel.Tags, DnsRecordPatchModel.Tags),
// so adding or removing one tag means reading the item first (spec/cli-surface.md "Create and
// update").
internal static class TagEdits
{
    public static bool NeedsCurrentTags(IReadOnlyList<string>? set, IReadOnlyList<string>? add, IReadOnlyList<string>? remove) =>
        set is null && (add is not null || remove is not null);

    // Null when none of the three flags is given, so the update leaves the tags as they are.
    // current is null unless NeedsCurrentTags says to read it.
    public static IReadOnlyList<string>? Apply(
        IReadOnlyList<string>? set,
        IReadOnlyList<string>? add,
        IReadOnlyList<string>? remove,
        IEnumerable<string>? current)
    {
        if (set is null && add is null && remove is null)
        {
            return null;
        }

        var tags = (set ?? current ?? []).Concat(add ?? []).Distinct(StringComparer.Ordinal);

        return remove is null ? tags.ToArray() : tags.Where(tag => !remove.Contains(tag, StringComparer.Ordinal)).ToArray();
    }
}
