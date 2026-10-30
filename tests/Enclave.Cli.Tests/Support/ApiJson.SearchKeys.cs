using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// Lists of the portal's SearchKey as the API writes them (Enclave.Sdk.Api SearchKeyTests), taken
// from the portal's *SearchKeyService classes. Between them they give every property a value other
// than its default, so output that drops, renames or rewrites a property differs from the body.

// Search key lists, the GET .../meta/search-keys bodies.
internal static partial class ApiJson
{
    // The systems' "key" and "tags" search keys (portal SystemSearchKeyService), which the API
    // also serves for systems waiting for approval
    // (UnapprovedSystemsController.GetSearchKeyMetadata).
    public static string SystemSearchKeys() => SearchKeys(
        SearchKey(
            "key",
            null,
            "EnrolmentKey",
            "Filter your search by one or more enrolment keys.",
            "Filter by the name of the key used to enrol the system",
            null,
            canHaveMultiple: false,
            isDefault: false,
            useExactMatch: false),
        SearchKey(
            "tags",
            ["Or"],
            "Tags",
            "Filter your search by one or more tags.",
            "Filter to systems that have a set of tags assigned",
            null,
            canHaveMultiple: true,
            isDefault: false,
            useExactMatch: true));

    // The enrolment keys' "name" and "uses" search keys (portal EnrolmentKeySearchKeyService).
    public static string KeySearchKeys() => SearchKeys(
        SearchKey(
            "name",
            null,
            "None",
            "Filter your search by key name.",
            "Filter by the name of the enrolment key",
            null,
            canHaveMultiple: false,
            isDefault: true,
            useExactMatch: false),
        SearchKey(
            "uses",
            ["LessThan", "GreaterThan"],
            "None",
            "Filter your search by uses, select either lesser than or greater than, input a specific value or unlimited.",
            "Filter by uses remaining on a key",
            null,
            canHaveMultiple: false,
            isDefault: false,
            useExactMatch: false));

    // The policies' "state" and "tags" search keys (portal PolicySearchKeyService).
    public static string PolicySearchKeys() => SearchKeys(
        SearchKey(
            "state",
            null,
            "None",
            "Filter your search by state, either enabled or disabled.",
            "Search for policies that are enabled or disabled",
            ["Enabled", "Disabled"],
            canHaveMultiple: false,
            isDefault: false,
            useExactMatch: false),
        SearchKey(
            "tags",
            ["Or"],
            "Tags",
            "Filter your search by one or more tags.",
            "Filter by the tags assigned to the policy",
            null,
            canHaveMultiple: true,
            isDefault: false,
            useExactMatch: false));

    // The tags' "tag" and "uses" search keys (portal TagSearchKeyService).
    public static string TagSearchKeys() => SearchKeys(
        SearchKey(
            "tag",
            null,
            "None",
            "Filter your search by a partial tag name.",
            "Filter by tag name",
            null,
            canHaveMultiple: false,
            isDefault: true,
            useExactMatch: false),
        SearchKey(
            "uses",
            ["LessThan", "GreaterThan"],
            "None",
            "Filter your search by the number of places a tag is used, select either less than, greater than or input a value.",
            "Filter by the number of places a tag is used",
            null,
            canHaveMultiple: false,
            isDefault: false,
            useExactMatch: false));

    private static string SearchKeys(params JsonObject[] keys) => new JsonArray(keys).ToJsonString();

    private static JsonObject SearchKey(
        string name,
        string[]? modifiers,
        string dataType,
        string description,
        string hintText,
        string[]? hintValues,
        bool canHaveMultiple,
        bool isDefault,
        bool useExactMatch) => new()
    {
        ["name"] = name,
        ["modifiers"] = Strings(modifiers),
        ["dataType"] = dataType,
        ["description"] = description,
        ["hintText"] = hintText,
        ["hintValues"] = Strings(hintValues),
        ["canHaveMultiple"] = canHaveMultiple,
        ["isDefault"] = isDefault,
        ["useExactMatch"] = useExactMatch,
    };

    private static JsonArray? Strings(string[]? values) =>
        values is null ? null : new JsonArray(values.Select(value => (JsonNode?)value).ToArray());
}
