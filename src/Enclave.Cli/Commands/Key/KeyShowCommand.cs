using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// `key show`: one key, with its secret, printed as the API returned it (proposed-cli-surface.md
/// "Output").
/// </summary>
internal static class KeyShowCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("show", "Show a key, with its secret.", CommandScope.Organisation);
        var key = verb.Add(CliArguments.OptionalText("key", "The key's description, matched whole and ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The key's ID.", IdFormats.Int32));

        verb.ExactlyOne(key, id);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var keyId = await KeyLookup.IdAsync(context, org, key, id);

            // The lookup's list items are EnrolmentKeySummaryModel, which has no IP constraints or
            // notes, so the key it finds is read by its ID and the output is EnrolmentKeyModel
            // whichever way the key was given. A 404 means the key does not exist, exit 5 ("Several
            // IDs").
            var found = await SingleItem.CallAsync(() => org.Client.EnrolmentKeys.GetAsync(keyId));
            await context.Output.WriteAsync(found, context.CancellationToken);
        });

        return verb;
    }
}
