using System.CommandLine;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Policy;

internal static class PolicySeveralCommand
{
    public static CliVerb Enable()
    {
        var verb = Several("enable", "Enable policies; with --for or --until, enable one policy until then.", out var policies, out var ids);
        var timing = Timing.AddTo(verb, "Enable the policy", "policy");

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, policies, ids, ListKind.Policy, IdFormats.Int32);

            if (timing.Expiry(context) is not { } expiry)
            {
                await RunBulkAsync(context, selection, policies.Name, (org, batch) => org.Client.Policies.EnablePoliciesAsync(batch.Select(PolicyId.FromInt)));
                return;
            }

            timing.RequireOne(context, selection, "policy");

            var org = await context.GetOrganisationAsync();
            var policyId = selection.Ids.Count == 1 ? selection.Ids[0] : (await PolicyLookup.IdsAsync(context, org, selection.Names, policies.Name))[0];
            var action = timing.Action(context);

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
        policies = verb.Add(CliArguments.Names("policy", "A policy's description, matched whole and ignoring case.", "a policy's description"));
        ids = verb.Add(CliOptions.IdList("--id", "Policy IDs, separated by commas.", IdFormats.Int32));

        verb.Exclusive(policies, ids);
        return verb;
    }

    private static async Task RunBulkAsync(CliContext context, ItemSelection<int> selection, string key, Func<OrganisationInUse, IReadOnlyList<int>, Task<int>> call)
    {
        if (await Bulk.WroteEmptyAsync(context, selection.Count))
        {
            return;
        }

        var org = await context.GetOrganisationAsync();
        var ids = selection.Names.Count > 0 ? (await PolicyLookup.IdsAsync(context, org, selection.Names, key)).Distinct().ToArray() : selection.Ids;

        var result = await Bulk.RunAsync(ids, batch => call(org, batch));
        await context.Output.WriteBulkAsync(result, context.CancellationToken);
    }
}
