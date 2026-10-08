using System.CommandLine;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// `policy enable`, `policy disable` and `policy delete`, which take any number of policies and
/// make the bulk call (proposed-cli-surface.md "Several IDs"). `enable` with --for or --until takes
/// one policy and makes the timed enable.
/// </summary>
internal static class PolicySeveralCommand
{
    public static CliVerb Enable()
    {
        var verb = Several("enable", "Enable policies; with --for or --until, enable one policy until then.", out var policies, out var ids);
        var forOption = verb.Add(CliOptions.Duration("--for", "Enable the policy for this long."));
        var untilOption = verb.Add(CliOptions.Time("--until", "Enable the policy until this time."));
        var then = verb.Add(CliOptions.Choice("--then", "What happens when the time is up: disable the policy (the default), or delete it.", ("disable", ExpiryAction.Disable), ("delete", ExpiryAction.Delete)));

        verb.Exclusive(forOption, untilOption);
        verb.Requires(then, forOption, untilOption);

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, policies, ids, ListKind.Policy, IdFormats.Int32);

            if (context.ExpiryFrom(forOption, untilOption) is not { } expiry)
            {
                await RunBulkAsync(context, selection, policies.Name, (org, batch) => org.Client.Policies.EnablePoliciesAsync(batch.Select(PolicyId.FromInt)));
                return;
            }

            // The API's timed enable takes one policy and has no bulk form ("Several IDs").
            if (selection.Ids.Count + selection.Names.Count != 1)
            {
                Option timed = context.IsGiven(forOption) ? forOption : untilOption;
                throw CliErrors.InvalidArgument(timed.Name, $"{timed.Name} takes one policy: the API's timed enable has no bulk form.");
            }

            var org = await context.GetOrganisationAsync();
            var policyId = selection.Ids.Count == 1 ? selection.Ids[0] : (await PolicyLookup.IdsAsync(context, org, selection.Names, policies.Name))[0];
            var action = context.Get(then) ?? ExpiryAction.Disable;

            // One policy named: a 404 means it does not exist, exit 5 ("Several IDs").
            var policy = await SingleItem.CallAsync(() => org.Client.Policies.EnableUntilAsync(PolicyId.FromInt(policyId), expiry, action));
            await context.Output.WriteAsync(policy, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Disable()
    {
        var verb = Several("disable", "Disable policies.", out var policies, out var ids);

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, policies, ids, ListKind.Policy, IdFormats.Int32);
            await RunBulkAsync(context, selection, policies.Name, (org, batch) => org.Client.Policies.DisablePoliciesAsync(batch.Select(PolicyId.FromInt)));
        });

        return verb;
    }

    public static CliVerb Delete()
    {
        var verb = Several("delete", "Delete policies.", out var policies, out var ids);

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, policies, ids, ListKind.Policy, IdFormats.Int32);
            await RunBulkAsync(context, selection, policies.Name, (org, batch) => org.Client.Policies.DeletePoliciesAsync(batch.Select(PolicyId.FromInt)));
        });

        return verb;
    }

    private static CliVerb Several(string name, string description, out Argument<IReadOnlyList<string>> policies, out Option<IReadOnlyList<int>?> ids)
    {
        var verb = new CliVerb(name, $"{description} Takes policy descriptions, IDs after --id, or \"-\" to read a list from stdin, and prints {{ requested, affected }}.", CommandScope.Organisation, changes: true);
        policies = verb.Add(CliArguments.Many("policy", "A policy's description, matched whole and ignoring case."));
        ids = verb.Add(CliOptions.IdList("--id", "Policy IDs, separated by commas.", IdFormats.Int32));

        verb.Exclusive(policies, ids);
        return verb;
    }

    // An empty list from stdin makes no call and needs no organisation, so a pipeline fed by an
    // empty list succeeds ("Several IDs"). Under --dry-run an empty list resolves the organisation
    // too, since the dry run output names it ("Dry run").
    private static async Task RunBulkAsync(CliContext context, ItemSelection<int> selection, string key, Func<OrganisationInUse, IReadOnlyList<int>, Task<int>> call)
    {
        if (selection.Ids.Count == 0 && selection.Names.Count == 0 && !context.IsDryRun)
        {
            await context.Output.WriteBulkAsync(new BulkResult(0, 0), context.CancellationToken);
            return;
        }

        var org = await context.GetOrganisationAsync();
        var ids = selection.Names.Count > 0 ? (await PolicyLookup.IdsAsync(context, org, selection.Names, key)).Distinct().ToArray() : selection.Ids;

        var result = await Bulk.RunAsync(ids, batch => call(org, batch));
        await context.Output.WriteBulkAsync(result, context.CancellationToken);
    }
}
