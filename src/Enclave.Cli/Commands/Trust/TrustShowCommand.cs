using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Trust;

internal static class TrustShowCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("show", "Show a trust requirement.", CommandScope.Organisation);
        var description = verb.Add(CliArguments.OptionalName("trust", "The trust requirement's description, matched whole and ignoring case.", "a trust requirement's description"));
        var id = verb.Add(CliOptions.Id("--id", "The trust requirement's ID.", IdFormats.Int32));

        verb.ExactlyOne(description, id);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var requirementId = await TrustLookup.IdAsync(context, org, context.Get(id), context.Get(description), description.Name);

            // The list a name is looked up in holds summaries without settings (portal
            // TrustRequirementSummaryModel), so show reads the requirement by ID.
            var requirement = await SingleItem.CallAsync(() => org.Client.TrustRequirements.GetAsync(TrustRequirementId.FromInt(requirementId)));
            await context.Output.WriteAsync(requirement, context.CancellationToken);
        });

        return verb;
    }
}
