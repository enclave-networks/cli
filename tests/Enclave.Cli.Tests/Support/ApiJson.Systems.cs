using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// Changing fields on a body ApiJson writes keeps every other property, which HarnessTests proves
// Enclave.Sdk.Api reads.

/// <summary>
/// Response bodies with some properties set by the test, for the system, enrolment key and log tests.
/// </summary>
internal static partial class ApiJson
{
    /// <summary>
    /// <paramref name="body"/> with each named property set, and added when missing.
    /// </summary>
    public static string Altered(string body, params (string Name, JsonNode? Value)[] changes)
    {
        var item = JsonNode.Parse(body)!.AsObject();

        foreach (var (name, value) in changes)
        {
            item[name] = value;
        }

        return item.ToJsonString();
    }
}
