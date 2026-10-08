using Enclave.Cli.Core;
using Enclave.Configuration.Data.Enums;

namespace Enclave.Cli.Commands.Systems;

/// <summary>
/// `system enable`: enables systems in bulk, or with --for or --until enables one system until
/// then, and afterwards disables or revokes it (proposed-cli-surface.md "Command options").
/// </summary>
internal static class SystemEnableCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "enable",
            SystemBulkCommand.Describe("Enable systems; with --for or --until, enable one system until then."),
            CommandScope.Organisation,
            changes: true);

        var systems = verb.Add(SystemBulkCommand.Systems());
        var forOption = verb.Add(CliOptions.Duration("--for", "Enable the system for this long."));
        var until = verb.Add(CliOptions.Time("--until", "Enable the system until this time."));

        // The API calls revoking a system on expiry Delete; the CLI names the value for what it does
        // to a system ("Options on every command").
        var then = verb.Add(CliOptions.Choice("--then", "What happens when the time is up: disable the system (the default), or revoke it.", ("disable", ExpiryAction.Disable), ("revoke", ExpiryAction.Delete)));

        verb.Exclusive(forOption, until);
        verb.Requires(then, forOption, until);

        // The API's timed enable takes one system, PUT systems/{systemId}/enable-until
        // (SystemsClient.EnableUntilAsync, Enclave.Sdk.Api 1.1.0), and has no bulk form, so --for
        // and --until take one system given by its ID ("Several IDs").
        verb.Check(context =>
        {
            if (!context.IsGiven(forOption) && !context.IsGiven(until))
            {
                return;
            }

            if (context.Get(systems) is not [var id] || id == CliArguments.Stdin)
            {
                throw CliErrors.InvalidArgument(systems.Name, "--for and --until enable one system: give one <systemId>. The API's timed enable has no bulk form.");
            }
        });

        verb.SetHandler(async context =>
        {
            // The expiry is read before the organisation, so a --until that has passed exits 2
            // before the token is checked ("Errors and exit codes").
            if (context.ExpiryFrom(forOption, until) is not { } expiry)
            {
                await SystemBulkCommand.RunAsync(context, systems, ListKind.System, (client, batch) => client.EnrolledSystems.BulkEnableAsync(batch));
                return;
            }

            var id = context.Get(systems)![0];
            var action = context.Get(then) ?? ExpiryAction.Disable;
            var org = await context.GetOrganisationAsync();

            var system = await SingleItem.CallAsync(() => org.Client.EnrolledSystems.EnableUntilAsync(id, expiry, action));
            await context.Output.WriteAsync(system, context.CancellationToken);
        });

        return verb;
    }
}
