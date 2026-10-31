using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Context;

// ~/.enclave/credentials.json (spec/cli-surface.md "Login, logout and status"). Enclave.Sdk.Api
// reads the token from it too, matching names ignoring case (EnclaveClient.ReadCredentialsFile), so
// the CLI reads every field the same way.
internal static class CredentialsFile
{
    private const string DefaultOrgField = "defaultOrgID";

    // Null when there is no home directory, or while ENCLAVE_CLI_PAT is set.
    public static string? PathFor(CliHost host) => EnclaveDirectory.PathOf(host, "credentials.json");

    // Null when there is no file, no home directory, or ENCLAVE_CLI_PAT is set.
    public static StoredCredentials? Read(CliHost host)
    {
        if (PathFor(host) is not { } path || host.Files.ReadText(path) is not { } text)
        {
            return null;
        }

        return Parse(text) is { } file
            ? new StoredCredentials(Text(file, "personalAccessToken"), Text(file, "apiBaseUrl"), Text(file, "partnerApiBaseUrl"), Written(file, DefaultOrgField), isValid: true)
            : new StoredCredentials(null, null, null, null, isValid: false);
    }

    // Replaces the file. Null partnerApiBaseUrl or defaultOrgId is left out. Exits 2 when there is no
    // home directory.
    public static string Write(CliHost host, string token, string apiBaseUrl, string? partnerApiBaseUrl, OrganisationGuid? defaultOrgId)
    {
        var file = new JsonObject
        {
            ["personalAccessToken"] = token,
            ["apiBaseUrl"] = apiBaseUrl,
        };

        if (partnerApiBaseUrl is not null)
        {
            file["partnerApiBaseUrl"] = partnerApiBaseUrl;
        }

        if (defaultOrgId is { } id)
        {
            file[DefaultOrgField] = id.ToString();
        }

        return Save(host, file);
    }

    // Leaves the rest of the file as it is, and starts one when there is none. Exits 2 when there is
    // no home directory.
    public static string SaveDefaultOrganisation(CliHost host, OrganisationGuid id)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = PathFor(host) ?? throw EnclaveDirectory.NoHome();
        var file = host.Files.ReadText(path) is { } text && Parse(text) is { } existing ? existing : [];

        // Names are read ignoring case, so another spelling left in the file could be read in place of
        // the new value.
        foreach (var key in file.Select(pair => pair.Key).Where(key => string.Equals(key, DefaultOrgField, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            file.Remove(key);
        }

        file[DefaultOrgField] = id.ToString();
        return Save(host, file);
    }

    // Readable by the current user only.
    private static string Save(CliHost host, JsonObject file)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = PathFor(host) ?? throw EnclaveDirectory.NoHome();
        host.Files.WriteText(path, file.ToJsonString(CliJson.Output) + "\n", privateToUser: true);
        return path;
    }

    // Null when text is not a JSON object.
    private static JsonObject? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonNode? Field(JsonObject file, string name) =>
        file.FirstOrDefault(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? Text(JsonObject file, string name) =>
        Field(file, name) is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0 ? text : null;

    // A value that is not text comes back as its JSON, so a number reads as a malformed ID rather than
    // as no default.
    private static string? Written(JsonObject file, string name) => Field(file, name) switch
    {
        null => null,
        JsonValue value when value.TryGetValue(out string? text) => text.Length > 0 ? text : null,
        var value => value.ToJsonString(),
    };
}
