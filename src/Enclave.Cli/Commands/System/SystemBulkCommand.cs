using System.CommandLine;
using Enclave.Cli.Core;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Commands.Systems;

/// <summary>
/// The system verbs that take several systems, approve, decline, enable, disable and revoke: any
/// number of system IDs, or "-" to read a list from stdin, sent in bulk calls of 200, printing
/// { requested, affected } (proposed-cli-surface.md "Several IDs").
/// </summary>
internal static class SystemBulkCommand
{
    /// <summary>
    /// A verb that sends the systems given to one bulk call. <paramref name="kind"/> is the list it
    /// reads from stdin: pending-system for approve and decline, which act on waiting systems only,
    /// and system for the others.
    /// </summary>
    public static CliVerb Create(string name, string description, ListKind kind, Func<IOrganisationScopedClient, IReadOnlyList<string>, Task<int>> call)
    {
        var verb = new CliVerb(name, Describe(description), CommandScope.Organisation, changes: true);
        var systems = verb.Add(Systems());
        verb.SetHandler(context => RunAsync(context, systems, kind, call));
        return verb;
    }

    /// <summary>
    /// The description of a verb that takes several systems: its own sentence, then how it takes
    /// them and what it prints.
    /// </summary>
    public static string Describe(string description) =>
        $"{description} Takes any number of system IDs, or \"{CliArguments.Stdin}\" to read a list from stdin, and prints {{ requested, affected }}.";

    public static Argument<IReadOnlyList<string>> Systems() => CliArguments.Many("systemId", "A system ID.", IdFormats.System);

    /// <summary>
    /// Reads the systems given, then sends them in bulk calls of 200 and prints the counts.
    /// </summary>
    public static async Task RunAsync(
        CliContext context,
        Argument<IReadOnlyList<string>> systems,
        ListKind kind,
        Func<IOrganisationScopedClient, IReadOnlyList<string>, Task<int>> call)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ids = await Items.ReadIdsAsync(context, systems, kind, IdFormats.System);

        // An empty list makes no call, so a pipeline fed by an empty list succeeds without a token
        // or an organisation ("Several IDs"). A dry run goes on to report what it would send, which
        // for an empty list is no request.
        if (ids.Count == 0 && !context.IsDryRun)
        {
            await context.Output.WriteBulkAsync(new BulkResult(0, 0), context.CancellationToken);
            return;
        }

        var org = await context.GetOrganisationAsync();
        var result = await Bulk.RunAsync(ids, batch => call(org.Client, batch));
        await context.Output.WriteBulkAsync(result, context.CancellationToken);
    }
}
