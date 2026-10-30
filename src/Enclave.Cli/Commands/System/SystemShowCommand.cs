using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Systems;

internal static class SystemShowCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("show", "Show a system, or with --pending a system waiting for approval.", CommandScope.Organisation);
        var systemId = verb.Add(CliArguments.Id("systemId", "The system's ID.", IdFormats.System));
        var pending = verb.Add(CliOptions.Flag("--pending", "Show a system waiting for approval."));

        verb.SetHandler(async context =>
        {
            var id = context.Get(systemId)!;
            var org = await context.GetOrganisationAsync();

            if (context.Get(pending))
            {
                var waiting = await SingleItem.CallAsync(() => org.Client.UnapprovedSystems.GetAsync(id));
                await context.Output.WriteAsync(waiting, context.CancellationToken);
            }
            else
            {
                var system = await SingleItem.CallAsync(() => org.Client.EnrolledSystems.GetAsync(id));
                await context.Output.WriteAsync(system, context.CancellationToken);
            }
        });

        return verb;
    }
}
