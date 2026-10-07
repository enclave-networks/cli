using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

public class SystemCommandTests
{
    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string KeysPath = TestData.OrgPath("enrolment-keys");

    // A key whose description starts with the same words is listed first, so a CLI that takes a
    // partial match, or the first item, finds key 13.
    private static readonly string BuildAgentsKeys = ApiJson.Page(ApiJson.Key(13, "build agents 2026-04"), ApiJson.Key(12, "build agents"));

    // A list reads every page before it prints (proposed-cli-surface.md "Output"); five systems two
    // to a page take three reads. Without filter flags the CLI sends no search, key or DNS filter,
    // and leaves disabled systems out, since --include-disabled exists to add them.
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

    // --filter works as the portal's search box does: the text is sent as typed, so it takes the
    // API's search syntax as well as plain words (proposed-cli-surface.md "Filters"). The first case
    // is example 39.
    [TestCase("version:<2024.8.0")]
    [TestCase("web tags:|prod,staging version:>2024.8.0")]
    [TestCase("db-01")]
    public async Task System_list_sends_the_filter_text_as_the_search_exactly_as_typed(string filter)
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--filter", filter);

        Assert.That(request.QueryValue("search"), Is.EqualTo(filter));
    }

    // Each flag is one of the API's search keys for systems (portal SystemSearchKeyService), added to
    // the search in the text the table in proposed-cli-surface.md "Filters" gives it, and the flags
    // combine with each other and with the --filter text. --os sends the API's own values, which it
    // matches exactly (it reads Mac as the stored Darwin). Several tags make one comma list, which the
    // API reads as items having every tag when the list has no modifier (portal
    // Modules/Search/BaseSearchKeyService.cs, BuildFilterAsync). Examples 37, 2 and 40 are the
    // --gateway case and the first two combined cases.
    [TestCase("--tag build", "tags:build")]
    [TestCase("--tag web,db", "tags:web,db")]
    [TestCase("--state connected", "state:connected")]
    [TestCase("--state disconnected", "state:disconnected")]
    [TestCase("--os windows", "os:Windows")]
    [TestCase("--os linux", "os:Linux")]
    [TestCase("--os mac", "os:Mac")]
    [TestCase("--type general", "type:general")]
    [TestCase("--type ephemeral", "type:ephemeral")]
    [TestCase("--gateway", "gateway:true")]
    [TestCase("--tag build --os linux --state connected", "tags:build os:Linux state:connected")]
    [TestCase("--os windows --state disconnected", "os:Windows state:disconnected")]
    [TestCase("--filter web --tag prod --gateway", "web tags:prod gateway:true")]
    [TestCase("--filter version:<2024.8.0 --type ephemeral", "version:<2024.8.0 type:ephemeral")]
    public async Task System_list_adds_the_search_key_flags_to_the_search_with_the_filter_text(string flags, string search)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, ["system", "list", .. flags.Split(' ')]);

        Assert.That(SearchTerms(request.QueryValue("search")), Is.EqualTo(SearchTerms(search)));
    }

    // --key-id uses the API's enrolment_key parameter, which takes one key's ID (portal
    // SystemsRequestModel.cs:16-18), and leaves the search alone.
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

    // --key looks the key up by its description with one list call, then sends its ID as --key-id
    // does, and writes nothing into the search (proposed-cli-surface.md "Filters" and "Names and
    // IDs"). Name lookups ask for disabled items too ("Filters"), so the systems of a key that has
    // since been disabled can be found.
    [Test]
    public async Task System_list_key_looks_the_key_up_by_description_and_sends_its_id_as_the_enrolment_key_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: BuildAgentsKeys);
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

    // A key ID is an integer (proposed-cli-surface.md "ID checks"); --key-id never takes a
    // description.
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

    // --dns-name uses the API's dns parameter, which finds the systems that answer to that name
    // (portal SystemsRequestModel.cs:43-45). Example 87.
    [Test]
    public async Task System_list_dns_name_sends_the_dns_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--dns-name", "db.internal");

        Assert.Multiple(() =>
        {
            Assert.That(request.QueryValue("dns"), Is.EqualTo("db.internal"));
            Assert.That(request.QueryValue("search"), Is.Null);
        });
    }

    // Enclave.Sdk.Api 1.1.0 writes the flag with bool.ToString() (SystemsClient.BuildQueryString),
    // which gives "True".
    [Test]
    public async Task System_list_include_disabled_sends_include_disabled_true()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--include-disabled");

        Assert.That(request.QueryValue("include_disabled"), Is.EqualTo("True"));
    }

    // --sort takes the API's SystemQuerySortMode members (portal Enclave.Configuration.Data), written
    // lower-case and hyphenated and matched ignoring case (proposed-cli-surface.md "Options on every
    // command"). The API reads the member name. Example 38 is the recently-connected case.
    [TestCase("recently-enrolled", "RecentlyEnrolled")]
    [TestCase("recently-connected", "RecentlyConnected")]
    [TestCase("description", "Description")]
    [TestCase("description-or-hostname", "DescriptionOrHostname")]
    [TestCase("enrolment-key-used", "EnrolmentKeyUsed")]
    [TestCase("Recently-Connected", "RecentlyConnected")]
    public async Task System_list_sort_sends_the_api_sort_name(string sort, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", SystemsPath, "system", "list", "--sort", sort);

        Assert.That(request.QueryValue("sort"), Is.EqualTo(expected));
    }

    // Each option takes the values proposed-cli-surface.md "Command options" lists for it. "1" is
    // SystemQuerySortMode's underlying value for RecentlyConnected, so a CLI that takes numeric enum
    // values accepts it. --state disabled belongs to key list and policy list ("Filters"); a system's
    // state is connected or disconnected.
    [TestCase("--sort newest")]
    [TestCase("--sort 1")]
    [TestCase("--state online")]
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

    // --not-seen-for has no API search key, so the CLI keeps the systems whose lastSeen is longer ago
    // than the duration, from every page it reads, and total counts the matches
    // (proposed-cli-surface.md "Filters"). Every system here enrolled long ago, so a CLI that reads
    // enrolledAt for a system that has been seen lists RECENT and ONLINE too. The matches are on all
    // three pages, so a CLI that filters one page misses some.
    [Test]
    public async Task System_list_not_seen_for_keeps_the_systems_last_seen_longer_ago_and_counts_only_them_in_total()
    {
        using var run = CliRun.Start();
        var now = DateTimeOffset.UtcNow;
        run.StubPages(
            SystemsPath,
            2,
            SystemSeen("STALE1", "Disconnected", now.AddDays(-100)),
            SystemSeen("ONLINE", "Connected", now.AddMinutes(-1)),
            SystemSeen("RECENT", "Disconnected", now.AddDays(-10)),
            SystemSeen("STALE2", "Disconnected", now.AddDays(-120)),
            SystemSeen("STALE3", "Disconnected", now.AddDays(-91)));

        var result = await run.RunAsync("system", "list", "--not-seen-for", "90d");

        var items = CliAssert.List(result, "system");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("STALE1,STALE2,STALE3"));
            Assert.That(run.PagesRequested(SystemsPath), Is.EqualTo("0,1,2"));
        });
    }

    // A system never seen counts from when it enrolled (proposed-cli-surface.md "Filters"), so one
    // that has just enrolled and not yet connected is not listed for removal, and one that enrolled
    // long ago and never connected is.
    [Test]
    public async Task System_list_not_seen_for_counts_a_system_never_seen_from_when_it_enrolled()
    {
        using var run = CliRun.Start();
        var now = DateTimeOffset.UtcNow;
        var systems = ApiJson.Page(SystemNeverSeen("NEWBIE", now.AddDays(-1)), SystemNeverSeen("ABANDONED", now.AddDays(-100)));
        run.Stub("GET", SystemsPath, json: systems);

        var result = await run.RunAsync("system", "list", "--not-seen-for", "90d");

        var items = CliAssert.List(result, "system");
        Assert.That(JsonRead.StringFieldList(items, "systemId"), Is.EqualTo("ABANDONED"));
    }

    // Example 14: the list --not-seen-for prints is the input of system revoke, which revokes the
    // listed systems and no others. Both commands run in one sandbox, as a shell pipeline runs them.
    // The route and result field are those of Enclave.Sdk.Api 1.1.0 SystemsClient.RevokeSystemsAsync.
    [Test]
    public async Task System_revoke_given_the_list_of_systems_not_seen_for_90_days_revokes_those_systems()
    {
        using var run = CliRun.Start();
        var now = DateTimeOffset.UtcNow;
        var systems = ApiJson.Page(
            SystemSeen("STALE1", "Disconnected", now.AddDays(-100)),
            SystemSeen("RECENT", "Disconnected", now.AddDays(-10)),
            SystemSeen("STALE2", "Disconnected", now.AddDays(-200)),
            SystemNeverSeen("NEWBIE", now.AddHours(-1)));
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

    // The output is the model the API returned (proposed-cli-surface.md "Output"), so its description
    // is one the command could not have made up.
    [Test]
    public async Task System_show_gets_the_system_and_prints_the_model_the_api_returned()
    {
        using var run = CliRun.Start();
        run.Stub("GET", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE", "from-the-api"));

        var result = await run.RunAsync("system", "show", "ABCDE");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo($"{SystemsPath}/ABCDE"));
            Assert.That(result.StdoutJson.GetProperty("systemId").GetString(), Is.EqualTo("ABCDE"));
            Assert.That(result.StdoutJson.GetProperty("description").GetString(), Is.EqualTo("from-the-api"));
        });
    }

    // Single-ID commands exit 5 for an unknown ID (proposed-cli-surface.md "Several IDs").
    [Test]
    public async Task System_show_of_an_unknown_system_exits_5_with_not_found()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", $"{SystemsPath}/ZZZZZ", 404, "Not Found", "No such system.");

        var result = await run.RunAsync("system", "show", "ZZZZZ");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo($"{SystemsPath}/ZZZZZ"));
    }

    // An update sends only the fields given (proposed-cli-surface.md "Create and update").
    // Enclave.Sdk.Api 1.1.0 keys a patch body by SystemPatchModel's property names (PatchClient.Set).
    // The output is the model the API returned, whose description differs from the one sent.
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

    // An update with no change flag exits 2 (proposed-cli-surface.md "Details"): there is nothing to
    // send.
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

    // --set-tags replaces the whole list and needs no read (proposed-cli-surface.md "Create and
    // update", "Calls per command").
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

    // --add-tags reads the system, then patches its whole tag list (proposed-cli-surface.md "Create
    // and update"), so the tags it has stay. The order of the tags does not matter to the API, so
    // they are compared sorted. Example 10.
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

    // --remove-tags reads the system and patches its whole tag list without the tags named
    // (proposed-cli-surface.md "Create and update"). Example 42.
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

    // --enable-gateway-for makes the system a gateway for each subnet given, and its label is sent as
    // the route's name (proposed-cli-surface.md "Command options"). New routes go with userEntered
    // true and weight 0, as the portal sends them (portal-spa
    // redux/reducers/app/detailTab/systems.ts:44-83). The flag reads the system first to keep what
    // stays ("Calls per command"). The second label holds a comma, which is why the flag is repeated
    // and never a comma list. Example 75.
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

    // The subnets given replace the routes a user entered, and the subnets the system found itself
    // (userEntered false) stay, as in the portal (proposed-cli-surface.md "Command options"; portal-spa
    // redux/reducers/app/detailTab/systems.ts:44-83). The old office route goes and the found route
    // stays as it was. The order of the routes does not matter to the API, so they are found by
    // subnet. Example 25.
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

    // A route that stays keeps its weight (proposed-cli-surface.md "Command options"), the preference
    // the API gives it over other gateways' routes to the same subnet
    // (SystemGatewayRouteModel.Weight), and its label ("A flag that replaces a list keeps the label
    // of every entry that stays"). The subnet added beside it is new, with weight 0.
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

    // A new entry given without a label has none (proposed-cli-surface.md "Command options"): the
    // route is sent with no name, or a null one.
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

    // --disable-gateway stops the system acting as a gateway: its route list becomes empty. It has no
    // labels to keep, so it makes no read (proposed-cli-surface.md "Calls per command"). Example 43.
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

    // The two gateway flags contradict each other and exit 2 (proposed-cli-surface.md "Command
    // options", "Details").
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

    // A command that takes several items sends them in one bulk call and prints requested and
    // affected (proposed-cli-surface.md "Several IDs"). Routes and result fields are those of
    // Enclave.Sdk.Api 1.1.0 SystemsClient (BulkEnableAsync, BulkDisableAsync, RevokeSystemsAsync).
    // The API counts two of the three systems, and the output carries its count. The disable case is
    // examples 44 and 45, where the shell expands the file's IDs into arguments.
    [TestCase("enable", "PUT", "systems/enable", "systemsUpdated")]
    [TestCase("disable", "PUT", "systems/disable", "systemsUpdated")]
    [TestCase("revoke", "DELETE", "systems", "systemsRevoked")]
    public async Task System_bulk_verb_sends_every_system_given_in_one_bulk_call_and_prints_requested_and_affected(
        string verb, string method, string pathSuffix, string resultField)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        var path = TestData.OrgPath(pathSuffix);
        run.StubBulk(method, path, resultField, 2);

        var result = await run.RunAsync("system", verb, "ABCDE", "FGHIJ", "KLMNO");

        CliAssert.Bulk(result, 3, 2);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(string.Join(",", request.BodyIds("systemIds")), Is.EqualTo("ABCDE,FGHIJ,KLMNO"));
        });
    }

    // --for takes one system and calls its enable-until route, since the API's timed enable has no
    // bulk form, and prints the system the API returns (proposed-cli-surface.md "Several IDs").
    // --then revoke is the API's Delete action ("Options on every command"), and the expiry is sent
    // as a UTC instant ("Details"). The expected window spans the run, with a second either side for
    // a CLI that rounds to whole seconds. Example 7.
    [Test]
    public async Task System_enable_for_a_duration_then_revoke_puts_enable_until_with_the_delete_action_and_prints_the_system()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/K7P2Q/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("K7P2Q", "visitor laptop"));

        var before = DateTimeOffset.UtcNow;
        var result = await run.RunAsync("system", "enable", "K7P2Q", "--for", "24h", "--then", "revoke");
        var after = DateTimeOffset.UtcNow;

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(expiry, Is.InRange(before.AddHours(24).AddSeconds(-1), after.AddHours(24).AddSeconds(1)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Delete"));
            Assert.That(result.StdoutJson.GetProperty("systemId").GetString(), Is.EqualTo("K7P2Q"));
            Assert.That(result.StdoutJson.GetProperty("description").GetString(), Is.EqualTo("visitor laptop"));
        });
    }

    // A duration is minutes, hours or days, and afterwards the system is disabled unless --then
    // revoke is given (proposed-cli-surface.md "Command options"). Values are matched ignoring case
    // ("Options on every command").
    [TestCase("--for 30m", 30, "Disable")]
    [TestCase("--for 8h --then disable", 8 * 60, "Disable")]
    [TestCase("--for 14d --then REVOKE", 14 * 24 * 60, "Delete")]
    public async Task System_enable_for_a_duration_sends_an_expiry_that_long_from_now_with_the_then_action(string flags, int minutes, string action)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/K7P2Q/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("K7P2Q"));
        var span = TimeSpan.FromMinutes(minutes);

        var before = DateTimeOffset.UtcNow;
        var request = await CliAssert.AcceptedAsync(run, "PUT", path, ["system", "enable", "K7P2Q", .. flags.Split(' ')]);
        var after = DateTimeOffset.UtcNow;

        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.InRange(before + span - TimeSpan.FromSeconds(1), after + span + TimeSpan.FromSeconds(1)));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo(action));
        });
    }

    // --until takes an RFC 3339 time with its zone, and the expiry is that instant, sent in UTC
    // (proposed-cli-surface.md "Command options", "Details").
    [TestCase("2036-10-09T17:30:00Z")]
    [TestCase("2036-10-09T13:30:00-04:00")]
    public async Task System_enable_until_a_time_with_a_zone_sends_that_instant_in_utc(string until)
    {
        using var run = CliRun.Start();
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

    // A time without a zone is read in the machine's time zone (proposed-cli-surface.md "Command
    // options"). The CLI runs in the test's process, so TimeZoneInfo.Local is the zone it reads, and
    // the expected instant holds on a machine in any zone. Example 12, in a later year so that the
    // time stays in the future.
    [Test]
    public async Task System_enable_until_a_time_without_a_zone_reads_it_in_the_local_time_zone()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/M4R8T/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("M4R8T"));
        var local = new DateTime(2036, 10, 9, 17, 30, 0, DateTimeKind.Unspecified);
        var expected = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, TimeZoneInfo.Local));

        var request = await CliAssert.AcceptedAsync(run, "PUT", path, "system", "enable", "M4R8T", "--until", "2036-10-09T17:30");

        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(expected));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Disable"));
        });
    }

    // A clock time means its next occurrence in the machine's time zone (proposed-cli-surface.md
    // "Command options"): 18:00 local time, after the run starts and no more than a day after it
    // ends. The bound is 25 hours, the longest day when clocks go back.
    [Test]
    public async Task System_enable_until_a_clock_time_sends_its_next_occurrence_in_the_local_time_zone()
    {
        using var run = CliRun.Start();
        var path = $"{SystemsPath}/M4R8T/enable-until";
        run.Stub("PUT", path, json: ApiJson.System("M4R8T"));

        var before = DateTimeOffset.UtcNow;
        var request = await CliAssert.AcceptedAsync(run, "PUT", path, "system", "enable", "M4R8T", "--until", "18:00");
        var after = DateTimeOffset.UtcNow;

        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(TimeZoneInfo.ConvertTime(expiry, TimeZoneInfo.Local).TimeOfDay, Is.EqualTo(TimeSpan.FromHours(18)));
            Assert.That(expiry, Is.GreaterThan(before));
            Assert.That(expiry, Is.LessThanOrEqualTo(after.AddHours(25)));
        });
    }

    // --then takes disable or revoke on a system; delete is the value for keys and policies
    // (proposed-cli-surface.md "Command options"). --for and --until each take a duration or a time.
    // Options that contradict each other exit 2: --for with --until, or --then without either; and
    // --until in the past exits 2 ("Details"). Each corrected command is a timed enable of one system.
    [TestCase("--for 24h --then delete")]
    [TestCase("--for 24h --until 2036-10-09T17:30:00Z")]
    [TestCase("--then revoke")]
    [TestCase("--then disable")]
    [TestCase("--until 2020-01-01T00:00:00Z")]
    [TestCase("--for tomorrow")]
    [TestCase("--until someday")]
    public async Task System_enable_with_invalid_or_contradicting_timing_flags_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("PUT", $"{SystemsPath}/K7P2Q/enable-until", json: ApiJson.System("K7P2Q"));
        run.Stub("PUT", $"{SystemsPath}/enable", json: ApiJson.Bulk("systemsUpdated", 1));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "enable", "K7P2Q", .. flags.Split(' ')],
            ["system", "enable", "K7P2Q", "--for", "24h"],
            "PUT",
            $"{SystemsPath}/K7P2Q/enable-until");
    }

    // A timed enable is a single-ID command, which exits 5 for an unknown ID (proposed-cli-surface.md
    // "Several IDs").
    [Test]
    public async Task System_enable_for_a_duration_of_an_unknown_system_exits_5_with_not_found()
    {
        using var run = CliRun.Start();
        run.StubProblem("PUT", $"{SystemsPath}/ZZZZZ/enable-until", 404, "Not Found", "No such system.");

        var result = await run.RunAsync("system", "enable", "ZZZZZ", "--for", "24h");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo($"{SystemsPath}/ZZZZZ/enable-until"));
    }

    // The API splits a search at whitespace into terms and applies each, whatever their order
    // (portal BaseSearchKeyService.ParseSearchString), so the terms are compared sorted.
    private static string SearchTerms(string? search) =>
        string.Join(" ", (search ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));

    // A command that adds or removes tags patches the whole list, in no order the API depends on.
    private static string SortedStrings(JsonElement array) =>
        string.Join(",", JsonAssert.Strings(array).Order(StringComparer.Ordinal));

    private static string SortedSubnets(JsonElement routes) =>
        string.Join(",", routes.EnumerateArray().Select(route => JsonAssert.Property(route, "subnet").GetString()).Order(StringComparer.Ordinal));

    private static JsonElement Route(JsonElement routes, string subnet) =>
        routes.EnumerateArray().Single(route => string.Equals(JsonAssert.Property(route, "subnet").GetString(), subnet, StringComparison.Ordinal));

    // lastSeen is a DateTimeOffset in SystemSummaryModel, written as ApiJson writes those, in UTC
    // with +00:00. ApiJson.System's enrolledAt is long before any of these tests' times.
    private static string SystemSeen(string systemId, string state, DateTimeOffset lastSeen) =>
        ApiJson.Altered(
            ApiJson.System(systemId),
            ("state", state),
            ("lastSeen", Utc(lastSeen)));

    // A system that enrolled and has never connected: no connection time and no lastSeen.
    private static string SystemNeverSeen(string systemId, DateTimeOffset enrolledAt) =>
        ApiJson.Altered(
            ApiJson.System(systemId),
            ("state", "Disconnected"),
            ("connectedAt", null),
            ("lastSeen", null),
            ("enrolledAt", Utc(enrolledAt)));

    private static string Utc(DateTimeOffset time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    // A route as the API holds it (portal Systems/Models/SystemGatewayRouteModel.cs).
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
