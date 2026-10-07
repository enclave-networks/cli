namespace Enclave.Cli.Tests.Support;

// The bodies are lists of the API's SearchKey (portal
// src/Enclave.Configuration.Data/Modules/Search/SearchKey.cs), written as the API writes them: every
// property, camelCase names, enums by name, and null for a list that is not set (Enclave.Sdk.Api
// 1.1.0, SearchKeyTests). The keys are taken from the portal's search key services
// (Enclave.Configuration.Data/Modules/*/*SearchKeyService.cs). Between them they give every property
// a value other than its default, so output that drops, renames or rewrites any property differs
// from the body.

/// <summary>
/// Search key lists, the GET .../meta/search-keys bodies.
/// </summary>
internal static partial class ApiJson
{
    /// <summary>
    /// The systems' "key" and "tags" search keys (portal SystemSearchKeyService.cs). The API serves
    /// the same keys for systems waiting for approval (portal UnapprovedSystemsController.cs,
    /// GetSearchKeyMetadata, reads ISearchKeyService&lt;SystemDM&gt;).
    /// </summary>
    public static string SystemSearchKeys() => """
        [
          {
            "name": "key",
            "modifiers": null,
            "dataType": "EnrolmentKey",
            "description": "Filter your search by one or more enrolment keys.",
            "hintText": "Filter by the name of the key used to enrol the system",
            "hintValues": null,
            "canHaveMultiple": false,
            "isDefault": false,
            "useExactMatch": false
          },
          {
            "name": "tags",
            "modifiers": [ "Or" ],
            "dataType": "Tags",
            "description": "Filter your search by one or more tags.",
            "hintText": "Filter to systems that have a set of tags assigned",
            "hintValues": null,
            "canHaveMultiple": true,
            "isDefault": false,
            "useExactMatch": true
          }
        ]
        """;

    /// <summary>
    /// The enrolment keys' "name" and "uses" search keys (portal EnrolmentKeySearchKeyService.cs).
    /// </summary>
    public static string KeySearchKeys() => """
        [
          {
            "name": "name",
            "modifiers": null,
            "dataType": "None",
            "description": "Filter your search by key name.",
            "hintText": "Filter by the name of the enrolment key",
            "hintValues": null,
            "canHaveMultiple": false,
            "isDefault": true,
            "useExactMatch": false
          },
          {
            "name": "uses",
            "modifiers": [ "LessThan", "GreaterThan" ],
            "dataType": "None",
            "description": "Filter your search by uses, select either lesser than or greater than, input a specific value or unlimited.",
            "hintText": "Filter by uses remaining on a key",
            "hintValues": null,
            "canHaveMultiple": false,
            "isDefault": false,
            "useExactMatch": false
          }
        ]
        """;

    /// <summary>
    /// The policies' "state" and "tags" search keys (portal PolicySearchKeyService.cs).
    /// </summary>
    public static string PolicySearchKeys() => """
        [
          {
            "name": "state",
            "modifiers": null,
            "dataType": "None",
            "description": "Filter your search by state, either enabled or disabled.",
            "hintText": "Search for policies that are enabled or disabled",
            "hintValues": [ "Enabled", "Disabled" ],
            "canHaveMultiple": false,
            "isDefault": false,
            "useExactMatch": false
          },
          {
            "name": "tags",
            "modifiers": [ "Or" ],
            "dataType": "Tags",
            "description": "Filter your search by one or more tags.",
            "hintText": "Filter by the tags assigned to the policy",
            "hintValues": null,
            "canHaveMultiple": true,
            "isDefault": false,
            "useExactMatch": false
          }
        ]
        """;

    /// <summary>
    /// The tags' "tag" and "uses" search keys (portal TagSearchKeyService.cs).
    /// </summary>
    public static string TagSearchKeys() => """
        [
          {
            "name": "tag",
            "modifiers": null,
            "dataType": "None",
            "description": "Filter your search by a partial tag name.",
            "hintText": "Filter by tag name",
            "hintValues": null,
            "canHaveMultiple": false,
            "isDefault": true,
            "useExactMatch": false
          },
          {
            "name": "uses",
            "modifiers": [ "LessThan", "GreaterThan" ],
            "dataType": "None",
            "description": "Filter your search by the number of places a tag is used, select either less than, greater than or input a value.",
            "hintText": "Filter by the number of places a tag is used",
            "hintValues": null,
            "canHaveMultiple": false,
            "isDefault": false,
            "useExactMatch": false
          }
        ]
        """;
}
