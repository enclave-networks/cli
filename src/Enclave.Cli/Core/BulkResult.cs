namespace Enclave.Cli.Core;

// spec/cli-surface.md "Several IDs"
internal sealed record BulkResult(int Requested, int Affected);
