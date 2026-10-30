using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

public class SystemCommandTests
{
    // 20:00 UTC is 01:30 the next day in TestData.LocalZone (UTC+05:30), so a time read in the
    // wrong zone gives a different instant and date.
    private static readonly DateTimeOffset Now = new(2030, 3, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string KeysPath = TestData.OrgPath("enrolment-keys");

    // Without filter flags the CLI sends no search, key or DNS filter, and leaves disabled systems
    // out.
    [Test]
    public async Task System_list_reads_every_page_and_prints_every_system_as_a_system_list()
    {
        using var run = CliRun.Start();
        var ids = TestData.Ids("system", 5);
        run.StubPages(SystemsPath, 2, ids.Select(id => ApiJson.System(id)).ToArray());

        var result = await run.RunAsync("system", "list");

        var items = CliAssert.List(result, "system");
        var requests = run.RequestsTo("GET", SystemsPath);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo(string.Join(",", ids)));
            Assert.That(run.PagesRequested(SystemsPath), Is.EqualTo("0,1,2"));
            Assert.That(requests.Select(request => request.QueryValue("search")), Is.All.Null);
            Assert.That(requests.Select(request => request.QueryValue("enrolment_key")), Is.All.Null);
            Assert.That(requests.Select(request => request.QueryValue("dns")), Is.All.Null);
            Assert.That(requests.Select(request => request.QueryValue("include_disabled")), Has.None.EqualTo("True"));
        });
    }

    // The text takes the API's search syntax as well as plain words, as the portal's search box
    // does (spec/cli-surface.md "Filters").
    [TestCase("web tags:|prod,staging version:>2024.8.0")]
    [TestCase("db-01")]
    public async Task System_list_sends_the_filter_text_as_the_search_exactly_as_typed(string filter)
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--filter", filter);

        Assert.That(request.QueryValue("search"), Is.EqualTo(filter));
    }

    // Each flag adds one of the API's system search keys (portal SystemSearchKeyService) in the
    // form spec/cli-surface.md "Filters" gives it. --os sends the API's own value, which the API
    // matches exactly. Several tags make one comma list, which the API reads as every tag (portal
    // BaseSearchKeyService.BuildFilterAsync). Examples 37 and 2 are the --gateway case and the
    // first combined case.
    [TestCase("--tag build", "tags:build")]
    [TestCase("--tag web,db", "tags:web,db")]
    [TestCase("--state connected", "state:connected")]
    [TestCase("--os linux", "os:Linux")]
    [TestCase("--type general", "type:general")]
    [TestCase("--gateway", "gateway:true")]
    [TestCase("--tag build --os linux --state connected", "tags:build os:Linux state:connected")]
    [TestCase("--filter web --tag prod --gateway", "web tags:prod gateway:true")]
    [TestCase("--filter version:<2024.8.0 --type ephemeral", "version:<2024.8.0 type:ephemeral")]
    public async Task System_list_adds_the_search_key_flags_to_the_search_with_the_filter_text(string flags, string search)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, ["system", "list", .. flags.Split(' ')]);

        Assert.That(SearchTerms.Of(request.QueryValue("search")), Is.EqualTo(SearchTerms.Of(search)));
    }

    // The API filters by key with its enrolment_key parameter (portal
    // SystemsRequestModel.EnrolmentKey), so the search stays empty.
    [Test]
    public async Task System_list_key_id_sends_the_enrolment_key_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--key-id", "12");

        Assert.Multiple(() =>
        {
            Assert.That(request.QueryValue("enrolment_key"), Is.EqualTo("12"));
            Assert.That(request.QueryValue("search"), Is.Null);
        });
    }

    // The lookup asks for disabled keys too, so the systems of a key since disabled can be found
    // (spec/cli-surface.md "Filters").
    [Test]
    public async Task System_list_key_looks_the_key_up_by_description_and_sends_its_id_as_the_enrolment_key_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.BuildAgentsKeys());
        run.Stub("GET", SystemsPath, json: ApiJson.Page(ApiJson.System("ABCDE")));

        var result = await run.RunAsync("system", "list", "--key", "build agents");

        var items = CliAssert.List(result, "system");
        var requests = run.RequestsTo("GET", SystemsPath);
        Assert.That(requests, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].QueryValue("enrolment_key"), Is.EqualTo("12"));
            Assert.That(requests[0].QueryValue("search"), Is.Null);
            Assert.That(run.RequestsTo("GET", KeysPath).Select(request => request.QueryValue("include_disabled")), Is.All.EqualTo("True"));
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("ABCDE"));
        });
    }

    // "build" is a description, which --key-id never takes (spec/cli-surface.md "ID checks").
    [Test]
    public async Task System_list_key_id_that_is_not_an_integer_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--key-id", "build"],
            ["system", "list", "--key-id", "12"],
            "GET",
            SystemsPath);
    }

    // The API binds its dns parameter (portal SystemsRequestModel.DnsName) but its list handler
    // never reads it (PaginatedSystemsHandler), so a filter sent there would list every system as a
    // match (spec/cli-surface.md "Left out").
    [Test]
    public async Task System_list_dns_name_is_an_unknown_option_and_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page(ApiJson.System("ABCDE")));

        var result = await run.RunAsync("system", "list", "--dns-name", "db.internal");

        CliAssert.Rejected(run, result);
        Assert.Multiple(() =>
        {
            CliAssert.Problems(result.Error, "--dns-name");
            Assert.That(result.Error.GetProperty("detail").GetString(), Does.Contain("--dns-name"));
        });
    }

    // Enclave.Sdk.Api writes the flag with bool.ToString() (SystemsClient.BuildQueryString),
    // which gives "True".
    [Test]
    public async Task System_list_include_disabled_sends_include_disabled_true()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--include-disabled");

        Assert.That(request.QueryValue("include_disabled"), Is.EqualTo("True"));
    }

    // "1" is SystemQuerySortMode's value for RecentlyConnected, which a CLI that parses numeric enum
    // values would accept. --state disabled belongs to key list and policy list; a system's state is
    // connected or disconnected (spec/cli-surface.md "Filters").
    [TestCase("--sort 1")]
    [TestCase("--state disabled")]
    [TestCase("--os freebsd")]
    [TestCase("--type permanent")]
    [TestCase("--not-seen-for long")]
    public async Task System_list_with_a_value_its_option_does_not_take_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", .. flags.Split(' ')],
            ["system", "list"],
            "GET",
            SystemsPath);
    }

    // The API has no search key for this, so the CLI filters every page it reads
    // (spec/cli-surface.md "Filters"). RECENT enrolled long ago, so a CLI that reads enrolledAt of a
    // system that has been seen lists it. The matches are on all three pages, so a CLI that filters
    // one page misses some.
    [Test]
    public async Task System_list_not_seen_for_keeps_the_systems_last_seen_longer_ago_and_counts_only_them_in_total()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.StubPages(
            SystemsPath,
            2,
            SystemSeen("STALE1", "Disconnected", Now.AddDays(-100)),
            SystemSeen("ONLINE", "Connected", Now.AddMinutes(-1)),
            SystemSeen("RECENT", "Disconnected", Now.AddDays(-10)),
            SystemSeen("STALE2", "Disconnected", Now.AddDays(-120)),
            SystemSeen("STALE3", "Disconnected", Now.AddDays(-91)));

        var result = await run.RunAsync("system", "list", "--not-seen-for", "90d");

        var items = CliAssert.List(result, "system");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("STALE1,STALE2,STALE3"));
            Assert.That(run.PagesRequested(SystemsPath), Is.EqualTo("0,1,2"));
        });
    }

    // Counting from enrolment keeps a system that has just enrolled, and not yet connected, off a
    // list meant for removal (spec/cli-surface.md "Filters").
    [Test]
    public async Task System_list_not_seen_for_counts_a_system_never_seen_from_when_it_enrolled()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var systems = ApiJson.Page(SystemNeverSeen("NEWBIE", Now.AddDays(-1)), SystemNeverSeen("ABANDONED", Now.AddDays(-100)));
        run.Stub("GET", SystemsPath, json: systems);

        var result = await run.RunAsync("system", "list", "--not-seen-for", "90d");

        var items = CliAssert.List(result, "system");
        Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("ABANDONED"));
    }

    // lastSeen is when the discovery service last wrote the system's session, which a system that
    // stays connected can go long without (portal SystemExtensions.CreateBasicModel; services
    // PeerSessionReporter.HandleUpdateSessionAsync). The portal shows lastSeen only for a
    // disconnected system (portal-spa Systems/Columns/Status.tsx, getStatusText). A CLI that reads
    // lastSeen or enrolledAt alone lists LIVE and LIVENEW.
    [Test]
    public async Task System_list_not_seen_for_never_lists_a_connected_system_whatever_its_last_seen()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var systems = ApiJson.Page(
            SystemSeen("LIVE", "Connected", Now.AddDays(-200)),
            ApiJson.Altered(SystemNeverSeen("LIVENEW", Now.AddDays(-200)), ("state", "Connected")),
            SystemSeen("STALE", "Disconnected", Now.AddDays(-100)));
        run.Stub("GET", SystemsPath, json: systems);

        var result = await run.RunAsync("system", "list", "--not-seen-for", "90d");

        var items = CliAssert.List(result, "system");
        Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("STALE"));
    }

    // state:disconnected matches on the session whether or not the system is enabled, so it
    // keeps out a disabled system that is online, which the API reports as Disabled (portal
    // SystemSearchKeyService, the "state" key; SystemExtensions). The fake answers any other search
    // with LIVEOFF, such a system, which example 14 would revoke.
    [TestCase("--not-seen-for 90d")]
    [TestCase("--not-seen-for 90d --include-disabled")]
    public async Task System_list_not_seen_for_asks_the_api_for_disconnected_systems_only(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var stale = SystemSeen("STALE", "Disconnected", Now.AddDays(-100));
        var liveDisabled = ApiJson.Altered(SystemSeen("LIVEOFF", "Disabled", Now.AddDays(-200)), ("isEnabled", false));
        run.Stub("GET", SystemsPath, json: ApiJson.Page(stale, liveDisabled));
        run.StubWithQuery("GET", SystemsPath, "search", "state:disconnected", json: ApiJson.Page(stale));

        var result = await run.RunAsync(["system", "list", .. flags.Split(' ')]);

        var items = CliAssert.List(result, "system");
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().QueryValue("search"), Is.EqualTo("state:disconnected"));
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("STALE"));
        });
    }

    // The API reads only the first state: term of a search (portal
    // BaseSearchKeyService.AddOrUpdateQueryDetails; SystemSearchKeyService, the "state" key reads
    // Values[0]), so a state: term in the filter cannot turn the search to connected systems. The
    // fake answers any other search with LIVEOFF, a connected system, as the API answers one whose
    // first state: term is state:connected.
    [Test]
    public async Task System_list_not_seen_for_sends_state_disconnected_ahead_of_the_filter_text()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var stale = SystemSeen("STALE", "Disconnected", Now.AddDays(-100));
        var liveDisabled = ApiJson.Altered(SystemSeen("LIVEOFF", "Disabled", Now.AddDays(-200)), ("isEnabled", false));
        run.Stub("GET", SystemsPath, json: ApiJson.Page(liveDisabled));
        run.StubWithQuery("GET", SystemsPath, "search", "state:disconnected state:connected web", json: ApiJson.Page(stale));

        var result = await run.RunAsync("system", "list", "--filter", "state:connected web", "--not-seen-for", "90d", "--include-disabled");

        var items = CliAssert.List(result, "system");
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().QueryValue("search"), Is.EqualTo("state:disconnected state:connected web"));
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("STALE"));
        });
    }

    // --not-seen-for lists disconnected systems only (spec/cli-surface.md "Filters"). A CLI that
    // read the token first exits 3 in the run without one.
    [Test]
    public async Task System_list_state_connected_with_not_seen_for_exits_2_before_the_token_and_any_call()
    {
        using var run = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");
        run.Stub("GET", SystemsPath, json: ApiJson.Page());
        string[] contradicting = ["system", "list", "--state", "connected", "--not-seen-for", "1d"];

        var tokenless = await withoutToken.RunAsync(contradicting);

        CliAssert.Rejected(withoutToken, tokenless);
        CliAssert.Problems(tokenless.Error, "--state");
        await CliAssert.RejectedThenAcceptedAsync(run, contradicting, ["system", "list", "--not-seen-for", "1d"], "GET", SystemsPath);
    }

    // --state disconnected asks for the systems --not-seen-for asks for, so the pair is accepted
    // (spec/cli-surface.md "Filters").
    [Test]
    public async Task System_list_state_disconnected_with_not_seen_for_sends_state_disconnected_once_ahead_of_the_filter_text()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--filter", "web", "--state", "disconnected", "--not-seen-for", "1d");

        Assert.That(request.QueryValue("search"), Is.EqualTo("state:disconnected web"));
    }

    // No DateTimeOffset is earlier than year 1, so the CLI has no time to compare lastSeen with.
    // 1000000d is about 2,700 years.
    [Test]
    public async Task System_list_not_seen_for_a_duration_reaching_before_year_1_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var rejected = await run.RunAsync("system", "list", "--not-seen-for", "1000000d");

        CliAssert.Rejected(run, rejected);
        CliAssert.Problems(rejected.Error, "--not-seen-for");
        await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--not-seen-for", "90d");
    }

    // Example 14, with both commands in one sandbox as a shell pipeline runs them. LIVE is
    // connected, so it is not revoked whatever its lastSeen. The route and result field are those
    // of Enclave.Sdk.Api SystemsClient.RevokeSystemsAsync.
    [Test]
    public async Task System_revoke_given_the_list_of_systems_not_seen_for_90_days_revokes_those_systems()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var systems = ApiJson.Page(
            SystemSeen("STALE1", "Disconnected", Now.AddDays(-100)),
            SystemSeen("RECENT", "Disconnected", Now.AddDays(-10)),
            SystemSeen("LIVE", "Connected", Now.AddDays(-300)),
            SystemSeen("STALE2", "Disconnected", Now.AddDays(-200)),
            SystemNeverSeen("NEWBIE", Now.AddHours(-1)));
        run.Stub("GET", SystemsPath, json: systems);
        run.StubBulk("DELETE", SystemsPath, "systemsRevoked", 2);

        var listed = await run.RunAsync("system", "list", "--not-seen-for", "90d");
        CliAssert.Succeeded(listed);
        run.StdinText = listed.Stdout;
        var result = await run.RunAsync("system", "revoke", "-");

        CliAssert.Bulk(result, 2, 2);
        var revokes = run.RequestsTo("DELETE", SystemsPath);
        Assert.That(revokes, Has.Count.EqualTo(1));
        Assert.That(string.Join(",", revokes[0].BodyIds("systemIds")), Is.EqualTo("STALE1,STALE2"));
    }

    // Enclave.Sdk.Api keys a patch body by SystemPatchModel's property names (PatchClient.Set).
    // The API answers with another description, so the output shows it is the model returned.
    [Test]
    public async Task System_update_sends_the_description_and_notes_as_the_only_patch_fields_and_prints_the_updated_system()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE", "from-the-api"));
        string[] fields = ["Description", "Notes"];

        var result = await run.RunAsync("system", "update", "ABCDE", "--description", "web-02", "--notes", "rack 4");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo($"{SystemsPath}/ABCDE"));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("web-02"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("rack 4"));
            Assert.That(result.StdoutJson.GetProperty("description").GetString(), Is.EqualTo("from-the-api"));
        });
    }

    // spec/cli-surface.md "Details".
    [Test]
    public async Task System_update_without_a_change_flag_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "update", "ABCDE"],
            ["system", "update", "ABCDE", "--notes", "rack 4"],
            "PATCH",
            $"{SystemsPath}/ABCDE");
    }

    // It replaces the whole list, so it needs no read (spec/cli-surface.md "Calls per command").
    [Test]
    public async Task System_update_set_tags_sends_the_tags_as_the_whole_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", $"{SystemsPath}/ABCDE", "system", "update", "ABCDE", "--set-tags", "web,prod");

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("Tags"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(request.BodyJson, "Tags")), Is.EqualTo("web,prod"));
        });
    }

    // The API takes the whole tag list, so the CLI reads the system's tags to keep them
    // (spec/cli-surface.md "Create and update"). Example 10.
    [Test]
    public async Task System_update_add_tags_reads_the_system_and_sends_its_tags_with_the_new_tag()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/ABCDE";
        run.Stub("GET", path, json: ApiJson.WithUsedTags(ApiJson.System("ABCDE"), "web", "prod"));
        run.Stub("PATCH", path, json: ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "update", "ABCDE", "--add-tags", "monitoring");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(" | ", run.Calls()), Is.EqualTo($"GET {path} | PATCH {path}"));
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("Tags"));
            Assert.That(SortedStrings(JsonAssert.Property(patches[0].BodyJson, "Tags")), Is.EqualTo("monitoring,prod,web"));
        });
    }

    // Example 42.
    [Test]
    public async Task System_update_remove_tags_reads_the_system_and_sends_its_tags_without_the_removed_tag()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/ABCDE";
        run.Stub("GET", path, json: ApiJson.WithUsedTags(ApiJson.System("ABCDE"), "web", "legacy", "prod"));
        run.Stub("PATCH", path, json: ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "update", "ABCDE", "--remove-tags", "legacy");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(" | ", run.Calls()), Is.EqualTo($"GET {path} | PATCH {path}"));
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("Tags"));
            Assert.That(SortedStrings(JsonAssert.Property(patches[0].BodyJson, "Tags")), Is.EqualTo("prod,web"));
        });
    }

    // New routes go with userEntered true and weight 0, as the portal sends them (portal-spa
    // redux/reducers/app/detailTab/systems.ts, createSystemDetailsRoute). The second label holds a
    // comma, which is why the flag is repeated and never a comma list. Example 75.
    [Test]
    public async Task System_update_enable_gateway_for_sends_each_subnet_as_a_new_user_entered_route_named_by_its_label()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/GW001";
        run.Stub("GET", path, json: ApiJson.System("GW001"));
        run.Stub("PATCH", path, json: ApiJson.System("GW001"));
        string[] arguments =
        [
            "system", "update", "GW001",
            "--enable-gateway-for", "10.0.0.0/16=Office LAN",
            "--enable-gateway-for", "10.1.0.0/16=Warehouse, ground floor",
        ];

        var result = await run.RunAsync(arguments);

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var routes = JsonAssert.Property(patches[0].BodyJson, "GatewayRoutes");
        Assert.That(routes.GetArrayLength(), Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(" | ", run.Calls()), Is.EqualTo($"GET {path} | PATCH {path}"));
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("GatewayRoutes"));
            Assert.That(JsonRead.StringFieldList(routes, "subnet"), Is.EqualTo("10.0.0.0/16,10.1.0.0/16"));
            Assert.That(JsonAssert.Property(routes[0], "name").GetString(), Is.EqualTo("Office LAN"));
            Assert.That(JsonAssert.Property(routes[1], "name").GetString(), Is.EqualTo("Warehouse, ground floor"));
            Assert.That(routes.EnumerateArray().Select(route => JsonAssert.Property(route, "userEntered").GetBoolean()), Is.All.True);
            Assert.That(JsonRead.IntFieldList(routes, "weight"), Is.EqualTo("0,0"));
        });
    }

    // The portal keeps found routes the same way (portal-spa
    // redux/reducers/app/detailTab/systems.ts, setSystemDetailsKnownRoutes). Routes are found by
    // subnet, since their order does not matter to the API. Example 25.
    [Test]
    public async Task System_update_enable_gateway_for_replaces_the_routes_a_user_entered_and_keeps_those_the_system_found()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/GW001";
        var gateway = SystemWithRoutes(
            "GW001",
            GatewayRoute("192.168.1.0/24", userEntered: true, weight: 0, "Old office"),
            GatewayRoute("172.16.0.0/12", userEntered: false, weight: 3, "Found LAN"));
        run.Stub("GET", path, json: gateway);
        run.Stub("PATCH", path, json: ApiJson.System("GW001"));

        var result = await run.RunAsync("system", "update", "GW001", "--enable-gateway-for", "10.0.0.0/16=Office LAN");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var routes = JsonAssert.Property(patches[0].BodyJson, "GatewayRoutes");
        Assert.That(SortedSubnets(routes), Is.EqualTo("10.0.0.0/16,172.16.0.0/12"));
        var added = Route(routes, "10.0.0.0/16");
        var found = Route(routes, "172.16.0.0/12");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(added, "name").GetString(), Is.EqualTo("Office LAN"));
            Assert.That(JsonAssert.Property(added, "userEntered").GetBoolean(), Is.True);
            Assert.That(JsonAssert.Property(found, "userEntered").GetBoolean(), Is.False);
            Assert.That(JsonAssert.Property(found, "weight").GetInt32(), Is.EqualTo(3));
            Assert.That(JsonAssert.Property(found, "name").GetString(), Is.EqualTo("Found LAN"));
        });
    }

    // Sent as a user-entered route as well, the subnet would be in the list twice, which the API
    // refuses (portal SystemPatchModelValidator, MustNotHaveDuplicates on GatewayRoutes); sent as a
    // user-entered route alone, the system would no longer have it as found.
    [Test]
    public async Task System_update_enable_gateway_for_a_subnet_the_system_found_sends_one_found_route_with_its_weight_and_the_new_label()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/GW001";
        run.Stub("GET", path, json: SystemWithRoutes("GW001", GatewayRoute("172.16.0.0/12", userEntered: false, weight: 3, "Found LAN")));
        run.Stub("PATCH", path, json: ApiJson.System("GW001"));

        var result = await run.RunAsync("system", "update", "GW001", "--enable-gateway-for", "172.16.0.0/12=Warehouse");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var routes = JsonAssert.Property(patches[0].BodyJson, "GatewayRoutes");
        Assert.That(routes.GetArrayLength(), Is.EqualTo(1));
        var route = routes[0];
        Assert.Multiple(() =>
        {
            Assert.That(route.GetProperty("subnet").GetString(), Is.EqualTo("172.16.0.0/12"));
            Assert.That(route.GetProperty("userEntered").GetBoolean(), Is.False);
            Assert.That(route.GetProperty("weight").GetInt32(), Is.EqualTo(3));
            Assert.That(route.GetProperty("name").GetString(), Is.EqualTo("Warehouse"));
        });
    }

    // The weight is the route's preference over other gateways' routes to the same subnet (portal
    // SystemGatewayRouteModel.Weight).
    [Test]
    public async Task System_update_enable_gateway_for_keeps_the_weight_and_label_of_a_route_that_stays()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/GW001";
        run.Stub("GET", path, json: SystemWithRoutes("GW001", GatewayRoute("10.0.0.0/16", userEntered: true, weight: 5, "Office LAN")));
        run.Stub("PATCH", path, json: ApiJson.System("GW001"));

        var result = await run.RunAsync("system", "update", "GW001", "--enable-gateway-for", "10.0.0.0/16", "--enable-gateway-for", "10.1.0.0/16");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var routes = JsonAssert.Property(patches[0].BodyJson, "GatewayRoutes");
        Assert.That(SortedSubnets(routes), Is.EqualTo("10.0.0.0/16,10.1.0.0/16"));
        var stays = Route(routes, "10.0.0.0/16");
        var added = Route(routes, "10.1.0.0/16");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(stays, "weight").GetInt32(), Is.EqualTo(5));
            Assert.That(JsonAssert.Property(stays, "name").GetString(), Is.EqualTo("Office LAN"));
            Assert.That(JsonAssert.Property(added, "weight").GetInt32(), Is.Zero);
            Assert.That(JsonAssert.Property(added, "userEntered").GetBoolean(), Is.True);
        });
    }

    // No name and a null name both leave the route without a label.
    [Test]
    public async Task System_update_enable_gateway_for_a_subnet_without_a_label_sends_a_route_without_a_name()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/GW001";
        run.Stub("GET", path, json: ApiJson.System("GW001"));
        run.Stub("PATCH", path, json: ApiJson.System("GW001"));

        var result = await run.RunAsync("system", "update", "GW001", "--enable-gateway-for", "10.2.0.0/16");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var routes = JsonAssert.Property(patches[0].BodyJson, "GatewayRoutes");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(routes, "subnet"), Is.EqualTo("10.2.0.0/16"));
            Assert.That(routes[0].TryGetProperty("name", out var name) ? name.ValueKind : JsonValueKind.Null, Is.EqualTo(JsonValueKind.Null));
        });
    }

    // It has no labels to keep, so it makes no read (spec/cli-surface.md "Calls per command").
    // Example 43.
    [Test]
    public async Task System_update_disable_gateway_sends_an_empty_gateway_route_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/GW001", json: ApiJson.System("GW001"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", $"{SystemsPath}/GW001", "system", "update", "GW001", "--disable-gateway");

        var routes = JsonAssert.Property(request.BodyJson, "GatewayRoutes");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("GatewayRoutes"));
            Assert.That(routes.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(routes.GetArrayLength(), Is.Zero);
        });
    }

    // The API refuses a route list that holds a subnet twice (portal SystemPatchModelValidator,
    // MustNotHaveDuplicates on Subnet). The error leaves the subnet out: the option checks no format,
    // so its value can be a token passed by mistake (spec/cli-surface.md "Errors and exit codes").
    [TestCase("10.0.0.0/16", "10.0.0.0/16=Office")]
    [TestCase("10.0.0.0/16=Office", "10.0.0.0/16=Office")]
    [TestCase("10.0.0.0/16", "10.0.0.0/16")]
    public async Task System_update_enable_gateway_for_a_subnet_given_twice_exits_2_without_a_request(string first, string second)
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/GW001";
        run.Stub("GET", path, json: ApiJson.System("GW001"));
        run.Stub("PATCH", path, json: ApiJson.System("GW001"));

        var rejected = await run.RunAsync("system", "update", "GW001", "--enable-gateway-for", first, "--enable-gateway-for", "10.1.0.0/16", "--enable-gateway-for", second);
        CliAssert.Rejected(run, rejected);
        Assert.Multiple(() =>
        {
            CliAssert.Problems(rejected.Error, "--enable-gateway-for");
            Assert.That(rejected.Stderr, Does.Not.Contain("10.0.0.0"));
        });

        var corrected = await run.RunAsync("system", "update", "GW001", "--enable-gateway-for", "10.1.0.0/16", "--enable-gateway-for", second);
        CliAssert.Succeeded(corrected);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.That(SortedSubnets(JsonAssert.Property(patches[0].BodyJson, "GatewayRoutes")), Is.EqualTo("10.0.0.0/16,10.1.0.0/16"));
    }

    // spec/cli-surface.md "Details".
    [Test]
    public async Task System_update_enable_gateway_for_with_disable_gateway_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", $"{SystemsPath}/GW001", json: ApiJson.System("GW001"));
        run.Stub("PATCH", $"{SystemsPath}/GW001", json: ApiJson.System("GW001"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "update", "GW001", "--enable-gateway-for", "10.0.0.0/16", "--disable-gateway"],
            ["system", "update", "GW001", "--disable-gateway"],
            "PATCH",
            $"{SystemsPath}/GW001");
    }

    // The API's timed enable has no bulk form, so --for takes one system and prints its model
    // (spec/cli-surface.md "Several IDs"). Example 7.
    [Test]
    public async Task System_enable_for_a_duration_then_revoke_puts_enable_until_with_the_delete_action_and_prints_the_system()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{SystemsPath}/K7P2Q/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("K7P2Q", "visitor laptop"));

        var result = await run.RunAsync("system", "enable", "K7P2Q", "--for", "24h", "--then", "revoke");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(expiry, Is.EqualTo(Now.AddHours(24)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Delete"));
            Assert.That(result.StdoutJson.GetProperty("systemId").GetString(), Is.EqualTo("K7P2Q"));
            Assert.That(result.StdoutJson.GetProperty("description").GetString(), Is.EqualTo("visitor laptop"));
        });
    }

    // Without --then the system is disabled at expiry (spec/cli-surface.md "Command options").
    [TestCase("--for 30m", 30, "Disable")]
    [TestCase("--for 8h --then disable", 8 * 60, "Disable")]
    [TestCase("--for 14d --then REVOKE", 14 * 24 * 60, "Delete")]
    public async Task System_enable_for_a_duration_sends_an_expiry_that_long_from_now_with_the_then_action(string flags, int minutes, string action)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{SystemsPath}/K7P2Q/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("K7P2Q"));

        var request = await CliAssert.AcceptedAsync(run, "PUT", path, ["system", "enable", "K7P2Q", .. flags.Split(' ')]);

        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(Now + TimeSpan.FromMinutes(minutes)));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo(action));
        });
    }

    [TestCase("2036-10-09T17:30:00Z")]
    [TestCase("2036-10-09T13:30:00-04:00")]
    public async Task System_enable_until_a_time_with_a_zone_sends_that_instant_in_utc(string until)
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{SystemsPath}/M4R8T/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("M4R8T"));

        var request = await CliAssert.AcceptedAsync(run, "PUT", path, "system", "enable", "M4R8T", "--until", until);

        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2036, 10, 9, 17, 30, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Disable"));
        });
    }

    // 17:30 in TestData.LocalZone (UTC+05:30) is 12:00 UTC. Example 12, in a year after the fixed
    // clock.
    [Test]
    public async Task System_enable_until_a_time_without_a_zone_reads_it_in_the_local_time_zone()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{SystemsPath}/M4R8T/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("M4R8T"));

        var request = await CliAssert.AcceptedAsync(run, "PUT", path, "system", "enable", "M4R8T", "--until", "2036-10-09T17:30");

        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2036, 10, 9, 12, 0, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Disable"));
        });
    }

    // The clock reads 01:30 on the 15th in the test's zone, so the next 18:00 is later that day,
    // 12:30 UTC.
    [Test]
    public async Task System_enable_until_a_clock_time_sends_its_next_occurrence_in_the_local_time_zone()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{SystemsPath}/M4R8T/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("M4R8T"));

        var request = await CliAssert.AcceptedAsync(run, "PUT", path, "system", "enable", "M4R8T", "--until", "18:00");

        Assert.That(JsonRead.ExpiryDateTime(request.BodyJson), Is.EqualTo(new DateTimeOffset(2030, 3, 15, 12, 30, 0, TimeSpan.Zero)));
    }

    // In the hour repeated when the clocks go back, 01:30 happens twice, and the next occurrence is
    // the earlier one. The clock reads the first 01:15 of that night in TestData.DaylightSavingZone
    // (00:15 UTC), so 01:30 is 00:30 UTC; read as standard time it would be 01:30 UTC.
    [Test]
    public async Task System_enable_until_a_clock_time_in_the_repeated_hour_sends_its_next_occurrence()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 25, 0, 15, 0, TimeSpan.Zero), TestData.DaylightSavingZone);
        var path = $"{SystemsPath}/M4R8T/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("M4R8T"));

        var request = await CliAssert.AcceptedAsync(run, "PUT", path, "system", "enable", "M4R8T", "--until", "01:30");

        Assert.That(JsonRead.ExpiryDateTime(request.BodyJson), Is.EqualTo(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero)));
    }

    // spec/cli-surface.md "Details". 5000000d, about 13,700 years, reaches past the last time a
    // DateTimeOffset holds.
    [TestCase("--for 24h --until 2036-10-09T17:30:00Z")]
    [TestCase("--then revoke")]
    [TestCase("--then disable")]
    [TestCase("--until 2020-01-01T00:00:00Z")]
    [TestCase("--for tomorrow")]
    [TestCase("--until someday")]
    [TestCase("--for 5000000d")]
    public async Task System_enable_with_invalid_or_contradicting_timing_flags_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("PUT", $"{SystemsPath}/K7P2Q/enable-until", json: ApiJson.System("K7P2Q"));
        run.Stub("PUT", $"{SystemsPath}/enable", json: ApiJson.Bulk("systemsUpdated", 1));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "enable", "K7P2Q", .. flags.Split(' ')],
            ["system", "enable", "K7P2Q", "--for", "24h"],
            "PUT",
            $"{SystemsPath}/K7P2Q/enable-until");
    }

    // A timed enable takes one system, so an unknown ID exits 5 (spec/cli-surface.md "Several
    // IDs").
    [Test]
    public async Task System_enable_for_a_duration_of_an_unknown_system_exits_5_with_not_found()
    {
        using var run = CliRun.Start();
        run.StubProblem("PUT", $"{SystemsPath}/ZZZZZ/enable-until", 404, "Not Found", "No such system.");

        var result = await run.RunAsync("system", "enable", "ZZZZZ", "--for", "24h");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo($"{SystemsPath}/ZZZZZ/enable-until"));
    }

    // The API does not depend on the order of the tags.
    private static string SortedStrings(JsonElement array) =>
        string.Join(",", JsonAssert.Strings(array).Order(StringComparer.Ordinal));

    private static string SortedSubnets(JsonElement routes) =>
        string.Join(",", routes.EnumerateArray().Select(route => JsonAssert.Property(route, "subnet").GetString()).Order(StringComparer.Ordinal));

    private static JsonElement Route(JsonElement routes, string subnet) =>
        routes.EnumerateArray().Single(route => string.Equals(JsonAssert.Property(route, "subnet").GetString(), subnet, StringComparison.Ordinal));

    // lastSeen is a DateTimeOffset in SystemSummaryModel, which ApiJson writes in UTC with +00:00.
    // ApiJson.System's enrolledAt is long before these tests' times.
    private static string SystemSeen(string systemId, string state, DateTimeOffset lastSeen) =>
        ApiJson.Altered(
            ApiJson.System(systemId),
            ("state", state),
            ("lastSeen", Utc(lastSeen)));

    private static string SystemNeverSeen(string systemId, DateTimeOffset enrolledAt) =>
        ApiJson.Altered(
            ApiJson.System(systemId),
            ("state", "Disconnected"),
            ("connectedAt", null),
            ("lastSeen", null),
            ("enrolledAt", Utc(enrolledAt)));

    private static string Utc(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    // The fields of portal SystemGatewayRouteModel.
    private static JsonObject GatewayRoute(string subnet, bool userEntered, int weight, string name) =>
        new() { ["subnet"] = subnet, ["userEntered"] = userEntered, ["weight"] = weight, ["name"] = name };

    private static string SystemWithRoutes(string systemId, params JsonObject[] routes)
    {
        var array = new JsonArray();

        foreach (var route in routes)
        {
            array.Add(route);
        }

        return ApiJson.Altered(ApiJson.System(systemId), ("gatewayRoutes", array));
    }
}
