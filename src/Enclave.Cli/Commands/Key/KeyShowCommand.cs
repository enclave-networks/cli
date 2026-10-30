using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Key;

internal static class KeyShowCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("show", "Show a key, with its secret.", CommandScope.Organisation);
        var key = verb.Add(CliArguments.OptionalName("key", "The key's description, matched whole and ignoring case.", "a key's description"));
        var id = verb.Add(CliOptions.Id("--id", "The key's ID.", IdFormats.Int32));

        verb.ExactlyOne(key, id);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var keyId = await KeyLookup.IdAsync(context, org, key, id);

            // The lookup's list items are EnrolmentKeySummaryModel, which has no IP constraints or
            // notes, so the key is read by ID and prints the same whichever way it was given.
            var found = await SingleItem.CallAsync(() => org.Client.EnrolmentKeys.GetAsync(keyId));
            await context.Output.WriteAsync(found, context.CancellationToken);
        });

        return verb;
    }
}
