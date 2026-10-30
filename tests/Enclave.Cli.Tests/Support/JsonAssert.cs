using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

internal static class JsonAssert
{
    // The named property of a JSON object, matched exactly or else ignoring case. Fails the test
    // when the value is not an object or has no such property.
    public static JsonElement Property(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            throw new AssertionException($"Expected a JSON object holding \"{name}\", found {obj.ValueKind}: {obj}");
        }

        if (obj.TryGetProperty(name, out var exact))
        {
            return exact;
        }

        // Enclave.Sdk.Api keys a PATCH body by the C# property name (PatchClient.Set), so PATCH
        // bodies use PascalCase.
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new AssertionException($"Expected a JSON property \"{name}\" in {obj}");
    }

    // Fails the test when the value is not an array of strings.
    public static string[] Strings(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new AssertionException($"Expected a JSON array of strings, found {array.ValueKind}: {array}");
        }

        return array.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String
                ? item.GetString()!
                : throw new AssertionException($"Expected a JSON array of strings, found an item of kind {item.ValueKind}: {array}"))
            .ToArray();
    }
}
