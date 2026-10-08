using System.Globalization;
using System.Text.Json;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Reads JSON into forms that are easy to assert on.
/// </summary>
// The *List methods join values with commas. Comparing a joined string keeps constant arrays out of
// assertions (CA1861) and prints both sides readably when a test fails.
internal static class JsonRead
{
    /// <summary>
    /// Parses JSON text into an element that outlives its document.
    /// </summary>
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// The property names of a JSON object, in order.
    /// </summary>
    public static string[] PropertyNames(JsonElement obj) =>
        obj.EnumerateObject().Select(property => property.Name).ToArray();

    /// <summary>
    /// The property names of a JSON object, in order, joined with commas.
    /// </summary>
    public static string PropertyNameList(JsonElement obj) => string.Join(",", PropertyNames(obj));

    /// <summary>
    /// The items of a JSON array of strings, joined with commas.
    /// </summary>
    public static string StringList(JsonElement array) => string.Join(",", JsonAssert.Strings(array));

    /// <summary>
    /// The items of a JSON array of integers, joined with commas.
    /// </summary>
    public static string IntList(JsonElement array) =>
        string.Join(",", array.EnumerateArray().Select(item => item.GetInt32().ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// One string property of each object in a JSON array, joined with commas.
    /// </summary>
    public static string StringFieldList(JsonElement array, string name) =>
        string.Join(",", array.EnumerateArray().Select(item => JsonAssert.Property(item, name).GetString()));

    /// <summary>
    /// One integer property of each object in a JSON array, joined with commas.
    /// </summary>
    public static string IntFieldList(JsonElement array, string name) =>
        string.Join(",", array.EnumerateArray().Select(item => JsonAssert.Property(item, name).GetInt32().ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// The GUID a JSON string holds, in any form Guid.TryParse reads, or null for any other value.
    /// </summary>
    // GUIDs are compared as GUIDs: the API writes them as 32 hex digits, people type them
    // hyphenated, and the CLI may print either.
    public static Guid? GuidOf(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var guid) ? guid : null;

    /// <summary>
    /// The GUID in the "id" property of a JSON object, as <see cref="GuidOf"/> reads it, or null when
    /// the value is not an object or has no "id".
    /// </summary>
    public static Guid? IdOf(JsonElement obj) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("id", out var id) ? GuidOf(id) : null;

    /// <summary>
    /// The expiryDateTime of a timed enable body (AutoExpireModel) as a point in time.
    /// </summary>
    public static DateTimeOffset ExpiryDateTime(JsonElement body) =>
        DateTimeOffset.Parse(JsonAssert.Property(body, "expiryDateTime").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None);
}
