using System.CommandLine;
using Enclave.Cli.Core;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Commands.Systems;

internal static class SystemBulkCommand
{
    public static CliVerb Create(string name, string description, ListKind kind, Func<IOrganisationScopedClient, IReadOnlyList<string>, Task<int>> call)
    {
        var verb = new CliVerb(name, Describe(description), CommandScope.Organisation, changes: true);
        var systems = verb.Add(Systems());
        verb.SetHandler(context => RunAsync(context, systems, kind, call));
        return verb;
    }

    public static string Describe(string description) =>
        $"{description} Takes any number of system IDs, or \"{CliArguments.Stdin}\" to read a list from stdin, and prints {{ requested, affected }}.";

    public static Argument<IReadOnlyList<string>> Systems() => CliArguments.Many("systemId", "A system ID.", IdFormats.System);

    public static async Task RunAsync(
        CliContext context,
        Argument<IReadOnlyList<string>> systems,
        ListKind kind,
        Func<IOrganisationScopedClient, IReadOnlyList<string>, Task<int>> call)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ids = await Items.ReadIdsAsync(context, systems, kind, IdFormats.System);

        if (await Bulk.WroteEmptyAsync(context, ids.Count))
        {
            return;
        }

        var org = await context.GetOrganisationAsync();
        var result = await Bulk.RunAsync(ids, batch => call(org.Client, batch));
        await context.Output.WriteBulkAsync(result, context.CancellationToken);
    }
}
