using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Context;

// ~/.enclave/cli.json: { "org": { "id", "name" }, "partner": { "id" } }.
internal static class SettingsFile
{
    // Null when there is no home directory.
    public static string? PathFor(CliHost host) => EnclaveDirectory.PathOf(host, "cli.json");

    // Null when there is no file or no home directory. A file that is not a JSON object exits 2;
    // callers read it only when no higher source chose (spec/cli-surface.md "Details"), so a
    // malformed file fails only the commands that need it.
    public static JsonObject? Read(CliHost host)
    {
        if (PathFor(host) is not { } path || host.Files.ReadText(path) is not { } text)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw CliErrors.InvalidArgument($"{path} does not hold a JSON object. Save a default again with `enclave-cli org use` or `enclave-cli partner use`, or delete the file.");
        }
        catch (JsonException)
        {
            throw CliErrors.InvalidArgument($"{path} is not valid JSON. Save a default again with `enclave-cli org use` or `enclave-cli partner use`, or delete the file.");
        }
    }

    // The default saved under key ("org" or "partner"), or null when none is saved. One without a
    // valid ID exits 2 naming the file and saveWith, the command that saves it again.
    // use never saves a default without a valid ID, so only a missing default means none is chosen.
    public static (Guid Id, string? Name, string Path)? ReadDefault(CliHost host, string key, string noun, string saveWith)
    {
        if (PathFor(host) is not { } path || Read(host)?[key] is not { } saved)
        {
            return null;
        }

        if (saved is not JsonObject entry
            || entry["id"] is not JsonValue value
            || !value.TryGetValue(out string? text)
            || !IdFormats.Guid.TryParse(text, out var id))
        {
            throw CliErrors.InvalidArgument($"The default {noun} in {path} has no valid ID. Save it again with `{saveWith}`.");
        }

        var name = entry["name"] is JsonValue savedName && savedName.TryGetValue(out string? nameText) ? nameText : null;
        return (id, name, path);
    }

    public static string SaveOrganisation(CliHost host, OrganisationGuid id, string name) =>
        Save(host, "org", new JsonObject { ["id"] = id.ToString(), ["name"] = name });

    public static string SavePartner(CliHost host, Guid id) =>
        Save(host, "partner", new JsonObject { ["id"] = id.ToString("D") });

    // Keeps the other defaults the file holds; a file that is not a JSON object is replaced. Exits
    // 2 when there is no home directory.
    public static string Save(CliHost host, string key, JsonObject value)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = PathFor(host) ?? throw EnclaveDirectory.NoHome();
        JsonObject settings;

        try
        {
            settings = host.Files.ReadText(path) is { } text && JsonNode.Parse(text) is JsonObject existing ? existing : [];
        }
        catch (JsonException)
        {
            settings = [];
        }

        settings[key] = value;

        // The file holds no secret: an organisation's ID and name and a partner's ID.
        host.Files.WriteText(path, settings.ToJsonString(CliJson.Output) + "\n", privateToUser: false);
        return path;
    }
}
