using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Context;

/// <summary>
/// ~/.enclave/cli.json, the CLI's own settings: the default organisation `org use` saves,
/// { "org": { "id", "name" } }, and the default partner `partner use` saves, { "partner": { "id" } }.
/// </summary>
internal static class SettingsFile
{
    public static string PathFor(CliHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return Path.Combine(host.HomeDirectory, ".enclave", "cli.json");
    }

    /// <summary>
    /// The settings object, or null when there is no file. A file that is not a JSON object exits
    /// 2; it is read only when no higher source chose the organisation or partner
    /// (proposed-cli-surface.md "Details"), so a malformed file fails only the commands that need it.
    /// </summary>
    public static JsonObject? Read(CliHost host)
    {
        var path = PathFor(host);
        var text = host.Files.ReadText(path);

        if (text is null)
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

    /// <summary>
    /// Saves the default organisation as { "org": { "id", "name" } }, keeping the saved partner.
    /// </summary>
    public static void SaveOrganisation(CliHost host, OrganisationGuid id, string name) =>
        Save(host, "org", new JsonObject { ["id"] = id.ToString(), ["name"] = name });

    /// <summary>
    /// Saves the default partner as { "partner": { "id" } }, keeping the saved organisation.
    /// </summary>
    public static void SavePartner(CliHost host, Guid id) =>
        Save(host, "partner", new JsonObject { ["id"] = id.ToString("D") });

    /// <summary>
    /// Sets one default and writes the file, keeping the other defaults it holds. A file that is not
    /// a JSON object is replaced.
    /// </summary>
    public static void Save(CliHost host, string key, JsonObject value)
    {
        ArgumentNullException.ThrowIfNull(host);

        var path = PathFor(host);
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
    }
}
