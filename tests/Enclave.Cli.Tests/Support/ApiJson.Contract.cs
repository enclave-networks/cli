using System.Text.Json;
using System.Text.Json.Serialization;
using Enclave.Sdk.Api.Data;

namespace Enclave.Cli.Tests.Support;

// Response bodies as an Enclave.Sdk.Api model writes them, for checking that the CLI prints models
// unchanged.
internal static partial class ApiJson
{
    // A copy of Enclave.Sdk.Api's Constants.JsonSerializerOptions, which is internal. The gateway
    // priority converter goes first, since System.Text.Json uses the first converter that can
    // convert a type.
    public static JsonSerializerOptions SdkModelJsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new GatewayPriorityTypeJsonConverter(), new JsonStringEnumConverter() },
    };

    // The JSON Enclave.Sdk.Api writes after reading json into model: what a command that prints
    // that model prints for the body.
    public static Task<JsonElement> AsSdkModelAsync(string json, Type model) =>
        WriteSdkModelAsync(JsonSerializer.Deserialize(json, model, SdkModelJsonOptions), model);

    // Some models hold IAsyncEnumerable properties, which only the asynchronous serializer writes.
    public static async Task<JsonElement> WriteSdkModelAsync(object? value, Type model)
    {
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, value, model, SdkModelJsonOptions);
        stream.Position = 0;

        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }
}
