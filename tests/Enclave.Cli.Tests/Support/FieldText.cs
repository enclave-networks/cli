using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

// Text forms of the fields of a body or of output, in which a test states the values it expects.
internal static class FieldText
{
    // What Text reads for a field that is missing or null.
    public const string None = "(none)";

    // What Text reads for an empty string.
    public const string Empty = "(empty)";

    // The named field of a JSON object as text: a string as it is, None or Empty, or json:<raw> for
    // any other kind. Fails the test when the value is not an object.
    // Names match ignoring case for PATCH bodies' PascalCase keys. An empty string reads apart
    // from null, since an empty label is sent as null (spec/cli-surface.md "Details"). json:<raw>
    // keeps a boolean from passing for the string "true" a trust requirement's condition takes
    // (portal TrustRequirementSettingsModel).
    public static string Text(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            throw new AssertionException($"Expected a JSON object holding \"{name}\", found {obj.ValueKind}: {obj}");
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.Null => None,
                JsonValueKind.String => property.Value.GetString() is { Length: > 0 } text ? text : Empty,
                _ => "json:" + property.Value.GetRawText(),
            };
        }

        return None;
    }

    // The entries sorted and joined with "; ", for a list whose order does not change what it does.
    public static string Set(params string[] entries) => string.Join("; ", entries.Order(StringComparer.Ordinal));

    // The property names of a body as Set joins them, so a test can state the exact set of fields
    // an update sends.
    public static string Keys(JsonElement body) => Set(JsonRead.PropertyNames(body));
}
