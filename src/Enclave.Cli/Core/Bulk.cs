namespace Enclave.Cli.Core;

/// <summary>
/// Sends the bulk calls of a command that takes several items (proposed-cli-surface.md "Several IDs").
/// </summary>
internal static class Bulk
{
    /// <summary>
    /// The most IDs the API takes in one bulk call (portal Enclave.Utilities/HardLimits.cs:22,
    /// MaxBulkIds).
    /// </summary>
    public const int MaxIdsPerCall = 200;

    /// <summary>
    /// Sends the IDs in calls of <see cref="MaxIdsPerCall"/>, in order, and adds up the counts. An
    /// empty list makes no call and gives { 0, 0 }. A failed call stops the command: its error
    /// carries requested and affected for the calls that succeeded before it, and running the
    /// command again is safe, since the API does not count items already in that state.
    /// </summary>
    /// <typeparam name="TId">The ID type: string for system IDs and tag names, int for integer IDs.</typeparam>
    /// <param name="ids">The IDs, duplicates already removed.</param>
    /// <param name="call">One bulk call, returning the count the API answers with, as in
    /// <c>batch => org.Client.Policies.DisablePoliciesAsync(batch.Select(PolicyId.FromInt))</c>.</param>
    public static async Task<BulkResult> RunAsync<TId>(IReadOnlyList<TId> ids, Func<IReadOnlyList<TId>, Task<int>> call)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(call);

        var requested = 0;
        var affected = 0;

        foreach (var batch in ids.Chunk(MaxIdsPerCall))
        {
            int count;

            try
            {
                count = await call(batch);
            }
            catch (Exception exception)
            {
                throw ApiErrors.ToCliException(exception).WithBulkCounts(requested, affected);
            }

            requested += batch.Length;
            affected += count;
        }

        return new BulkResult(requested, affected);
    }
}
