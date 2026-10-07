using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enclave.Cli.Core;

/// <summary>
/// The JSON settings the CLI writes models and its own output with.
/// </summary>
internal static class CliJson
{
    // The naming policy and the enum converter are the ones Enclave.Sdk.Api reads and writes models
    // with (Constants.JsonSerializerOptions, Enclave.Sdk.Api 1.0.5), which the package keeps
    // internal, so a model prints with the API's field names and enum member names. Identifier types
    // such as OrganisationGuid carry their own converters as attributes, which apply under any
    // options. The relaxed encoder writes characters such as + and non-ASCII letters as themselves,
    // where the default encoder writes them as escape sequences (a time's "+00:00" offset included);
    // the output is never embedded in HTML, the case the default encoder guards against.

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
        Converters = { new JsonStringEnumConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = indented,
    };
}
