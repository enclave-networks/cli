using Enclave.Cli.Context;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Trust;

internal static class TrustLookup
{
    /// <summary>
    /// The IDs in the order the descriptions are given. A description that matches none, or
    /// several, exits 2 with the candidates. Makes no call when <paramref name="descriptions"/> is
    /// empty.
    /// </summary>
    /// <param name="context">The command's context.</param>
    /// <param name="org">The organisation the trust requirements are in.</param>
    /// <param name="descriptions">The descriptions given.</param>
    /// <param name="key">The option or argument the descriptions came from, for errors.</param>
    public static async Task<IReadOnlyList<int>> IdsAsync(CliContext context, OrganisationInUse org, IReadOnlyList<string> descriptions, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(org);

        var requirements = await NameLookup.FindAsync(
            descriptions,
            (page, perPage) => org.Client.TrustRequirements.GetTrustRequirementsAsync(pageNumber: page, perPage: perPage),
            requirement => requirement.Description,
            requirement => requirement.Id.ToInt(),
            key,
            "trust requirement",
            context.CancellationToken);

        return requirements.Select(requirement => requirement.Id.ToInt()).ToArray();
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
