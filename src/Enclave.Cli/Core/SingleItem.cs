namespace Enclave.Cli.Core;

// A 404 from a command that names one item exits 5 not_found; any other command reports a 404 as
// api_error (spec/cli-surface.md "Several IDs").
internal static class SingleItem
{
    public static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            return await call();
        }
        catch (Exception exception) when (exception is not CliException)
        {
            throw ApiErrors.ToCliException(exception, notFoundIsUnknownItem: true);
        }
    }

    public static async Task CallAsync(Func<Task> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        try
        {
            await call();
        }
        catch (Exception exception) when (exception is not CliException)
        {
            throw ApiErrors.ToCliException(exception, notFoundIsUnknownItem: true);
        }
    }
}
