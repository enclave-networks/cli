using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// Property names are the camelCase names of the Enclave.Sdk.Api.Data 304.48.0 models, and enums
// are written as their member names, which is how Enclave.Sdk.Api 1.0.4 reads them
// (Constants.JsonSerializerOptions). GUID identifiers (OrganisationGuid, AccountGuid) are 32 hex
// digits without hyphens, the only form their JSON converters read and the form they write. Each
// body carries every property of the model it represents, with DateTime values ending in Z and
// DateTimeOffset values ending in +00:00, so a model read from a body and written back with the
// SDK's options gives the same JSON. HarnessTests checks both.

/// <summary>
/// Response bodies in the Enclave API's JSON, for the fake API to serve.
/// </summary>
internal static class ApiJson
{
    private const string Created = "2026-01-01T00:00:00Z";

    private const string Modified = "2026-01-02T09:30:00Z";

    private const string EnrolledAtOffset = "2026-01-01T00:00:00+00:00";

    private const string ConnectedAtOffset = "2026-01-02T09:00:00+00:00";

    private const string LastSeenOffset = "2026-01-02T09:30:00+00:00";

    /// <summary>
    /// A PaginatedResponseModel holding the given items, with metadata.total equal to the item count.
    /// </summary>
    public static string Page(params string[] items) => PageOf(items.Length, items);

    /// <summary>
    /// A PaginatedResponseModel for the first page of a list of <paramref name="total"/> items, the
    /// page size being the number of items given. Pagination metadata and links follow
    /// PaginatedQueryHandler.GetResponseAsync in the portal (Enclave.Api.Scaffolding).
    /// </summary>
    public static string PageOf(int total, params string[] items)
    {
        var lastPage = items.Length > 0 && total > items.Length
            ? (int)Math.Ceiling(total / (double)items.Length) - 1
            : 0;
        int? nextPage = lastPage > 0 ? 1 : null;

        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(JsonNode.Parse(item));
        }

