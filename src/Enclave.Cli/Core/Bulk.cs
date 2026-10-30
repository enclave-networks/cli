namespace Enclave.Cli.Core;

// spec/cli-surface.md "Several IDs"
internal static class Bulk
{
    // The API's limit (portal Enclave.Utilities/HardLimits.cs, MaxBulkIds).
    public const int MaxIdsPerCall = 200;

    // Takes IDs with duplicates removed. An empty list makes no call. A failed call stops the
    // command, and its error carries the counts of the calls before it; running the command again
    // is safe, since the API does not count items already in that state.
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

    // For an empty list, prints { 0, 0 } and returns true: the command makes no call. Under --dry-run
    // it returns false, and the command goes on to print its dry run output.
    // An empty list needs no token or organisation, so a pipeline fed by one succeeds ("Several IDs").
    // A dry run goes on because its output names the organisation ("Dry run").
    public static async Task<bool> WroteEmptyAsync(CliContext context, int count)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (count > 0 || context.IsDryRun)
        {
            return false;
        }

        await context.Output.WriteBulkAsync(new BulkResult(0, 0), context.CancellationToken);
        return true;
    }
}
