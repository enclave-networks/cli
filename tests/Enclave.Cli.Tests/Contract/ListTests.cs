using System.Globalization;
using System.Text.Json;
using Enclave.Api.Modules.AccountManagement.PublicAccount.Models;
using Enclave.Api.Modules.ActivityLogs.Logs.Models;
using Enclave.Api.Modules.OrganisationManagement.Organisation.Models;
using Enclave.Api.Modules.SystemManagement.Dns.Models;
using Enclave.Api.Modules.SystemManagement.EnrolmentKeys.Models;
using Enclave.Api.Modules.SystemManagement.Policies.Models;
using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Api.Modules.SystemManagement.Tags.Models;
using Enclave.Api.Modules.SystemManagement.TrustRequirements.Models;
using Enclave.Api.Modules.SystemManagement.UnapprovedSystems.Models;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Contract;

// A list prints { "kind", "items", "total" }, where kind names what the items are and total is the
// number of items printed (proposed-cli-surface.md "Output"). It reads every page, one call per
// page of 200, the most the API returns (portal PaginationDefaults.cs:11), until the response's
// metadata.nextPage is null ("Calls per command"), and prints once every page is read, so stdout
// never holds part of a list. log reads only the pages it needs ("Command options"), so it takes
// part in the envelope rules here and not in the paging rules.
public class ListTests
{
    private const string PageSize = "200";

    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string[] OsWindowsStateDisconnected = ["os:Windows", "state:disconnected"];

    private static readonly Dictionary<string, PagedList> PagedLists = new PagedList[]
    {
        new("system list", "systems", "system", typeof(SystemSummaryModel), id => ApiJson.System(id)),
        new("system list --pending", "unapproved-systems", "pending-system", typeof(UnapprovedSystemSummaryModel), id => ApiJson.PendingSystem(id)),
        new("key list", "enrolment-keys", "key", typeof(EnrolmentKeySummaryModel), id => ApiJson.Key(Number(id))),
        new("policy list", "policies", "policy", typeof(PolicyModel), id => ApiJson.Policy(Number(id))),
        new("tag list", "tags", "tag", typeof(TagSummaryModel), ApiJson.Tag),
        new("dns list-zones", "dns/zones", "zone", typeof(DnsZoneSummaryModel), id => ApiJson.Zone(Number(id), Invariant($"zone{id}"))),
        new("dns list-hostnames", "dns/records", "hostname", typeof(DnsRecordSummaryModel), id => ApiJson.Record(Number(id), Invariant($"host{id}"))),
        new("trust list", "trust-requirements", "trust", typeof(TrustRequirementSummaryModel), id => ApiJson.Trust(Number(id))),
    }.ToDictionary(list => list.Command, StringComparer.Ordinal);

    public static IEnumerable<TestCaseData> PagedListCommands() =>
        PagedLists.Keys.Select(command => new TestCaseData(command).SetArgDisplayNames(command));

    // 450 items take three pages of 200; 400 fill two pages exactly, where reading on until a page
    // comes back empty would ask for a third; one item and no items take one page.
    public static IEnumerable<TestCaseData> PageCounts()
    {
        foreach (var command in PagedLists.Keys)
        {
            yield return new TestCaseData(command, 450, "0,1,2").SetArgDisplayNames(command, "450");
            yield return new TestCaseData(command, 400, "0,1").SetArgDisplayNames(command, "400");
            yield return new TestCaseData(command, 1, "0").SetArgDisplayNames(command, "1");
            yield return new TestCaseData(command, 0, "0").SetArgDisplayNames(command, "0");
        }
    }

