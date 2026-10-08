using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;

namespace Enclave.Cli.Context;

/// <summary>
/// ~/.enclave/credentials.json, which holds the token and the API's base URL, and the partner API's
/// base URL where it is not production's. The file belongs to Enclave.Sdk.Api, which reads
/// personalAccessToken, baseUrl and partnerApiBaseUrl from it, ignoring case
/// (EnclaveClient.ReadCredentialsFile, version 1.1.0); the CLI keeps that format.
/// </summary>
internal static class CredentialsFile
{
    public static string PathFor(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return Path.Combine(host.HomeDirectory, ".enclave", "credentials.json");
    }

    /// <summary>
    /// What the file holds, or null when there is no file.
    /// </summary>
    public static StoredCredentials? Read(CliHost host)
    {
        var text = host.Files.ReadText(PathFor(host));

        if (text is null)
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

    /// <summary>
    /// Writes the file with the token and base URL, and the partner API's base URL when one is
    /// given, readable by the current user only.
    /// </summary>
    public static void Write(CliHost host, string token, string baseUrl, string? partnerApiBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        var file = new JsonObject
        {
            ["personalAccessToken"] = token,
            ["baseUrl"] = baseUrl,
        };

        if (partnerApiBaseUrl is not null)
        {
            file["partnerApiBaseUrl"] = partnerApiBaseUrl;
        }

        host.Files.WriteText(PathFor(host), file.ToJsonString(CliJson.Output) + "\n", privateToUser: true);
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
