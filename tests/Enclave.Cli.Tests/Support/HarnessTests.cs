using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Enclave.Api.Modules.AccountManagement.PublicAccount.Models;
using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Api.Scaffolding.Pagination.Models;
using Enclave.Cli.Context;
using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api;
using Enclave.Sdk.Api.Clients.Interfaces;
using Enclave.Sdk.Api.Data;
using Enclave.Sdk.Api.Exceptions;
using NUnit.Framework;
using NUnit.Framework.Internal;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Support;

// The tests in this project trust three things this fixture proves: that every ApiJson body is JSON
// Enclave.Sdk.Api 1.1.0 reads into the model it names, field for field, that a CliRun confines the
// CLI to its own environment, home directory and fake APIs, and that the stubs, the request records
// and the CliAssert checks behave as their documentation says. A check that passed everything would
// let every test that relies on it pass, so each check is shown failing on the outputs it rejects.
public class HarnessTests
{
    private const string SystemId = "ABCDE";

    private const int ResourceId = 7;

    // The options Enclave.Sdk.Api 1.1.0 reads and writes JSON with (Constants.JsonSerializerOptions,
    // which is internal to the package): the gateway priority converter goes ahead of the enum
    // converter, since System.Text.Json uses the first converter that can convert a type.
    private static readonly JsonSerializerOptions SdkJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new GatewayPriorityTypeJsonConverter(), new JsonStringEnumConverter() },
    };

    // The options EnclaveClient.ReadCredentialsFile reads credentials.json with (Enclave.Sdk.Api 1.1.0).
    private static readonly JsonSerializerOptions CredentialsJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<TestCaseData> ResourceBodies()
    {
        yield return Resource(
            nameof(ApiJson.System),
            ApiJson.System(SystemId, "db"),
            "systems",
            async org => await FirstAsync((await org.EnrolledSystems.GetSystemsAsync()).Items),
            $"systems/{SystemId}",
            async org => await org.EnrolledSystems.GetAsync(SystemId));

        yield return Resource(
            nameof(ApiJson.PendingSystem),
            ApiJson.PendingSystem(SystemId, "db"),
            "unapproved-systems",
            async org => await FirstAsync((await org.UnapprovedSystems.GetSystemsAsync()).Items),
            $"unapproved-systems/{SystemId}",
            async org => await org.UnapprovedSystems.GetAsync(SystemId));

        yield return Resource(
            nameof(ApiJson.Key),
            ApiJson.Key(ResourceId, "laptops", "KEY-123"),
            "enrolment-keys",
            async org => await FirstAsync((await org.EnrolmentKeys.GetEnrolmentKeysAsync()).Items),
            $"enrolment-keys/{ResourceId}",
            async org => await org.EnrolmentKeys.GetAsync(EnrolmentKeyId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Policy),
            ApiJson.Policy(ResourceId, "admins"),
            "policies",
            async org => await FirstAsync((await org.Policies.GetPoliciesAsync()).Items),
            $"policies/{ResourceId}",
            async org => await org.Policies.GetAsync(PolicyId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Tag),
            ApiJson.Tag("servers"),
            "tags",
            async org => await FirstAsync((await org.Tags.GetAsync()).Items),
            "tags/servers",
            async org => await org.Tags.GetAsync("servers"));

        yield return Resource(
            nameof(ApiJson.Zone),
            ApiJson.Zone(ResourceId, "corp"),
            "dns/zones",
            async org => await FirstAsync((await org.Dns.GetZonesAsync()).Items),
            $"dns/zones/{ResourceId}",
            async org => await org.Dns.GetZoneAsync(DnsZoneId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Record),
            ApiJson.Record(ResourceId, "db"),
            "dns/records",
            async org => await FirstAsync((await org.Dns.GetRecordsAsync()).Items),
            $"dns/records/{ResourceId}",
            async org => await org.Dns.GetRecordAsync(DnsRecordId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Trust),
            ApiJson.Trust(ResourceId, "staff"),
            "trust-requirements",
            async org => await FirstAsync((await org.TrustRequirements.GetTrustRequirementsAsync()).Items),
            $"trust-requirements/{ResourceId}",
            async org => await org.TrustRequirements.GetAsync(TrustRequirementId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Log),
            ApiJson.Log("System ABCDE enrolled"),
            "logs",
            async org => await FirstAsync((await org.Logs.GetLogsAsync()).Items),
            getPath: null,
            readOne: null);
    }

    public static IEnumerable<TestCaseData> BulkCalls()
    {
        yield return Bulk("systemsRevoked", "DELETE", "systems", org => org.EnrolledSystems.RevokeSystemsAsync(SystemId));
        yield return Bulk("systemsUpdated", "PUT", "systems/enable", org => org.EnrolledSystems.BulkEnableAsync(SystemId));
        yield return Bulk("systemsUpdated", "PUT", "systems/disable", org => org.EnrolledSystems.BulkDisableAsync(SystemId));
        yield return Bulk("systemsApproved", "PUT", "unapproved-systems/approve", org => org.UnapprovedSystems.ApproveSystemsAsync(SystemId));
        yield return Bulk("systemsDeclined", "DELETE", "unapproved-systems", org => org.UnapprovedSystems.DeclineSystems(SystemId));
        yield return Bulk("keysModified", "PUT", "enrolment-keys/enable", org => org.EnrolmentKeys.BulkEnableAsync(EnrolmentKeyId.FromInt(ResourceId)));
        yield return Bulk("keysModified", "PUT", "enrolment-keys/disable", org => org.EnrolmentKeys.BulkDisableAsync(EnrolmentKeyId.FromInt(ResourceId)));
        yield return Bulk("policiesDeleted", "DELETE", "policies", org => org.Policies.DeletePoliciesAsync(PolicyId.FromInt(ResourceId)));
        yield return Bulk("policiesUpdated", "PUT", "policies/enable", org => org.Policies.EnablePoliciesAsync(PolicyId.FromInt(ResourceId)));
        yield return Bulk("policiesUpdated", "PUT", "policies/disable", org => org.Policies.DisablePoliciesAsync(PolicyId.FromInt(ResourceId)));
        yield return Bulk("tagsDeleted", "DELETE", "tags", org => org.Tags.DeleteTagsAsync("servers"));
        yield return Bulk("dnsRecordsDeleted", "DELETE", "dns/records", org => org.Dns.DeleteRecordsAsync(DnsRecordId.FromInt(ResourceId)));
        yield return Bulk("requirementsDeleted", "DELETE", "trust-requirements", org => org.TrustRequirements.DeleteTrustRequirementsAsync(TrustRequirementId.FromInt(ResourceId)));
    }

    public static IEnumerable<TestCaseData> ErrorCodes() =>
        CliAssert.ExitCodes.Select(pair => new TestCaseData(pair.Key, pair.Value).SetArgDisplayNames(pair.Key));

    // The ID property of each model a list prints: SystemSummaryModel and
    // UnapprovedSystemSummaryModel.SystemId, the integer Id of keys, policies, zones, DNS records and
    // trust requirements, TagSummaryModel.Tag, AccountOrganisationModel.OrgId, OrganisationUser.Id,
    // OrganisationInviteModel.EmailAddress, and in the portal's partner API CustomerModel.Id and
    // CustomerUserModel.Id.
    public static IEnumerable<TestCaseData> ListKindsWithIds()
    {
        yield return IdCase("system", "systemId", JsonValueKind.String);
        yield return IdCase("pending-system", "systemId", JsonValueKind.String);
        yield return IdCase("key", "id", JsonValueKind.Number);
        yield return IdCase("policy", "id", JsonValueKind.Number);
        yield return IdCase("tag", "tag", JsonValueKind.String);
        yield return IdCase("zone", "id", JsonValueKind.Number);
        yield return IdCase("hostname", "id", JsonValueKind.Number);
        yield return IdCase("trust", "id", JsonValueKind.Number);
        yield return IdCase("org", "orgId", JsonValueKind.String);
        yield return IdCase("user", "id", JsonValueKind.String);
        yield return IdCase("invite", "emailAddress", JsonValueKind.String);
        yield return IdCase("customer", "id", JsonValueKind.String);
        yield return IdCase("admin", "id", JsonValueKind.String);
    }

    public static IEnumerable<string> KindsWithIds() => CliList.Kinds.Where(kind => kind != "log");

    // Each resource body is served as a list item and as the single resource, and read through the
    // Enclave.Sdk.Api calls a command makes. Writing each model back and comparing it with the body proves every
    // property name and value type: a misspelt name leaves the model's property at its default,
    // which the comparison reports, and a wrong value type fails the read.
    [TestCaseSource(nameof(ResourceBodies))]
    public async Task Resource_body_reads_through_the_sdk_list_and_get_calls_and_writes_back_unchanged(
        string body,
        string listPath,
        Func<IOrganisationClient, Task<object>> readItem,
        string? getPath,
        Func<IOrganisationClient, Task<object>>? readOne)
    {
        ArgumentNullException.ThrowIfNull(listPath);
        ArgumentNullException.ThrowIfNull(readItem);

        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath(listPath), json: ApiJson.Page(body));

        if (getPath is not null)
        {
            run.Stub("GET", TestData.OrgPath(getPath), json: body);
        }

        var org = ConnectOrganisation(run);

        var models = new List<object> { await readItem(org) };

        if (readOne is not null)
        {
            models.Add(await readOne(org));
        }

        await AssertBodyMatchesModelsAsync(body, models);
    }

    [Test]
    public async Task Page_body_reads_as_a_paginated_response_whose_metadata_follows_the_api()
    {
        using var run = CliRun.Start();
        var body = ApiJson.PageOf(5, ApiJson.System("A"), ApiJson.System("B"));
        run.Stub("GET", TestData.OrgPath("systems"), json: body);

        var page = await ConnectOrganisation(run).EnrolledSystems.GetSystemsAsync();

        var ids = new List<string>();
        await foreach (var item in page.Items)
        {
            ids.Add(item.SystemId);
        }

        var json = Parse(body);

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", ids), Is.EqualTo("A,B"));
            Assert.That(page.Metadata.Total, Is.EqualTo(5));
            Assert.That(page.Metadata.FirstPage, Is.Zero);
            Assert.That(page.Metadata.LastPage, Is.EqualTo(2));
            Assert.That(page.Metadata.NextPage, Is.EqualTo(1));
            Assert.That(page.Metadata.PrevPage, Is.Null);
            AssertSameJson(Write(page.Metadata), json.GetProperty("metadata"));
            AssertSameJson(Write(page.Links), json.GetProperty("links"));
        });
    }

    [Test]
    public async Task Single_page_body_has_a_total_equal_to_its_item_count_and_no_next_page()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page(ApiJson.System("A"), ApiJson.System("B")));

        var page = await ConnectOrganisation(run).EnrolledSystems.GetSystemsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.Metadata.Total, Is.EqualTo(2));
            Assert.That(page.Metadata.LastPage, Is.Zero);
            Assert.That(page.Metadata.NextPage, Is.Null);
            Assert.That(page.Links.Next, Is.Null);
        });
    }

    [Test]
    public async Task Orgs_body_reads_through_GetOrganisationsAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName));
        run.Stub("GET", "/account/orgs", json: body);

        var orgs = await Connect(run).GetOrganisationsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(orgs.Select(org => org.OrgId), Is.EqualTo(new[] { OrganisationGuid.FromGuid(TestData.OrgId), OrganisationGuid.FromGuid(TestData.OtherOrgId) }));
            Assert.That(orgs.Select(org => org.OrgName), Is.EqualTo(new[] { TestData.OrgName, TestData.OtherOrgName }));
            AssertSameJson(Write(orgs), Parse(body).GetProperty("orgs"));
        });
    }

    [Test]
    public async Task OrgProperties_body_reads_through_the_organisation_GetAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.OrgProperties(TestData.OrgName);
        run.Stub("GET", TestData.OrgPath(), json: body);

        var properties = await ConnectOrganisation(run).GetAsync();

        Assert.That(properties, Is.Not.Null);
        await AssertBodyMatchesModelsAsync(body, [properties!]);
    }

    [Test]
    public async Task Users_body_reads_through_GetOrganisationUsersAsync()
    {
        using var run = CliRun.Start();
        var userId = Guid.NewGuid();
        var body = ApiJson.Users((userId, "ops@acme.example"));
        run.Stub("GET", TestData.OrgPath("users"), json: body);

        var users = await ConnectOrganisation(run).GetOrganisationUsersAsync();

        Assert.Multiple(() =>
        {
            Assert.That(users.Single().Id, Is.EqualTo(AccountGuid.FromGuid(userId)));
            Assert.That(users.Single().EmailAddress, Is.EqualTo("ops@acme.example"));
            AssertSameJson(Write(users), Parse(body).GetProperty("users"));
        });
    }

    [Test]
    public async Task Invites_body_reads_through_GetPendingInvitesAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.Invites("new@acme.example", "other@acme.example");
        run.Stub("GET", TestData.OrgPath("invites"), json: body);

        var invites = await ConnectOrganisation(run).GetPendingInvitesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", invites.Select(invite => invite.EmailAddress)), Is.EqualTo("new@acme.example,other@acme.example"));
            AssertSameJson(Write(invites), Parse(body).GetProperty("invites"));
        });
    }

    [Test]
    public async Task DnsSummary_body_reads_through_GetPropertiesSummaryAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.DnsSummary();
        run.Stub("GET", TestData.OrgPath("dns"), json: body);

        var summary = await ConnectOrganisation(run).Dns.GetPropertiesSummaryAsync();

        await AssertBodyMatchesModelsAsync(body, [summary]);
    }

    [TestCaseSource(nameof(BulkCalls))]
    public async Task Bulk_body_reads_through_the_sdk_bulk_call(string field, string method, string path, Func<IOrganisationClient, Task<int>> call)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(call);

        using var run = CliRun.Start();
        run.Stub(method, TestData.OrgPath(path), json: ApiJson.Bulk(field, 3));

        var count = await call(ConnectOrganisation(run));

        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void StubProblem_reaches_the_sdk_as_problem_details()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", TestData.OrgPath($"systems/{SystemId}"), 404, "Not Found", "No such system.");

        var ex = Assert.ThrowsAsync<EnclaveApiException>(() => ConnectOrganisation(run).EnrolledSystems.GetAsync(SystemId));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ProblemDetails.Status, Is.EqualTo(404));
            Assert.That(ex.ProblemDetails.Title, Is.EqualTo("Not Found"));
            Assert.That(ex.ProblemDetails.Detail, Is.EqualTo("No such system."));
        });
    }

    [Test]
    public async Task Requests_record_method_path_query_body_and_authorization_in_order()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("DELETE", TestData.OrgPath("systems"), json: ApiJson.Bulk("systemsRevoked", 2));
        var org = ConnectOrganisation(run);

        await org.EnrolledSystems.GetSystemsAsync(searchTerm: "a,b c", pageNumber: 2);
        await org.EnrolledSystems.RevokeSystemsAsync("A", "B");

        var requests = run.Requests;

        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].Method, Is.EqualTo("GET"));
            Assert.That(requests[0].Path, Is.EqualTo(TestData.OrgPath("systems")));
            Assert.That(requests[0].Query["search"], Is.EqualTo("a,b c"));
            Assert.That(requests[0].Query["page"], Is.EqualTo("2"));
            Assert.That(requests[0].Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(requests[1].Method, Is.EqualTo("DELETE"));
            Assert.That(string.Join(",", JsonAssert.Strings(JsonAssert.Property(requests[1].BodyJson, "systemIds"))), Is.EqualTo("A,B"));
        });
    }

    [Test]
    public void SingleRequest_fails_the_test_unless_exactly_one_request_arrived()
    {
        using var run = CliRun.Start();

        Assert.Throws<AssertionException>(() => run.SingleRequest());
    }

    // A failed API call completes the CLI's task from inside the catch blocks of the async methods
    // that awaited it, and on .NET 10.0.0 and 10.0.1 a test resuming inline there loses the failures
    // Assert.Multiple throws (CliRun.RunAsync gives the details). The CLI's methods on the stack
    // where the test resumes show whether it resumed inside them. The run's outcome and its one
    // request prove it took the failed-call path. The checks are plain Assert.That calls, which
    // throw from the test's body and so are reported wherever the test resumes.
    [Test]
    public async Task RunAsync_resumes_the_test_outside_the_cli_call_stack_after_a_failed_api_call()
    {
        using var run = CliRun.Start();
        var systems = TestData.OrgPath("systems");
        run.StubProblem("GET", systems, 500, "Server error");

        var result = await run.RunAsync("system", "list");
        var cliMethods = new StackTrace().GetFrames()
            .Select(frame => frame.GetMethod()?.DeclaringType)
            .Where(type => type?.Assembly == typeof(Program).Assembly)
            .Select(type => type!.FullName)
            .ToArray();

        Assert.That(result.ExitCode, Is.EqualTo(CliAssert.ExitCodes["transient"]), result.ToString());
        Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {systems}" }));
        Assert.That(cliMethods, Is.Empty, "The test resumed inside the CLI's call stack.");
    }

    [Test]
    public async Task StubWithQuery_takes_precedence_over_Stub_for_the_same_path()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.PageOf(3, ApiJson.System("A")));
        run.StubWithQuery("GET", TestData.OrgPath("systems"), "page", "1", json: ApiJson.PageOf(3, ApiJson.System("B")));
        var systems = ConnectOrganisation(run).EnrolledSystems;

        var first = await FirstAsync((await systems.GetSystemsAsync(pageNumber: 0)).Items);
        var second = await FirstAsync((await systems.GetSystemsAsync(pageNumber: 1)).Items);

        Assert.Multiple(() =>
        {
            Assert.That(((SystemSummaryModel)first).SystemId, Is.EqualTo("A"));
            Assert.That(((SystemSummaryModel)second).SystemId, Is.EqualTo("B"));
        });
    }

    // The expected metadata is worked out by hand from PaginatedQueryHandler.GetResponseAsync in the
    // portal: lastPage = ceil(total / perPage) - 1, prevPage = page - 1 from page 1 on, and nextPage =
    // page + 1 before the last page. Five items two to a page give pages 0 to 2, and page 3 is the
    // page after the last. Four items two to a page fill pages 0 and 1 exactly.
    [TestCase(0, 2, 5, 2, null, 1, 2)]
    [TestCase(1, 2, 5, 2, 0, 2, 2)]
    [TestCase(2, 2, 5, 1, 1, null, 2)]
    [TestCase(3, 2, 5, 0, 2, null, 2)]
    [TestCase(1, 2, 4, 2, 0, null, 1)]
    [TestCase(0, 200, 0, 0, null, null, 0)]
    public async Task PageAt_body_reads_as_the_page_the_api_writes_for_that_request(
        int page, int perPage, int total, int itemCount, int? prevPage, int? nextPage, int lastPage)
    {
        using var run = CliRun.Start();
        var items = Enumerable.Range(0, itemCount).Select(index => ApiJson.System(Invariant($"P{index}"))).ToArray();
        var body = ApiJson.PageAt(page, perPage, total, items);
        run.Stub("GET", TestData.OrgPath("systems"), json: body);

        var response = await ConnectOrganisation(run).EnrolledSystems.GetSystemsAsync();
        var ids = await SystemIdsAsync(response);
        var json = Parse(body);

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.EqualTo(string.Join(",", Enumerable.Range(0, itemCount).Select(index => Invariant($"P{index}")))));
            Assert.That(response.Metadata.Total, Is.EqualTo(total));
            Assert.That(response.Metadata.FirstPage, Is.Zero);
            Assert.That(response.Metadata.PrevPage, Is.EqualTo(prevPage));
            Assert.That(response.Metadata.NextPage, Is.EqualTo(nextPage));
            Assert.That(response.Metadata.LastPage, Is.EqualTo(lastPage));
            AssertSameJson(Write(response.Metadata), json.GetProperty("metadata"));
            AssertSameJson(Write(response.Links), json.GetProperty("links"));
        });
    }

    // A page holding more items than its page size, or items past the total, is not a page the API
    // can send, so a test that asks for one is told at once.
    [Test]
    public void PageAt_rejects_a_page_the_api_cannot_send()
    {
        var item = ApiJson.System("A");

        Assert.Multiple(() =>
        {
            Assert.That(() => ApiJson.PageAt(0, 1, 2, item, item), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => ApiJson.PageAt(1, 2, 3, item, item), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => ApiJson.PageAt(-1, 2, 3), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    // The pages are asked for out of order, so a stub that served pages in the order requests
    // arrive, and not by page number, gives the wrong items. The request without a page parameter
    // is how Enclave.Sdk.Api asks when given no page number, which the API reads as page 0.
    [Test]
    public async Task StubPages_serves_each_page_to_the_request_for_its_page_number()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems");
        run.StubPages(path, 2, ApiJson.System("A"), ApiJson.System("B"), ApiJson.System("C"), ApiJson.System("D"), ApiJson.System("E"));
        var systems = ConnectOrganisation(run).EnrolledSystems;

        var third = await systems.GetSystemsAsync(pageNumber: 2);
        var first = await systems.GetSystemsAsync(pageNumber: 0);
        var second = await systems.GetSystemsAsync(pageNumber: 1);
        var unnumbered = await systems.GetSystemsAsync();
        var afterLast = await systems.GetSystemsAsync(pageNumber: 3);

        var pages = new[] { third, first, second, unnumbered, afterLast };
        var ids = new List<string>();
        foreach (var page in pages)
        {
            ids.Add(await SystemIdsAsync(page));
        }

        Assert.Multiple(() =>
        {
            Assert.That(string.Join("|", ids), Is.EqualTo("E|A,B|C,D|A,B|"));
            Assert.That(
                string.Join("|", pages.Select(page => Invariant($"prev={page.Metadata.PrevPage} next={page.Metadata.NextPage}"))),
                Is.EqualTo("prev=1 next=|prev= next=1|prev=0 next=2|prev= next=1|prev=2 next="));
            Assert.That(pages.Select(page => page.Metadata.Total), Is.All.EqualTo(5));
            Assert.That(pages.Select(page => page.Metadata.LastPage), Is.All.EqualTo(2));
            Assert.That(run.PagesRequested(path), Is.EqualTo("2,0,1,0,3"));
        });

        Assert.ThrowsAsync<HttpRequestException>(() => systems.GetSystemsAsync(pageNumber: 4));
    }

    // Reading as the CLI reads a list, from page 0 until nextPage is null, must give every item once,
    // in order, and stop at the last page. 400 items fill two pages exactly, which is where an
    // off-by-one in the last page would show.
    [TestCase(450, 200, "0,1,2")]
    [TestCase(400, 200, "0,1")]
    [TestCase(5, 2, "0,1,2")]
    [TestCase(1, 200, "0")]
    [TestCase(0, 200, "0")]
    public async Task StubPages_read_by_following_nextPage_give_every_item_once_in_order(int count, int pageSize, string pagesRead)
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems");
        var expected = TestData.Ids("system", count);
        run.StubPages(path, pageSize, expected.Select(id => ApiJson.System(id)).ToArray());
        var systems = ConnectOrganisation(run).EnrolledSystems;

        var read = new List<string>();
        int? page = 0;

        // The bound stops a stub whose nextPage never ends from hanging the test.
        for (var reads = 0; page is not null && reads < 10; reads++)
        {
            var response = await systems.GetSystemsAsync(pageNumber: page, perPage: 200);
            var ids = await SystemIdsAsync(response);
            read.AddRange(ids.Split(',', StringSplitOptions.RemoveEmptyEntries));
            Assert.That(response.Metadata.Total, Is.EqualTo(count));
            page = response.Metadata.NextPage;
        }

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", read), Is.EqualTo(string.Join(",", expected)));
            Assert.That(run.PagesRequested(path), Is.EqualTo(pagesRead));
        });
    }

    // A failed page read must reach the CLI as the API's error for that page alone, so a test can
    // prove the pages before it were read and nothing was printed.
    [Test]
    public async Task StubPageProblem_answers_its_page_with_problem_details_and_leaves_the_other_pages()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems");
        run.StubPages(path, 2, ApiJson.System("A"), ApiJson.System("B"), ApiJson.System("C"), ApiJson.System("D"), ApiJson.System("E"));
        run.StubPageProblem(path, 1, 503, "Service Unavailable", "Try again later.");
        var systems = ConnectOrganisation(run).EnrolledSystems;

        var first = await SystemIdsAsync(await systems.GetSystemsAsync(pageNumber: 0));
        var ex = Assert.ThrowsAsync<EnclaveApiException>(() => systems.GetSystemsAsync(pageNumber: 1));
        var third = await SystemIdsAsync(await systems.GetSystemsAsync(pageNumber: 2));

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("A,B"));
            Assert.That(ex!.ProblemDetails.Status, Is.EqualTo(503));
            Assert.That(ex.ProblemDetails.Title, Is.EqualTo("Service Unavailable"));
            Assert.That(ex.ProblemDetails.Detail, Is.EqualTo("Try again later."));
            Assert.That(third, Is.EqualTo("E"));
        });
    }

    [Test]
    public async Task StubPageProblem_for_page_0_answers_a_request_without_a_page_parameter()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems");
        run.StubPages(path, 1, ApiJson.System("A"), ApiJson.System("B"));
        run.StubPageProblem(path, 0, 500, "Internal Server Error");
        var systems = ConnectOrganisation(run).EnrolledSystems;

        var ex = Assert.ThrowsAsync<EnclaveApiException>(() => systems.GetSystemsAsync());
        var second = await SystemIdsAsync(await systems.GetSystemsAsync(pageNumber: 1));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ProblemDetails.Status, Is.EqualTo(500));
            Assert.That(second, Is.EqualTo("B"));
        });
    }

    // Different counts for each call show that the stub answers calls in order, which lets a test
    // prove the CLI adds up the counts of every call. The fourth call shows the last count repeats,
    // and a single count answers every call.
    [TestCase(new[] { 200, 150, 40 }, "200,150,40,40")]
    [TestCase(new[] { 200, 150 }, "200,150,150,150")]
    [TestCase(new[] { 5 }, "5,5,5,5")]
    public async Task StubBulk_answers_successive_calls_with_successive_counts_and_records_each_body(int[] affected, string expected)
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/disable");
        run.StubBulk("PUT", path, "systemsUpdated", affected);
        var systems = ConnectOrganisation(run).EnrolledSystems;

        var counts = new List<int>
        {
            await systems.BulkDisableAsync("A", "B", "C"),
            await systems.BulkDisableAsync("D", "E"),
            await systems.BulkDisableAsync("F"),
            await systems.BulkDisableAsync("G"),
        };

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", counts), Is.EqualTo(expected));
            Assert.That(string.Join("|", run.RequestsTo("PUT", path).Select(request => string.Join(",", request.BodyIds("systemIds")))), Is.EqualTo("A,B,C|D,E|F|G"));
            Assert.That(run.RequestsTo("GET", path), Is.Empty);
        });
    }

    // A bulk run that fails part way is served by a success and then an error. The error is problem
    // details, which Enclave.Sdk.Api turns into EnclaveApiException, as it does the API's errors.
    // Policy IDs are typed integer IDs, which Enclave.Sdk.Api writes as JSON numbers.
    [Test]
    public async Task StubSequence_answers_in_order_and_sends_an_error_status_as_problem_details()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies");
        run.StubSequence(
            "DELETE",
            path,
            (200, ApiJson.Bulk("policiesDeleted", 2)),
            (503, ApiJson.Problem(503, "Service Unavailable", "Try again later.")));
        var policies = ConnectOrganisation(run).Policies;

        var deleted = await policies.DeletePoliciesAsync(PolicyId.FromInt(7), PolicyId.FromInt(8));
        var failed = Assert.ThrowsAsync<EnclaveApiException>(() => policies.DeletePoliciesAsync(PolicyId.FromInt(9)));
        var repeated = Assert.ThrowsAsync<EnclaveApiException>(() => policies.DeletePoliciesAsync(PolicyId.FromInt(10)));

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.EqualTo(2));
            Assert.That(failed!.ProblemDetails.Status, Is.EqualTo(503));
            Assert.That(failed.ProblemDetails.Detail, Is.EqualTo("Try again later."));
            Assert.That(repeated!.ProblemDetails.Status, Is.EqualTo(503));
            Assert.That(string.Join("|", run.RequestsTo("DELETE", path).Select(request => string.Join(",", request.BodyIds("policyIds")))), Is.EqualTo("7,8|9|10"));
        });
    }

    // A proxy in front of the API answers with its own error page, which Enclave.Sdk.Api passes on as
    // HttpRequestException with the status (it reads problem details from application/problem+json
    // only, Handlers/ProblemDetailsHttpMessageHandler.cs:29, version 1.1.0).
    [Test]
    public void StubRaw_reaches_the_sdk_as_an_http_error_without_problem_details()
    {
        using var run = CliRun.Start();
        run.StubRaw("GET", TestData.OrgPath("systems"), 502, "text/html", "<html><body>Bad Gateway</body></html>");

        var ex = Assert.ThrowsAsync<HttpRequestException>(() => ConnectOrganisation(run).EnrolledSystems.GetSystemsAsync());

        Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));
    }

    [TestCase(null, 0)]
    [TestCase("0", 0)]
    [TestCase("3", 3)]
    public void PageNumber_reads_the_page_parameter_and_a_missing_one_as_page_0(string? page, int expected) =>
        Assert.That(RequestWithPage(page).PageNumber, Is.EqualTo(expected));

    [TestCase("x")]
    [TestCase("-1")]
    [TestCase("1.5")]
    [TestCase("")]
    public void PageNumber_fails_the_test_when_the_page_parameter_is_not_a_whole_number(string page) =>
        Assert.That(() => RequestWithPage(page).PageNumber, Throws.InstanceOf<AssertionException>());

    [Test]
    public void BodyIds_reads_string_and_integer_ids_as_text() =>
        Assert.That(string.Join(",", RequestWithBody("""{ "ids": ["A", 7, "tag-1"] }""").BodyIds("ids")), Is.EqualTo("A,7,tag-1"));

    [TestCase("""{ "ids": "A" }""")]
    [TestCase("""{ "ids": [{ "id": 1 }] }""")]
    [TestCase("""{ "ids": [1.5] }""")]
    [TestCase("""{ "ids": [null] }""")]
    [TestCase("""{ "other": [] }""")]
    [TestCase("not json")]
    public void BodyIds_fails_the_test_unless_the_property_is_an_array_of_strings_and_integers(string body) =>
        Assert.That(() => RequestWithBody(body).BodyIds("ids"), Throws.InstanceOf<AssertionException>());

    [Test]
    public void SaveCredentials_writes_the_file_Enclave_Sdk_Api_reads()
    {
        using var run = CliRun.Start();

        run.SaveCredentials("saved-token");

        var options = JsonSerializer.Deserialize<EnclaveClientOptions>(run.Files.ReadText(run.CredentialsPath)!, CredentialsJsonOptions);

        Assert.Multiple(() =>
        {
            Assert.That(run.CredentialsPath, Is.EqualTo(Path.Combine(run.Home, ".enclave", "credentials.json")));
            Assert.That(options!.PersonalAccessToken, Is.EqualTo("saved-token"));
            Assert.That(options.BaseUrl, Is.EqualTo(run.ApiBaseUrl));
            Assert.That(run.Files.IsPrivate(run.CredentialsPath), Is.True);
        });
    }

    // The CLI must never see the environment of the process running the tests, which on a developer
    // machine can hold a real token. A variable set in the process environment is invisible to the
    // host CliRun builds, and the host reads values from CliRun.Environment alone.
    [Test]
    public void Host_sees_only_the_run_environment_home_directory_stdin_and_fake_api()
    {
        const string ProbeName = "ENCLAVE_CLI_HARNESS_PROBE";
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_EXTRA"] = "extra";
        run.Environment["ENCLAVE_ORG_ID"] = null;
        run.StdinText = "line one\nline two\n";
        run.StdinIsTerminal = true;

        Environment.SetEnvironmentVariable(ProbeName, "from-process");
        try
        {
            var host = run.CreateHost();

            Assert.Multiple(() =>
            {
                Assert.That(host.GetEnvironmentVariable(ProbeName), Is.Null);
                Assert.That(host.GetEnvironmentVariable("PATH"), Is.Null);
                Assert.That(host.GetEnvironmentVariable("ENCLAVE_TOKEN"), Is.EqualTo(TestData.Token));
                Assert.That(host.GetEnvironmentVariable("ENCLAVE_EXTRA"), Is.EqualTo("extra"));
                Assert.That(host.GetEnvironmentVariable("ENCLAVE_ORG_ID"), Is.Null);
                Assert.That(host.HomeDirectory, Is.EqualTo(run.Home));
                Assert.That(host.HomeDirectory, Does.StartWith(Path.GetTempPath()));
                Assert.That(Directory.Exists(host.HomeDirectory), Is.False);
                Assert.That(host.Files, Is.SameAs(run.Files));
                Assert.That(run.Files.Paths, Is.Empty);
                Assert.That(host.Stdin.ReadToEnd(), Is.EqualTo("line one\nline two\n"));
                Assert.That(host.StdinIsTerminal, Is.True);
                Assert.That(host.DefaultApiUrl, Is.EqualTo(run.ApiUrl));
                Assert.That(host.DefaultApiUrl.Host, Is.EqualTo("127.0.0.1"));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProbeName, null);
        }
    }

    // The partner API runs on its own host (proposed-cli-surface.md "Partner API"). A run gives the
    // CLI a second fake's URL as the partner API address, on loopback at another port, so a partner
    // call can never reach the live partner API, and a partner call sent to the main API's address
    // shows. The client is the one ApiAccess builds from the run's host, which every command's calls
    // go through, so the call stands for any partner command's.
    [Test]
    public async Task Partner_api_calls_of_a_run_reach_its_fake_partner_api_with_the_token()
    {
        using var run = CliRun.Start();
        var customers = TestData.PartnerPath("customers");
        run.Stub("GET", customers, json: ApiJson.Page());
        var host = run.CreateHost();
        using var network = new HttpClientHandler();

        var partner = ApiAccess.Resolve(host).CreateClient(network).CreatePartnerClient(PartnerId.FromGuid(TestData.PartnerId));
        await partner.Customers.GetCustomersAsync();

        Assert.Multiple(() =>
        {
            Assert.That(host.DefaultPartnerApiUrl, Is.EqualTo(run.PartnerApiUrl));
            Assert.That(run.PartnerApiUrl, Is.Not.EqualTo(run.ApiUrl));
            Assert.That(run.PartnerApiUrl.Host, Is.EqualTo("127.0.0.1"));
            Assert.That(run.PartnerApiRequests.Select(request => request.Call), Is.EqualTo(new[] { $"GET {customers}" }));
            Assert.That(run.ApiRequests, Is.Empty);
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // A stub goes to the fake that serves its path, and each fake answers the other API's routes
    // with 421 Misdirected Request, which Enclave.Sdk.Api 1.1.0 raises as EnclaveApiException for a
    // problem details body. The client with the addresses swapped sends each call to the wrong fake;
    // the client with them right shows both stubs exist.
    [Test]
    public async Task Each_fake_api_answers_only_its_own_api_routes()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("GET", TestData.PartnerPath("customers"), json: ApiJson.Page());
        var right = Connect(run.ApiBaseUrl, run.PartnerApiBaseUrl);
        var swapped = Connect(run.PartnerApiBaseUrl, run.ApiBaseUrl);

        await OrganisationOf(right).EnrolledSystems.GetSystemsAsync();
        await PartnerOf(right).Customers.GetCustomersAsync();
        var mainCallAtThePartnerApi = Assert.ThrowsAsync<EnclaveApiException>(() => OrganisationOf(swapped).EnrolledSystems.GetSystemsAsync());
        var partnerCallAtTheMainApi = Assert.ThrowsAsync<EnclaveApiException>(() => PartnerOf(swapped).Customers.GetCustomersAsync());

        Assert.Multiple(() =>
        {
            Assert.That(mainCallAtThePartnerApi!.ProblemDetails.Status, Is.EqualTo(421));
            Assert.That(partnerCallAtTheMainApi!.ProblemDetails.Status, Is.EqualTo(421));
            Assert.That(run.ApiRequests.Select(request => request.Call), Is.EqualTo(new[] { $"GET {TestData.OrgPath("systems")}", $"GET {TestData.PartnerPath("customers")}" }));
            Assert.That(run.PartnerApiRequests.Select(request => request.Call), Is.EqualTo(new[] { $"GET {TestData.PartnerPath("customers")}", $"GET {TestData.OrgPath("systems")}" }));
        });
    }

    // Requests holds both fakes' requests in the order they arrived, so a test reads a run's calls
    // in the order the CLI sent them whichever API each went to; ApiRequests and PartnerApiRequests
    // hold each fake's own.
    [Test]
    public async Task Requests_holds_the_requests_of_both_fake_apis_in_the_order_received()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("GET", TestData.PartnerPath("customers"), json: ApiJson.Page());
        var client = Connect(run.ApiBaseUrl, run.PartnerApiBaseUrl);

        await OrganisationOf(client).EnrolledSystems.GetSystemsAsync(pageNumber: 1);
        await PartnerOf(client).Customers.GetCustomersAsync();
        await OrganisationOf(client).EnrolledSystems.GetSystemsAsync(pageNumber: 2);

        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {TestData.OrgPath("systems")}", $"GET {TestData.PartnerPath("customers")}", $"GET {TestData.OrgPath("systems")}" }));
            Assert.That(string.Join(",", run.Requests.Select(request => request.QueryValue("page") ?? "none")), Is.EqualTo("1,none,2"));
            Assert.That(string.Join(",", run.ApiRequests.Select(request => request.QueryValue("page"))), Is.EqualTo("1,2"));
            Assert.That(run.PartnerApiRequests.Select(request => request.Call), Is.EqualTo(new[] { $"GET {TestData.PartnerPath("customers")}" }));
        });
    }

    // The check the partner tests rely on: a partner call at the main API's address fails it, and
    // so does a run that sent nothing, as well as a run that did not exit 0.
    [Test]
    public async Task AssertSentToThePartnerApi_fails_the_test_unless_the_partner_api_address_alone_received_the_calls()
    {
        using var run = CliRun.Start();
        var success = new CliResult(0, "{}", string.Empty);
        run.Stub("GET", TestData.PartnerPath("customers"), json: ApiJson.Page());

        AssertFails(() => PartnerApiFake.AssertSentToThePartnerApi(run, success));

        await PartnerOf(Connect(run.ApiBaseUrl, run.PartnerApiBaseUrl)).Customers.GetCustomersAsync();

        Assert.That(() => PartnerApiFake.AssertSentToThePartnerApi(run, success), Throws.Nothing);
        AssertFails(() => PartnerApiFake.AssertSentToThePartnerApi(run, new CliResult(1, string.Empty, ErrorLine("api_error") + "\n")));

        Assert.ThrowsAsync<EnclaveApiException>(() => PartnerOf(Connect(run.ApiBaseUrl, run.ApiBaseUrl)).Customers.GetCustomersAsync());

        AssertFails(() => PartnerApiFake.AssertSentToThePartnerApi(run, success));
    }

    // A test fixes the clock by setting CliRun.Time, which reaches the CLI only if the host carries
    // that provider. A run that sets nothing gets the system clock, so it sees the time as the CLI
    // does outside tests.
    [Test]
    public void Host_carries_the_clock_the_run_sets_and_the_system_clock_by_default()
    {
        using var defaultRun = CliRun.Start();
        using var fixedRun = CliRun.Start();
        var clock = new FixedTimeProvider(new DateTimeOffset(2030, 3, 14, 20, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
        fixedRun.Time = clock;

        Assert.Multiple(() =>
        {
            Assert.That(defaultRun.CreateHost().Time, Is.SameAs(TimeProvider.System));
            Assert.That(fixedRun.CreateHost().Time, Is.SameAs(clock));
            Assert.That(fixedRun.CreateHost().Time, Is.SameAs(fixedRun.Time));
        });
    }

    // Tests whose answer depends on the time read FixedTimeProvider, so it must give the instant it
    // was given, in UTC whatever offset that instant was written with (TimeProvider.GetUtcNow
    // returns a zero offset, .NET 8 documentation), and the local time in the zone it was given
    // (TimeProvider.GetLocalNow converts GetUtcNow into LocalTimeZone). The zone is the tests' own,
    // TestData.LocalZone, at UTC+05:30, so the local time differs from UTC in its date and minutes as
    // well as its hour, whatever zone the runner is in; the instant is written at UTC-04:00 so the
    // conversion to UTC shows.
    [Test]
    public void FixedTimeProvider_reads_the_given_instant_in_utc_and_the_local_time_in_the_given_zone()
    {
        var zone = TestData.LocalZone;
        var instant = new DateTimeOffset(2030, 3, 14, 16, 0, 0, TimeSpan.FromHours(-4));
        var clock = new FixedTimeProvider(instant, zone);

        var utcNow = clock.GetUtcNow();
        var localNow = clock.GetLocalNow();

        Assert.Multiple(() =>
        {
            Assert.That(utcNow, Is.EqualTo(instant));
            Assert.That(utcNow.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(utcNow.DateTime, Is.EqualTo(new DateTime(2030, 3, 14, 20, 0, 0, DateTimeKind.Unspecified)));
            Assert.That(clock.GetUtcNow(), Is.EqualTo(utcNow));
            Assert.That(clock.LocalTimeZone, Is.SameAs(zone));
            Assert.That(localNow.Offset, Is.EqualTo(TimeSpan.FromMinutes(330)));
            Assert.That(localNow.DateTime, Is.EqualTo(new DateTime(2030, 3, 15, 1, 30, 0, DateTimeKind.Unspecified)));
        });
    }

    // ENCLAVE_ORG takes a name and ENCLAVE_ORG_ID an ID (proposed-cli-surface.md "Context"). The
    // test organisation is given by ID, which needs no lookup call, so a run's requests are the
    // command's own.
    [Test]
    public void Run_environment_defaults_to_the_test_token_and_the_test_organisation_id()
    {
        using var run = CliRun.Start();

        Assert.Multiple(() =>
        {
            Assert.That(run.Environment["ENCLAVE_TOKEN"], Is.EqualTo(TestData.Token));
            Assert.That(run.Environment["ENCLAVE_ORG_ID"], Is.EqualTo(TestData.OrgId.ToString()));
            Assert.That(run.Environment.ContainsKey("ENCLAVE_ORG"), Is.False);
            Assert.That(run.StdinText, Is.Empty);
            Assert.That(run.StdinIsTerminal, Is.False);
        });
    }

    // Tests keep their files in memory, so the harness writes nothing to the disk: the files a test
    // sets up are in run.Files and no directory appears at the home or work path.
    [Test]
    public void Harness_files_live_in_memory_and_never_on_disk()
    {
        using var run = CliRun.Start();

        var input = run.WriteFile("policy.json", "{}");
        run.SaveCredentials(TestData.Token);

        Assert.Multiple(() =>
        {
            Assert.That(run.Files.ReadText(input), Is.EqualTo("{}"));
            Assert.That(run.Files.IsPrivate(input), Is.False);
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.True);
            Assert.That(Directory.Exists(run.Home), Is.False);
            Assert.That(Directory.Exists(run.WorkDirectory), Is.False);
        });
    }

    // RunAsync fails a run that wrote to the disk under the run's paths, so a CLI that goes around
    // CliHost.Files cannot pass a test. The directory is made here to stand in for such a write.
    [Test]
    public void RunAsync_fails_when_a_directory_appears_on_disk_under_the_run()
    {
        using var run = CliRun.Start();
        Directory.CreateDirectory(run.Home);

        Assert.That(async () => await run.RunAsync("--version"), Throws.TypeOf<AssertionException>());
    }

    [Test]
    public async Task RunAsync_captures_stdout_and_the_exit_code()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("--version");

        var expected = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StdoutLines, Is.EqualTo(new[] { expected }));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task RunAsync_captures_stderr_and_a_nonzero_exit_code()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("--no-such-option");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Stderr, Does.Contain("--no-such-option"));
        });
    }

    [Test]
    public void Result_json_accessors_fail_the_test_when_the_output_is_not_json()
    {
        var result = new CliResult(1, "not json", "first\nsecond\n");

        Assert.Multiple(() =>
        {
            Assert.Throws<AssertionException>(() => _ = result.StdoutJson);
            Assert.Throws<AssertionException>(() => _ = result.StderrJson);
            Assert.Throws<AssertionException>(() => _ = result.Error);
        });
    }

    [Test]
    public void Result_error_returns_the_error_object_of_the_single_stderr_line()
    {
        var result = new CliResult(5, string.Empty, "{\"error\":{\"code\":\"not_found\",\"status\":404}}\r\n");

        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("not_found"));
            Assert.That(JsonAssert.Property(result.Error, "Status").GetInt32(), Is.EqualTo(404));
            Assert.That(result.StderrJson, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void List_returns_the_items_of_a_list_of_the_given_kind()
    {
        var result = Output(CliList.WithIds("key", "7", "8"));

        var items = CliAssert.List(result, "key");

        Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("7,8"));
    }

    [TestCase("""{ "kind": "system", "items": [], "total": 0 }""")]
    [TestCase("""{ "total": 1, "items": [{ "systemId": "A" }], "kind": "system" }""")]
    public void List_passes_a_list_of_exactly_the_given_kind(string stdout) =>
        Assert.That(() => CliAssert.List(Output(stdout), "system"), Throws.Nothing);

    // The kind is compared exactly because a command reading the list from stdin rejects any other
    // kind. The other cases break the envelope { kind, items, total } or the rule that total counts
    // the items.
    [TestCase(0, """{ "kind": "pending-system", "items": [], "total": 0 }""")]
    [TestCase(0, """{ "kind": "System", "items": [], "total": 0 }""")]
    [TestCase(0, """{ "kind": "system", "items": [{ "systemId": "A" }], "total": 2 }""")]
    [TestCase(0, """{ "kind": "system", "items": [] }""")]
    [TestCase(0, """{ "kind": "system", "items": [], "total": 0, "truncated": false }""")]
    [TestCase(0, """{ "kind": "system", "items": {}, "total": 0 }""")]
    [TestCase(0, """{ "kind": "system", "items": [], "total": "0" }""")]
    [TestCase(0, """{ "items": [], "total": 0 }""")]
    [TestCase(0, """[]""")]
    [TestCase(1, """{ "kind": "system", "items": [], "total": 0 }""")]
    public void List_fails_the_test_unless_the_run_printed_a_list_of_exactly_the_given_kind(int exitCode, string stdout) =>
        AssertFails(() => CliAssert.List(Output(stdout, exitCode), "system"));

    [Test]
    public void List_throws_for_a_kind_the_cli_does_not_print() =>
        Assert.That(() => CliAssert.List(Output("""{ "kind": "systems", "items": [], "total": 0 }"""), "systems"), Throws.ArgumentException);

    [TestCase("""{ "requested": 3, "affected": 2 }""")]
    [TestCase("""{ "affected": 2, "requested": 3 }""")]
    public void Bulk_passes_the_requested_and_affected_counts_given(string stdout) =>
        Assert.That(() => CliAssert.Bulk(Output(stdout), requested: 3, affected: 2), Throws.Nothing);

    [TestCase(0, """{ "requested": 2, "affected": 3 }""")]
    [TestCase(0, """{ "requested": 3 }""")]
    [TestCase(0, """{ "requested": 3, "affected": 2, "failed": 1 }""")]
    [TestCase(0, """{ "requested": "3", "affected": "2" }""")]
    [TestCase(0, """[3, 2]""")]
    [TestCase(1, """{ "requested": 3, "affected": 2 }""")]
    public void Bulk_fails_the_test_unless_the_run_printed_exactly_those_counts(int exitCode, string stdout) =>
        AssertFails(() => CliAssert.Bulk(Output(stdout, exitCode), requested: 3, affected: 2));

    // Each code of the fixed set, given alone, must be checked against the exit code the contract
    // gives it; one exit code off must fail.
    [TestCaseSource(nameof(ErrorCodes))]
    public void Failed_takes_the_exit_code_from_the_error_code(string code, int exitCode)
    {
        Assert.That(() => CliAssert.Failed(ErrorOutput(exitCode, code), code), Throws.Nothing);

        AssertFails(() => CliAssert.Failed(ErrorOutput(exitCode + 1, code), code));
    }

    [Test]
    public void Failed_fails_the_test_for_another_code_output_on_stdout_or_more_than_one_error()
    {
        var twoErrors = new CliResult(5, string.Empty, ErrorLine("not_found") + "\n" + ErrorLine("not_found") + "\n");

        AssertFails(() => CliAssert.Failed(ErrorOutput(5, "api_error"), "not_found"));
        AssertFails(() => CliAssert.Failed(ErrorOutput(5, "not_found", stdout: "{}"), "not_found"));
        AssertFails(() => CliAssert.Failed(twoErrors, "not_found"));
    }

    // An error code outside the fixed set, or an exit code the contract does not give that code,
    // can never be met, so the test is told it is wrong.
    [Test]
    public void Failed_and_Rejected_throw_for_a_code_or_exit_code_outside_the_contract()
    {
        using var run = CliRun.Start();
        var result = ErrorOutput(7, "transient");

        Assert.Multiple(() =>
        {
            Assert.That(() => CliAssert.Failed(result, "confirmation_required"), Throws.ArgumentException);
            Assert.That(() => CliAssert.Failed(result, 7, "transient"), Throws.ArgumentException);
            Assert.That(() => CliAssert.Rejected(run, result, "confirmation_required"), Throws.ArgumentException);
            Assert.That(() => CliAssert.Rejected(run, result, 7, "transient"), Throws.ArgumentException);
        });
    }

    [Test]
    public async Task Rejected_fails_the_test_when_the_fake_api_received_a_request()
    {
        using var run = CliRun.Start();
        var result = ErrorOutput(2, "no_org");

        Assert.That(() => CliAssert.Rejected(run, result, "no_org"), Throws.Nothing);

        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        await ConnectOrganisation(run).EnrolledSystems.GetSystemsAsync();

        AssertFails(() => CliAssert.Rejected(run, result, "no_org"));
    }

    // The ID field of each kind is stated here from the model a list of that kind prints, so a
    // CliList that put an ID in the wrong property, or wrote a number as a string, fails.
    [TestCaseSource(nameof(ListKindsWithIds))]
    public void CliList_WithIds_builds_a_list_whose_items_carry_each_id_in_the_kind_id_field(string kind, string field, JsonValueKind valueKind)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(field);

        var ids = TestData.Ids(kind, 3);
        var json = CliList.WithIds(kind, ids);
        var list = Parse(json);
        var items = list.GetProperty("items").EnumerateArray().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(CliList.IdField(kind), Is.EqualTo(field));
            Assert.That(list.GetProperty("kind").GetString(), Is.EqualTo(kind));
            Assert.That(list.GetProperty("total").GetInt32(), Is.EqualTo(3));
            Assert.That(items.Select(item => item.GetProperty(field).ValueKind), Is.All.EqualTo(valueKind));
            Assert.That(string.Join(",", items.Select(item => IdText(item.GetProperty(field)))), Is.EqualTo(string.Join(",", ids.Select(Canonical))));
            Assert.That(CliAssert.List(Output(json), kind).GetArrayLength(), Is.EqualTo(3));
        });
    }

    [Test]
    public void CliList_id_cases_cover_every_list_kind_but_log() =>
        Assert.That(ListKindsWithIds().Select(data => (string)data.Arguments[0]!).Append("log"), Is.EquivalentTo(CliList.Kinds));

    [Test]
    public void CliList_Of_wraps_the_items_given_with_their_count_as_total()
    {
        var list = Parse(CliList.Of("log", ApiJson.Log("one"), ApiJson.Log("two")));

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(list), Is.EqualTo("kind,items,total"));
            Assert.That(list.GetProperty("kind").GetString(), Is.EqualTo("log"));
            Assert.That(JsonRead.StringFieldList(list.GetProperty("items"), "message"), Is.EqualTo("one,two"));
            Assert.That(list.GetProperty("total").GetInt32(), Is.EqualTo(2));
        });
    }

    [Test]
    public void CliList_throws_for_log_ids_and_unknown_kinds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => CliList.WithIds("log", "1"), Throws.ArgumentException);
            Assert.That(() => CliList.IdField("log"), Throws.ArgumentException);
            Assert.That(() => CliList.Of("systems"), Throws.ArgumentException);
            Assert.That(() => CliList.WithIds("systems", "A"), Throws.ArgumentException);
        });
    }

    // Each ID must pass the format the proposal's "ID checks" table gives its kind, or a test using
    // it would see the CLI reject it for the wrong reason. Distinct IDs keep the CLI's removal of
    // duplicates from changing the count.
    [TestCaseSource(nameof(KindsWithIds))]
    public void Ids_are_distinct_and_valid_for_their_kind(string kind)
    {
        var ids = TestData.Ids(kind, 450);

        Assert.Multiple(() =>
        {
            Assert.That(ids, Has.Length.EqualTo(450));
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Is.All.Matches<string>(id => IsValidId(kind, id)));
        });
    }

    private static TestCaseData Resource(
        string name,
        string body,
        string listPath,
        Func<IOrganisationClient, Task<object>> readItem,
        string? getPath,
        Func<IOrganisationClient, Task<object>>? readOne) =>
        new TestCaseData(body, listPath, readItem, getPath, readOne).SetArgDisplayNames(name);

    private static TestCaseData Bulk(string field, string method, string path, Func<IOrganisationClient, Task<int>> call) =>
        new TestCaseData(field, method, path, call).SetArgDisplayNames(field, method, path);

    private static EnclaveClient Connect(CliRun run) => Connect(run.ApiBaseUrl, run.PartnerApiBaseUrl);

    // The partner API address is always given: left out, Enclave.Sdk.Api 1.1.0 uses production's
    // (EnclaveClientOptions.PartnerApiBaseUrl).
    private static EnclaveClient Connect(string baseUrl, string partnerApiBaseUrl) => new(new EnclaveClientOptions
    {
        BaseUrl = baseUrl,
        PartnerApiBaseUrl = partnerApiBaseUrl,
        PersonalAccessToken = TestData.Token,
    });

    private static IOrganisationClient OrganisationOf(EnclaveClient client) =>
        client.CreateOrganisationClient(new AccountOrganisationModel(
            OrganisationGuid.FromGuid(TestData.OrgId),
            TestData.OrgName,
            UserOrganisationRole.Owner,
            partnerAccess: false));

    private static IPartnerClient PartnerOf(EnclaveClient client) => client.CreatePartnerClient(PartnerId.FromGuid(TestData.PartnerId));

    private static IOrganisationClient ConnectOrganisation(CliRun run) => OrganisationOf(Connect(run));

    private static async Task<object> FirstAsync<T>(IAsyncEnumerable<T> items)
        where T : notnull
    {
        await foreach (var item in items)
        {
            return item;
        }

        throw new AssertionException("Expected at least one item in the page.");
    }

    // Every property each model writes must be in the body with the same value, and every property
    // in the body must be one that at least one of the models writes. A body serves both the list
    // item and the single resource, so a property only one of them has is allowed.
    private static async Task AssertBodyMatchesModelsAsync(string body, IReadOnlyList<object> models)
    {
        var expected = Parse(body);
        var written = new HashSet<string>(StringComparer.Ordinal);
        var mismatches = new List<string>();

        foreach (var model in models)
        {
            var actual = await WriteAsync(model);

            foreach (var property in actual.EnumerateObject())
            {
                written.Add(property.Name);

                if (!expected.TryGetProperty(property.Name, out var value))
                {
                    mismatches.Add($"The body lacks \"{property.Name}\", which {model.GetType().Name} writes as {property.Value.GetRawText()}.");
                }
                else if (!SameJson(value, property.Value))
                {
                    mismatches.Add($"{model.GetType().Name}.{property.Name} reads {value.GetRawText()} from the body and writes {property.Value.GetRawText()}.");
                }
            }
        }

        foreach (var property in expected.EnumerateObject().Where(property => !written.Contains(property.Name)))
        {
            mismatches.Add($"No model reads the body's \"{property.Name}\".");
        }

        Assert.That(mismatches, Is.Empty);
    }

    private static void AssertSameJson(JsonElement actual, JsonElement expected) =>
        Assert.That(SameJson(actual, expected), Is.True, $"Expected {expected.GetRawText()}{Environment.NewLine}but the model writes {actual.GetRawText()}");

    // Objects compare by property name whatever the order, since a model writes its properties in
    // declaration order and the bodies list them in their own order. Strings compare by value, so an
    // escape sequence and the character it stands for are equal.
    private static bool SameJson(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftProperties = left.EnumerateObject().ToList();

                return leftProperties.Count == right.EnumerateObject().Count()
                    && leftProperties.All(property => right.TryGetProperty(property.Name, out var other) && SameJson(property.Value, other));

            case JsonValueKind.Array:
                var leftItems = left.EnumerateArray().ToList();
                var rightItems = right.EnumerateArray().ToList();

                return leftItems.Count == rightItems.Count
                    && leftItems.Zip(rightItems).All(pair => SameJson(pair.First, pair.Second));

            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                return left.GetDecimal() == right.GetDecimal();

            default:
                return true;
        }
    }

    private static async Task<JsonElement> WriteAsync(object model)
    {
        // Some models hold IAsyncEnumerable properties, which only the asynchronous serializer writes.
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, model, model.GetType(), SdkJsonOptions);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static JsonElement Write<T>(T value) => JsonSerializer.SerializeToElement(value, SdkJsonOptions);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static TestCaseData IdCase(string kind, string field, JsonValueKind valueKind) =>
        new TestCaseData(kind, field, valueKind).SetArgDisplayNames(kind);

    private static async Task<string> SystemIdsAsync(PaginatedResponseModel<SystemSummaryModel> page)
    {
        var ids = new List<string>();
        await foreach (var item in page.Items)
        {
            ids.Add(item.SystemId);
        }

        return string.Join(",", ids);
    }

    private static CliResult Output(string stdout, int exitCode = 0) => new(exitCode, stdout, string.Empty);

    private static string ErrorLine(string code) => new JsonObject
    {
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["status"] = null,
            ["title"] = null,
            ["detail"] = null,
            ["errors"] = null,
        },
    }.ToJsonString();

    private static CliResult ErrorOutput(int exitCode, string code, string stdout = "") =>
        new(exitCode, stdout, ErrorLine(code) + "\n");

    // A failed NUnit assertion records the failure in the current test's result as well as throwing
    // AssertionException, or MultipleAssertException at the end of Assert.Multiple. The check runs in
    // an IsolatedContext, whose result is discarded on leaving it (NUnit 4.4.0 XML docs,
    // TestExecutionContext.IsolatedContext), so the expected failure does not fail this test. Inside
    // an outer Assert.Multiple a failed assertion would record and carry on without throwing, so
    // callers use this outside one.
    private static void AssertFails(TestDelegate check)
    {
        ResultStateException? failure = null;

        using (new TestExecutionContext.IsolatedContext())
        {
            try
            {
                check();
            }
            catch (ResultStateException ex)
            {
                failure = ex;
            }
        }

        Assert.That(failure, Is.InstanceOf<AssertionException>().Or.InstanceOf<MultipleAssertException>(), "Expected the check to fail the test.");
    }

    private static RecordedRequest RequestWithPage(string? page) => new(
        "GET",
        TestData.OrgPath("systems"),
        page is null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal) { ["page"] = page },
        null,
        null);

    private static RecordedRequest RequestWithBody(string body) =>
        new("PUT", TestData.OrgPath("systems/disable"), new Dictionary<string, string>(StringComparer.Ordinal), body, null);

    private static string IdText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    // GUID IDs are given in the hyphenated form people type, and the API writes them as 32 hex
    // digits (OrganisationGuid and AccountGuid JSON converters), so they are compared in that form.
    private static string Canonical(string id) =>
        Guid.TryParse(id, CultureInfo.InvariantCulture, out var guid) ? guid.ToString("N") : id;

    // The formats of proposed-cli-surface.md "ID checks". The tag rule is the API's
    // ^([a-z0-9]+[-.])*[a-z0-9]+$ (portal TagValidationExtensions.cs:13).
    private static bool IsValidId(string kind, string id) => kind switch
    {
        "system" or "pending-system" => id.Length > 0 && id.All(char.IsAsciiLetterOrDigit),
        "key" or "policy" or "zone" or "hostname" or "trust" =>
            int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0,
        "tag" => id.Split('-', '.').All(part => part.Length > 0 && part.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))),
        "org" or "user" or "customer" or "admin" => Guid.TryParse(id, CultureInfo.InvariantCulture, out _),
        "invite" => id.Count(c => c == '@') == 1 && !id.StartsWith('@') && !id.EndsWith('@'),
        _ => false,
    };
}
