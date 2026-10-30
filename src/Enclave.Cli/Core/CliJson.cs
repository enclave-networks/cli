using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Enclave.Sdk.Api.Data;

namespace Enclave.Cli.Core;

internal static class CliJson
{
    // The naming policy and the first two converters copy Enclave.Sdk.Api's, which the package keeps
    // internal (Constants.JsonSerializerOptions), so a model prints with the API's field and enum
    // names. GatewayPriorityTypeJsonConverter goes first because System.Text.Json uses the first
    // converter that can convert a type, and JsonStringEnumConverter converts every enum. The relaxed
    // encoder writes + and non-ASCII letters as themselves; the output is never embedded in HTML,
    // the case the default encoder guards against.

    // Indented, for people reading stdout as well as agents.
    public static JsonSerializerOptions Output { get; } = Create(indented: true);

    // For stderr, where each error is one line.
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    // System.Text.Json reads a time with an offset other than Z as local time in the machine's zone,
    // and one without a zone as Unspecified (dotnet/runtime release/10.0, JsonHelpers.Date.cs,
    // TryParseAsISO). ToUniversalTime converts back with the zone the reader used, so this reads the
    // machine's zone, outside CliHost. A time without a zone is UTC, as the API writes its times.
    public static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new GatewayPriorityTypeJsonConverter(), new JsonStringEnumConverter(), new UtcDateTimeConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = indented,
    };

    // Output times are UTC (AGENTS.md "CLI contract"); System.Text.Json writes a time by its kind, so
    // a local one would print at the machine's offset. A DateTime? uses this converter through
    // System.Text.Json's nullable converter.
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(ToUtc(value));
        }
    }
}
