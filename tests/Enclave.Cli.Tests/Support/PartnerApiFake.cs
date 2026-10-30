using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The fake partner API that partner customer commands run against, and the check that a run's
/// calls went to it.
/// </summary>
// The partner tests and the safety tests share this fake, so they cannot drift apart on what the
// partner API answers.
internal static class PartnerApiFake
{
    private const string PartnerIdVariable = "ENCLAVE_PARTNER_ID";

    // The bodies StubPartnerApi serves for the test customer's admins and invites.
    public static string AdminBody => ApiJson.CustomerAdmin(TestData.CustomerAdminAccountId, TestData.CustomerAdminEmail);

    public static string OwnerBody => ApiJson.CustomerAdmin(TestData.CustomerOwnerAccountId, TestData.CustomerOwnerEmail, "Owner");

    public static string StaffBody => ApiJson.CustomerAdmin(TestData.PartnerStaffAccountId, "staff@partner.example");

    public static string OtherInviteBody => ApiJson.CustomerInvite(TestData.CustomerOrgId, 2, "kim@globex.example");

    public static string InviteBody => ApiJson.CustomerInvite(TestData.CustomerOrgId, TestData.CustomerInviteNumber, TestData.CustomerInviteEmail);

    public static string NewInviteBody => ApiJson.CustomerInvite(TestData.CustomerOrgId, 4, TestData.CustomerInviteEmail);

    /// <summary>
    /// A sandbox with the test partner chosen and the answers of <see cref="StubPartnerApi"/>.
    /// </summary>
    public static CliRun StartWithPartner()
    {
        var run = StartWithPartnerOnly();
        StubPartnerApi(run);
        return run;
    }

    /// <summary>
    /// A sandbox with the test partner chosen and nothing stubbed.
    /// </summary>
    // For a test that serves a route differently from StubPartnerApi: two stubs for one route at one
    // priority leave WireMock.Net's choice between them unstated.
    public static CliRun StartWithPartnerOnly()
    {
        var run = CliRun.Start();
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        return run;
    }

    /// <summary>
    /// Answers every customer route for two customers: Initech, then Globex Ltd, so a name lookup
    /// that took the first customer acts on the wrong one. Each has an owner, then the admin
    /// alex@example.com, and invites to kim@globex.example, then sam@globex.example.
    /// </summary>
    public static void StubPartnerApi(CliRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        StubCustomerList(run, TestData.PartnerId);
        StubCustomer(run, TestData.NewCustomerOrgId, TestData.NewCustomerName);
        StubCustomer(run, TestData.CustomerOrgId, TestData.CustomerName);
        run.Stub("POST", TestData.PartnerPath("customers"), json: ApiJson.Customer(TestData.NewCustomerOrgId, TestData.NewCustomerName));
    }

    /// <summary>
    /// Answers this partner's customer list with Initech, then Globex Ltd.
    /// </summary>
    public static void StubCustomerList(CliRun run, Guid partnerId)
    {
        ArgumentNullException.ThrowIfNull(run);

        run.StubPages(
            TestData.PartnerPathOf(partnerId, "customers"),
            200,
            ApiJson.Customer(TestData.NewCustomerOrgId, TestData.NewCustomerName, "2025.6.0"),
            ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName, "2026.1.1"));
    }

    /// <summary>
    /// Asserts the run exited 0 having sent at least one request, all to this partner's routes (the
    /// test partner's when none is given) and none to the main API's address.
    /// </summary>
    public static void AssertSentToThePartnerApi(CliRun run, CliResult result, Guid? partnerId = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        var prefix = TestData.PartnerPathOf(partnerId ?? TestData.PartnerId) + "/";

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.PartnerApiRequests.Select(request => request.Path), Is.Not.Empty.And.All.StartsWith(prefix), result.ToString());
            Assert.That(run.ApiRequests.Select(request => $"{request.Method} {request.Path}"), Is.Empty, $"The main API's address received calls.{Environment.NewLine}{result}");
        });
    }

    /// <summary>
    /// The path of one of a customer's pending invites, as cancel-invite addresses it.
    /// </summary>
    public static string InvitePath(string orgId, int number) =>
        TestData.CustomerPath(orgId, $"invites/{TestData.CustomerInviteId(orgId, number)}");

    // Invite IDs carry the customer's organisation ID, as the API's do.
    private static void StubCustomer(CliRun run, string orgId, string name)
    {
        var customer = ApiJson.Customer(orgId, name);

        run.Stub("GET", TestData.CustomerPath(orgId), json: customer);
        run.Stub("PATCH", TestData.CustomerPath(orgId), json: customer);
        run.Stub("PUT", TestData.CustomerPath(orgId, "convert"), json: customer);
        run.Stub("PUT", TestData.CustomerPath(orgId, "enable-auto-sync"), json: customer);
        run.Stub("PUT", TestData.CustomerPath(orgId, "disable-auto-sync"), json: customer);
        run.Stub("GET", TestData.CustomerPath(orgId, "admins"), json: ApiJson.CustomerAdmins(OwnerBody, AdminBody));
        run.Stub("PUT", TestData.CustomerAdminPath(orgId, TestData.PartnerStaffAccountId), json: StaffBody);
        run.Stub("DELETE", TestData.CustomerAdminPath(orgId, TestData.PartnerStaffAccountId), json: StaffBody);
        run.Stub("DELETE", TestData.CustomerAdminPath(orgId, TestData.CustomerAdminAccountId), json: AdminBody);
        run.Stub("GET", TestData.CustomerPath(orgId, "invites"), json: ApiJson.CustomerInvites(
            ApiJson.CustomerInvite(orgId, 2, "kim@globex.example"),
            ApiJson.CustomerInvite(orgId, TestData.CustomerInviteNumber, TestData.CustomerInviteEmail)));
        run.Stub("POST", TestData.CustomerPath(orgId, "invites"), json: ApiJson.CustomerInvite(orgId, 4, TestData.CustomerInviteEmail));
        run.Stub("DELETE", InvitePath(orgId, TestData.CustomerInviteNumber), json: ApiJson.CancelledCustomerInvite(TestData.CustomerInviteEmail));
    }
}
