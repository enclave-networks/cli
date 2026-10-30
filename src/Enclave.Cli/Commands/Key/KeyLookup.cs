using System.CommandLine;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Key;

internal static class KeyLookup
{
    // The IDs, in the order given. A description that matches no key, or several, exits 2.
    // The lookup includes disabled keys, since enable takes a disabled key by name ("Filters").
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

    // Declare the description and --id ExactlyOne on the command.
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
