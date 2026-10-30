namespace Enclave.Cli.Tests.Support;

internal static partial class ApiJson
{
    // For a lookup of "build agents". A key whose description starts with the same words is listed
    // first, so a CLI that takes a partial match, or the first item, finds key 13.
    public static string BuildAgentsKeys() => Page(Key(13, "build agents 2026-04"), Key(12, "build agents"));
}
