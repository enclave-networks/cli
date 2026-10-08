using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Policy;

/// <summary>
/// Finds policies by description (proposed-cli-surface.md "Names and IDs").
/// </summary>
internal static class PolicyLookup
{
    /// <summary>
    /// The ID of each policy named, in the order given, read with one list call. A description that
    /// matches no policy, or several, exits 2 with the candidates.
    /// </summary>
    /// <param name="context">The command's context.</param>
    /// <param name="org">The organisation the policies are in.</param>
    /// <param name="descriptions">The descriptions given.</param>
    /// <param name="key">The argument the descriptions came from, for errors.</param>
    public static async Task<IReadOnlyList<int>> IdsAsync(CliContext context, OrganisationInUse org, IReadOnlyList<string> descriptions, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(org);

        // The lookup asks for disabled policies too, since enable takes a disabled policy by name
        // ("Filters"), and the API leaves them out unless asked (portal PolicyRepository.cs:381).
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

    /// <summary>
    /// The ID of the one policy a single-item command names: --id, or the description, looked up.
    /// </summary>
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
