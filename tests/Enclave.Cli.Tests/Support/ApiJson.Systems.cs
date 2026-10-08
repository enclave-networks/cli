using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// The system, waiting system, enrolment key and activity log tests filter on fields the ApiJson
// bodies fix, such as lastSeen, enrolledAt, timeStamp and level, so they change those fields on a
// body ApiJson writes. The body keeps every other property, which HarnessTests proves Enclave.Sdk.Api reads.

/// <summary>
/// Response bodies with some properties set by the test, for the system, enrolment key and log tests.
/// </summary>
internal static partial class ApiJson
{
    /// <summary>
    /// The JSON object <paramref name="body"/> with each named property set to the value given, added
    /// when the body lacks it. The other properties keep their values.
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