    // The organisation lists come back whole from the API (EnclaveClient.GetOrganisationsAsync, and
    // OrganisationScopedClient GetOrganisationUsersAsync and GetPendingInvitesAsync, in
    // Enclave.Sdk.Api 1.1.0), and log reads only the pages it needs; every list prints the same envelope ("Output":
    // `log` included), with the kinds "Output" names: org for `org list`, user for `org list-users`
    // and invite for `org list-invites`.
    public static IEnumerable<TestCaseData> WholeLists()
    {
        yield return WholeList(
            "org list",
            "/account/orgs",
            ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)),
            "org",
            typeof(AccountOrganisationModel),
            ApiJson.Org(TestData.OrgId, TestData.OrgName),
            ApiJson.Org(TestData.OtherOrgId, TestData.OtherOrgName));
        yield return WholeList(
            "org list-users",
            TestData.OrgPath("users"),
            ApiJson.Users((TestData.OtherOrgId, "sam@acme.example"), (TestData.PartnerId, "alex@acme.example")),
            "user",
            typeof(OrganisationUser),
            ApiJson.User(TestData.OtherOrgId, "sam@acme.example"),
            ApiJson.User(TestData.PartnerId, "alex@acme.example"));
        yield return WholeList(
            "org list-invites",
            TestData.OrgPath("invites"),
            ApiJson.Invites("sam@acme.example", "alex@acme.example"),
            "invite",
            typeof(OrganisationInviteModel),
            ApiJson.Invite("sam@acme.example"),
            ApiJson.Invite("alex@acme.example"));
        yield return WholeList(
            "log",
            TestData.OrgPath("logs"),
            ApiJson.Page(ApiJson.Log("System ABCDE enrolled"), ApiJson.Log("Policy 7 enabled")),
            "log",
            typeof(LogEntryModel),
            ApiJson.Log("System ABCDE enrolled"),
            ApiJson.Log("Policy 7 enabled"));
    }

    // A failed page read exits with that page's error, mapped as any API error is ("Errors and exit
    // codes"). A list is not a single-ID command, so a 404 on a page is api_error. The pages after
    // the failed one are never asked for.
    public static IEnumerable<TestCaseData> PageFailures()
    {
        yield return new TestCaseData(0, 403, "forbidden", "0");
        yield return new TestCaseData(1, 401, "token_invalid", "0,1");
        yield return new TestCaseData(1, 429, "transient", "0,1");
        yield return new TestCaseData(2, 500, "transient", "0,1,2");
        yield return new TestCaseData(1, 400, "api_error", "0,1");
        yield return new TestCaseData(1, 404, "api_error", "0,1");
    }

    // Agents read list items by the API's field names, so each item is the Enclave.Sdk.Api model
    // unchanged, and the envelope adds only kind and total ("Output"). The kind is what a command
    // reading the list from stdin checks ("Several IDs").
    [TestCaseSource(nameof(PagedListCommands))]
    public async Task A_paged_list_prints_every_api_item_unchanged_in_an_envelope_of_its_kind(string command)
    {
        var list = PagedLists[command];
        using var run = CliRun.Start();
        var items = Items(list, 3);
        run.StubPages(list.Path, 200, items);

        var result = await run.RunAsync(list.Args);

        var printed = CliAssert.List(result, list.Kind);
        Assert.Multiple(() =>
        {
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Calls(), Is.All.EqualTo($"GET {list.Path}"));
            Assert.That(run.PagesRequested(list.Path), Is.EqualTo("0"));
            Assert.That(printed.GetArrayLength(), Is.EqualTo(3));
        });

        await AssertItemsUnchangedAsync(printed, items, list.Model);
    }

    [TestCaseSource(nameof(WholeLists))]
    public async Task An_organisation_list_or_the_log_prints_every_api_item_unchanged_in_an_envelope_of_its_kind(string command, string path, string body, string kind, Type model, string[] items)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(items);
        using var run = CliRun.Start();
        run.Stub("GET", path, json: body);

        var result = await run.RunAsync(command.Split(' '));

        var printed = CliAssert.List(result, kind);
        Assert.Multiple(() =>
        {
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Calls(), Is.Not.Empty.And.All.EqualTo($"GET {path}"));
            Assert.That(printed.GetArrayLength(), Is.EqualTo(items.Length));
        });

        await AssertItemsUnchangedAsync(printed, items, model);
    }

    // StubPages serves each page only to the request for its page number and answers whatever
    // per_page a request asks for, so the page numbers and per_page of every request are checked
    // here. The items printed show every page was read, in order, and none twice.
    [TestCaseSource(nameof(PageCounts))]
    public async Task A_list_reads_every_page_of_200_until_nextPage_is_null_and_prints_every_item(string command, int count, string pages)
    {
        var list = PagedLists[command];
        using var run = CliRun.Start();
        var ids = TestData.Ids(list.Kind, count);
        run.StubPages(list.Path, 200, ids.Select(list.Item).ToArray());

        var result = await run.RunAsync(list.Args);

        var printed = CliAssert.List(result, list.Kind);
        var requests = run.Requests;
        Assert.Multiple(() =>
        {
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.PagesRequested(list.Path), Is.EqualTo(pages));
            Assert.That(requests.Select(request => $"{request.Method} {request.Path}"), Is.All.EqualTo($"GET {list.Path}"));
            Assert.That(requests.Select(request => request.QueryValue("per_page")), Is.All.EqualTo(PageSize));
            Assert.That(PrintedIds(printed, list.Kind), Is.EqualTo(string.Join(",", ids)));
        });
    }

    // A list printed from the pages read before a failure would look complete to a caller, so a
    // failed page read prints nothing and exits with that page's error ("Output").
    [TestCaseSource(nameof(PagedListCommands))]
    public async Task A_list_whose_page_read_fails_prints_nothing_and_exits_with_that_pages_error(string command)
    {
        var list = PagedLists[command];
        using var run = CliRun.Start();
        run.StubPages(list.Path, 200, Items(list, 450));
        run.StubPageProblem(list.Path, 1, 503, "Service Unavailable", "Try again later.");

        var result = await run.RunAsync(list.Args);

        var error = CliAssert.Failed(result, "transient");
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(list.Path), Is.EqualTo("0,1"));
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(503));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("Service Unavailable"));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo("Try again later."));
        });
    }

    [TestCaseSource(nameof(PageFailures))]
    public async Task A_failed_page_read_exits_with_the_code_for_its_status(int page, int status, string code, string pages)
    {
        using var run = CliRun.Start();
        run.StubPages(SystemsPath, 200, Items(PagedLists["system list"], 450));
        run.StubPageProblem(SystemsPath, page, status, "Problem title", "Problem detail");

        var result = await run.RunAsync("system", "list");

        var error = CliAssert.Failed(result, code);
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(SystemsPath), Is.EqualTo(pages));
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(status));
        });
    }

    // Example 38. The CLI takes the lower-case, hyphenated name and sends the API's own,
    // RecentlyConnected, which Enclave.Sdk.Api 1.1.0 writes into the sort parameter
    // (SystemsClient.BuildQueryString). Each page is a separate request, so each carries the sort.
    [Test]
    public async Task System_list_sort_recently_connected_asks_for_every_page_sorted_by_RecentlyConnected()
    {
        using var run = CliRun.Start();
        var ids = TestData.Ids("system", 250);
        run.StubPages(SystemsPath, 200, ids.Select(id => ApiJson.System(id)).ToArray());

        var result = await run.RunAsync("system", "list", "--sort", "recently-connected");

        var printed = CliAssert.List(result, "system");
        var requests = run.RequestsTo("GET", SystemsPath);
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(SystemsPath), Is.EqualTo("0,1"));
            Assert.That(requests.Select(request => request.QueryValue("sort")), Is.All.EqualTo("RecentlyConnected"));
            Assert.That(PrintedIds(printed, "system"), Is.EqualTo(string.Join(",", ids)));
        });
    }

    // Example 39. --filter is sent as typed, so it takes the API's search syntax as well as plain
    // words ("Filters"), and every page is read with the same search.
    [Test]
    public async Task System_list_filter_sends_the_text_as_typed_with_every_page_request()
    {
        using var run = CliRun.Start();
        run.StubPages(SystemsPath, 200, Items(PagedLists["system list"], 250));

        var result = await run.RunAsync("system", "list", "--filter", "version:<2024.8.0");

        CliAssert.List(result, "system");
        var requests = run.RequestsTo("GET", SystemsPath);
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(SystemsPath), Is.EqualTo("0,1"));
            Assert.That(requests.Select(request => request.QueryValue("search")), Is.All.EqualTo("version:<2024.8.0"));
        });
    }

    // Example 40. --os and --state are search keys the API defines for systems (portal
    // SystemSearchKeyService.cs, GetSearchKeys: "os", "state"), which the CLI adds to the search
    // text as the API's search values ("Filters"): `state:disconnected`, and `os:Windows`, since
    // the API matches the os value exactly against the platform name it stores (UseExactMatch,
    // which BaseSearchKeyService.GetBasicSearchFilter turns into an equality filter). The order of
    // the terms does not change the search, so they are compared as a set. The fake API does not
    // filter, so every item it serves is a match, and total counts the items printed.
    [Test]
    public async Task System_list_os_windows_state_disconnected_searches_by_both_keys_on_every_page_and_totals_the_items_printed()
    {
        using var run = CliRun.Start();
        run.StubPages(SystemsPath, 200, Items(PagedLists["system list"], 250));

        var result = await run.RunAsync("system", "list", "--os", "windows", "--state", "disconnected");

        var printed = CliAssert.List(result, "system");
        var requests = run.RequestsTo("GET", SystemsPath);
        Assert.Multiple(() =>
        {
            Assert.That(printed.GetArrayLength(), Is.EqualTo(250));
            Assert.That(run.PagesRequested(SystemsPath), Is.EqualTo("0,1"));

            foreach (var request in requests)
            {
                var terms = (request.QueryValue("search") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.That(terms, Is.EquivalentTo(OsWindowsStateDisconnected), request.ToString());
            }
        });
    }

    private static string[] Items(PagedList list, int count) =>
        TestData.Ids(list.Kind, count).Select(list.Item).ToArray();

    private static int Number(string id) => int.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture);

    // Integer IDs print as JSON numbers and the rest as strings; both are compared as text.
    private static string PrintedIds(JsonElement items, string kind)
    {
        var field = CliList.IdField(kind);

        return string.Join(",", items.EnumerateArray().Select(item =>
        {
            var id = item.GetProperty(field);
            return id.ValueKind == JsonValueKind.String ? id.GetString() : id.GetRawText();
        }));
    }

    private static async Task AssertItemsUnchangedAsync(JsonElement printed, string[] items, Type model)
    {
        var expected = new List<JsonElement>();
        foreach (var item in items)
        {
            expected.Add(await ApiJson.AsSdkModelAsync(item, model));
        }

        Assert.Multiple(() =>
        {
            for (var i = 0; i < Math.Min(expected.Count, printed.GetArrayLength()); i++)
            {
                Assert.That(
                    JsonElement.DeepEquals(printed[i], expected[i]),
                    Is.True,
                    $"Expected item {i} as the {model.Name} writes it:{Environment.NewLine}{expected[i]}{Environment.NewLine}Found:{Environment.NewLine}{printed[i]}");
            }
        });
    }

    private static TestCaseData WholeList(string command, string path, string body, string kind, Type model, params string[] items) =>
        new TestCaseData(command, path, body, kind, model, items).SetArgDisplayNames(command);

    // Item builds the API body of the item with a given ID, so a test can serve items whose IDs it
    // knows and check the order they are printed in.
    private sealed record PagedList(string Command, string PathSuffix, string Kind, Type Model, Func<string, string> Item)
    {
        public string Path => TestData.OrgPath(PathSuffix);

        public string[] Args => Command.Split(' ');
    }
}
