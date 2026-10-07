using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Response bodies as an Enclave.Sdk.Api model writes them, for checking that the CLI prints models unchanged.
/// </summary>
internal static partial class ApiJson
{
    // The options Enclave.Sdk.Api 1.0.4 reads and writes models with (Constants.JsonSerializerOptions,
    // internal to the package): camelCase names and enums as their member names.
    private static readonly JsonSerializerOptions SdkModelJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The JSON Enclave.Sdk.Api writes after reading <paramref name="json"/> into <paramref name="model"/>: what a
    /// command that prints that model prints for the body.
    /// </summary>
    // Reading through the model drops the body's fields the model lacks, such as the ones a list
    // item's summary model does not carry. Writing is asynchronous because some models hold
    // IAsyncEnumerable properties, which only the asynchronous serializer writes.
    public static async Task<JsonElement> AsSdkModelAsync(string json, Type model)
    {
        var value = JsonSerializer.Deserialize(json, model, SdkModelJsonOptions);

        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, model, SdkModelJsonOptions);
        stream.Position = 0;

        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
