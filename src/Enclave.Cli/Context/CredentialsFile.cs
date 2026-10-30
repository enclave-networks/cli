using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

// ~/.enclave/credentials.json belongs to Enclave.Sdk.Api, which reads personalAccessToken, baseUrl
// and partnerApiBaseUrl from it ignoring case (EnclaveClient.ReadCredentialsFile), so the CLI keeps
// that format.
internal static class CredentialsFile
{
    // Null when there is no home directory.
    public static string? PathFor(CliHost host) => EnclaveDirectory.PathOf(host, "credentials.json");

    // Null when there is no file or no home directory.
    public static StoredCredentials? Read(CliHost host)
    {
        if (PathFor(host) is not { } path || host.Files.ReadText(path) is not { } text)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject file)
            {
                return new StoredCredentials(null, null, null, isValid: false);
            }

            return new StoredCredentials(Text(file, "personalAccessToken"), Text(file, "baseUrl"), Text(file, "partnerApiBaseUrl"), isValid: true);
        }
        catch (JsonException)
        {
            return new StoredCredentials(null, null, null, isValid: false);
        }
    }

    // Readable by the current user only. Exits 2 when there is no home directory.
    public static string Write(CliHost host, string token, string baseUrl, string? partnerApiBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = PathFor(host) ?? throw EnclaveDirectory.NoHome();
        var file = new JsonObject
        {
            ["personalAccessToken"] = token,
            ["baseUrl"] = baseUrl,
        };

        if (partnerApiBaseUrl is not null)
        {
            file["partnerApiBaseUrl"] = partnerApiBaseUrl;
        }

        host.Files.WriteText(path, file.ToJsonString(CliJson.Output) + "\n", privateToUser: true);
        return path;
    }

    private static string? Text(JsonObject file, string name)
    {
        foreach (var (key, value) in file)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && value is JsonValue text && text.TryGetValue(out string? result))
            {
                return string.IsNullOrEmpty(result) ? null : result;
            }
        }

        return null;
    }
}
