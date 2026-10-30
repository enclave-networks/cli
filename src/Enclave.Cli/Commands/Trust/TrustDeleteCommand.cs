using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Trust;

internal static class TrustDeleteCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("delete", "Delete trust requirements. Takes descriptions, IDs after --id, or \"-\" to read a list from stdin, and prints { requested, affected }.", CommandScope.Organisation, changes: true);
        var requirements = verb.Add(CliArguments.Names("trust", "A trust requirement's description, matched whole and ignoring case.", "a trust requirement's description"));
        var ids = verb.Add(CliOptions.IdList("--id", "Trust requirement IDs, separated by commas.", IdFormats.Int32));

        verb.Exclusive(requirements, ids);

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, requirements, ids, ListKind.Trust, IdFormats.Int32);

            if (await Bulk.WroteEmptyAsync(context, selection.Count))
            {
                return;
            }

            var org = await context.GetOrganisationAsync();
            var requirementIds = selection.Names.Count > 0
                ? (await TrustLookup.IdsAsync(context, org, selection.Names, requirements.Name)).Distinct().ToArray()
                : selection.Ids;

            var result = await Bulk.RunAsync(
                requirementIds,
                batch => org.Client.TrustRequirements.DeleteTrustRequirementsAsync(batch.Select(TrustRequirementId.FromInt).ToArray()));

            await context.Output.WriteBulkAsync(result, context.CancellationToken);
        });

        return verb;
    }
}
