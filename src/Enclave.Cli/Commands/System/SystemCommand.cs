using Enclave.Cli.Core;

// The namespace is Systems: a namespace segment named System would hide the System namespace from
// every name lookup inside it.
namespace Enclave.Cli.Commands.Systems;

internal static class SystemCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("system", "systems", "Systems.");

        noun.Add(SystemListCommand.Create());
        noun.Add(SystemShowCommand.Create());
        noun.Add(SystemUpdateCommand.Create());

        // approve and decline act on waiting systems, so they read the list `system list --pending`
        // prints ("Several IDs").
        noun.Add(SystemBulkCommand.Create("approve", "Approves systems waiting for approval.", ListKind.PendingSystem, (client, batch) => client.UnapprovedSystems.ApproveSystemsAsync(batch)));
        noun.Add(SystemBulkCommand.Create("decline", "Declines systems waiting for approval.", ListKind.PendingSystem, (client, batch) => client.UnapprovedSystems.DeclineSystems(batch)));
        noun.Add(SystemEnableCommand.Create());
        noun.Add(SystemBulkCommand.Create("disable", "Disables systems.", ListKind.System, (client, batch) => client.EnrolledSystems.BulkDisableAsync(batch)));
        noun.Add(SystemBulkCommand.Create("revoke", "Revokes systems.", ListKind.System, (client, batch) => client.EnrolledSystems.RevokeSystemsAsync(batch)));

        return noun;
    }
}
