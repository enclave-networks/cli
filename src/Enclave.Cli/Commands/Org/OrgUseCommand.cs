using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Org;

/// <summary>
/// `org use`: saves the default organisation in ~/.enclave/cli.json (proposed-cli-surface.md
/// "Login, logout and status").
/// </summary>
internal static class OrgUseCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "use",
            "Save the default organisation in ~/.enclave/cli.json, chosen by name or by --id; later commands use it with no lookup. Makes one call, to find the name for an ID or the ID for a name, and prints the organisation.");

        var name = verb.Add(CliArguments.OptionalText("name", "The organisation's name, matched whole and ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The organisation's ID.", IdFormats.Guid, "orgId"));
        verb.ExactlyOne(name, id);

        verb.SetHandler(async context =>
        {
            var organisations = await context.GetClient().GetOrganisationsAsync();

            var organisation = context.Get(id) is { } guid
                ? organisations.FirstOrDefault(candidate => candidate.OrgId.Equals(OrganisationGuid.FromGuid(guid)))
                    ?? throw CliErrors.NameNotUnique(id.Name, $"The token sees no organisation with ID {guid:D}.", OrganisationResolver.Candidates([]))
                : OrganisationResolver.MatchName(context.Get(name)!, organisations, name.Name);

            SettingsFile.SaveOrganisation(context.Host, organisation.OrgId, organisation.OrgName);
            context.Verbose($"Saved {organisation.OrgId} as the default organisation in {SettingsFile.PathFor(context.Host)}.");

            await context.Output.WriteAsync(organisation, context.CancellationToken);
        });

        return verb;
    }
}
