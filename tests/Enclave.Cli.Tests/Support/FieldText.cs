using System.Text.Json;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Text forms of the fields of a body or of output, in which a test states the values it expects.
/// </summary>
internal static class FieldText
{
    /// <summary>
    /// What <see cref="Text"/> reads for a field that is missing or null.
    /// </summary>
    public const string None = "(none)";

    /// <summary>
    /// What <see cref="Text"/> reads for an empty string.
    /// </summary>
    public const string Empty = "(empty)";

    /// <summary>
    /// The named field of a JSON object as text: a string as it is, <see cref="None"/> or
    /// <see cref="Empty"/>, or json:&lt;raw&gt; for any other kind. Fails the test when the value is
    /// not an object.
    /// </summary>
    // Names match ignoring case, since PATCH bodies key the top level in PascalCase
    // (JsonAssert.Property). Missing and null read as (none), an unset label; an empty string reads
    // as (empty), since an empty label is sent as null ("Details"); any other JSON kind reads as
    // json:<raw>, so a boolean or number never passes for text, such as the string "true" the API
    // reads in a trust requirement's condition (portal TrustRequirementSettingsModel).
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

    /// <summary>
    /// The entries sorted and joined with "; ", for a list whose order does not change what it does.
    /// </summary>
    public static string Set(params string[] entries) => string.Join("; ", entries.Order(StringComparer.Ordinal));

    /// <summary>
    /// The property names of a body as <see cref="Set"/> joins them.
    /// </summary>
    // Sorted, so a test can state the exact set of fields an update sends.
    public static string Keys(JsonElement body) => Set(JsonRead.PropertyNames(body));
}
