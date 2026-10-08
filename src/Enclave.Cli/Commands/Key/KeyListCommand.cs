using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.EnrolmentKeys.Enums;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// `key list`: the enrolment keys, with their secrets (proposed-cli-surface.md "Command options",
/// "Filters", "Output").
/// </summary>
internal static class KeyListCommand
{
    private const string Disabled = "disabled";

    public static CliVerb Create()
    {
        var verb = new CliVerb("list", "List the enrolment keys, with their secrets, reading every page.", CommandScope.Organisation);

        var filter = verb.Add(CliOptions.Text("--filter", "Search text, sent as typed; takes the API's search syntax as well as plain words."));
        var tags = verb.Add(CliOptions.TagList("--tag", "Keys with every tag given."));

        // The values each flag stands for are the API's search values ("Filters"): the approval key
        // reads "gated" as Manual and matches nothing for "manual", and the state key reads "nouses"
        // (portal EnrolmentKeySearchKeyService.cs, BuildFilterAsync).
        var approval = verb.Add(CliOptions.ChoiceText("--approval", "Keys whose systems are approved automatically, or need approval.", ("automatic", "automatic"), ("manual", "gated")));
        var state = verb.Add(CliOptions.ChoiceText("--state", "Enabled keys, disabled keys, or keys with no uses left.", ("enabled", "enabled"), (Disabled, Disabled), ("no-uses", "nouses")));
        var includeDisabled = verb.Add(CliOptions.Flag("--include-disabled", "Include disabled keys."));
        var sort = verb.Add(CliOptions.Enum(
            "--sort",
            "The order to list keys in.",
            EnrolmentKeySortOrder.Description,
            EnrolmentKeySortOrder.LastUsed,
            EnrolmentKeySortOrder.ApprovalMode,
            EnrolmentKeySortOrder.UsesRemaining));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            var search = new SearchText(context.Get(filter))
                .Add("tags", context.Get(tags))
                .Add("approval", context.Get(approval))
                .Add("state", context.Get(state))
                .ToSearchTerm();

            // The API leaves disabled keys out unless include_disabled is true, whatever the search
            // (portal EnrolmentKeyRepository.cs, BuildFilterAsync), so --state disabled asks for them
            // too and lists them without --include-disabled ("Filters").
            bool? withDisabled = context.Get(includeDisabled) || context.Get(state) == Disabled ? true : null;

            var keys = await Paging.ReadAllAsync(
                (page, perPage) => org.Client.EnrolmentKeys.GetEnrolmentKeysAsync(search, withDisabled, context.Get(sort), page, perPage),
                context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Key, keys, context.CancellationToken);
        });

        return verb;
    }
}
