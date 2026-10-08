namespace Enclave.Cli.Core;

/// <summary>
/// The output of a command that takes several items: how many IDs it sent and how many items the
/// API changed. Fewer affected than requested means some IDs were unknown or already in that state
/// (proposed-cli-surface.md "Several IDs").
/// </summary>
internal sealed record BulkResult(int Requested, int Affected);
