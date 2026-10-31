using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Org;

internal static class OrgUseCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "use",
            "Saves the default organisation.");

        var name = verb.Add(CliArguments.OptionalText("name", "The organisation's name, matched whole and ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The organisation's ID.", IdFormats.Guid, "orgId"));
        verb.ExactlyOne(name, id);
        verb.RefuseBlank(name, context => context.Get(name), "an organisation name");
        EnclaveDirectory.RequireHome(verb);

        verb.SetHandler(async context =>
        {
            var organisations = await context.GetClient().GetOrganisationsAsync();

            var organisation = context.Get(id) is { } guid
                ? organisations.FirstOrDefault(candidate => candidate.OrgId.Equals(OrganisationGuid.FromGuid(guid)))
                    ?? throw CliErrors.NameNotUnique(id.Name, $"The token sees no organisation with ID {guid:D}.", OrganisationResolver.Candidates([]))
                : OrganisationResolver.MatchName(context.Get(name)!, organisations, name.Name);

            context.Trace.Saved(CredentialsFile.SaveDefaultOrganisation(context.Host, organisation.OrgId));

            await context.Output.WriteAsync(organisation, context.CancellationToken);
        });

        return verb;
    }
}
