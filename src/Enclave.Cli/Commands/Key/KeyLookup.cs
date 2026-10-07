using System.CommandLine;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// Finds keys by description (proposed-cli-surface.md "Names and IDs").
/// </summary>
internal static class KeyLookup
{
    /// <summary>
    /// The IDs of the keys the descriptions name, in order, read with one list call. A description
    /// that matches no key, or several, exits 2 with the matches as candidates.
    /// </summary>
    // The lookup asks for disabled keys too, since enable takes a disabled key by name ("Filters"),
    // and the API lists disabled keys only when include_disabled is true (portal
    // EnrolmentKeyRepository.cs, BuildFilterAsync).
    public static async Task<IReadOnlyList<EnrolmentKeyId>> FindAsync(CliContext context, OrganisationInUse org, IReadOnlyList<string> descriptions, string key)
    {
        var keys = await NameLookup.FindAsync(
            descriptions,
            (page, perPage) => org.Client.EnrolmentKeys.GetEnrolmentKeysAsync(includeDisabled: true, pageNumber: page, perPage: perPage),
            enrolmentKey => enrolmentKey.Description,
            enrolmentKey => enrolmentKey.Id,
            key,
            "key",
            context.CancellationToken);

        return keys.Select(enrolmentKey => enrolmentKey.Id).ToArray();
    }

    /// <summary>
    /// The ID of the one key a command names: --id, which makes no call, or the key its description
    /// argument names. Declare the two as ExactlyOne on the command.
    /// </summary>
    public static async Task<EnrolmentKeyId> IdAsync(CliContext context, OrganisationInUse org, Argument<string?> description, Option<int?> id)
    {
        if (context.Get(id) is { } given)
        {
            return EnrolmentKeyId.FromInt(given);
        }

        var found = await FindAsync(context, org, [context.Get(description)!], description.Name);
        return found[0];
    }
}
