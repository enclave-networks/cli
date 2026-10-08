using System.Globalization;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// Systems waiting for approval are reached with --pending on system list, show and update, and
// approved or declined with system approve and decline (proposed-cli-surface.md "Commands"). The
// API keeps them apart from enrolled systems, under unapproved-systems.
public class SystemPendingCommandTests
{
    private static readonly string PendingPath = TestData.OrgPath("unapproved-systems");

    private static readonly string ApprovePath = TestData.OrgPath("unapproved-systems/approve");

    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string KeysPath = TestData.OrgPath("enrolment-keys");

    // A key whose description starts with the same words is listed first, so a CLI that takes a
    // partial match, or the first item, finds key 13.
    private static readonly string BuildAgentsKeys = ApiJson.Page(ApiJson.Key(13, "build agents 2026-04"), ApiJson.Key(12, "build agents"));

    // The list's kind is pending-system, so that approve and decline can check a list given to them
    // (proposed-cli-surface.md "Output"). Three systems two to a page take two reads.
    [Test]
    public async Task System_list_pending_reads_every_page_of_the_waiting_systems_and_prints_a_pending_system_list()
    {
        using var run = CliRun.Start();
        run.StubPages(PendingPath, 2, ApiJson.PendingSystem("XYZ12"), ApiJson.PendingSystem("XYZ13"), ApiJson.PendingSystem("XYZ14"));

        var result = await run.RunAsync("system", "list", "--pending");

        var items = CliAssert.List(result, "pending-system");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("XYZ12,XYZ13,XYZ14"));
            Assert.That(run.PagesRequested(PendingPath), Is.EqualTo("0,1"));
            Assert.That(run.RequestsTo("GET", SystemsPath), Is.Empty);
        });
    }

    // --key-id uses the API's enrolment_key parameter (Enclave.Sdk.Api 1.1.0
    // UnapprovedSystemsClient.BuildQueryString). Example 3, first command.
    [Test]
    public async Task System_list_pending_key_id_sends_the_enrolment_key_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", PendingPath, "system", "list", "--pending", "--key-id", "12");

        Assert.Multiple(() =>
        {
            Assert.That(request.QueryValue("enrolment_key"), Is.EqualTo("12"));
            Assert.That(request.QueryValue("search"), Is.Null);
        });
    }

    // --key looks the key up by its description with one list call, then sends its ID as --key-id
    // does (proposed-cli-surface.md "Filters" and "Names and IDs"). Name lookups ask for disabled
    // items too ("Filters").
    [Test]
    public async Task System_list_pending_key_looks_the_key_up_by_description_and_sends_its_id_as_the_enrolment_key_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: BuildAgentsKeys);
        run.Stub("GET", PendingPath, json: ApiJson.Page(ApiJson.PendingSystem("XYZ12")));

        var result = await run.RunAsync("system", "list", "--pending", "--key", "build agents");

        CliAssert.List(result, "pending-system");
        var requests = run.RequestsTo("GET", PendingPath);
        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].QueryValue("enrolment_key"), Is.EqualTo("12"));
            Assert.That(requests[0].QueryValue("search"), Is.Null);
            Assert.That(run.RequestsTo("GET", KeysPath).Select(request => request.QueryValue("include_disabled")), Is.All.EqualTo("True"));
        });
    }

    // --filter sends its text as typed, and --tag adds the API's tags key to the search
    // (proposed-cli-surface.md "Filters"). The waiting systems are searched with the enrolled
    // systems' search keys (portal UnapprovedSystems/Handlers/PaginatedUnapprovedSystemsHandler.cs
    // passes the search to the system repository).
    [TestCase("--filter laptop", "laptop")]
    [TestCase("--filter version:<2024.8.0", "version:<2024.8.0")]
    [TestCase("--tag kiosk", "tags:kiosk")]
    public async Task System_list_pending_sends_the_filter_and_tag_as_the_search(string flags, string search)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", PendingPath, ["system", "list", "--pending", .. flags.Split(' ')]);

        Assert.That(request.QueryValue("search"), Is.EqualTo(search));
    }

    // --sort takes UnapprovedSystemQuerySortMode's members (portal
    // UnapprovedSystems/Models/UnapprovedSystemQuerySortMode.cs), lower-case and hyphenated
    // (proposed-cli-surface.md "Options on every command").
    [TestCase("recently-enrolled", "RecentlyEnrolled")]
    [TestCase("description", "Description")]
    [TestCase("enrolment-key-used", "EnrolmentKeyUsed")]
    public async Task System_list_pending_sort_sends_the_api_sort_name(string sort, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", PendingPath, "system", "list", "--pending", "--sort", sort);

        Assert.That(request.QueryValue("sort"), Is.EqualTo(expected));
    }

    // With --pending, --sort takes the waiting systems' sort values (proposed-cli-surface.md
    // "Details"), and UnapprovedSystemQuerySortMode has no RecentlyConnected member.
    [Test]
    public async Task System_list_pending_with_a_sort_only_enrolled_systems_have_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--pending", "--sort", "recently-connected"],
            ["system", "list", "--pending", "--sort", "recently-enrolled"],
            "GET",
            PendingPath);
    }

    // --pending with an option only enrolled systems have exits 2 (proposed-cli-surface.md
    // "Details"). The corrected command leaves the option out.
    [TestCase("--include-disabled")]
    [TestCase("--state connected")]
    [TestCase("--os linux")]
    [TestCase("--type general")]
    [TestCase("--gateway")]
    [TestCase("--dns-name db.internal")]
    [TestCase("--not-seen-for 90d")]
    public async Task System_list_pending_with_an_option_only_enrolled_systems_have_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--pending", .. flags.Split(' ')],
            ["system", "list", "--pending"],
            "GET",
            PendingPath);
    }

    // --waiting-for without --pending exits 2 (proposed-cli-surface.md "Details"); enrolled systems
    // are not waiting.
    [Test]
    public async Task System_list_waiting_for_without_pending_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--waiting-for", "7d"],
            ["system", "list", "--pending", "--waiting-for", "7d"],
            "GET",
            PendingPath);
    }

    // --waiting-for has no API search key, so the CLI keeps the systems whose enrolledAt is longer ago
    // than the duration, from every page it reads, and total counts the matches
    // (proposed-cli-surface.md "Filters"). The matches are on both pages, so a CLI that filters one
    // page misses one.
    [Test]
    public async Task System_list_pending_waiting_for_keeps_the_systems_waiting_longer_and_counts_only_them_in_total()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        run.StubPages(
            PendingPath,
            2,
            Waiting("OLD01", now.AddDays(-10)),
            Waiting("NEW01", now.AddDays(-2)),
            Waiting("NEW02", now.AddHours(-1)),
            Waiting("OLD02", now.AddDays(-30)));

        var result = await run.RunAsync("system", "list", "--pending", "--waiting-for", "7d");

        var items = CliAssert.List(result, "pending-system");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("OLD01,OLD02"));
            Assert.That(run.PagesRequested(PendingPath), Is.EqualTo("0,1"));
        });
    }

    // A duration that reaches back before year 1 names no time the CLI can compare enrolledAt with
    // (DateTimeOffset.MinValue), so it is an argument error that names its option, and exits 2
    // before any call. 1000000d, about 2,700 years, reaches back before year 1 from the fixed clock
    // in 2030, in the test's own zone, TestData.LocalZone, as in Contract/TimeInputTests.cs.
    [Test]
    public async Task System_list_pending_waiting_for_a_duration_reaching_before_year_1_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(new DateTimeOffset(2030, 3, 14, 20, 0, 0, TimeSpan.Zero), TestData.LocalZone);
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        var rejected = await run.RunAsync("system", "list", "--pending", "--waiting-for", "1000000d");

        CliAssert.Rejected(run, rejected);
        Assert.That(JsonRead.PropertyNameList(rejected.Error.GetProperty("errors")), Is.EqualTo("--waiting-for"));
        await CliAssert.AcceptedAsync(run, "GET", PendingPath, "system", "list", "--pending", "--waiting-for", "7d");
    }

    // Example 3: the list system list --pending prints is the input of system approve, which
    // approves the listed systems. Both commands run in one sandbox, as a shell pipeline runs them.
    [Test]
    public async Task System_approve_given_the_waiting_systems_enrolled_with_key_12_approves_those_systems()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page(ApiJson.PendingSystem("XYZ12"), ApiJson.PendingSystem("XYZ13")));
        run.StubBulk("PUT", ApprovePath, "systemsApproved", 2);

        var listed = await run.RunAsync("system", "list", "--pending", "--key-id", "12");
        CliAssert.Succeeded(listed);
        run.StdinText = listed.Stdout;
        var result = await run.RunAsync("system", "approve", "-");

        CliAssert.Bulk(result, 2, 2);
        var approvals = run.RequestsTo("PUT", ApprovePath);
        Assert.That(approvals, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", PendingPath)[0].QueryValue("enrolment_key"), Is.EqualTo("12"));
            Assert.That(string.Join(",", approvals[0].BodyIds("systemIds")), Is.EqualTo("XYZ12,XYZ13"));
        });
    }

    // Example 5: the list --waiting-for prints is the input of system decline, which declines the
    // systems waiting more than a week and no others.
    [Test]
    public async Task System_decline_given_the_systems_waiting_more_than_a_week_declines_those_systems()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var waiting = ApiJson.Page(Waiting("OLD01", now.AddDays(-10)), Waiting("NEW01", now.AddDays(-2)), Waiting("OLD02", now.AddDays(-30)));
        run.Stub("GET", PendingPath, json: waiting);
        run.StubBulk("DELETE", PendingPath, "systemsDeclined", 2);

        var listed = await run.RunAsync("system", "list", "--pending", "--waiting-for", "7d");
        CliAssert.Succeeded(listed);
        run.StdinText = listed.Stdout;
        var result = await run.RunAsync("system", "decline", "-");

        CliAssert.Bulk(result, 2, 2);
        var declines = run.RequestsTo("DELETE", PendingPath);
        Assert.That(declines, Has.Count.EqualTo(1));
        Assert.That(string.Join(",", declines[0].BodyIds("systemIds")), Is.EqualTo("OLD01,OLD02"));
    }

    // show --pending reads the waiting system, never an enrolled one, and prints the model the API
    // returned.
    [Test]
    public async Task System_show_pending_gets_the_waiting_system_and_prints_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", $"{PendingPath}/XYZ12", json: ApiJson.PendingSystem("XYZ12", "lobby kiosk"));

        var result = await run.RunAsync("system", "show", "XYZ12", "--pending");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo($"{PendingPath}/XYZ12"));
            Assert.That(result.StdoutJson.GetProperty("systemId").GetString(), Is.EqualTo("XYZ12"));
            Assert.That(result.StdoutJson.GetProperty("description").GetString(), Is.EqualTo("lobby kiosk"));
            Assert.That(result.StdoutJson.GetProperty("enrolledFrom").GetString(), Is.EqualTo("203.0.113.10"));
        });
    }

    // An update sends only the fields given (proposed-cli-surface.md "Create and update").
    // Enclave.Sdk.Api 1.1.0 keys the patch body by UnapprovedSystemPatchModel's property names
    // (PatchClient.Set). The output is the model the API returned.
    [Test]
    public async Task System_update_pending_sends_the_description_notes_and_tags_to_the_waiting_system()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/XYZ12", json: ApiJson.PendingSystem("XYZ12", "from-the-api"));
        string[] fields = ["Description", "Notes", "Tags"];
        string[] arguments =
        [
            "system", "update", "XYZ12", "--pending",
            "--description", "lobby kiosk",
            "--notes", "awaiting IT",
            "--set-tags", "kiosk,lobby",
        ];

        var result = await run.RunAsync(arguments);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo($"{PendingPath}/XYZ12"));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("lobby kiosk"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("awaiting IT"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "Tags")), Is.EqualTo("kiosk,lobby"));
            Assert.That(result.StdoutJson.GetProperty("description").GetString(), Is.EqualTo("from-the-api"));
        });
    }

    // An update with no change flag exits 2 (proposed-cli-surface.md "Details"): there is nothing to
    // send.
    [Test]
    public async Task System_update_pending_without_a_change_flag_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/XYZ12", json: ApiJson.PendingSystem("XYZ12"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "update", "XYZ12", "--pending"],
            ["system", "update", "XYZ12", "--pending", "--notes", "awaiting IT"],
            "PATCH",
            $"{PendingPath}/XYZ12");
    }

    // Example 41: tag a system while it waits, then approve it, so it has its tags when it joins.
    [Test]
    public async Task System_update_pending_set_tags_then_approve_tags_the_waiting_system_and_approves_it()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/XYZ12", json: ApiJson.PendingSystem("XYZ12"));
        run.StubBulk("PUT", ApprovePath, "systemsApproved", 1);

        var updated = await run.RunAsync("system", "update", "XYZ12", "--pending", "--set-tags", "kiosk,lobby");
        CliAssert.Succeeded(updated);
        var result = await run.RunAsync("system", "approve", "XYZ12");

        CliAssert.Bulk(result, 1, 1);
        var requests = run.Requests;
        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].Method, Is.EqualTo("PATCH"));
            Assert.That(requests[0].Path, Is.EqualTo($"{PendingPath}/XYZ12"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(requests[0].BodyJson, "Tags")), Is.EqualTo("kiosk,lobby"));
            Assert.That(requests[1].Method, Is.EqualTo("PUT"));
            Assert.That(requests[1].Path, Is.EqualTo(ApprovePath));
            Assert.That(string.Join(",", requests[1].BodyIds("systemIds")), Is.EqualTo("XYZ12"));
        });
    }

    // --add-tags reads the waiting system, then patches its whole tag list, so the tags it has stay
    // (proposed-cli-surface.md "Create and update"). The read and the patch both go to the waiting
    // system. The order of the tags does not matter to the API, so they are compared sorted.
    [Test]
    public async Task System_update_pending_add_tags_reads_the_waiting_system_and_sends_its_tags_with_the_new_tag()
    {
        using var run = CliRun.Start();
        var path = $"{PendingPath}/XYZ12";
        run.Stub("GET", path, json: ApiJson.WithUsedTags(ApiJson.PendingSystem("XYZ12"), "kiosk"));
        run.Stub("PATCH", path, json: ApiJson.PendingSystem("XYZ12"));

        var result = await run.RunAsync("system", "update", "XYZ12", "--pending", "--add-tags", "lobby");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(" | ", run.Calls()), Is.EqualTo($"GET {path} | PATCH {path}"));
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("Tags"));
            Assert.That(string.Join(",", JsonAssert.Strings(JsonAssert.Property(patches[0].BodyJson, "Tags")).Order(StringComparer.Ordinal)), Is.EqualTo("kiosk,lobby"));
        });
    }

    // --pending with the gateway flags exits 2 (proposed-cli-surface.md "Details"): a waiting system
    // has no gateway routes to set (UnapprovedSystemPatchModel).
    [TestCase("--enable-gateway-for 10.0.0.0/16")]
    [TestCase("--disable-gateway")]
    public async Task System_update_pending_with_a_gateway_flag_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/XYZ12", json: ApiJson.PendingSystem("XYZ12"));
        run.Stub("GET", $"{PendingPath}/XYZ12", json: ApiJson.PendingSystem("XYZ12"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "update", "XYZ12", "--pending", .. flags.Split(' ')],
            ["system", "update", "XYZ12", "--pending", "--description", "lobby kiosk"],
            "PATCH",
            $"{PendingPath}/XYZ12");
    }

    // approve and decline act on waiting systems only (proposed-cli-surface.md "Commands"), in one
    // bulk call for any number of systems ("Several IDs"). Routes and result fields are those of
    // Enclave.Sdk.Api 1.1.0 UnapprovedSystemsClient (ApproveSystemsAsync, DeclineSystems). Bulk
    // approve succeeds when some systems were not approved (portal UnapprovedSystemsController.cs:
    // 170-177), so the API counts one of the two and the output carries its count.
    [TestCase("approve", "PUT", "unapproved-systems/approve", "systemsApproved")]
    [TestCase("decline", "DELETE", "unapproved-systems", "systemsDeclined")]
    public async Task System_approve_and_decline_send_every_system_given_in_one_bulk_call_and_print_requested_and_affected(
        string verb, string method, string pathSuffix, string resultField)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        var path = TestData.OrgPath(pathSuffix);
        run.StubBulk(method, path, resultField, 1);

        var result = await run.RunAsync("system", verb, "XYZ12", "XYZ13");

        CliAssert.Bulk(result, 2, 1);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(string.Join(",", request.BodyIds("systemIds")), Is.EqualTo("XYZ12,XYZ13"));
        });
    }

    // enrolledAt is a DateTime in UnapprovedSystemSummaryModel, written as ApiJson writes those, in
    // UTC with Z.
    private static string Waiting(string systemId, DateTime enrolledAt) =>
        ApiJson.Altered(
            ApiJson.PendingSystem(systemId),
            ("enrolledAt", enrolledAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
}
