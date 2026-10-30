using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// Every test chooses a valid partner through ENCLAVE_PARTNER_ID, so a bad argument is the only
// error a case can have. An argument case runs the corrected command after the rejected one,
// since an unknown command is rejected the same way (CliAssert.RejectedThenAcceptedAsync).
//
// PartnerContextTests and PartnerSafetyTests share Commands, so the three cannot disagree on which
// commands exist or what they send.
public class PartnerCommandTests
{
    private const string FileToken = "file-token-4c7d21";

    private static readonly string[] InvalidBillingMonths = ["0", "2", "6", "13", "48", "twelve"];

    // CustomerCreateModel's fields, which a create always sends ("Command options").
    private static readonly string[] CreateFields =
    [
        "name", "ownerEmail", "domain", "industryDiscount", "initialSystemsCount", "initialGatewaysCount", "contactName", "hardLimit", "adminAutoSyncIsEnabled",
    ];

    // One valid command line per partner customer verb ("Commands"). Changes marks the verbs that
    // take --dry-run. list-invites only reads, although the API asks for WriteCustomers to read
    // invites (portal CustomersController.GetPendingCustomerAdminInvites). Routes and methods are
    // the partner API's (https://partner-api.enclave.io/swagger/v1/swagger.json).
    internal static IReadOnlyList<CustomerCommand> Commands { get; } =
    [
        new("list", TakesCustomer: false, Changes: false, [], "GET", TestData.PartnerPath("customers")),
        new("show", TakesCustomer: true, Changes: false, [], "GET", TestData.CustomerPath(TestData.CustomerOrgId)),
        new("create", TakesCustomer: false, Changes: true, [TestData.NewCustomerName], "POST", TestData.PartnerPath("customers")),
        new("update", TakesCustomer: true, Changes: true, ["--systems", "30"], "PATCH", TestData.CustomerPath(TestData.CustomerOrgId)),
        new("convert", TakesCustomer: true, Changes: true, ["--billing-months", "12"], "PUT", TestData.CustomerPath(TestData.CustomerOrgId, "convert")),
        new("list-admins", TakesCustomer: true, Changes: false, [], "GET", TestData.CustomerPath(TestData.CustomerOrgId, "admins")),
        new("add-admin", TakesCustomer: true, Changes: true, ["--user-id", TestData.PartnerStaffAccountId], "PUT", TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.PartnerStaffAccountId)),
        new("remove-admin", TakesCustomer: true, Changes: true, ["--user", TestData.CustomerAdminEmail], "DELETE", TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.CustomerAdminAccountId), TestData.CustomerPath(TestData.CustomerOrgId, "admins")),
        new("list-invites", TakesCustomer: true, Changes: false, [], "GET", TestData.CustomerPath(TestData.CustomerOrgId, "invites")),
        new("invite", TakesCustomer: true, Changes: true, ["--email", TestData.CustomerInviteEmail], "POST", TestData.CustomerPath(TestData.CustomerOrgId, "invites")),
        new("cancel-invite", TakesCustomer: true, Changes: true, ["--email", TestData.CustomerInviteEmail], "DELETE", PartnerApiFake.InvitePath(TestData.CustomerOrgId, TestData.CustomerInviteNumber), TestData.CustomerPath(TestData.CustomerOrgId, "invites")),
        new("enable-auto-sync", TakesCustomer: true, Changes: true, [], "PUT", TestData.CustomerPath(TestData.CustomerOrgId, "enable-auto-sync")),
        new("disable-auto-sync", TakesCustomer: true, Changes: true, [], "PUT", TestData.CustomerPath(TestData.CustomerOrgId, "disable-auto-sync")),
    ];

    // A customer's only ID is its organisation ID (portal CustomersController parses the route's
    // customer ID as an OrganisationGuid).
    [TestCaseSource(nameof(CommandsByOrgIdWithCalls))]
    public async Task Every_partner_customer_command_given_org_id_makes_its_call_without_a_customer_lookup(string[] args, string[] calls)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(calls), result.ToString());
    }

    // Given by name, a command looks the customer up, then makes its call; an admin or invite given
    // by email address adds a read of the customer's admins or invites ("Calls per command"). The
    // main API's fake answers partner routes 421, so a call sent there would also fail the command
    // ("Partner API").
    [TestCaseSource(nameof(CommandsByNameWithCalls))]
    public async Task Every_partner_customer_command_sends_its_calls_to_the_partner_api_address_and_none_to_the_api_address(string[] args, string[] calls)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.PartnerApiRequests.Select(request => request.Call), Is.EqualTo(calls), result.ToString());
            Assert.That(run.ApiRequests.Select(request => request.Call), Is.Empty, result.ToString());
        });
    }

    // ENCLAVE_TOKEN keeps the file's partnerApiBaseUrl as it keeps baseUrl ("Partner API"). The
    // run's own fakes serve the partner API too, so a CLI that ignored the file would succeed and
    // show only in their requests.
    [TestCase(false)]
    [TestCase(true)]
    public async Task A_partner_customer_command_calls_the_partner_api_address_in_credentials_json_whichever_source_supplies_the_token(bool tokenFromEnvironment)
    {
        using var run = PartnerApiFake.StartWithPartner();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        var customers = TestData.PartnerPath("customers");
        LoopbackApi.Stub(otherApi, "GET", customers, 200, ApiJson.Page(ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName)));
        run.SaveCredentials(FileToken, partnerApiBaseUrl: otherUrl);

        if (!tokenFromEnvironment)
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
        }

        var result = await run.RunAsync("partner", "customer", "list");

        var received = LoopbackApi.Received(otherApi);
        CliAssert.List(result, "customer");
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.Empty, "The call went to a default address.");
            Assert.That(received.Select(request => request.Call), Is.EqualTo(new[] { $"GET {customers}" }));
            Assert.That(received.Select(request => request.Authorization), Is.EqualTo(new[] { $"Bearer {(tokenFromEnvironment ? TestData.Token : FileToken)}" }));
        });
    }

    // Example 20. The fake serves two customers a page, so five take three pages. The list takes no
    // filter options, so page and per_page are its whole query ("Output").
    [Test]
    public async Task Partner_customer_list_reads_every_page_and_prints_the_customers_as_a_customer_list()
    {
        using var run = PartnerApiFake.StartWithPartnerOnly();
        var ids = TestData.Ids("customer", 5);
        var bodies = ids.Select((id, i) => ApiJson.Customer(id, $"Customer {i}", "2026.1.1")).ToArray();
        run.StubPages(TestData.PartnerPath("customers"), 2, bodies);

        var result = await run.RunAsync("partner", "customer", "list");

        var items = CliAssert.List(result, "customer");
        var requests = run.RequestsTo("GET", TestData.PartnerPath("customers"));
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(TestData.PartnerPath("customers")), Is.EqualTo("0,1,2"));
            Assert.That(run.Requests, Has.Count.EqualTo(3));
            Assert.That(requests.Select(request => request.QueryValue("per_page")), Is.All.EqualTo("200"));
            Assert.That(requests.Select(request => string.Join(",", request.Query.Keys.Order(StringComparer.Ordinal))), Is.All.EqualTo("page,per_page"));
            Assert.That(items.GetArrayLength(), Is.EqualTo(5));
            Assert.That(Unchanged(items, bodies), Is.True, result.ToString());
        });
    }

    // The body carries every CustomerModel field, so one the CLI drops or renames shows.
    [Test]
    public async Task Partner_customer_show_prints_the_customer_as_the_api_returns_it()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "show", "--org-id", TestData.CustomerOrgId);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.CustomerPath(TestData.CustomerOrgId)));
            Assert.That(Unchanged(result.StdoutJson, ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName)), Is.True, result.ToString());
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // The customer named is second in the list, so a lookup that took the first shows Initech.
    [TestCase(TestData.CustomerName)]
    [TestCase("GLOBEX LTD")]
    [TestCase("globex ltd")]
    public async Task A_customer_name_is_matched_whole_and_ignoring_case(string name)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "show", name);

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.PartnerPath("customers")}", $"GET {TestData.CustomerPath(TestData.CustomerOrgId)}" }));
    }

    // A part of a name is no match ("Names and IDs").
    [TestCase("Globex")]
    [TestCase("Globex Ltd.")]
    [TestCase("Umbrella Corp")]
    public async Task A_customer_name_that_matches_no_customer_exits_2_with_no_candidates(string name)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "show", name);

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").EnumerateArray(), Is.Empty);
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.PartnerPath("customers")}" }));
        });
    }

    // The candidates let the caller choose by --org-id without another call ("Errors and exit
    // codes"). The customer that does not match is not one.
    [Test]
    public async Task A_customer_name_that_matches_several_customers_exits_2_with_them_as_candidates()
    {
        using var run = PartnerApiFake.StartWithPartnerOnly();
        var twin = TestData.Ids("customer", 1)[0];
        run.StubPages(
            TestData.PartnerPath("customers"),
            200,
            ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName),
            ApiJson.Customer(TestData.NewCustomerOrgId, TestData.NewCustomerName),
            ApiJson.Customer(twin, "GLOBEX LTD"));

        var result = await run.RunAsync("partner", "customer", "convert", TestData.CustomerName, "--billing-months", "12");

        var error = CliAssert.Failed(result, "invalid_argument");
        var candidates = JsonAssert.Property(error, "candidates");
        Assert.Multiple(() =>
        {
            Assert.That(candidates.EnumerateArray().Select(candidate => JsonRead.GuidOf(JsonAssert.Property(candidate, "id"))), Is.EquivalentTo(new Guid?[] { Guid.Parse(TestData.CustomerOrgId, CultureInfo.InvariantCulture), Guid.Parse(twin, CultureInfo.InvariantCulture) }));
            Assert.That(JsonRead.StringFieldList(candidates, "name"), Is.EqualTo($"{TestData.CustomerName},GLOBEX LTD"));
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.PartnerPath("customers")}" }));
        });
    }

    // The customer named is the last of five, on the third page the fake serves.
    [Test]
    public async Task The_customer_lookup_reads_every_page_of_the_customer_list()
    {
        using var run = PartnerApiFake.StartWithPartnerOnly();
        var others = TestData.Ids("customer", 4).Select((id, i) => ApiJson.Customer(id, $"Customer {i}")).ToArray();
        run.StubPages(TestData.PartnerPath("customers"), 2, [.. others, ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName)]);
        run.Stub("GET", TestData.CustomerPath(TestData.CustomerOrgId), json: ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName));

        var result = await run.RunAsync("partner", "customer", "show", TestData.CustomerName);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(TestData.PartnerPath("customers")), Is.EqualTo("0,1,2"));
            Assert.That(run.Calls().Last(), Is.EqualTo($"GET {TestData.CustomerPath(TestData.CustomerOrgId)}"));
        });
    }

    // The values for flags left out are the create table's ("Command options"). Raw JSON is
    // compared so null is told apart from an empty string.
    [Test]
    public async Task Partner_customer_create_sends_every_field_with_the_portal_values_for_the_flags_left_out()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "create", TestData.NewCustomerName);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.PartnerPath("customers")));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(CreateFields), request.ToString());
            Assert.That(Raw(body, "name"), Is.EqualTo(Json("Initech")));
            Assert.That(Raw(body, "initialSystemsCount"), Is.EqualTo(Json(50)));
            Assert.That(Raw(body, "initialGatewaysCount"), Is.EqualTo(Json(0)));
            Assert.That(Raw(body, "industryDiscount"), Is.EqualTo(Json(false)));
            Assert.That(Raw(body, "hardLimit"), Is.EqualTo(Json(false)));
            Assert.That(Raw(body, "adminAutoSyncIsEnabled"), Is.EqualTo(Json(false)));
            Assert.That(Raw(body, "ownerEmail"), Is.EqualTo(Json<string?>(null)));
            Assert.That(Raw(body, "domain"), Is.EqualTo(Json<string?>(null)));
            Assert.That(Raw(body, "contactName"), Is.EqualTo(Json<string?>(null)));
            Assert.That(Unchanged(result.StdoutJson, ApiJson.Customer(TestData.NewCustomerOrgId, TestData.NewCustomerName)), Is.True, result.ToString());
        });
    }

    // 2 systems and 0 gateways are the smallest counts the API accepts (portal
    // CustomerCreateModelValidator).
    [TestCase("--owner", "it@initech.example", "ownerEmail", "it@initech.example")]
    [TestCase("--domain", "initech.example", "domain", "initech.example")]
    [TestCase("--contact", "Peter Gibbons", "contactName", "Peter Gibbons")]
    [TestCase("--systems", "2", "initialSystemsCount", 2)]
    [TestCase("--gateways", "3", "initialGatewaysCount", 3)]
    [TestCase("--industry-discount", null, "industryDiscount", true)]
    [TestCase("--hard-limit", null, "hardLimit", true)]
    [TestCase("--auto-sync", null, "adminAutoSyncIsEnabled", true)]
    public async Task Partner_customer_create_sends_each_flag_as_its_field(string option, string? value, string field, object expected)
    {
        using var run = PartnerApiFake.StartWithPartner();
        string[] flag = value is null ? [option] : [option, value];

        var request = await CliAssert.AcceptedAsync(run, "POST", TestData.PartnerPath("customers"), ["partner", "customer", "create", TestData.NewCustomerName, .. flag]);

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(CreateFields), request.ToString());
            Assert.That(Raw(body, field), Is.EqualTo(Json(expected)));
            Assert.That(Raw(body, "name"), Is.EqualTo(Json("Initech")));
        });
    }

    // Example 85.
    [Test]
    public async Task Partner_customer_create_example_sends_the_owner_domain_counts_and_auto_sync()
    {
        using var run = PartnerApiFake.StartWithPartner();
        string[] args = ["partner", "customer", "create", "Initech", "--owner", "it@initech.example", "--domain", "initech.example", "--systems", "20", "--gateways", "1", "--auto-sync"];

        var request = await CliAssert.AcceptedAsync(run, "POST", TestData.PartnerPath("customers"), args);

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(Raw(body, "name"), Is.EqualTo(Json("Initech")));
            Assert.That(Raw(body, "ownerEmail"), Is.EqualTo(Json("it@initech.example")));
            Assert.That(Raw(body, "domain"), Is.EqualTo(Json("initech.example")));
            Assert.That(Raw(body, "initialSystemsCount"), Is.EqualTo(Json(20)));
            Assert.That(Raw(body, "initialGatewaysCount"), Is.EqualTo(Json(1)));
            Assert.That(Raw(body, "adminAutoSyncIsEnabled"), Is.EqualTo(Json(true)));
            Assert.That(Raw(body, "industryDiscount"), Is.EqualTo(Json(false)));
            Assert.That(Raw(body, "hardLimit"), Is.EqualTo(Json(false)));
            Assert.That(Raw(body, "contactName"), Is.EqualTo(Json<string?>(null)));
        });
    }

    // Enclave.Sdk.Api keys a PATCH body by the model's property names (PatchClient.Set) and the API
    // reads them ignoring case, so names are compared ignoring case.
    [TestCase("--name", "Globex Limited", "Name", "Globex Limited")]
    [TestCase("--contact", "Jo Bloggs", "ContactName", "Jo Bloggs")]
    [TestCase("--systems", "2", "LicensedAgentsCount", 2)]
    [TestCase("--gateways", "0", "LicensedGatewaysCount", 0)]
    [TestCase("--industry-discount", null, "IndustryDiscount", true)]
    [TestCase("--no-industry-discount", null, "IndustryDiscount", false)]
    [TestCase("--hard-limit", null, "EnableHardLimit", true)]
    [TestCase("--no-hard-limit", null, "EnableHardLimit", false)]
    public async Task Partner_customer_update_patches_only_the_field_each_flag_sets(string option, string? value, string field, object expected)
    {
        using var run = PartnerApiFake.StartWithPartner();
        string[] flag = value is null ? [option] : [option, value];

        var result = await run.RunAsync(["partner", "customer", "update", "--org-id", TestData.CustomerOrgId, .. flag]);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.CustomerPath(TestData.CustomerOrgId)));
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo(field).IgnoreCase);
            Assert.That(Raw(body, field), Is.EqualTo(Json(expected)));
            Assert.That(Unchanged(result.StdoutJson, ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName)), Is.True, result.ToString());
        });
    }

    [Test]
    public async Task Partner_customer_update_sends_every_flag_given_in_one_patch()
    {
        using var run = PartnerApiFake.StartWithPartner();
        string[] fields = ["Name", "ContactName", "LicensedAgentsCount", "LicensedGatewaysCount", "IndustryDiscount", "EnableHardLimit"];
        string[] args = ["partner", "customer", "update", "--org-id", TestData.CustomerOrgId, "--name", "Globex Limited", "--contact", "Jo Bloggs", "--systems", "30", "--gateways", "2", "--no-industry-discount", "--hard-limit"];

        var request = await CliAssert.AcceptedAsync(run, "PATCH", TestData.CustomerPath(TestData.CustomerOrgId), args);

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields).IgnoreCase);
            Assert.That(Raw(body, "Name"), Is.EqualTo(Json("Globex Limited")));
            Assert.That(Raw(body, "ContactName"), Is.EqualTo(Json("Jo Bloggs")));
            Assert.That(Raw(body, "LicensedAgentsCount"), Is.EqualTo(Json(30)));
            Assert.That(Raw(body, "LicensedGatewaysCount"), Is.EqualTo(Json(2)));
            Assert.That(Raw(body, "IndustryDiscount"), Is.EqualTo(Json(false)));
            Assert.That(Raw(body, "EnableHardLimit"), Is.EqualTo(Json(true)));
        });
    }

    // Example 86. The cases are the periods the API accepts (portal ConvertCustomerModelValidator).
    [TestCase("1")]
    [TestCase("12")]
    [TestCase("24")]
    [TestCase("36")]
    public async Task Partner_customer_convert_puts_the_billing_period_and_prints_the_customer(string months)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "convert", "--org-id", TestData.CustomerOrgId, "--billing-months", months);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(TestData.CustomerPath(TestData.CustomerOrgId, "convert")));
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("billingPeriodMonths"));
            Assert.That(Raw(body, "billingPeriodMonths"), Is.EqualTo(months));
            Assert.That(Unchanged(result.StdoutJson, ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName)), Is.True, result.ToString());
        });
    }

    // The API returns the owners and admins in one body, with no pages (Enclave.Sdk.Api
    // CustomersClient.GetAdminsAsync).
    [Test]
    public async Task Partner_customer_list_admins_prints_the_admins_as_an_admin_list()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "list-admins", "--org-id", TestData.CustomerOrgId);

        var items = CliAssert.List(result, "admin");
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.CustomerPath(TestData.CustomerOrgId, "admins")}" }));
            Assert.That(Unchanged(items, [PartnerApiFake.OwnerBody, PartnerApiFake.AdminBody]), Is.True, result.ToString());
        });
    }

    // Example 65. The account goes in the route, with no body (portal
    // CustomersController.AddCustomerAdmins).
    [Test]
    public async Task Partner_customer_add_admin_puts_the_account_and_prints_the_admin()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "add-admin", "--org-id", TestData.CustomerOrgId, "--user-id", TestData.PartnerStaffAccountId);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.PartnerStaffAccountId)));
            Assert.That(request.Body, Is.Null.Or.Empty);
            Assert.That(Unchanged(result.StdoutJson, PartnerApiFake.StaffBody), Is.True, result.ToString());
        });
    }

    // Example 34. The match ignores case ("Names and IDs"). The admin comes after the owner, so a
    // lookup that took the first admin removes the owner.
    [TestCase(TestData.CustomerAdminEmail)]
    [TestCase("ALEX@Example.COM")]
    public async Task Partner_customer_remove_admin_looks_the_email_up_in_the_customer_admins(string email)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "remove-admin", "--org-id", TestData.CustomerOrgId, "--user", email);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[]
            {
                $"GET {TestData.CustomerPath(TestData.CustomerOrgId, "admins")}",
                $"DELETE {TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.CustomerAdminAccountId)}",
            }));
            Assert.That(Unchanged(result.StdoutJson, PartnerApiFake.AdminBody), Is.True, result.ToString());
        });
    }

    [Test]
    public async Task Partner_customer_remove_admin_with_user_id_removes_the_account_without_a_lookup()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "remove-admin", "--org-id", TestData.CustomerOrgId, "--user-id", TestData.CustomerAdminAccountId);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"DELETE {TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.CustomerAdminAccountId)}" }));
            Assert.That(Unchanged(result.StdoutJson, PartnerApiFake.AdminBody), Is.True, result.ToString());
        });
    }

    [Test]
    public async Task Partner_customer_remove_admin_given_an_email_no_admin_has_exits_2_with_no_candidates()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "remove-admin", "--org-id", TestData.CustomerOrgId, "--user", "nobody@example.com");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").EnumerateArray(), Is.Empty);
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.CustomerPath(TestData.CustomerOrgId, "admins")}" }));
        });
    }

    // The API returns the pending invites in one body, with no pages (Enclave.Sdk.Api
    // CustomersClient.GetPendingInvitesAsync).
    [Test]
    public async Task Partner_customer_list_invites_prints_the_pending_invites_as_an_invite_list()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "list-invites", "--org-id", TestData.CustomerOrgId);

        var items = CliAssert.List(result, "invite");
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.CustomerPath(TestData.CustomerOrgId, "invites")}" }));
            Assert.That(Unchanged(items, [PartnerApiFake.OtherInviteBody, PartnerApiFake.InviteBody]), Is.True, result.ToString());
        });
    }

    // Example 66. The body is CreateCustomerAdminInviteModel.
    [Test]
    public async Task Partner_customer_invite_posts_the_email_address_and_prints_the_invite()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "invite", "--org-id", TestData.CustomerOrgId, "--email", TestData.CustomerInviteEmail);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.CustomerPath(TestData.CustomerOrgId, "invites")));
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("emailAddress"));
            Assert.That(Raw(body, "emailAddress"), Is.EqualTo(Json(TestData.CustomerInviteEmail)));
            Assert.That(Unchanged(result.StdoutJson, PartnerApiFake.NewInviteBody), Is.True, result.ToString());
        });
    }

    // The partner API cancels an invite by its ID ("Partner API"). Sam's invite comes second, so a
    // lookup that took the first cancels Kim's. The API answers with a null ID, and Enclave.Sdk.Api
    // fills in the ID it sent (CustomersClient.CancelInviteAsync).
    [TestCase(TestData.CustomerInviteEmail)]
    [TestCase("SAM@GLOBEX.example")]
    public async Task Partner_customer_cancel_invite_looks_the_email_up_and_deletes_that_invite(string email)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "cancel-invite", "--org-id", TestData.CustomerOrgId, "--email", email);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[]
            {
                $"GET {TestData.CustomerPath(TestData.CustomerOrgId, "invites")}",
                $"DELETE {PartnerApiFake.InvitePath(TestData.CustomerOrgId, TestData.CustomerInviteNumber)}",
            }));
            Assert.That(Unchanged(result.StdoutJson, PartnerApiFake.InviteBody), Is.True, result.ToString());
        });
    }

    [Test]
    public async Task Partner_customer_cancel_invite_for_an_address_with_no_pending_invite_exits_2_with_no_candidates()
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", "cancel-invite", "--org-id", TestData.CustomerOrgId, "--email", "nobody@example.com");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").EnumerateArray(), Is.Empty);
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.CustomerPath(TestData.CustomerOrgId, "invites")}" }));
        });
    }

    // Example 65. Each route takes no body (portal CustomersController.EnableAutoSync and
    // DisableAutoSync).
    [TestCase("enable-auto-sync")]
    [TestCase("disable-auto-sync")]
    public async Task Partner_customer_auto_sync_commands_put_to_their_own_route_and_print_the_customer(string verb)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync("partner", "customer", verb, "--org-id", TestData.CustomerOrgId);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(TestData.CustomerPath(TestData.CustomerOrgId, verb)));
            Assert.That(request.Body, Is.Null.Or.Empty);
            Assert.That(Unchanged(result.StdoutJson, ApiJson.Customer(TestData.CustomerOrgId, TestData.CustomerName)), Is.True, result.ToString());
        });
    }

    // For a customer the partner does not have, the auto-sync routes answer 204 with no body where
    // the other routes answer 404 (portal CustomersController.EnableAutoSync; Enclave.Sdk.Api
    // CustomersClient.SetAutoSyncAsync).
    [TestCase("enable-auto-sync")]
    [TestCase("disable-auto-sync")]
    public async Task Partner_customer_auto_sync_for_a_customer_the_partner_does_not_have_exits_5(string verb)
    {
        ArgumentNullException.ThrowIfNull(verb);
        using var run = PartnerApiFake.StartWithPartnerOnly();
        var path = TestData.CustomerPath(TestData.CustomerOrgId, verb);
        run.Stub("PUT", path, 204);

        var result = await run.RunAsync("partner", "customer", verb, "--org-id", TestData.CustomerOrgId);

        CliAssert.Failed(result, "not_found");
        Assert.That(run.Calls(), Is.EqualTo(new[] { $"PUT {path}" }));
    }

    // A command that acts on a customer is a single-ID command ("Several IDs"). Its first call
    // fails, and nothing follows.
    [TestCaseSource(nameof(CommandsForAnUnknownCustomer))]
    public async Task A_partner_customer_command_exits_5_when_the_partner_api_does_not_know_the_customer(string[] args, string firstCall)
    {
        ArgumentNullException.ThrowIfNull(firstCall);
        using var run = PartnerApiFake.StartWithPartnerOnly();
        var call = firstCall.Split(' ');
        run.StubProblem(call[0], call[1], 404, "Not Found", "Customer cannot be found.");

        var result = await run.RunAsync(args);

        CliAssert.Failed(result, "not_found");
        Assert.That(run.Calls(), Is.EqualTo(new[] { firstCall }));
    }

    // spec/cli-surface.md "Examples" 20, 34, 65, 66, 85 and 86, written as the spec writes them.
    [TestCaseSource(nameof(Examples))]
    public async Task The_partner_examples_in_the_specification_send_their_calls(string[] args, string lastCall)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls().LastOrDefault(), Is.EqualTo(lastCall));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    [TestCase("partner", "customers", "show", TestData.CustomerName)]
    [TestCase("partner", "customers", "add-admin", TestData.CustomerName, "--user-id", TestData.PartnerStaffAccountId)]
    public async Task Plural_nouns_run_the_singular_partner_customer_command(params string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var singularRun = PartnerApiFake.StartWithPartner();
        using var pluralRun = PartnerApiFake.StartWithPartner();
        string[] singular = ["partner", "customer", .. args[2..]];

        var singularResult = await singularRun.RunAsync(singular);
        var pluralResult = await pluralRun.RunAsync(args);

        CliAssert.Succeeded(singularResult);
        Assert.Multiple(() =>
        {
            Assert.That(singularRun.Calls(), Is.Not.Empty);
            Assert.That(pluralRun.Calls(), Is.EqualTo(singularRun.Calls()));
            Assert.That(pluralResult.ExitCode, Is.EqualTo(singularResult.ExitCode));
            Assert.That(pluralRun.Comparable(pluralResult.Stdout), Is.EqualTo(singularRun.Comparable(singularResult.Stdout)));
        });
    }

    // A CLI that answered any words after "partner" the same way would pass the test above.
    [Test]
    public async Task A_plural_noun_applies_the_argument_checks_of_the_singular_command()
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync("partner", "customers", "convert", TestData.CustomerName, "--billing-months", "6"));

        CliAssert.Succeeded(await run.RunAsync("partner", "customers", "convert", TestData.CustomerName, "--billing-months", "12"));
        Assert.That(run.Calls().Last(), Is.EqualTo($"PUT {TestData.CustomerPath(TestData.CustomerOrgId, "convert")}"));
    }

    [TestCase("partner", "customerz", "list")]
    [TestCase("partner", "customer", "lst")]
    public async Task An_unknown_partner_noun_or_verb_exits_2_invalid_argument(params string[] args)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
    }

    // Personal access tokens cannot carry the scopes for the partner's own properties, users and
    // invites, and countries, referlink and oldest-version serve the partner portal's screens
    // (spec/cli-surface.md "Left out", "Partner API"). A noun's parts take hyphenated verbs, so
    // there are no sub-nouns ("Shape and naming").
    [TestCase("partner", "list")]
    [TestCase("partner", "show")]
    [TestCase("partner", "update", "--name", "Acme Partners")]
    [TestCase("partner", "list-users")]
    [TestCase("partner", "remove-user", "--user-id", TestData.PartnerStaffAccountId)]
    [TestCase("partner", "list-invites")]
    [TestCase("partner", "invite", "--email", TestData.CustomerInviteEmail)]
    [TestCase("partner", "cancel-invite", "--email", TestData.CustomerInviteEmail)]
    [TestCase("partner", "user", "list")]
    [TestCase("partner", "invite", "list")]
    [TestCase("partner", "countries")]
    [TestCase("partner", "referlink")]
    [TestCase("partner", "customer", "oldest-version", TestData.CustomerName)]
    [TestCase("partner", "customer", "admin", "list", TestData.CustomerName)]
    [TestCase("partner", "customer", "admin", "add", TestData.CustomerName, TestData.PartnerStaffAccountId)]
    [TestCase("partner", "customer", "invite", "list", TestData.CustomerName)]
    [TestCase("partner", "customer", "invite", "send", TestData.CustomerName, TestData.CustomerInviteEmail)]
    [TestCase("partner", "customer", "auto-sync", "enable", TestData.CustomerName)]
    public async Task Partner_commands_the_specification_leaves_out_exit_2_invalid_argument(params string[] args)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result);
    }

    [TestCaseSource(nameof(BadArguments))]
    public async Task A_bad_argument_exits_2_invalid_argument_before_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    // The run has no token, so a CLI that checked the token first exits 3, and the customer is given
    // by name, so a CLI that looked it up first shows the call ("Errors and exit codes").
    [TestCase("remove-admin", "--user", "")]
    [TestCase("remove-admin", "--user", "   ")]
    [TestCase("invite", "--email", "")]
    [TestCase("invite", "--email", " \t ")]
    [TestCase("cancel-invite", "--email", "")]
    [TestCase("cancel-invite", "--email", "  ")]
    public async Task A_blank_email_address_exits_2_invalid_argument_before_the_token_and_any_call(string verb, string option, string value)
    {
        using var run = PartnerApiFake.StartWithPartner();
        run.Environment.Remove("ENCLAVE_TOKEN");

        var result = await run.RunAsync(ForCustomer(verb, option, value));

        CliAssert.Rejected(run, result);
        CliAssert.Problems(result.Error, option);
    }

    // A blank name would cost the customer lookup and match nothing, or reach the API, which refuses
    // it (portal CustomerCreateModelValidator and CustomerPatchModelValidator, NotEmpty). The run
    // has no token, so a CLI that checked the token first exits 3.
    [TestCase("customer", "show", "")]
    [TestCase("customer", "show", "   ")]
    [TestCase("customer", "convert", " \t ", "--billing-months", "12")]
    [TestCase("name", "create", "")]
    [TestCase("name", "create", "  ")]
    [TestCase("--name", "update", "--org-id", TestData.CustomerOrgId, "--name", "")]
    [TestCase("--name", "update", "--org-id", TestData.CustomerOrgId, "--name", " ")]
    public async Task A_blank_customer_name_exits_2_invalid_argument_before_the_token_and_any_call(string key, string verb, params string[] rest)
    {
        using var run = PartnerApiFake.StartWithPartner();
        run.Environment.Remove("ENCLAVE_TOKEN");

        var result = await run.RunAsync(Line(verb, rest));

        CliAssert.Rejected(run, result);
        CliAssert.Problems(result.Error, key);
    }

    // Sent as given, a blank owner is refused (portal CustomerCreateModelValidator, EmailAddress;
    // FluentValidation 11.9.0 AspNetCoreCompatibleEmailValidator) and a blank domain or contact is
    // stored (CreateCustomerHandler), where a flag left out sends null. A CLI that sent the value
    // makes the call in the run with a token; one that checked the token first exits 3 without.
    [TestCase("--owner", "")]
    [TestCase("--owner", "   ")]
    [TestCase("--domain", "")]
    [TestCase("--domain", " \t ")]
    [TestCase("--contact", "")]
    [TestCase("--contact", "  ")]
    public async Task A_blank_owner_domain_or_contact_on_create_exits_2_invalid_argument_before_the_token_and_any_call(string option, string value)
    {
        using var withToken = PartnerApiFake.StartWithPartner();
        using var withoutToken = PartnerApiFake.StartWithPartner();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");
        var args = Line("create", TestData.NewCustomerName, option, value);

        var first = await withToken.RunAsync(args);
        var second = await withoutToken.RunAsync(args);

        CliAssert.Rejected(withToken, first);
        CliAssert.Rejected(withoutToken, second);
        Assert.Multiple(() =>
        {
            CliAssert.Problems(first.Error, option);
            Assert.That(second.Error.GetRawText(), Is.EqualTo(first.Error.GetRawText()));
        });
    }

    // CliRun sets ENCLAVE_ORG_ID, which must not stand in for the customer ("Context").
    [TestCaseSource(nameof(CommandsWithoutTheCustomer))]
    public async Task A_customer_command_without_a_customer_exits_2_invalid_argument(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    // The command cannot tell which customer is meant ("Details").
    [TestCaseSource(nameof(CommandsWithNameAndOrgId))]
    public async Task A_customer_name_with_org_id_exits_2_invalid_argument(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    [TestCaseSource(nameof(ReadsWithDryRun))]
    public async Task Dry_run_is_an_unknown_option_on_a_partner_customer_command_that_only_reads(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    internal static IEnumerable<TestCaseData> CommandsByName() =>
        Commands.Select(command => Case(command.ByName()));

    // The cast selects the TestCaseData(object) constructor, which passes the array as the test's one
    // argument; the params object[] constructor would take each string as a separate argument.
    internal static TestCaseData Case(string[] args) =>
        new TestCaseData((object)args).SetArgDisplayNames(Display(args));

    internal static TestCaseData Pair(string[] rejected, string[] corrected) =>
        new TestCaseData(rejected, corrected).SetArgDisplayNames(Display(rejected));

    internal static string[] Line(string verb, params string[] rest) => ["partner", "customer", verb, .. rest];

    internal static string[] ForCustomer(string verb, params string[] rest) => ["partner", "customer", verb, TestData.CustomerName, .. rest];

    private static IEnumerable<TestCaseData> CommandsByNameWithCalls() =>
        Commands.Select(command => new TestCaseData(command.ByName(), command.Calls(byName: true)).SetArgDisplayNames(Display(command.ByName())));

    private static IEnumerable<TestCaseData> CommandsByOrgIdWithCalls() =>
        Commands.Where(command => command.TakesCustomer)
            .Select(command => new TestCaseData(command.ByOrgId(TestData.CustomerOrgId), command.Calls(byName: false)).SetArgDisplayNames(Display(command.ByOrgId(TestData.CustomerOrgId))));

    // The first call for a customer given by --org-id is the admins or invites read where the
    // command looks one up.
    private static IEnumerable<TestCaseData> CommandsForAnUnknownCustomer() =>
        Commands.Where(command => command.TakesCustomer)
            .Select(command => new TestCaseData(command.ByOrgId(TestData.CustomerOrgId), command.Calls(byName: false)[0]).SetArgDisplayNames(Display(command.ByOrgId(TestData.CustomerOrgId))));

    private static IEnumerable<TestCaseData> Examples()
    {
        yield return Example(["partner", "customer", "list"], "GET", TestData.PartnerPath("customers"));
        yield return Example(["partner", "customer", "remove-admin", "Globex Ltd", "--user", "alex@example.com"], "DELETE", TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.CustomerAdminAccountId));
        yield return Example(["partner", "customer", "add-admin", "Globex Ltd", "--user-id", "5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25"], "PUT", TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.PartnerStaffAccountId));
        yield return Example(["partner", "customer", "enable-auto-sync", "Globex Ltd"], "PUT", TestData.CustomerPath(TestData.CustomerOrgId, "enable-auto-sync"));
        yield return Example(["partner", "customer", "add-admin", "--org-id", "6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b", "--user-id", "5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25"], "PUT", TestData.CustomerAdminPath(TestData.CustomerOrgId, TestData.PartnerStaffAccountId));
        yield return Example(["partner", "customer", "enable-auto-sync", "--org-id", "6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b"], "PUT", TestData.CustomerPath(TestData.CustomerOrgId, "enable-auto-sync"));
        yield return Example(["partner", "customer", "invite", "Globex Ltd", "--email", "sam@globex.example"], "POST", TestData.CustomerPath(TestData.CustomerOrgId, "invites"));
        yield return Example(["partner", "customer", "create", "Initech", "--owner", "it@initech.example", "--domain", "initech.example", "--systems", "20", "--gateways", "1", "--auto-sync"], "POST", TestData.PartnerPath("customers"));
        yield return Example(["partner", "customer", "convert", "Initech", "--billing-months", "12"], "PUT", TestData.CustomerPath(TestData.NewCustomerOrgId, "convert"));
        yield return Example(["partner", "customer", "convert", "--org-id", "8d2e4f6a-1b3c-4d5e-9f70-a1b2c3d4e5f6", "--billing-months", "12"], "PUT", TestData.CustomerPath(TestData.NewCustomerOrgId, "convert"));
    }

    private static TestCaseData Example(string[] args, string method, string path) =>
        new TestCaseData(args, $"{method} {path}").SetArgDisplayNames(Display(args));

    private static IEnumerable<TestCaseData> CommandsWithoutTheCustomer() =>
        Commands.Where(command => command.TakesCustomer).Select(command => Pair(command.WithoutCustomer(), command.ByName()));

    private static IEnumerable<TestCaseData> CommandsWithNameAndOrgId() =>
        Commands.Where(command => command.TakesCustomer).Select(command => Pair([.. command.ByName(), "--org-id", TestData.CustomerOrgId], command.ByName()));

    private static IEnumerable<TestCaseData> ReadsWithDryRun() =>
        Commands.Where(command => !command.Changes).Select(command => Pair([.. command.ByName(), "--dry-run"], command.ByName()));

    private static IEnumerable<TestCaseData> BadArguments()
    {
        // convert requires --billing-months, so the command states the period it commits to.
        yield return Pair(ForCustomer("convert"), ForCustomer("convert", "--billing-months", "12"));

        foreach (var months in InvalidBillingMonths)
        {
            yield return Pair(ForCustomer("convert", "--billing-months", months), ForCustomer("convert", "--billing-months", "12"));
        }

        // --systems is at least 2, and counts are whole numbers ("Command options").
        yield return Pair(Line("create"), Line("create", TestData.NewCustomerName));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "1"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "0"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "two"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--gateways", "one"), Line("create", TestData.NewCustomerName, "--gateways", "1"));

        // Auto-sync has its own routes and the owner is set at create only, so neither is an update
        // flag.
        yield return Pair(ForCustomer("update", "--systems", "1"), ForCustomer("update", "--systems", "2"));
        yield return Pair(ForCustomer("update", "--systems", "30", "--auto-sync"), ForCustomer("update", "--systems", "30"));
        yield return Pair(ForCustomer("update", "--systems", "30", "--owner", "it@initech.example"), ForCustomer("update", "--systems", "30"));

        // No change, and options that contradict each other ("Details").
        yield return Pair(ForCustomer("update"), ForCustomer("update", "--systems", "30"));
        yield return Pair(ForCustomer("update", "--industry-discount", "--no-industry-discount"), ForCustomer("update", "--industry-discount"));
        yield return Pair(ForCustomer("update", "--hard-limit", "--no-hard-limit"), ForCustomer("update", "--hard-limit"));
        yield return Pair(ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail, "--user-id", TestData.PartnerStaffAccountId), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));

        // add-admin takes --user-id only: looking up a partner user needs a scope personal access
        // tokens cannot carry ("Names and IDs").
        yield return Pair(ForCustomer("add-admin", "--user", TestData.CustomerAdminEmail), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return Pair(ForCustomer("add-admin"), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));

        yield return Pair(ForCustomer("remove-admin"), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
        yield return Pair(ForCustomer("invite"), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
        yield return Pair(ForCustomer("cancel-invite"), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));

        foreach (var blank in new[] { string.Empty, " " })
        {
            yield return Pair(ForCustomer("remove-admin", "--user", blank), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
            yield return Pair(ForCustomer("invite", "--email", blank), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
            yield return Pair(ForCustomer("cancel-invite", "--email", blank), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));
            yield return Pair(Line("create", TestData.NewCustomerName, "--owner", blank), Line("create", TestData.NewCustomerName, "--owner", "it@initech.example"));
            yield return Pair(Line("create", TestData.NewCustomerName, "--domain", blank), Line("create", TestData.NewCustomerName, "--domain", "initech.example"));
            yield return Pair(Line("create", TestData.NewCustomerName, "--contact", blank), Line("create", TestData.NewCustomerName, "--contact", "Peter Gibbons"));
        }

        // Anything besides the customer is a named option ("Commands").
        yield return Pair(ForCustomer("add-admin", TestData.PartnerStaffAccountId), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return Pair(ForCustomer("remove-admin", TestData.CustomerAdminEmail), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
        yield return Pair(ForCustomer("invite", TestData.CustomerInviteEmail), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
        yield return Pair(ForCustomer("cancel-invite", TestData.CustomerInviteEmail), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));

        // Options the CLI does not have (AGENTS.md "CLI contract"); --limit is on log only, and the
        // list takes no filters.
        yield return Pair(ForCustomer("convert", "--billing-months", "12", "--yes"), ForCustomer("convert", "--billing-months", "12"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--from-file", "customer.json"), Line("create", TestData.NewCustomerName));
        yield return Pair(Line("create", TestData.NewCustomerName, "--template"), Line("create", TestData.NewCustomerName));
        yield return Pair(Line("list", "-o", "json"), Line("list"));
        yield return Pair(Line("list", "--all"), Line("list"));
        yield return Pair(Line("list", "--limit", "10"), Line("list"));
        yield return Pair(Line("list", "--search", "Globex"), Line("list"));
        yield return Pair(Line("list", "--token", "abc123"), Line("list"));

        yield return Pair(ForCustomer("show", "--no-such-option"), ForCustomer("show"));
    }

    // Compared as values, so indentation does not matter and a field the model drops or retypes
    // shows.
    private static bool Unchanged(JsonElement printed, string body) =>
        JsonElement.DeepEquals(JsonRead.WithoutTrace(printed), JsonRead.Parse(body));

    private static bool Unchanged(JsonElement printedItems, string[] bodies) =>
        printedItems.ValueKind == JsonValueKind.Array
        && printedItems.GetArrayLength() == bodies.Length
        && printedItems.EnumerateArray().Zip(bodies).All(pair => Unchanged(pair.First, pair.Second));

    private static string Raw(JsonElement body, string field) => JsonAssert.Property(body, field).GetRawText();

    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    // Test names show each case as the command line it runs, with placeholders for the fixed IDs.
    private static string Display(string[] args) => string.Join(' ', args.Select(DisplayArg));

    private static string DisplayArg(string arg) => arg switch
    {
        TestData.CustomerOrgId => "<orgId>",
        TestData.PartnerStaffAccountId => "<accountId>",
        _ when arg == TestData.PartnerId.ToString() => "<partnerId>",
        _ when arg.Contains(' ', StringComparison.Ordinal) => $"'{arg}'",
        _ => arg,
    };

    // One partner customer command. LookupPath is the admins or invites read made before its call.
    internal sealed record CustomerCommand(string Verb, bool TakesCustomer, bool Changes, IReadOnlyList<string> Options, string Method, string Path, string? LookupPath = null)
    {
        public string[] ByName() => TakesCustomer ? Build(TestData.CustomerName) : Build();

        public string[] ByOrgId(string orgId) => Build("--org-id", orgId);

        public string[] WithoutCustomer() => Build();

        // The calls the command makes, as "METHOD path".
        public string[] Calls(bool byName)
        {
            var calls = new List<string>();

            if (byName && TakesCustomer)
            {
                calls.Add($"GET {TestData.PartnerPath("customers")}");
            }

            if (LookupPath is not null)
            {
                calls.Add($"GET {LookupPath}");
            }

            calls.Add($"{Method} {Path}");
            return [.. calls];
        }

        private string[] Build(params string[] customer) => ["partner", "customer", Verb, .. customer, .. Options];
    }
}
