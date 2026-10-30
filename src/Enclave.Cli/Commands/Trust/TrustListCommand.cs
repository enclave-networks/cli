using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.TrustRequirements.Enums;

namespace Enclave.Cli.Commands.Trust;

internal static class TrustListCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("list", "List the trust requirements, reading every page.", CommandScope.Organisation);
        var filter = verb.Add(CliOptions.Filter("--filter", "Search text, sent as typed; takes the API's search syntax as well as plain words."));

        // The values are the API's TrustRequirementType names, which the type search key reads
        // (portal TrustRequirementSearchKeyService.BuildFilterAsync).
        var type = verb.Add(CliOptions.ChoiceText("--type", "Sign-in or public IP requirements.", ("user-auth", "UserAuthentication"), ("public-ip", "PublicIp")));
        var sort = verb.Add(CliOptions.Enum("--sort", "The order to list trust requirements in.", TrustRequirementSortOrder.Description, TrustRequirementSortOrder.RecentlyCreated));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var search = new SearchText(context.Get(filter)).Add("type", context.Get(type)).ToSearchTerm();

            var requirements = await Paging.ReadAllAsync(
                (page, perPage) => org.Client.TrustRequirements.GetTrustRequirementsAsync(search, context.Get(sort), page, perPage),
                context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Trust, requirements, context.CancellationToken);
        });

        return verb;
    }
}
