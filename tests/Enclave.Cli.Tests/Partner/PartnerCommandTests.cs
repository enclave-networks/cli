using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// Partner customer commands call the partner API through Enclave.Sdk.Api 1.1.0's partner client
// (IPartnerClient.Customers), one call per command. A customer given by name, an admin given by
// email address and an invite given by email address each add one read: the customer list, the
// customer's admins and the customer's invites (proposed-cli-surface.md "Calls per command", "Names
// and IDs"). CliRun serves the partner API from a fake at its own address, and each test checks the
// requests the fakes received as well as what the command printed.
//
// Every test here chooses a valid partner through ENCLAVE_PARTNER_ID, so a bad argument is the only
// error a case can have.
//
// A rejection alone does not show that a command checked its input, since an unknown command is
// rejected the same way. Each argument case therefore runs the corrected command in the same
// sandbox and expects it to succeed with its calls sent to the partner API, which only a command
// that exists and accepted its arguments does (the reason CliAssert.RejectedThenAcceptedAsync gives
// for the main API's commands).
//
// This class also holds the table of partner customer commands, which PartnerContextTests and
// PartnerSafetyTests draw on, so the three fixtures cannot drift apart on which commands exist or
// what they send. The fake partner API they run against is Support/PartnerApiFake.cs.
public class PartnerCommandTests
{
    private const string FileToken = "file-token-4c7d21";

    private static readonly string[] InvalidBillingMonths = ["0", "2", "6", "13", "48", "twelve"];

    // Every field of the API's CustomerCreateModel (Enclave.Sdk.Api 1.1.0, which writes it in
    // camelCase; the partner API schema). A create sends each one ("Command options").
    private static readonly string[] CreateFields =
    [
        "name", "ownerEmail", "domain", "industryDiscount", "initialSystemsCount", "initialGatewaysCount", "contactName", "hardLimit", "adminAutoSyncIsEnabled",
    ];

    // One valid command line for each partner customer verb in the commands tree
    // (proposed-cli-surface.md "Commands"), the customer given by name, with the call it makes for
    // the test customer and the read it makes first, if any. Changes marks the verbs that change
    // something through the API, which take --dry-run ("Options on every command"). list-invites
    // only reads, although the API asks for WriteCustomers to read invites (portal
    // CustomersController.cs, GET {customerId}/invites). The routes and methods are the partner
    // API's (https://partner-api.enclave.io/swagger/v1/swagger.json).
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

