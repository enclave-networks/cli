using System.Text.Json.Nodes;
using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Auth;

// spec/cli-surface.md "Login, logout and status".
internal static class StatusCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "status",
            "Show where the token comes from, the organisation and partner a command would use and what chose them, and the token's role in the organisation. Makes one call, which checks the token.",
            CommandScope.Organisation | CommandScope.Partner);

        verb.SetHandler(async context =>
        {
            var client = context.GetClient();

            // A malformed or contradictory source exits 2 before the call (spec/cli-surface.md
            // "Errors and exit codes").
            var choice = OrganisationResolver.Choose(context);
            var partnerChoice = PartnerResolver.Choose(context);

            // The one call checks the token, and a name or the only organisation is resolved from
            // its response.
            var organisations = await client.GetOrganisationsAsync();
            JsonObject? org = null;

            if (choice?.Id is { } id)
            {
                var known = organisations.FirstOrDefault(organisation => organisation.OrgId.Equals(id));
                org = Org(id.ToString(), known?.OrgName ?? choice.Name, known?.Role.ToString(), choice.Source);
            }
            else if (choice?.Name is { } name)
            {
                var match = OrganisationResolver.MatchName(name, organisations, choice.Source);
                org = Org(match.OrgId.ToString(), match.OrgName, match.Role.ToString(), choice.Source);
            }
            else if (organisations.Count == 1)
            {
                var only = organisations[0];
                org = Org(only.OrgId.ToString(), only.OrgName, only.Role.ToString(), OrganisationResolver.OnlyOrganisation);
            }

            var partner = partnerChoice is { } chosen
                ? new JsonObject { ["id"] = chosen.Id.ToString("D"), ["source"] = chosen.Source }
                : null;

            await context.Output.WriteAsync(
                new JsonObject
                {
                    ["token"] = new JsonObject { ["source"] = context.Access.TokenSource },
                    ["org"] = org,
                    ["partner"] = partner,
                },
                context.CancellationToken);
        });

        return verb;
    }

    private static JsonObject Org(string id, string? name, string? role, string source) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["role"] = role,
        ["source"] = source,
    };
}
