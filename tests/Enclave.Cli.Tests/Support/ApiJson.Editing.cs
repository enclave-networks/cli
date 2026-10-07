using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// A system, a waiting system, an enrolment key and a DNS record return their tags as IUsedTagModel
// objects (portal Enclave.Api/Modules/SystemManagement/Tags/Models/UsedTagModel.cs), and their patch
// models take tag names (SystemPatchModel.Tags, DnsRecordPatchModel.Tags), so a command that adds
// or removes tags reads objects and sends names. Each ref is a new value in the form the API
// creates refs in (portal Enclave.Configuration.Data/Identifiers/TagRefId.cs, TagRefId.New); no
// command reads it.

/// <summary>
/// Response bodies with tags, for the tests of commands that change tag lists.
/// </summary>
internal static partial class ApiJson
{
    /// <summary>
    /// The JSON object <paramref name="body"/> with its "tags" set to one IUsedTagModel object for
    /// each tag name given, in order.
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