    // A customer given by name is looked up in the partner's customer list, and an admin or invite
    // given by email address in the customer's admins or invites, then the command makes its call
    // ("Names and IDs", "Calls per command").
    [TestCaseSource(nameof(CommandsByNameWithCalls))]
    public async Task Every_partner_customer_command_given_the_customer_by_name_looks_it_up_then_makes_its_call(string[] args, string[] calls)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(calls), result.ToString());
    }

    // A customer is given by its name or by --org-id, its organisation ID, which is the only ID a
    // customer has and needs no lookup (proposed-cli-surface.md "Commands", "Names and IDs"; portal
    // CustomersController.cs:69 reads the route's customer ID as an OrganisationGuid).
    [TestCaseSource(nameof(CommandsByOrgIdWithCalls))]
    public async Task Every_partner_customer_command_given_org_id_makes_its_call_without_a_customer_lookup(string[] args, string[] calls)
    {
        using var run = PartnerApiFake.StartWithPartner();

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(calls), result.ToString());
    }

    // The partner API runs on its own host, beside the main API (proposed-cli-surface.md "Partner
    // API"), and partner calls go to the partner API's address: the host's default here, which is
    // the run's fake partner API. The main API's fake answers partner routes 421, so a call sent
    // there would fail the command as well as show in its requests.
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

    // credentials.json's partnerApiBaseUrl is the partner API address, read beside baseUrl, and
    // ENCLAVE_TOKEN keeps it as it keeps baseUrl (proposed-cli-surface.md "Partner API", "Login,
    // logout and status"). A third fake at another address shows where the command's call went with
    // which token. The run's own fakes, at the default addresses, serve the partner API too, so a
    // CLI that ignored the file would succeed there and show only in their requests.
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

    // Example 20. A list reads every page, 200 items at a time, the most the API returns, and
    // prints once all are read (proposed-cli-surface.md "Output", "Calls per command"). The fake
    // serves two customers to a page, so five customers take three pages, and a CLI that stopped
    // after the first page would print two. The list takes no filter options, so it sends nothing
    // else.
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

    // Partner customer commands print the Enclave.Sdk.Api model unchanged, so field names match the
    // API (AGENTS.md "CLI contract"); the body carries every CustomerModel field.
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

    // Names match whole, ignoring case ("Names and IDs"). The customer named is the second in the
    // list, so a lookup that took the first customer shows Initech.
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

    // A name that matches no customer exits 2 invalid_argument with an empty candidates list, and a
    // part of a name is no match ("Names and IDs", "Errors and exit codes"). Only the lookup is sent.
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

    // A name that matches several customers exits 2 with the matches as { id, name }, so the caller
    // can choose one by --org-id without another call ("Errors and exit codes"). The customer that
    // does not match is not a candidate.
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

    // The lookup reads the whole customer list, every page of it ("Names and IDs"). The customer
    // named is the last of five, on the third page the fake serves.
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

    // A create sends every field of the API's CustomerCreateModel, with the partner portal's values
    // for the flags left out: 50 systems, 0 gateways, and no discount request, hard limit or
    // auto-sync; the owner, domain and contact go as null (proposed-cli-surface.md "Command
    // options", the create table). The body holds exactly the model's fields, so a field left out
    // of the body fails the check as well as a field the model does not have, and null is told
    // apart from an empty text.
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
            Assert.That(Raw(body, "name"), Is.EqualTo("\"Initech\""));
            Assert.That(Raw(body, "initialSystemsCount"), Is.EqualTo("50"));
            Assert.That(Raw(body, "initialGatewaysCount"), Is.EqualTo("0"));
            Assert.That(Raw(body, "industryDiscount"), Is.EqualTo("false"));
            Assert.That(Raw(body, "hardLimit"), Is.EqualTo("false"));
            Assert.That(Raw(body, "adminAutoSyncIsEnabled"), Is.EqualTo("false"));
            Assert.That(Raw(body, "ownerEmail"), Is.EqualTo("null"));
            Assert.That(Raw(body, "domain"), Is.EqualTo("null"));
            Assert.That(Raw(body, "contactName"), Is.EqualTo("null"));
            Assert.That(Unchanged(result.StdoutJson, ApiJson.Customer(TestData.NewCustomerOrgId, TestData.NewCustomerName)), Is.True, result.ToString());
        });
    }

    // Each create flag of proposed-cli-surface.md "Command options" is given alone, and sets its
    // field of the create table. 2 systems and 0 gateways are the smallest counts the API accepts
    // (portal CustomerCreateModelValidator.cs).
    [TestCase("--owner", "it@initech.example", "ownerEmail", "\"it@initech.example\"")]
    [TestCase("--domain", "initech.example", "domain", "\"initech.example\"")]
    [TestCase("--contact", "Peter Gibbons", "contactName", "\"Peter Gibbons\"")]
    [TestCase("--systems", "2", "initialSystemsCount", "2")]
    [TestCase("--gateways", "3", "initialGatewaysCount", "3")]
    [TestCase("--industry-discount", null, "industryDiscount", "true")]
    [TestCase("--hard-limit", null, "hardLimit", "true")]
    [TestCase("--auto-sync", null, "adminAutoSyncIsEnabled", "true")]
    public async Task Partner_customer_create_sends_each_flag_as_its_field(string option, string? value, string field, string json)
    {
        using var run = PartnerApiFake.StartWithPartner();
        string[] flag = value is null ? [option] : [option, value];

        var request = await CliAssert.AcceptedAsync(run, "POST", TestData.PartnerPath("customers"), ["partner", "customer", "create", TestData.NewCustomerName, .. flag]);

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(CreateFields), request.ToString());
            Assert.That(Raw(body, field), Is.EqualTo(json));
            Assert.That(Raw(body, "name"), Is.EqualTo("\"Initech\""));
        });
    }

    // Example 85: a customer for about 20 systems and a gateway, owned by its IT lead, with the
    // partner's staff kept as admins.
    [Test]
    public async Task Partner_customer_create_example_sends_the_owner_domain_counts_and_auto_sync()
    {
        using var run = PartnerApiFake.StartWithPartner();
        string[] args = ["partner", "customer", "create", "Initech", "--owner", "it@initech.example", "--domain", "initech.example", "--systems", "20", "--gateways", "1", "--auto-sync"];

        var request = await CliAssert.AcceptedAsync(run, "POST", TestData.PartnerPath("customers"), args);

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(Raw(body, "name"), Is.EqualTo("\"Initech\""));
            Assert.That(Raw(body, "ownerEmail"), Is.EqualTo("\"it@initech.example\""));
            Assert.That(Raw(body, "domain"), Is.EqualTo("\"initech.example\""));
            Assert.That(Raw(body, "initialSystemsCount"), Is.EqualTo("20"));
            Assert.That(Raw(body, "initialGatewaysCount"), Is.EqualTo("1"));
            Assert.That(Raw(body, "adminAutoSyncIsEnabled"), Is.EqualTo("true"));
            Assert.That(Raw(body, "industryDiscount"), Is.EqualTo("false"));
            Assert.That(Raw(body, "hardLimit"), Is.EqualTo("false"));
            Assert.That(Raw(body, "contactName"), Is.EqualTo("null"));
        });
    }

    // An update sends only the fields given (proposed-cli-surface.md "Create and update"). --systems
    // sets LicensedAgentsCount and --gateways LicensedGatewaysCount (portal
    // CustomerPatchModelValidator.cs:14-16), and each --no- flag sends false. Enclave.Sdk.Api keys a
    // PATCH body by the model's property names (PatchClient.Set, 1.1.0), and the API reads names
    // ignoring case, so the names are compared ignoring case.
    [TestCase("--name", "Globex Limited", "Name", "\"Globex Limited\"")]
    [TestCase("--contact", "Jo Bloggs", "ContactName", "\"Jo Bloggs\"")]
    [TestCase("--systems", "2", "LicensedAgentsCount", "2")]
    [TestCase("--gateways", "0", "LicensedGatewaysCount", "0")]
    [TestCase("--industry-discount", null, "IndustryDiscount", "true")]
    [TestCase("--no-industry-discount", null, "IndustryDiscount", "false")]
    [TestCase("--hard-limit", null, "EnableHardLimit", "true")]
    [TestCase("--no-hard-limit", null, "EnableHardLimit", "false")]
    public async Task Partner_customer_update_patches_only_the_field_each_flag_sets(string option, string? value, string field, string json)
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
            Assert.That(Raw(body, field), Is.EqualTo(json));
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
            Assert.That(Raw(body, "Name"), Is.EqualTo("\"Globex Limited\""));
            Assert.That(Raw(body, "ContactName"), Is.EqualTo("\"Jo Bloggs\""));
            Assert.That(Raw(body, "LicensedAgentsCount"), Is.EqualTo("30"));
            Assert.That(Raw(body, "LicensedGatewaysCount"), Is.EqualTo("2"));
            Assert.That(Raw(body, "IndustryDiscount"), Is.EqualTo("false"));
            Assert.That(Raw(body, "EnableHardLimit"), Is.EqualTo("true"));
        });
    }

    // Example 86. convert puts the billing period it is given, one of the periods the API accepts
    // (proposed-cli-surface.md "Command options"; portal ConvertCustomerModelValidator.cs:13-17), as
    // ConvertCustomerModel.BillingPeriodMonths.
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

    // The API returns the owners and admins in one body, with no pages (CustomersClient.GetAdminsAsync,
    // Enclave.Sdk.Api 1.1.0), and the CLI prints them as an admin list ("Output").
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

    // Example 65. add-admin takes the partner user's account ID (proposed-cli-surface.md "Names and
    // IDs") and puts it in the route, with no body (portal CustomersController.cs,
    // AddCustomerAdmins). It prints the admin the API returns.
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

    // Example 34. remove-admin takes the admin's email address, looked up in the customer's admins,
    // matching whole and ignoring case ("Names and IDs"). The admin is the second the lookup reads,
    // after the owner, so a lookup that took the first removes the owner.
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

    // --user-id gives the account ID and makes no lookup ("Names and IDs").
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

    // An email address no admin has matches nothing, which exits 2 invalid_argument with no
    // candidates ("Errors and exit codes"), and nothing is removed.
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

    // The API returns the pending invites in one body, with no pages
    // (CustomersClient.GetPendingInvitesAsync, Enclave.Sdk.Api 1.1.0), and the CLI prints them as an
    // invite list ("Output").
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

    // Example 66. The API takes the invite as { emailAddress } (CreateCustomerAdminInviteModel), and
    // the CLI prints the invite it returns.
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
            Assert.That(Raw(body, "emailAddress"), Is.EqualTo($"\"{TestData.CustomerInviteEmail}\""));
            Assert.That(Unchanged(result.StdoutJson, PartnerApiFake.NewInviteBody), Is.True, result.ToString());
        });
    }

    // The partner API cancels an invite by its ID, so the CLI looks the email address up in the
    // customer's invites, matching whole and ignoring case (proposed-cli-surface.md "Partner API",
    // "Names and IDs"). Sam's invite is the second the lookup reads, so a lookup that took the first
    // cancels Kim's. The API answers with the invite's email address and a null ID, and
    // Enclave.Sdk.Api 1.1.0 gives the cancelled invite the ID it sent (CustomersClient.CancelInviteAsync).
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

    // An address with no pending invite matches nothing, which exits 2 invalid_argument with no
    // candidates, and nothing is cancelled.
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

    // Example 65. Auto-sync changes through a route of its own for each direction, with no body
    // (proposed-cli-surface.md "Command options"; portal CustomersController.cs, EnableAutoSync and
    // DisableAutoSync), and the CLI prints the customer the API returns.
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

    // Partner customer commands exit 5 for an unknown customer ("Several IDs"). For a customer the
    // partner does not have, the auto-sync routes answer 204 with no body (portal
    // CustomersController.cs, EnableAutoSync and DisableAutoSync; CustomersClient.SetAutoSyncAsync,
    // Enclave.Sdk.Api 1.1.0), where the other routes answer 404.
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

    // Every partner customer command that acts on a customer is a single-ID command, which exits 5
    // when the API answers 404 for its customer ("Several IDs"). The customer's first call is the
    // one that fails, and nothing follows it.
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

    // The partner examples of proposed-cli-surface.md ("Examples" 20, 34, 65, 66, 85 and 86), each
    // command written as the specification writes it, with the call it ends with.
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

    // Nouns are singular, and a plural name is a hidden alias for one (proposed-cli-surface.md
    // "Shape and naming"), so an agent that guesses the plural reaches the same command: the same
    // calls and the same output.
    [TestCase("partners", "customer", "list")]
    [TestCase("partner", "customers", "list")]
    [TestCase("partners", "customers", "list")]
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
            Assert.That(pluralResult.Stdout, Is.EqualTo(singularResult.Stdout));
        });
    }

    // A CLI that answered any words after "partner" the same way would pass the test above. The alias
    // runs the singular command, so it applies that command's argument checks as well.
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

    // The partner's own properties, users and invites, and listing partners, need scopes a personal
    // access token cannot carry, and the partner API's countries, referlink and customer
    // oldest-version routes serve the partner portal's screens; the CLI has no commands for them
    // (proposed-cli-surface.md "Left out", "Partner API"). A noun's own parts take hyphenated verbs
    // in place of another level ("Shape and naming"), so the customer admin, invite and auto-sync
    // sub-nouns do not exist either. Each of these is an unknown command.
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

    // An empty or blank address names no admin and no invite, so --user and --email given one exit 2
    // invalid_argument with the other argument checks, keyed by the option, before the token is
    // read and before any call, the customer lookup included ("Errors and exit codes": arguments,
    // then the token, then the call). The run has no token, so a CLI that checked the token first
    // would exit 3; the customer is given by name, so a CLI that looked it up first would show the
    // call.
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
        Assert.That(JsonRead.PropertyNameList(JsonAssert.Property(result.Error, "errors")), Is.EqualTo(option), result.ToString());
    }

    // An empty or blank name names no customer. Given for the customer a command acts on, it would
    // cost the customer lookup and match nothing; given to create or to update --name, it would
    // reach the API, which refuses a blank name (portal
    // Enclave.Partner.Api/Modules/CustomerManagement/Customers/Validators/CustomerCreateModelValidator.cs:19
    // and CustomerPatchModelValidator.cs:10, NotEmpty, which FluentValidation fails for a string
    // that is empty or white space). Each exits 2 invalid_argument with the other argument checks,
    // keyed by the argument or option, before the token is read and before any call ("Errors and
    // exit codes": arguments, then the token, then the call). The run has no token, so a CLI that
    // checked the token first would exit 3.
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
        Assert.That(JsonRead.PropertyNameList(JsonAssert.Property(result.Error, "errors")), Is.EqualTo(key), result.ToString());
    }

    // A command that acts on a customer needs one, by name or by --org-id (proposed-cli-surface.md
    // "Commands"). CliRun sets ENCLAVE_ORG_ID, which chooses the organisation that organisation
    // commands act within; partner commands use the partner ("Context"), so it does not stand in
    // for the customer.
    [TestCaseSource(nameof(CommandsWithoutTheCustomer))]
    public async Task A_customer_command_without_a_customer_exits_2_invalid_argument(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    // A name with its ID option contradict each other and exit 2 (proposed-cli-surface.md
    // "Details"): the command cannot tell which customer is meant.
    [TestCaseSource(nameof(CommandsWithNameAndOrgId))]
    public async Task A_customer_name_with_org_id_exits_2_invalid_argument(string[] rejected, string[] corrected)
    {
        using var run = PartnerApiFake.StartWithPartner();

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    // --dry-run applies to commands that change something through the API, and an option a command
    // does not take is unknown to it and exits 2 (proposed-cli-surface.md "Options on every
    // command", "Details").
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

    // The first call each command makes for a customer given by --org-id: the admins or invites
    // read for a command that looks one up, and the command's own call for the rest.
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
        // convert requires --billing-months, one of the periods the API accepts, so the command
        // states the billing period it commits to (proposed-cli-surface.md "Command options").
        yield return Pair(ForCustomer("convert"), ForCustomer("convert", "--billing-months", "12"));

        foreach (var months in InvalidBillingMonths)
        {
            yield return Pair(ForCustomer("convert", "--billing-months", months), ForCustomer("convert", "--billing-months", "12"));
        }

        // create requires the new customer's name, and --systems is at least 2 (the create table of
        // "Command options"). Counts are whole numbers.
        yield return Pair(Line("create"), Line("create", TestData.NewCustomerName));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "1"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "0"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--systems", "two"), Line("create", TestData.NewCustomerName, "--systems", "2"));
        yield return Pair(Line("create", TestData.NewCustomerName, "--gateways", "one"), Line("create", TestData.NewCustomerName, "--gateways", "1"));

        // update --systems is at least 2 ("Command options"). Auto-sync changes only through
        // enable-auto-sync and disable-auto-sync, which have their own API routes, and the owner is
        // set at create only, so neither is an update flag.
        yield return Pair(ForCustomer("update", "--systems", "1"), ForCustomer("update", "--systems", "2"));
        yield return Pair(ForCustomer("update", "--systems", "30", "--auto-sync"), ForCustomer("update", "--systems", "30"));
        yield return Pair(ForCustomer("update", "--systems", "30", "--owner", "it@initech.example"), ForCustomer("update", "--systems", "30"));

        // An update with no change flag, and flags that contradict each other, exit 2 ("Details").
        yield return Pair(ForCustomer("update"), ForCustomer("update", "--systems", "30"));
        yield return Pair(ForCustomer("update", "--industry-discount", "--no-industry-discount"), ForCustomer("update", "--industry-discount"));
        yield return Pair(ForCustomer("update", "--hard-limit", "--no-hard-limit"), ForCustomer("update", "--hard-limit"));
        yield return Pair(ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail, "--user-id", TestData.PartnerStaffAccountId), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));

        // add-admin takes the account by --user-id only: the account is a partner user, and listing
        // partner users needs a scope personal access tokens cannot carry ("Names and IDs").
        yield return Pair(ForCustomer("add-admin", "--user", TestData.CustomerAdminEmail), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return Pair(ForCustomer("add-admin"), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));

        // The admin to remove and the address to invite or cancel are part of each command
        // ("Commands"); without them the command has nothing to act on.
        yield return Pair(ForCustomer("remove-admin"), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
        yield return Pair(ForCustomer("invite"), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
        yield return Pair(ForCustomer("cancel-invite"), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));

        // An empty or blank address names nobody, so it is no address at all.
        foreach (var blank in new[] { string.Empty, " " })
        {
            yield return Pair(ForCustomer("remove-admin", "--user", blank), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
            yield return Pair(ForCustomer("invite", "--email", blank), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
            yield return Pair(ForCustomer("cancel-invite", "--email", blank), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));
        }

        // Anything a partner customer command needs besides the customer is a named option
        // ("Commands"), so a second positional argument is an error.
        yield return Pair(ForCustomer("add-admin", TestData.PartnerStaffAccountId), ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return Pair(ForCustomer("remove-admin", TestData.CustomerAdminEmail), ForCustomer("remove-admin", "--user", TestData.CustomerAdminEmail));
        yield return Pair(ForCustomer("invite", TestData.CustomerInviteEmail), ForCustomer("invite", "--email", TestData.CustomerInviteEmail));
        yield return Pair(ForCustomer("cancel-invite", TestData.CustomerInviteEmail), ForCustomer("cancel-invite", "--email", TestData.CustomerInviteEmail));

        // --yes, --from-file, --template, -o, --all and --token are removed ("Changes to AGENTS.md"),
        // --limit stays on log only, and partner customer list takes no filter options ("Command
        // options").
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

    // The JSON of a printed model and of the body the fake served are compared as values, so the
    // check holds whatever the indentation, and a field the model does not read, or reads into the
    // wrong type, shows as a difference.
    private static bool Unchanged(JsonElement printed, string body) =>
        JsonElement.DeepEquals(printed, JsonRead.Parse(body));

    private static bool Unchanged(JsonElement printedItems, string[] bodies) =>
        printedItems.ValueKind == JsonValueKind.Array
        && printedItems.GetArrayLength() == bodies.Length
        && printedItems.EnumerateArray().Zip(bodies).All(pair => Unchanged(pair.First, pair.Second));

    private static string Raw(JsonElement body, string field) => JsonAssert.Property(body, field).GetRawText();

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

    /// <summary>
    /// One partner customer command: its verb, whether it acts on a customer, whether it changes
    /// something, its options, and the call it makes for the test customer, with the admins or
    /// invites read it makes first when it looks one up.
    /// </summary>
    internal sealed record CustomerCommand(string Verb, bool TakesCustomer, bool Changes, IReadOnlyList<string> Options, string Method, string Path, string? LookupPath = null)
    {
        public string[] ByName() => TakesCustomer ? Build(TestData.CustomerName) : Build();

        public string[] ByOrgId(string orgId) => Build("--org-id", orgId);

        public string[] WithoutCustomer() => Build();

        /// <summary>
        /// The calls the command makes, as "METHOD path": the customer list first when the customer
        /// is given by name, then the lookup read, then its own call.
        /// </summary>
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
