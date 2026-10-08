namespace Enclave.Cli.Core;

/// <summary>
/// Runs the call of a command that names one item: show, update, an enable with --for or --until,
/// dns delete-zone, org remove-user, org cancel-invite. A 404 from it exits 5 with not_found, where
/// any other command reports a 404 as api_error (proposed-cli-surface.md "Several IDs").
/// </summary>
internal static class SingleItem
{
    /// <summary>
    /// Runs the call and returns its model.
    /// </summary>
    /// <typeparam name="T">The model the call returns.</typeparam>
    /// <param name="call">The call, as in <c>() => org.Client.Policies.GetAsync(PolicyId.FromInt(id))</c>.</param>
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

    /// <summary>
    /// As <see cref="CallAsync{T}(Func{Task{T}})"/>, for a call that returns no model.
    /// </summary>
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
