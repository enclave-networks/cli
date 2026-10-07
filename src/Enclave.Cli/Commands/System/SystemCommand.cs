using Enclave.Cli.Core;

// The namespace is Systems: a namespace segment named System would hide the System namespace from
// every name lookup inside it.
namespace Enclave.Cli.Commands.Systems;

/// <summary>
/// The `system` noun: enrolled systems, and with --pending the systems waiting for approval
/// (proposed-cli-surface.md "Commands"). Systems have no names, so they are given by ID.
/// </summary>
internal static class SystemCommand
{
    public static CliNoun Create()
    {
        var noun = new CliNoun("system", "systems", "Enrolled systems, and with --pending the systems waiting for approval.");

        noun.Add(SystemListCommand.Create());
        noun.Add(SystemShowCommand.Create());
        noun.Add(SystemUpdateCommand.Create());

        // approve and decline act on waiting systems only, so they read the list system list
        // --pending prints; the other verbs act on enrolled systems ("Several IDs"). The calls and
        // routes are those of Enclave.Sdk.Api 1.0.5 UnapprovedSystemsClient and SystemsClient.
        noun.Add(SystemBulkCommand.Create("approve", "Approve systems waiting for approval.", ListKind.PendingSystem, (client, batch) => client.UnapprovedSystems.ApproveSystemsAsync(batch)));
        noun.Add(SystemBulkCommand.Create("decline", "Decline systems waiting for approval, removing them.", ListKind.PendingSystem, (client, batch) => client.UnapprovedSystems.DeclineSystems(batch)));
        noun.Add(SystemEnableCommand.Create());
        noun.Add(SystemBulkCommand.Create("disable", "Disable systems.", ListKind.System, (client, batch) => client.EnrolledSystems.BulkDisableAsync(batch)));
        noun.Add(SystemBulkCommand.Create("revoke", "Revoke systems, removing them from the organisation.", ListKind.System, (client, batch) => client.EnrolledSystems.RevokeSystemsAsync(batch)));

        return noun;
    }
}
