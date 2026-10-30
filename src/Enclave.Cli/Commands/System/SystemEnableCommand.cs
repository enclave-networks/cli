using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Systems;

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
        // The API calls revoking a system on expiry Delete; the CLI names the value for what it does
        // to a system ("Options on every command").
        var timing = Timing.AddTo(verb, "Enable the system", "system", deleteName: "revoke");

        verb.Check(context =>
        {
            if (!timing.IsGiven(context))
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
            if (timing.Expiry(context) is not { } expiry)
            {
                await SystemBulkCommand.RunAsync(context, systems, ListKind.System, (client, batch) => client.EnrolledSystems.BulkEnableAsync(batch));
                return;
            }

            var id = context.Get(systems)![0];
            var action = timing.Action(context);
            var org = await context.GetOrganisationAsync();

            var system = await SingleItem.CallAsync(() => org.Client.EnrolledSystems.EnableUntilAsync(id, expiry, action));
            await context.Output.WriteAsync(system, context.CancellationToken);
        });

        return verb;
    }
}
