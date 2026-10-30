using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Policy;

internal static class PolicyLookup
{
    // The IDs, in the order given. A description that matches no policy, or several, exits 2,
    // naming key.
    public static async Task<IReadOnlyList<int>> IdsAsync(CliContext context, OrganisationInUse org, IReadOnlyList<string> descriptions, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(org);

        // The lookup includes disabled policies, since enable takes a disabled policy by name
        // ("Filters").
        var policies = await NameLookup.FindAsync(
            descriptions,
            (page, perPage) => org.Client.Policies.GetPoliciesAsync(includeDisabled: true, pageNumber: page, perPage: perPage),
            policy => policy.Description,
            policy => policy.Id.ToInt(),
            key,
            "policy",
            context.CancellationToken);

        return policies.Select(policy => policy.Id.ToInt()).ToArray();
    }

    public static async Task<int> IdAsync(CliContext context, OrganisationInUse org, int? id, string? description, string key)
    {
        if (id is { } given)
        {
            return given;
        }

        var found = await IdsAsync(context, org, [description!], key);
        return found[0];
    }
}
