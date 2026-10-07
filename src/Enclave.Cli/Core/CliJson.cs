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
    // The naming policy and the converters are the ones Enclave.Sdk.Api reads and writes models with
    // (Constants.JsonSerializerOptions, Enclave.Sdk.Api 1.1.0), which the package keeps internal, so
    // a model prints with the API's field names and enum names. GatewayPriorityTypeJsonConverter
    // writes the gateway priority Enclave.Sdk.Api.Data 304.48.0 names Prioritised as Ordered, the
    // API's name for it (proposed-cli-surface.md "`Enclave.Sdk.Api` changes", item 9). It goes ahead
    // of the enum converter because System.Text.Json uses the first converter in the list that can
    // convert a type
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
        Converters = { new GatewayPriorityTypeJsonConverter(), new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = indented,
    };
}
