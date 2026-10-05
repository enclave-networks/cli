using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Reads values out of JSON for assertions, failing the test when the shape is wrong.
/// </summary>
internal static class JsonAssert
{
    /// <summary>
    /// The named property of a JSON object, matched exactly or else ignoring case. Fails the test
    /// when the value is not an object or has no such property.
    /// </summary>
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

        // Enclave.Sdk.Api's PatchClient keys a PATCH body by the C# property name
        // (PatchClient.cs, Set: property.Member.Name, version 1.0.4), so PATCH bodies use PascalCase
        // keys while every other body uses camelCase.
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new AssertionException($"Expected a JSON property \"{name}\" in {obj}");
    }

    /// <summary>
    /// The items of a JSON array of strings. Fails the test when the value is not such an array.
    /// </summary>
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
