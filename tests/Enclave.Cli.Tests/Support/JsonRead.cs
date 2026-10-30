using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// The *List methods join values with commas. Comparing a joined string keeps constant arrays out of
// assertions (CA1861) and prints both sides readably when a test fails.
internal static class JsonRead
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // The element as CliJson.Output writes JSON: indented, with the relaxed encoder.
    public static string Indented(JsonElement element) => JsonSerializer.Serialize(element, IndentedJson);

    // Parses JSON text into an element that outlives its document.
    public static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static string[] PropertyNames(JsonElement obj) =>
        obj.EnumerateObject().Select(property => property.Name).ToArray();

    public static string PropertyNameList(JsonElement obj) => string.Join(",", PropertyNames(obj));

    // A "trace" anywhere but last is kept, so a check of the names fails.
    public static string[] PropertyNamesBeforeTrace(JsonElement obj) =>
        PropertyNames(obj) is [.. var before, "trace"] ? before : PropertyNames(obj);

    // A "trace" anywhere but last is kept, so a comparison fails.
    public static JsonElement WithoutTrace(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || PropertyNames(value) is not [.., "trace"])
        {
            return value;
        }

        var node = JsonNode.Parse(value.GetRawText())!.AsObject();
        node.Remove("trace");
        return Parse(node.ToJsonString());
    }

    public static string StringList(JsonElement array) => string.Join(",", JsonAssert.Strings(array));

    public static string IntList(JsonElement array) =>
        string.Join(",", array.EnumerateArray().Select(item => item.GetInt32().ToString(CultureInfo.InvariantCulture)));

    public static string StringFieldList(JsonElement array, string name) =>
        string.Join(",", array.EnumerateArray().Select(item => JsonAssert.Property(item, name).GetString()));

    public static string IntFieldList(JsonElement array, string name) =>
        string.Join(",", array.EnumerateArray().Select(item => JsonAssert.Property(item, name).GetInt32().ToString(CultureInfo.InvariantCulture)));

    // The GUID a JSON string holds, in any form Guid.TryParse reads, or null for any other value.
    // GUIDs are compared as GUIDs: the API writes them as 32 hex digits, people type them
    // hyphenated, and the CLI may print either.
    public static Guid? GuidOf(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var guid) ? guid : null;

    // The GUID in the "id" property of a JSON object, as GuidOf reads it.
    public static Guid? IdOf(JsonElement obj) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("id", out var id) ? GuidOf(id) : null;

    // The expiryDateTime of a timed enable body (AutoExpireModel) as a point in time.
    public static DateTimeOffset ExpiryDateTime(JsonElement body) =>
        DateTimeOffset.Parse(JsonAssert.Property(body, "expiryDateTime").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.None);
}
