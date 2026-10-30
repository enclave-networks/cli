using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// The API keeps systems waiting for approval apart from enrolled systems, under
// unapproved-systems.
public class SystemPendingCommandTests
{
    private static readonly string PendingPath = TestData.OrgPath("unapproved-systems");

    private static readonly string ApprovePath = TestData.OrgPath("unapproved-systems/approve");

    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string KeysPath = TestData.OrgPath("enrolment-keys");

    // The --waiting-for tests fix the clock, so each enrolledAt is a fixed time before it.
    private static readonly DateTimeOffset Now = new(2030, 3, 14, 20, 0, 0, TimeSpan.Zero);

    // Example 3, first command (Enclave.Sdk.Api UnapprovedSystemsClient.BuildQueryString).
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

    // The lookup asks for disabled keys too (spec/cli-surface.md "Filters").
    [Test]
    public async Task System_list_pending_key_looks_the_key_up_by_description_and_sends_its_id_as_the_enrolment_key_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.BuildAgentsKeys());
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

    // The API searches waiting systems with the enrolled systems' search keys (portal
    // PaginatedUnapprovedSystemsHandler passes the search to the system repository).
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

    // UnapprovedSystemQuerySortMode has none of these members (spec/cli-surface.md "Details").
    [TestCase("last-connected")]
    [TestCase("state")]
    [TestCase("version")]
    public async Task System_list_pending_with_a_sort_only_enrolled_systems_have_exits_2_without_a_request(string sort)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--pending", "--sort", sort],
            ["system", "list", "--pending", "--sort", "recently-enrolled"],
            "GET",
            PendingPath);
    }

    // spec/cli-surface.md "Details".
    [TestCase("--include-disabled")]
    [TestCase("--state connected")]
    [TestCase("--os linux")]
    [TestCase("--type general")]
    [TestCase("--gateway")]
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

    // Enrolled systems are not waiting (spec/cli-surface.md "Details").
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

    // The API has no search key for this, so the CLI filters every page it reads
    // (spec/cli-surface.md "Filters"); the matches are on all three pages. 7d before the clock is
    // 2030-03-07T20:00:00Z: EDGE01, enrolled then, has waited at least 7 days, and NEW01, a second
    // later, has not.
    [Test]
    public async Task System_list_pending_waiting_for_keeps_the_systems_waiting_longer_and_counts_only_them_in_total()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.StubPages(
            PendingPath,
            2,
            Waiting("OLD01", "2030-03-04T20:00:00Z"),
            Waiting("NEW01", "2030-03-07T20:00:01Z"),
            Waiting("NEW02", "2030-03-14T19:00:00Z"),
            Waiting("EDGE01", "2030-03-07T20:00:00Z"),
            Waiting("OLD02", "2030-02-12T20:00:00Z"));

        var result = await run.RunAsync("system", "list", "--pending", "--waiting-for", "7d");

        var items = CliAssert.List(result, "pending-system");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("OLD01,EDGE01,OLD02"));
            Assert.That(run.PagesRequested(PendingPath), Is.EqualTo("0,1,2"));
        });
    }

    // No DateTimeOffset is earlier than year 1, so the CLI has no time to compare enrolledAt with.
    // 1000000d is about 2,700 years.
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

    // Example 3, with both commands in one sandbox as a shell pipeline runs them.
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

    // Example 5, with both commands in one sandbox. OLD01 and OLD02 have waited 10 and 30 days, and
    // NEW01 2 days.
    [Test]
    public async Task System_decline_given_the_systems_waiting_more_than_a_week_declines_those_systems()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var waiting = ApiJson.Page(Waiting("OLD01", "2030-03-04T20:00:00Z"), Waiting("NEW01", "2030-03-12T20:00:00Z"), Waiting("OLD02", "2030-02-12T20:00:00Z"));
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

    // Enclave.Sdk.Api keys the patch body by UnapprovedSystemPatchModel's property names
    // (PatchClient.Set). The API answers with another description, so the output shows it is the
    // model returned.
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

    // spec/cli-surface.md "Details".
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

    // The API takes the whole tag list, so the CLI reads the waiting system's tags to keep them
    // (spec/cli-surface.md "Create and update"). Tags are compared sorted, since their order does
    // not matter to the API.
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

    // A waiting system has no gateway routes to set (UnapprovedSystemPatchModel; spec/cli-surface.md
    // "Details").
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

    // enrolledAt is a DateTime in UnapprovedSystemSummaryModel, which ApiJson writes in UTC with Z.
    private static string Waiting(string systemId, string enrolledAt) =>
        ApiJson.Altered(ApiJson.PendingSystem(systemId), ("enrolledAt", enrolledAt));
}
