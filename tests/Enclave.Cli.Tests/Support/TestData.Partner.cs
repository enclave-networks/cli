using System.Globalization;

namespace Enclave.Cli.Tests.Support;

// Values from the partner examples in spec/cli-surface.md "Examples", so a test command reads as
// the example it follows. IDs are hyphenated, as people type them.

/// <summary>
/// Fixed identities for the partner customer tests.
/// </summary>
internal static partial class TestData
{
    public const string CustomerName = "Globex Ltd";

    public const string CustomerOrgId = "6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b";

    public const string CustomerAdminEmail = "alex@example.com";

    /// <summary>
    /// The account ID of the customer admin <see cref="CustomerAdminEmail"/>.
    /// </summary>
    public const string CustomerAdminAccountId = "3c9a7e15-6d2b-4f80-a1c4-e5b7d9f02a68";

    /// <summary>
    /// The owner of the customer's organisation, listed with its admins ahead of
    /// <see cref="CustomerAdminEmail"/>.
    /// </summary>
    public const string CustomerOwnerEmail = "owner@globex.example";

    public const string CustomerOwnerAccountId = "a4d2f6b8-1e3c-4a57-9b0d-7c6e5f4a3b21";

    public const string CustomerInviteEmail = "sam@globex.example";

    public const string PartnerStaffAccountId = "5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25";

    public const string NewCustomerName = "Initech";

    public const string NewCustomerOrgId = "8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6";

    /// <summary>
    /// The number in the ID of the pending invite to <see cref="CustomerInviteEmail"/>.
    /// </summary>
    public const int CustomerInviteNumber = 3;

    public static readonly Guid PartnerStaffAccountGuid = Guid.Parse(PartnerStaffAccountId, CultureInfo.InvariantCulture);

    // Enclave.Sdk.Api writes partner, customer and account IDs into paths as 32 hex digits
    // (PartnerClient constructor, CustomersClient.CustomerRoute).
    public static string PartnerPath(string suffix = "") => PartnerPathOf(PartnerId, suffix);

    public static string PartnerPathOf(Guid partnerId, string suffix = "") =>
        suffix.Length == 0 ? $"/partner/{partnerId:N}" : $"/partner/{partnerId:N}/{suffix}";

    /// <summary>
    /// The path of one of the test partner's customers, by its organisation ID.
    /// </summary>
    public static string CustomerPath(string customerOrgId, string suffix = "")
    {
        var customer = $"customers/{Guid.Parse(customerOrgId, CultureInfo.InvariantCulture):N}";
        return PartnerPath(suffix.Length == 0 ? customer : $"{customer}/{suffix}");
    }

    /// <summary>
    /// The path of one of a customer's admins, as add-admin and remove-admin address it.
    /// </summary>
    public static string CustomerAdminPath(string customerOrgId, string accountId) =>
        CustomerPath(customerOrgId, $"admins/{Guid.Parse(accountId, CultureInfo.InvariantCulture):N}");

    /// <summary>
    /// The ID of a customer's admin invite: its organisation ID and a number, joined by a hyphen
    /// (portal OrganisationInviteId.Create, as Enclave.Sdk.Api's CustomersClientTests write it).
    /// </summary>
    public static string CustomerInviteId(string customerOrgId, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{Guid.Parse(customerOrgId, CultureInfo.InvariantCulture):N}-{number}");
}
