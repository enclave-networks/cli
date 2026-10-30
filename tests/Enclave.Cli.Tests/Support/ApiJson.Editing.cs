using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// Items return their tags as objects (portal UsedTagModel), and patch models take tag names
// (SystemPatchModel.Tags), so a command that changes tags reads objects and sends names. Each ref
// is new, in the form of portal TagRefId.New; no command reads it.

/// <summary>
/// Response bodies with tags, for the tests of commands that change tag lists.
/// </summary>
internal static partial class ApiJson
{
    /// <summary>
    /// <paramref name="body"/> with "tags" set to an IUsedTagModel object per tag, in order.
    /// </summary>
    public static string WithUsedTags(string body, params string[] tags)
    {
        var item = JsonNode.Parse(body)!.AsObject();
        var array = new JsonArray();

        foreach (var tag in tags)
        {
            array.Add(new JsonObject
            {
                ["tag"] = tag,
                ["ref"] = "ref:" + Guid.NewGuid().ToString("N"),
                ["colour"] = "#3b82f6",
            });
        }

        item["tags"] = array;
        return item.ToJsonString();
    }
}
