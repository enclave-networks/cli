using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Support;

// Response bodies in the Enclave API's JSON, for the fake API to serve.
// Each body carries every property of its Enclave.Sdk.Api.Data model in the form Enclave.Sdk.Api
// writes (Constants.JsonSerializerOptions), so a model read from a body and written back gives the
// same JSON, which HarnessTests checks: enums by member name, with Ordered for the gateway priority
// Prioritised; GUID IDs as 32 hex digits, the only form their converters read; DateTime ending in Z
// and DateTimeOffset in +00:00.
internal static partial class ApiJson
{
    private const string Created = "2026-01-01T00:00:00Z";

    private const string Modified = "2026-01-02T09:30:00Z";

    private const string EnrolledAtOffset = "2026-01-01T00:00:00+00:00";

    private const string ConnectedAtOffset = "2026-01-02T09:00:00+00:00";

    private const string LastSeenOffset = "2026-01-02T09:30:00+00:00";

    // A PaginatedResponseModel with the items as its only page.
    public static string Page(params string[] items) => PageAt(0, items.Length, items.Length, items);

    // A PaginatedResponseModel for the first page of a list of total items, the page size being the
    // number of items given.
    public static string PageOf(int total, params string[] items) => PageAt(0, items.Length, total, items);

    // Page page, from 0, of total items read perPage at a time, with the metadata the API writes
    // (portal PaginatedQueryHandler.GetResponseAsync): a page after the last has no items and no
    // nextPage.
    public static string PageAt(int page, int perPage, int total, params string[] items)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfNegative(perPage);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(items.Length, perPage);

        if (items.Length > 0)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan((page * perPage) + items.Length, total);
        }

        var lastPage = perPage > 0 && total > 0
            ? (int)Math.Ceiling(total / (double)perPage) - 1
            : 0;
        int? prevPage = page > 0 ? page - 1 : null;
        int? nextPage = page < lastPage ? page + 1 : null;

        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(JsonNode.Parse(item));
        }

        // The API writes absolute links that repeat the request's query (portal
        // PaginatedQueryHandler.CreatePageLinkUri). Enclave.Sdk.Api reads a link as a Uri, which
        // takes a relative reference too, and lists follow metadata.nextPage.
        return new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["total"] = total,
                ["firstPage"] = 0,
                ["prevPage"] = prevPage,
                ["lastPage"] = lastPage,
                ["nextPage"] = nextPage,
            },
            ["links"] = new JsonObject
            {
                ["first"] = "?page=0",
                ["prev"] = prevPage is null ? null : Invariant($"?page={prevPage}"),
                ["next"] = nextPage is null ? null : Invariant($"?page={nextPage}"),
                ["last"] = Invariant($"?page={lastPage}"),
            },
            ["items"] = array,
        }.ToJsonString();
    }

    // An RFC 9457 problem details body, the form the Enclave API reports errors in.
    public static string Problem(int status, string title, string? detail = null) => new JsonObject
    {
        ["type"] = "about:blank",
        ["title"] = title,
        ["status"] = status,
        ["detail"] = detail,
    }.ToJsonString();

    // A QueryAccountOrgsResponseModel, the GET /account/orgs body, of Org items.
    public static string Orgs(params (Guid Id, string Name)[] orgs) =>
        OrgsOf(orgs.Select(org => Org(org.Id, org.Name)).ToArray());

    // A QueryAccountOrgsResponseModel of the Org items given.
    public static string OrgsOf(params string[] orgs)
    {
        var array = new JsonArray();
        foreach (var org in orgs)
        {
            array.Add(JsonNode.Parse(org));
        }

        return new JsonObject { ["orgs"] = array }.ToJsonString();
    }

    // An AccountOrganisationModel. The roles are portal UserOrganisationRole names.
    public static string Org(Guid id, string name, string role = "Owner", bool partnerAccess = false) => new JsonObject
    {
        ["orgId"] = id.ToString("N"),
        ["orgName"] = name,
        ["role"] = role,
        ["partnerAccess"] = partnerAccess,
    }.ToJsonString();

    // A bulk action result, such as BulkSystemUpdateResult: one integer property.
    public static string Bulk(string field, int count) => new JsonObject { [field] = count }.ToJsonString();

    // A SystemModel, which also reads as a SystemSummaryModel list item.
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

    // An UnapprovedSystemModel, which also reads as an UnapprovedSystemSummaryModel list item.
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

    // An EnrolmentKeyModel, which also reads as an EnrolmentKeySummaryModel list item.
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

    // A PolicyModel, which is also the policy list item.
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

    // A TagModel, which also reads as a TagSummaryModel list item. The same name always gives the
    // same ref.
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

    // The ref Tag gives a tag name, "ref:" and 32 hex digits as the API writes it.
    public static string TagRef(string tag) =>
        "ref:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(tag)))[..32];

    // A DnsZoneModel, which also reads as a DnsZoneSummaryModel list item.
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

    // A DnsRecordModel in zone 1 ("enclave"), which also reads as a DnsRecordSummaryModel list item.
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

    // A TrustRequirementModel, which also reads as a TrustRequirementSummaryModel list item. The
    // body carries the summary model's "summary" as well, which TrustRequirementModel lacks.
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

    // A LogEntryModel.
    public static string Log(string message) => new JsonObject
    {
        ["timeStamp"] = Created,
        ["level"] = "Information",
        ["message"] = message,
        ["userName"] = "admin@acme.example",
        ["ipAddress"] = "203.0.113.5",
    }.ToJsonString();

    // An OrganisationPropertiesModel for the organisation TestData.OrgId.
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

    // An OrganisationUsersModel, the GET /org/{orgId}/users body, of User items.
    public static string Users(params (Guid Id, string Email)[] users)
    {
        var array = new JsonArray();
        foreach (var (id, email) in users)
        {
            array.Add(JsonNode.Parse(User(id, email)));
        }

        return new JsonObject { ["users"] = array }.ToJsonString();
    }

    // An OrganisationUser with role Admin.
    public static string User(Guid id, string email) => new JsonObject
    {
        ["id"] = id.ToString("N"),
        ["emailAddress"] = email,
        ["fullName"] = email.Split('@')[0],
        ["joinDate"] = Created,
        ["role"] = "Admin",
    }.ToJsonString();

    // An OrganisationPendingInvitesModel, the GET /org/{orgId}/invites body.
    public static string Invites(params string[] emails)
    {
        var array = new JsonArray();
        foreach (var email in emails)
        {
            array.Add(JsonNode.Parse(Invite(email)));
        }

        return new JsonObject { ["invites"] = array }.ToJsonString();
    }

    // An OrganisationInviteModel.
    public static string Invite(string email) => new JsonObject { ["emailAddress"] = email }.ToJsonString();

    // A DnsSummaryModel, the GET /org/{orgId}/dns body.
    public static string DnsSummary() => new JsonObject
    {
        ["zoneCount"] = 1,
        ["totalRecordCount"] = 2,
    }.ToJsonString();
}
