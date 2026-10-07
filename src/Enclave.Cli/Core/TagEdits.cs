namespace Enclave.Cli.Core;

/// <summary>
/// Works out the whole tag list an update sends from --set-tags, --add-tags and --remove-tags. The
/// patch models take the whole list (portal SystemPatchModel.Tags, DnsRecordPatchModel.Tags), so
/// adding or removing one tag means reading the item first (proposed-cli-surface.md "Create and
/// update").
/// </summary>
internal static class TagEdits
{
    /// <summary>
    /// Whether the item must be read first: --add-tags or --remove-tags without --set-tags.
    /// </summary>
    public static bool NeedsCurrentTags(IReadOnlyList<string>? set, IReadOnlyList<string>? add, IReadOnlyList<string>? remove) =>
        set is null && (add is not null || remove is not null);

    /// <summary>
    /// The list to send: --set-tags, or the item's tags, with the --add-tags not already there added
    /// at the end and the --remove-tags taken out, in order and without duplicates. Null when none
    /// of the three flags is given, so the update leaves the tags as they are.
    /// </summary>
    /// <param name="set">--set-tags; an empty list clears the tags.</param>
    /// <param name="add">--add-tags.</param>
    /// <param name="remove">--remove-tags.</param>
    /// <param name="current">The item's tag names, read when <see cref="NeedsCurrentTags"/> says so;
    /// null otherwise.</param>
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
