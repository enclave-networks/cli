namespace Enclave.Cli.Tests.Support;

// The customer, account and email values are the ones the partner examples of
// proposed-cli-surface.md use ("Examples" 34, 65, 66 and 85), so a test command reads as the
// example it follows. IDs are in the hyphenated form people type.

/// <summary>
/// Fixed identities for the partner customer tests.
/// </summary>
internal static partial class TestData
{
    public const string CustomerName = "Globex Ltd";

    public const string CustomerOrgId = "6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b";

    public const string CustomerAdminEmail = "alex@example.com";

    public const string CustomerInviteEmail = "sam@globex.example";

    public const string PartnerStaffAccountId = "5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25";

    public const string NewCustomerName = "Initech";
}
