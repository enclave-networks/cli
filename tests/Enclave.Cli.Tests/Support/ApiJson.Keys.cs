namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Enrolment key list bodies.
/// </summary>
internal static partial class ApiJson
{
    /// <summary>
    /// A page of two keys, 13 "build agents 2026-04" then 12 "build agents", for a lookup of the key
    /// described as "build agents".
    /// </summary>
    // A key whose description starts with the same words is listed first, so a CLI that takes a
    // partial match, or the first item, finds key 13.
    public static string BuildAgentsKeys() => Page(Key(13, "build agents 2026-04"), Key(12, "build agents"));
}