        return new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["total"] = total,
                ["firstPage"] = 0,
                ["prevPage"] = null,
                ["lastPage"] = lastPage,
                ["nextPage"] = nextPage,
            },
            ["links"] = new JsonObject
            {
                ["first"] = "?page=0",
                ["prev"] = null,
                ["next"] = nextPage is null ? null : "?page=1",
                ["last"] = $"?page={lastPage}",
            },
            ["items"] = array,
        }.ToJsonString();
    }

    /// <summary>
    /// A QueryAccountOrgsResponseModel, the GET /account/orgs body, whose orgs are
    /// AccountOrganisationModel with role Owner and partnerAccess false.
    /// </summary>
    public static string Orgs(params (Guid Id, string Name)[] orgs)
    {
        var array = new JsonArray();
        foreach (var (id, name) in orgs)
        {
            array.Add(new JsonObject
            {
                ["orgId"] = id.ToString("N"),
                ["orgName"] = name,
                ["role"] = "Owner",
                ["partnerAccess"] = false,
            });
        }

        return new JsonObject { ["orgs"] = array }.ToJsonString();
    }

    /// <summary>
    /// A bulk action result, such as BulkSystemUpdateResult: one integer property.
    /// </summary>
    public static string Bulk(string field, int count) => new JsonObject { [field] = count }.ToJsonString();

    /// <summary>
    /// A SystemModel, which also reads as a SystemSummaryModel list item.
    /// </summary>
    public static string System(string systemId, string description = "web") => new JsonObject
    {
        ["systemId"] = systemId,
        ["description"] = description,
        ["systemType"] = "GeneralPurpose",
        ["state"] = "Connected",
        ["connectedAt"] = ConnectedAtOffset,
        ["lastSeen"] = LastSeenOffset,
        ["enrolledAt"] = EnrolledAtOffset,
        ["enrolmentKeyId"] = 1,
        ["enrolmentKeyDescription"] = "key",
        ["enrolmentKeyIsDeleted"] = false,
        ["isEnabled"] = true,
        ["connectedFrom"] = "203.0.113.10",
        ["virtualAddress"] = "100.64.0.2",
        ["virtualNetwork"] = "100.64.0.0/10",
        ["hostname"] = "web-01",
        ["platformType"] = "Linux",
        ["osVersion"] = "Ubuntu 24.04",
        ["enclaveVersion"] = "2026.1.1",
        ["gatewayRoutes"] = new JsonArray(),
        ["tags"] = new JsonArray(),
        ["dns"] = new JsonArray(),
        ["knownSubnets"] = new JsonArray(),
        ["notes"] = null,
        ["disconnectedRetentionMinutes"] = null,
        ["autoExpire"] = null,
    }.ToJsonString();

    /// <summary>
    /// An UnapprovedSystemModel, which also reads as an UnapprovedSystemSummaryModel list item.
    /// </summary>
    public static string PendingSystem(string systemId, string description = "web") => new JsonObject
    {
        ["systemId"] = systemId,
        ["systemType"] = "GeneralPurpose",
        ["description"] = description,
        ["enrolledFrom"] = "203.0.113.10",
        ["enrolledAt"] = Created,
        ["tags"] = new JsonArray(),
        ["enrolmentKeyId"] = 1,
        ["enrolmentKeyDescription"] = "key",
        ["enrolmentKeyIsDeleted"] = false,
        ["notes"] = null,
        ["hostname"] = "web-01",
        ["platformType"] = "Linux",
        ["osVersion"] = "Ubuntu 24.04",
        ["enclaveVersion"] = "2026.1.1",
        ["connectedFrom"] = "203.0.113.10",
    }.ToJsonString();

    /// <summary>
    /// An EnrolmentKeyModel, which also reads as an EnrolmentKeySummaryModel list item.
    /// </summary>
    public static string Key(int id, string description = "key", string secret = "SECRET-KEY-VALUE") => new JsonObject
    {
        ["id"] = id,
        ["created"] = Created,
        ["lastUsed"] = null,
        ["type"] = "GeneralPurpose",
        ["approvalMode"] = "Automatic",
        ["status"] = "Enabled",
        ["key"] = secret,
        ["description"] = description,
        ["isEnabled"] = true,
        ["usesRemaining"] = 10,
        ["enrolledCount"] = 2,
        ["unapprovedCount"] = 0,
        ["tags"] = new JsonArray(),
        ["disconnectedRetentionMinutes"] = null,
        ["ipConstraints"] = new JsonArray(),
        ["notes"] = null,
        ["autoExpire"] = null,
    }.ToJsonString();

    /// <summary>
    /// A PolicyModel, which is also the policy list item.
    /// </summary>
    public static string Policy(int id, string description = "policy") => new JsonObject
    {
        ["id"] = id,
        ["type"] = "General",
        ["created"] = Created,
        ["description"] = description,
        ["isEnabled"] = true,
        ["state"] = "Active",
        ["senderTags"] = new JsonArray(),
        ["receiverTags"] = new JsonArray(),
        ["acls"] = new JsonArray(new JsonObject
        {
            ["protocol"] = "Any",
            ["ports"] = null,
            ["description"] = null,
        }),
        ["gatewayAllowedIpRanges"] = new JsonArray(),
        ["gateways"] = new JsonArray(),
        ["gatewayTrafficDirection"] = null,
        ["gatewayPriority"] = null,
        ["senderTrustRequirements"] = new JsonArray(),
        ["notes"] = null,
        ["autoExpire"] = null,
        ["activeHours"] = null,
    }.ToJsonString();

    /// <summary>
    /// A TagModel, which also reads as a TagSummaryModel list item. The ref is derived from the
    /// tag name, so the same name always gives the same ref.
    /// </summary>
    public static string Tag(string tag) => new JsonObject
    {
        ["tag"] = tag,
        ["ref"] = TagRef(tag),
        ["colour"] = "#3b82f6",
        ["created"] = Created,
        ["lastModified"] = Modified,
        ["lastReferenced"] = null,
        ["systems"] = 0,
        ["keys"] = 0,
        ["policies"] = 0,
        ["dnsZones"] = 0,
        ["dnsRecords"] = 0,
        ["notes"] = null,
        ["trustRequirements"] = new JsonArray(),
    }.ToJsonString();

    /// <summary>
    /// The ref ApiJson.Tag gives a tag name, in the API's "ref:" plus 32 hex digits form.
    /// </summary>
    public static string TagRef(string tag) =>
        "ref:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tag)))[..32];

    /// <summary>
    /// A DnsZoneModel, which also reads as a DnsZoneSummaryModel list item.
    /// </summary>
    public static string Zone(int id, string name) => new JsonObject
    {
        ["id"] = id,
        ["name"] = name,
        ["created"] = Created,
        ["recordCount"] = 0,
        ["recordTypeCounts"] = new JsonObject(),
        ["autoDnsTags"] = new JsonArray(),
        ["notes"] = null,
    }.ToJsonString();

    /// <summary>
    /// A DnsRecordModel in zone 1 ("enclave"), which also reads as a DnsRecordSummaryModel list item.
    /// </summary>
    public static string Record(int id, string name) => new JsonObject
    {
        ["id"] = id,
        ["name"] = name,
        ["type"] = "ENCLAVE",
        ["zoneId"] = 1,
        ["zoneName"] = "enclave",
        ["fqdn"] = $"{name}.enclave",
        ["tags"] = new JsonArray(),
        ["systems"] = new JsonArray(),
        ["autoGenerated"] = false,
        ["notes"] = null,
    }.ToJsonString();

    /// <summary>
    /// A TrustRequirementModel, which also reads as a TrustRequirementSummaryModel list item. The
    /// body carries the summary model's "summary" as well, which TrustRequirementModel lacks.
    /// </summary>
    public static string Trust(int id, string description = "trust") => new JsonObject
    {
        ["id"] = id,
        ["description"] = description,
        ["created"] = Created,
        ["modified"] = Modified,
        ["type"] = "UserAuthentication",
        ["usedInTags"] = 0,
        ["usedInPolicies"] = 0,
        ["summary"] = "Signed in with an Enclave account",
        ["notes"] = null,
        ["settings"] = new JsonObject
        {
            ["configuration"] = new JsonObject(),
            ["conditions"] = new JsonArray(),
        },
    }.ToJsonString();

    /// <summary>
    /// A LogEntryModel.
    /// </summary>
    public static string Log(string message) => new JsonObject
    {
        ["timeStamp"] = Created,
        ["level"] = "Information",
        ["message"] = message,
        ["userName"] = "admin@acme.example",
        ["ipAddress"] = "203.0.113.5",
    }.ToJsonString();

    /// <summary>
    /// An OrganisationPropertiesModel for the organisation TestData.OrgId.
    /// </summary>
    public static string OrgProperties(string name) => new JsonObject
    {
        ["id"] = TestData.OrgId.ToString("N"),
        ["created"] = Created,
        ["name"] = name,
        ["plan"] = "Business",
        ["billingIsPaidYearly"] = null,
        ["billingRate"] = null,
        ["billingDisableUpdate"] = null,
        ["billingCurrencySymbol"] = "$",
        ["website"] = null,
        ["phone"] = null,
        ["maxSystems"] = 100,
        ["enrolledSystems"] = 2,
        ["unapprovedSystems"] = 0,
        ["featureLimits"] = new JsonArray(),
        ["trialExpiry"] = null,
        ["trialState"] = "None",
        ["isSuspendedByAdmin"] = false,
        ["partner"] = null,
    }.ToJsonString();

    /// <summary>
    /// An OrganisationUsersModel, the GET /org/{orgId}/users body, whose users are
    /// OrganisationUser with role Admin.
    /// </summary>
    public static string Users(params (Guid Id, string Email)[] users)
    {
        var array = new JsonArray();
        foreach (var (id, email) in users)
        {
            array.Add(new JsonObject
            {
                ["id"] = id.ToString("N"),
                ["emailAddress"] = email,
                ["fullName"] = email.Split('@')[0],
                ["joinDate"] = Created,
                ["role"] = "Admin",
            });
        }

        return new JsonObject { ["users"] = array }.ToJsonString();
    }

    /// <summary>
    /// An OrganisationPendingInvitesModel, the GET /org/{orgId}/invites body, whose invites are
    /// OrganisationInviteModel.
    /// </summary>
    public static string Invites(params string[] emails)
    {
        var array = new JsonArray();
        foreach (var email in emails)
        {
            array.Add(new JsonObject { ["emailAddress"] = email });
        }

        return new JsonObject { ["invites"] = array }.ToJsonString();
    }

    /// <summary>
    /// A DnsSummaryModel, the GET /org/{orgId}/dns body.
    /// </summary>
    public static string DnsSummary() => new JsonObject
    {
        ["zoneCount"] = 1,
        ["totalRecordCount"] = 2,
    }.ToJsonString();
}
