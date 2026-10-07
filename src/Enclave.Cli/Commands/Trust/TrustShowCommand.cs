using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Trust;

/// <summary>
/// `trust show`: one trust requirement with its settings, as the API's GET by ID returns it; the
/// list items are summaries without settings (portal TrustRequirementSummaryModel).
/// </summary>
internal static class TrustShowCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("show", "Show a trust requirement.", CommandScope.Organisation);
        var description = verb.Add(CliArguments.OptionalText("trust", "The trust requirement's description, matched whole and ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The trust requirement's ID.", IdFormats.Int32));

        verb.ExactlyOne(description, id);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var requirementId = await TrustLookup.IdAsync(context, org, context.Get(id), context.Get(description), description.Name);

            // One trust requirement named: a 404 means it does not exist, exit 5 ("Several IDs").
            var requirement = await SingleItem.CallAsync(() => org.Client.TrustRequirements.GetAsync(TrustRequirementId.FromInt(requirementId)));
            await context.Output.WriteAsync(requirement, context.CancellationToken);
        });

        return verb;
    }
}
