using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Enclave.Sdk.Api.Data;

namespace Enclave.Cli.Core;

/// <summary>
/// The JSON settings the CLI writes models and its own output with.
/// </summary>
internal static class CliJson
{
    // The naming policy and the first two converters are the ones Enclave.Sdk.Api reads and writes
    // models with (Constants.JsonSerializerOptions, Enclave.Sdk.Api 1.1.0), which the package keeps
    // internal, so a model prints with the API's field names and enum names; UtcDateTimeConverter is
    // the CLI's own, and prints times in UTC. GatewayPriorityTypeJsonConverter writes the gateway
    // priority Enclave.Sdk.Api.Data 304.48.0 names Prioritised as Ordered, the API's name for it
    // (proposed-cli-surface.md "`Enclave.Sdk.Api` changes", item 9). It goes ahead of the enum
    // converter because System.Text.Json uses the first converter in the list that can convert a type
    // (https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/converters-how-to#converter-registration-precedence),
    // and JsonStringEnumConverter converts every enum. Identifier types such as OrganisationGuid
    // carry their own converters as attributes, which apply under any options. The relaxed encoder
    // writes characters such as + and non-ASCII letters as themselves, where the default encoder
    // writes them as escape sequences (a time's "+00:00" offset included); the output is never
    // embedded in HTML, the case the default encoder guards against.

    /// <summary>
    /// Settings for stdout: indented, for people reading it as well as agents.
    /// </summary>
    public static JsonSerializerOptions Output { get; } = Create(indented: true);

    /// <summary>
    /// Settings for stderr, where each error is one line.
    /// </summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new GatewayPriorityTypeJsonConverter(), new JsonStringEnumConverter(), new UtcDateTimeConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = indented,
    };

    // Output times are UTC (AGENTS.md "CLI contract"). System.Text.Json reads a DateTime written
    // with an offset other than Z as local time in the machine's zone (DateTimeOffset.LocalDateTime),
    // and one written without a zone as DateTimeKind.Unspecified (dotnet/runtime release/10.0,
    // src/libraries/System.Text.Json/src/System/Text/Json/JsonHelpers.Date.cs, TryParseAsISO), and
    // writes a DateTime by its kind: a local time with the machine's offset, +00:00 on a machine at
    // UTC, and an unspecified one with no zone. So a time the API wrote at +05:30 would print at
    // the machine's offset, different on each machine. ToUniversalTime converts a local time back
    // with the zone the reader used, which gives the instant the API wrote; a time without a zone is
    // UTC, as the API writes its times in UTC (LogCommand.AsUtc reads it the same way).
    // Utf8JsonWriter writes a UTC DateTime as System.Text.Json does by default, ending in Z. A
    // DateTime? uses this converter too, through System.Text.Json's nullable converter.
    private sealed class UtcDateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);

            writer.WriteStringValue(value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc));
        }
    }
}
