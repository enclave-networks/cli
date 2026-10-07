namespace Enclave.Cli.Core;

/// <summary>
/// The handler of a command whose options are declared and whose work is not written yet: it
/// resolves the token and organisation as the finished command will, then exits 1 with
/// not_implemented, having made no call but the organisation lookup.
/// </summary>
internal static class Placeholder
{
    public static async Task InOrganisationAsync(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = await context.GetOrganisationAsync();

        throw CliErrors.NotImplemented($"`{ParseProblems.CommandName(context.ParseResult.CommandResult)}` is not built yet.");
    }
}
