using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.Policies.Enums;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// `policy list`: every policy, reading every page (proposed-cli-surface.md "Command options",
/// "Filters").
/// </summary>
internal static class PolicyListCommand
{
    private const string Disabled = "disabled";

    public static CliVerb Create()
    {
        var verb = new CliVerb("list", "List the policies, reading every page.", CommandScope.Organisation);
        var filter = verb.Add(CliOptions.Text("--filter", "Search text, sent as typed; takes the API's search syntax as well as plain words."));
        var tags = verb.Add(CliOptions.TagList("--tag", "Policies with every tag given among their sender or receiver tags."));
        var state = verb.Add(CliOptions.ChoiceText("--state", "Enabled or disabled policies.", ("enabled", "enabled"), (Disabled, Disabled)));
        var includeDisabled = verb.Add(CliOptions.Flag("--include-disabled", "Include disabled policies."));
        var sort = verb.Add(CliOptions.Enum("--sort", "The order to list policies in.", PolicySortOrder.Description, PolicySortOrder.RecentlyCreated));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            var search = new SearchText(context.Get(filter))
                .Add("tags", context.Get(tags))
                .Add("state", context.Get(state))
                .ToSearchTerm();

            // Without include_disabled the API leaves disabled policies out before the search
            // applies (portal PolicyRepository.cs:381), so --state disabled asks for them too, and
            // lists them without --include-disabled ("Filters").
            bool? withDisabled = context.Get(includeDisabled) || context.Get(state) == Disabled ? true : null;

            var policies = await Paging.ReadAllAsync(
                (page, perPage) => org.Client.Policies.GetPoliciesAsync(search, withDisabled, context.Get(sort), page, perPage),
                context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Policy, policies, context.CancellationToken);
        });

        return verb;
    }
}
