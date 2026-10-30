using System.Globalization;
using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// Bodies follow Enclave.Sdk.Api.Partner.Models and the deployed schema
// (https://partner-api.enclave.io/swagger/v1/swagger.json), in the same form as the main API's.

// Partner API response bodies: customers, their admins and their admin invites.
internal static partial class ApiJson
{
    private const string TrialEnd = "2026-02-01T00:00:00+00:00";

    // A CustomerModel, the customer GET, create, update, convert and auto-sync body and the list
    // item. Only the list sets oldestEnclaveVersion (Enclave.Sdk.Api
    // CustomerModel.OldestEnclaveVersion).
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

    // A CustomerUsersModel, the GET .../customers/{id}/admins body.
    public static string CustomerAdmins(params string[] users)
    {
        var array = new JsonArray();
        foreach (var user in users)
        {
            array.Add(JsonNode.Parse(user));
        }

        return new JsonObject { ["users"] = array }.ToJsonString();
    }

    // A CustomerUserModel, also the body add-admin and remove-admin return. Role is Owner or Admin
    // (UserOrganisationRole).
    public static string CustomerAdmin(string accountId, string email, string role = "Admin") => new JsonObject
    {
        ["id"] = Hex(accountId),
        ["emailAddress"] = email,
        ["fullName"] = email.Split('@')[0],
        ["joinDate"] = Created,
        ["role"] = role,
    }.ToJsonString();

    // A CustomerPendingAdminInvitesModel, the GET .../customers/{id}/invites body.
    public static string CustomerInvites(params string[] invites)
    {
        var array = new JsonArray();
        foreach (var invite in invites)
        {
            array.Add(JsonNode.Parse(invite));
        }

        return new JsonObject { ["invites"] = array }.ToJsonString();
    }

    // A CustomerAdminInviteModel, also the body an invite returns.
    public static string CustomerInvite(string orgId, int number, string email) => new JsonObject
    {
        ["id"] = TestData.CustomerInviteId(orgId, number),
        ["emailAddress"] = email,
    }.ToJsonString();

    // The body the API answers a cancelled invite with, whose ID is null (portal
    // DeleteCustomerPendingAdminInviteHandler; Enclave.Sdk.Api CustomersClient.CancelInviteAsync).
    public static string CancelledCustomerInvite(string email) => new JsonObject
    {
        ["id"] = null,
        ["emailAddress"] = email,
    }.ToJsonString();

    private static string Hex(string guid) => Guid.Parse(guid, CultureInfo.InvariantCulture).ToString("N");
}
