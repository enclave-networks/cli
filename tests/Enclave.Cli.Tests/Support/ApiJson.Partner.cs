using System.Globalization;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// Partner API bodies have the property names, value types and nullability of the partner API's
// models, which Enclave.Sdk.Api 1.1.0 defines in Enclave.Sdk.Api.Partner.Models and the deployed
// schema gives (https://partner-api.enclave.io/swagger/v1/swagger.json, components.schemas
// CustomerModel, CustomerUsersModel, CustomerUserModel, CustomerPendingAdminInvitesModel and
// CustomerAdminInviteModel). As for the main API's bodies, each carries every property of its
// model, GUIDs are 32 hex digits, enums are member names, and times end in Z (DateTime) or
// +00:00 (DateTimeOffset), so a model read from a body and written back gives the same JSON.
// PartnerCommandTests compares what the CLI prints with these bodies, which shows they read into
// the models field for field.

/// <summary>
/// Partner API response bodies: customers, their admins and their admin invites.
/// </summary>
internal static partial class ApiJson
{
    private const string TrialEnd = "2026-02-01T00:00:00+00:00";

    /// <summary>
    /// A CustomerModel, the customer GET, create, update, convert and auto-sync body and the customer
    /// list item, for the customer with this organisation ID. Only the customer list sets
    /// oldestEnclaveVersion (CustomerModel.OldestEnclaveVersion, Enclave.Sdk.Api 1.1.0).
    /// </summary>
    public static string Customer(string orgId, string name, string? oldestEnclaveVersion = null) => new JsonObject
    {
        ["id"] = Hex(orgId),
        ["name"] = name,
        ["enrolledSystems"] = 12,
        ["configuredGateways"] = 1,
        ["licensedSystems"] = 50,
        ["licensedGateways"] = 2,
        ["hardLimit"] = -1,
        ["industryDiscount"] = false,
        ["contactName"] = "Jo Bloggs",
        ["billingPeriodMonths"] = null,
        ["status"] = "InPoC",
        ["trialEndDate"] = TrialEnd,
        ["hardLimitForcedOn"] = false,
        ["isSuspended"] = false,
        ["userHasAccess"] = true,
        ["nextInvoiceDate"] = null,
        ["isNfr"] = false,
        ["adminAutoSyncIsEnabled"] = false,
        ["oldestEnclaveVersion"] = oldestEnclaveVersion,
    }.ToJsonString();

    /// <summary>
    /// A CustomerUsersModel, the GET .../customers/{id}/admins body, whose users are CustomerUserModel.
    /// </summary>
    public static string CustomerAdmins(params string[] users)
    {
        var array = new JsonArray();
        foreach (var user in users)
        {
            array.Add(JsonNode.Parse(user));
        }

        return new JsonObject { ["users"] = array }.ToJsonString();
    }

    /// <summary>
    /// A CustomerUserModel: one item of <see cref="CustomerAdmins"/>, and the body add-admin and
    /// remove-admin return. Role is Owner or Admin (UserOrganisationRole).
    /// </summary>
    public static string CustomerAdmin(string accountId, string email, string role = "Admin") => new JsonObject
    {
        ["id"] = Hex(accountId),
        ["emailAddress"] = email,
        ["fullName"] = email.Split('@')[0],
        ["joinDate"] = Created,
        ["role"] = role,
    }.ToJsonString();

    /// <summary>
    /// A CustomerPendingAdminInvitesModel, the GET .../customers/{id}/invites body, whose invites are
    /// CustomerAdminInviteModel.
    /// </summary>
    public static string CustomerInvites(params string[] invites)
    {
        var array = new JsonArray();
        foreach (var invite in invites)
        {
            array.Add(JsonNode.Parse(invite));
        }

        return new JsonObject { ["invites"] = array }.ToJsonString();
    }

    /// <summary>
    /// A CustomerAdminInviteModel: one item of <see cref="CustomerInvites"/>, and the body an invite
    /// returns. The API writes the invite ID as a string, the organisation ID and a number joined by a
    /// hyphen (Enclave.Sdk.Api 1.1.0, CustomersClientTests).
    /// </summary>
    public static string CustomerInvite(string orgId, int number, string email) => new JsonObject
    {
        ["id"] = TestData.CustomerInviteId(orgId, number),
        ["emailAddress"] = email,
    }.ToJsonString();

    /// <summary>
    /// The body the API answers a cancelled invite with: the email address, and the ID written as
    /// null (portal DeleteCustomerPendingAdminInviteHandler.cs leaves it unset, as
    /// CustomersClient.CancelInviteAsync records, Enclave.Sdk.Api 1.1.0).
    /// </summary>
    public static string CancelledCustomerInvite(string email) => new JsonObject
    {
        ["id"] = null,
        ["emailAddress"] = email,
    }.ToJsonString();

    private static string Hex(string guid) => Guid.Parse(guid, CultureInfo.InvariantCulture).ToString("N");
}
