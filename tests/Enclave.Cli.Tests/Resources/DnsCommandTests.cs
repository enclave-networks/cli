using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// The API keeps db.internal as the record "db" in the zone "internal" (portal DnsRecordModel), so
// the CLI splits a hostname at the longest zone name it ends in ("Details").
public class DnsCommandTests
{
    // The detail the API sends with no-such-system (portal
    // DnsRecordHandlerBase.ValidateReferencedSystemsAreAllValidAsync).
    private const string UnknownSystemDetail = "One or more of the provided systems are not known approved systems in the organisation: ZZZZZ";

    private static readonly string Zones = TestData.OrgPath("dns/zones");

    private static readonly string Records = TestData.OrgPath("dns/records");

    private static readonly string[] ZoneCreateFields = ["name", "notes", "autoDnsTags"];

    // The fields of the API's DnsRecordCreateModel.
    private static readonly string[] RecordCreateFields = ["name", "zoneId", "type", "tags", "systems", "notes"];

    // lab is the second zone the lookup reads, so a lookup that took the first zone gets the wrong
    // one.
    [TestCase("lab")]
    [TestCase("LAB")]
    public async Task Dns_show_zone_looks_the_zone_up_by_name_ignoring_case_and_prints_it(string name)
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("GET", ZonePath("5"), json: ApiJson.Zone(5, "lab"));

        var result = await run.RunAsync("dns", "show-zone", name);

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {Zones}|GET {ZonePath("5")}"));
            Assert.That(JsonAssert.Property(output, "id").GetInt32(), Is.EqualTo(5));
            Assert.That(JsonAssert.Property(output, "name").GetString(), Is.EqualTo("lab"));
        });
    }

    // Zone 4 exists, so a CLI that read the argument "4" as an ID would print it.
    [TestCase("nowhere")]
    [TestCase("4")]
    public async Task Dns_show_zone_given_a_name_no_zone_has_exits_2_with_no_candidates(string name)
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("GET", ZonePath("4"), json: ApiJson.Zone(4, "internal"));

        var result = await run.RunAsync("dns", "show-zone", name);

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").EnumerateArray(), Is.Empty);
            Assert.That(run.Requests.Select(request => request.Path), Is.Not.Empty.And.All.EqualTo(Zones));
        });
    }

    // Example 29. A create sends every field, notes null when left out ("Create and update").
    [Test]
    public async Task Dns_create_zone_posts_the_name_and_auto_dns_tags_and_prints_the_created_zone()
    {
        using var run = CliRun.Start();
        run.Stub("POST", Zones, json: ApiJson.Zone(4, "internal"));

        var result = await run.RunAsync("dns", "create-zone", "internal", "--auto-dns-tags", "web");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(Zones));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(ZoneCreateFields));
            Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("internal"));
            Assert.That(JsonRead.StringList(body.GetProperty("autoDnsTags")), Is.EqualTo("web"));
            Assert.That(body.GetProperty("notes").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Dns_create_zone_posts_every_auto_dns_tag_and_the_notes()
    {
        using var run = CliRun.Start();
        run.Stub("POST", Zones, json: ApiJson.Zone(5, "lab"));

        var result = await run.RunAsync("dns", "create-zone", "lab", "--auto-dns-tags", "web,api", "--notes", "Lab systems");

        CliAssert.Succeeded(result);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(ZoneCreateFields));
            Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("lab"));
            Assert.That(JsonRead.StringList(body.GetProperty("autoDnsTags")), Is.EqualTo("web,api"));
            Assert.That(body.GetProperty("notes").GetString(), Is.EqualTo("Lab systems"));
        });
    }

    // The CLI sends the empty list itself, so the API's default never decides which systems get a
    // name in the zone.
    [Test]
    public async Task Dns_create_zone_without_auto_dns_tags_sends_an_empty_list()
    {
        using var run = CliRun.Start();
        run.Stub("POST", Zones, json: ApiJson.Zone(5, "lab"));

        var result = await run.RunAsync("dns", "create-zone", "lab");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(Zones));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(ZoneCreateFields));
            Assert.That(body.GetProperty("name").GetString(), Is.EqualTo("lab"));
            Assert.That(body.GetProperty("autoDnsTags").GetRawText(), Is.EqualTo(new JsonArray().ToJsonString()));
            Assert.That(body.GetProperty("notes").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    // Example 60.
    [TestCase("dns update-zone internal --set-auto-dns-tags web,api", true)]
    [TestCase("dns update-zone --id 4 --set-auto-dns-tags web,api", false)]
    public async Task Dns_update_zone_replaces_the_auto_dns_tags_and_sends_no_other_field(string commandLine, bool lookup)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("PATCH", ZonePath("4"), json: ApiJson.Zone(4, "internal"));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Succeeded(result);
        var expectedCalls = lookup ? $"GET {Zones}|PATCH {ZonePath("4")}" : $"PATCH {ZonePath("4")}";
        Assert.That(string.Join("|", run.Calls()), Is.EqualTo(expectedCalls));
        var body = run.Requests[^1].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("AutoDnsTags").IgnoreCase);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "AutoDnsTags")), Is.EqualTo("web,api"));
        });
    }

    // The answer differs from the request, so output copied from the request does not match.
    [Test]
    public async Task Dns_update_zone_sends_the_name_and_notes_given_and_prints_the_updated_zone()
    {
        using var run = CliRun.Start();
        var answer = JsonNode.Parse(ApiJson.Zone(4, "Corp"))!.AsObject();
        answer["recordCount"] = 12;
        run.Stub("PATCH", ZonePath("4"), json: answer.ToJsonString());

        var result = await run.RunAsync("dns", "update-zone", "--id", "4", "--name", "corp", "--notes", "Renamed");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        string[] fields = ["Name", "Notes"];
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(ZonePath("4")));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields).IgnoreCase);
            Assert.That(JsonAssert.Property(body, "Name").GetString(), Is.EqualTo("corp"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("Renamed"));
            Assert.That(result.StdoutJson.GetProperty("id").GetInt32(), Is.EqualTo(4));
            Assert.That(result.StdoutJson.GetProperty("name").GetString(), Is.EqualTo("Corp"));
            Assert.That(result.StdoutJson.GetProperty("recordCount").GetInt32(), Is.EqualTo(12));
        });
    }

    // The API has no bulk zone delete (portal DnsController.DeleteZone), so delete-zone prints the
    // deleted zone ("Several IDs").
    [TestCase("dns delete-zone internal", true)]
    [TestCase("dns delete-zone --id 4", false)]
    public async Task Dns_delete_zone_deletes_the_one_zone_and_prints_it(string commandLine, bool lookup)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("DELETE", ZonePath("4"), json: ApiJson.Zone(4, "internal"));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Succeeded(result);
        var expectedCalls = lookup ? $"GET {Zones}|DELETE {ZonePath("4")}" : $"DELETE {ZonePath("4")}";
        Assert.Multiple(() =>
        {
            Assert.That(string.Join("|", run.Calls()), Is.EqualTo(expectedCalls));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(result.StdoutJson, "name").GetString(), Is.EqualTo("internal"));
        });
    }

    [TestCase("dns delete-zone internal lab")]
    public async Task Dns_delete_zone_given_two_zones_exits_2_without_a_request(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("DELETE", ZonePath("4"), json: ApiJson.Zone(4, "internal"));
        run.Stub("DELETE", ZonePath("5"), json: ApiJson.Zone(5, "lab"));

        await CliAssert.RejectedThenAcceptedAsync(run, commandLine.Split(' '), ["dns", "delete-zone", "--id", "4"], "DELETE", ZonePath("4"));
    }

    [Test]
    public async Task Dns_list_hostnames_reads_every_page_and_prints_a_hostname_list()
    {
        using var run = CliRun.Start();
        run.StubPages(Records, 2, DbInternal(), WebInternal(), DbLab());

        var result = await run.RunAsync("dns", "list-hostnames");

        var items = CliAssert.List(result, "hostname");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("7,8,9"));
            Assert.That(JsonRead.StringFieldList(items, "fqdn"), Is.EqualTo("db.internal,web.internal,db.lab"));
            Assert.That(run.PagesRequested(Records), Is.EqualTo("0,1"));
            Assert.That(run.Requests.Select(request => request.Path), Is.All.EqualTo(Records));
            Assert.That(run.Requests.Select(request => request.QueryValue("zoneId")), Is.All.Null);
            Assert.That(run.Requests.Select(request => request.QueryValue("search")), Is.All.Null);
        });
    }

    // Example 62. DnsClient.GetRecordsAsync sends the zone as zoneId (Enclave.Sdk.Api).
    [TestCase("dns list-hostnames --zone internal", true)]
    [TestCase("dns list-hostnames --zone-id 4", false)]
    public async Task Dns_list_hostnames_in_a_zone_asks_the_api_for_that_zone_only(string commandLine, bool lookup)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        StubZones(run);
        run.StubPages(Records, 200, DbInternal(), WebInternal());

        var result = await run.RunAsync(commandLine.Split(' '));

        var items = CliAssert.List(result, "hostname");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("7,8"));
            Assert.That(run.RequestsTo("GET", Records).Select(request => request.QueryValue("zoneId")), Is.Not.Empty.And.All.EqualTo("4"));
            Assert.That(run.RequestsTo("GET", Zones), Has.Count.EqualTo(lookup ? 1 : 0));
        });
    }

    // Example 16.
    [Test]
    public async Task Dns_list_hostnames_sends_the_filter_as_the_api_search()
    {
        using var run = CliRun.Start();
        run.StubPages(Records, 200, OldApiLab(), OldApiInternal());

        var result = await run.RunAsync("dns", "list-hostnames", "--filter", "old-api");

        var items = CliAssert.List(result, "hostname");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("10,11"));
            Assert.That(run.Requests.Select(request => request.Path), Is.All.EqualTo(Records));
            Assert.That(run.Requests.Select(request => request.QueryValue("search")), Is.Not.Empty.And.All.EqualTo("old-api"));
            Assert.That(run.Requests.Select(request => request.QueryValue("zoneId")), Is.All.Null);
        });
    }

    [Test]
    public async Task Dns_list_hostnames_in_a_zone_no_zone_has_exits_2_without_listing_hostnames()
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.StubPages(Records, 200, DbInternal());

        var result = await run.RunAsync("dns", "list-hostnames", "--zone", "nowhere");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").EnumerateArray(), Is.Empty);
            Assert.That(run.RequestsTo("GET", Records), Is.Empty);
        });
    }

    // db.internal and db.lab are both the record "db", so only a lookup by the full hostname tells
    // them apart.
    [TestCase("db.lab", "9")]
    [TestCase("DB.Internal", "7")]
    public async Task Dns_show_hostname_looks_the_full_hostname_up_and_prints_it(string hostname, string id)
    {
        using var run = CliRun.Start();
        StubHostnames(run);
        run.Stub("GET", RecordPath("7"), json: DbInternal());
        run.Stub("GET", RecordPath("9"), json: DbLab());

        var result = await run.RunAsync("dns", "show-hostname", hostname);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {Records}|GET {RecordPath(id)}"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetRawText(), Is.EqualTo(id));
        });
    }

    // "db" is the record name of db.internal and db.lab, and the full name of neither.
    [TestCase("nowhere.internal")]
    [TestCase("db")]
    public async Task Dns_show_hostname_given_a_name_no_hostname_has_exits_2_with_no_candidates(string hostname)
    {
        using var run = CliRun.Start();
        StubHostnames(run);
        run.Stub("GET", RecordPath("7"), json: DbInternal());
        run.Stub("GET", RecordPath("9"), json: DbLab());

        var result = await run.RunAsync("dns", "show-hostname", hostname);

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").EnumerateArray(), Is.Empty);
            Assert.That(run.Requests.Select(request => request.Method), Is.Not.Empty.And.All.EqualTo("GET"));
            Assert.That(run.Requests.Select(request => request.Path), Has.None.EqualTo(RecordPath("7")).And.None.EqualTo(RecordPath("9")));
        });
    }

    // Example 15. ENCLAVE is the API's only record type (portal
    // DnsRecordTypeFormatConverter.ToPublicFormat). --tags left out is sent as an empty list
    // ("Details").
    [Test]
    public async Task Dns_create_hostname_posts_the_record_to_the_zone_its_name_ends_in()
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("POST", Records, json: DbInternal());

        var result = await run.RunAsync("dns", "create-hostname", "db.internal", "--systems", "ABCDE,FGHIJ", "--notes", "primary database pair");

        CliAssert.Succeeded(result);
        Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {Zones}|POST {Records}"));
        var body = run.Requests[^1].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(RecordCreateFields));
            Assert.That(body.GetProperty("tags").GetRawText(), Is.EqualTo(new JsonArray().ToJsonString()));
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("db"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("ENCLAVE"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "systems")), Is.EqualTo("ABCDE,FGHIJ"));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("primary database pair"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(7));
        });
    }

    // StubZones explains the zone list. The record name may contain dots ("Details").
    [TestCase("db.eu.internal", 8, "db")]
    [TestCase("db.corp.example", 6, "db")]
    [TestCase("db.primary.internal", 4, "db.primary")]
    public async Task Dns_create_hostname_posts_the_record_to_the_longest_zone_its_name_ends_in(string hostname, int zoneId, string name)
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("POST", Records, json: DbInternal());

        var result = await run.RunAsync("dns", "create-hostname", hostname);

        CliAssert.Succeeded(result);
        var posts = run.RequestsTo("POST", Records);
        Assert.That(posts, Has.Count.EqualTo(1));
        var body = posts[0].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo(name));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(zoneId));
        });
    }

    [Test]
    public async Task Dns_create_hostname_posts_the_tags_given()
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("POST", Records, json: WebInternal());

        var result = await run.RunAsync("dns", "create-hostname", "web.internal", "--tags", "web,frontend");

        CliAssert.Succeeded(result);
        var posts = run.RequestsTo("POST", Records);
        Assert.That(posts, Has.Count.EqualTo(1));
        Assert.That(JsonRead.StringList(JsonAssert.Property(posts[0].BodyJson, "tags")), Is.EqualTo("web,frontend"));
    }

    // xinternal ends in "internal" but not at a label boundary, and eu.internal would be the record
    // "eu" in internal if zones were not matched whole.
    [TestCase("db.nowhere")]
    [TestCase("db.xinternal")]
    [TestCase("internal")]
    [TestCase("eu.internal")]
    public async Task Dns_create_hostname_with_no_zone_or_equal_to_a_zone_name_exits_2_without_creating_it(string hostname)
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("POST", Records, json: DbInternal());

        var result = await run.RunAsync("dns", "create-hostname", hostname);

        CliAssert.Failed(result, "invalid_argument");
        Assert.That(run.RequestsTo("POST", Records), Is.Empty);
    }

    // Example 61.
    [TestCase("dns update-hostname db.internal --set-systems ABCDE,FGHIJ,KLMNO", true)]
    [TestCase("dns update-hostname --id 7 --set-systems ABCDE,FGHIJ,KLMNO", false)]
    public async Task Dns_update_hostname_replaces_the_systems_and_sends_no_other_field(string commandLine, bool lookup)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        StubHostnames(run);
        run.Stub("PATCH", RecordPath("7"), json: DbInternal());

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Succeeded(result);
        var expectedCalls = lookup ? $"GET {Records}|PATCH {RecordPath("7")}" : $"PATCH {RecordPath("7")}";
        Assert.That(string.Join("|", run.Calls()), Is.EqualTo(expectedCalls));
        var body = run.Requests[^1].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("Systems").IgnoreCase);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "Systems")), Is.EqualTo("ABCDE,FGHIJ,KLMNO"));
        });
    }

    [Test]
    public async Task Dns_update_hostname_sends_the_tags_and_notes_given_and_prints_the_updated_hostname()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", RecordPath("7"), json: ApiJson.HostnameRecord(7, "db", 4, "internal", "web", "db"));

        var result = await run.RunAsync("dns", "update-hostname", "--id", "7", "--set-tags", "web,db", "--notes", "Moved");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        string[] fields = ["Tags", "Notes"];
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(RecordPath("7")));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields).IgnoreCase);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "Tags")), Is.EqualTo("web,db"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("Moved"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "fqdn").GetString(), Is.EqualTo("db.internal"));
        });
    }

    // The API takes the record name within the zone (portal DnsRecordPatchModel.Name). The reads
    // that find the hostname's zone are left open, so only the PATCH is checked.
    [TestCase("dns update-hostname --id 7 --name db2.internal", "db2")]
    [TestCase("dns update-hostname db.internal --name db2.internal", "db2")]
    [TestCase("dns update-hostname --id 7 --name db.primary.internal", "db.primary")]
    public async Task Dns_update_hostname_name_takes_a_full_hostname_in_the_same_zone_and_sends_the_record_name(string commandLine, string name)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        StubZones(run);
        StubHostnames(run);
        run.Stub("GET", RecordPath("7"), json: DbInternal());
        run.Stub("PATCH", RecordPath("7"), json: DbInternal());

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", RecordPath("7"));
        Assert.That(patches, Has.Count.EqualTo(1));
        var body = patches[0].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests[^1].Method, Is.EqualTo("PATCH"));
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("Name").IgnoreCase);
            Assert.That(JsonAssert.Property(body, "Name").GetString(), Is.EqualTo(name));
        });
    }

    // The API cannot move a record between zones ("Details"); db2.eu.internal is in eu.internal, the
    // longest zone it ends in. No error repeats a value, which can be a token passed by mistake, so
    // stderr is checked for the part a message could quote.
    [TestCase("db.lab", "db.lab")]
    [TestCase("db2", "db2")]
    [TestCase("db2.eu.internal", "eu.internal")]
    public async Task Dns_update_hostname_name_outside_the_hostname_zone_exits_2_naming_only_the_option_without_a_change(string hostname, string partOfValue)
    {
        using var run = CliRun.Start();
        StubZones(run);
        StubHostnames(run);
        run.Stub("GET", RecordPath("7"), json: DbInternal());
        run.Stub("PATCH", RecordPath("7"), json: DbInternal());

        var result = await run.RunAsync("dns", "update-hostname", "--id", "7", "--name", hostname);

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("PATCH", RecordPath("7")), Is.Empty);
            CliAssert.Problems(error, "--name");
            Assert.That(result.Stderr, Does.Not.Contain(partOfValue));
        });
    }

    [Test]
    public async Task Dns_delete_hostname_looks_each_hostname_up_and_deletes_them_in_one_bulk_call()
    {
        using var run = CliRun.Start();
        StubHostnames(run);
        run.StubBulk("DELETE", Records, "dnsRecordsDeleted", 2);

        var result = await run.RunAsync("dns", "delete-hostname", "db.internal", "db.lab");

        CliAssert.Bulk(result, requested: 2, affected: 2);
        var deletes = run.RequestsTo("DELETE", Records);
        Assert.That(deletes, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", deletes[0].BodyIds("recordIds")), Is.EqualTo("7,9"));
            Assert.That(run.Requests.Select(request => request.Method), Has.Exactly(1).EqualTo("DELETE"));
        });
    }

    // The fake answers as the API does: 404 with the problem type zone-not-found or
    // dns-record-not-found (portal DnsController.ZoneNotFoundResult, DnsRecordNotFoundResult).
    [TestCase("dns show-zone --id 4", "GET", "dns/zones/4", "zone-not-found")]
    [TestCase("dns update-zone --id 4 --notes Reviewed", "PATCH", "dns/zones/4", "zone-not-found")]
    [TestCase("dns delete-zone --id 4", "DELETE", "dns/zones/4", "zone-not-found")]
    [TestCase("dns show-hostname --id 7", "GET", "dns/records/7", "dns-record-not-found")]
    [TestCase("dns update-hostname --id 7 --notes Reviewed", "PATCH", "dns/records/7", "dns-record-not-found")]
    public async Task Dns_single_id_commands_exit_5_for_an_id_the_api_does_not_know(string commandLine, string method, string suffix, string problemType)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(suffix);
        using var run = CliRun.Start();
        run.StubRaw(method, TestData.OrgPath(suffix), 404, LoopbackApi.ProblemContentType, Problem(404, problemType, "Not Found", "No such DNS item."));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Failed(result, "not_found");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath(suffix)));
        });
    }

    // The API answers 404 no-such-system for a system it does not know (portal
    // DnsRecordHandlerBase.ValidateReferencedSystemsAreAllValidAsync), but the hostname exists, so
    // exit 5 would be wrong.
    [TestCase("dns update-hostname --id 7 --set-systems ABCDE,ZZZZZ")]
    [TestCase("dns update-hostname db.internal --set-systems ABCDE,ZZZZZ")]
    public async Task Dns_update_hostname_with_a_system_the_api_does_not_know_exits_1_api_error(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        StubHostnames(run);
        run.StubRaw("PATCH", RecordPath("7"), 404, LoopbackApi.ProblemContentType, Problem(404, "no-such-system", "System Not Found", UnknownSystemDetail));

        var result = await run.RunAsync(commandLine.Split(' '));

        var error = CliAssert.Failed(result, "api_error");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo(UnknownSystemDetail));
            Assert.That(run.RequestsTo("PATCH", RecordPath("7")), Has.Count.EqualTo(1));
        });
    }

    // Only dns-record-not-found says the hostname was not found ("Errors and exit codes"). A 404 with
    // no body can come from a proxy, and zone-not-found is the API's type for a zone (portal
    // DnsController.ZoneNotFoundResult).
    [TestCase(null)]
    [TestCase("zone-not-found")]
    public async Task Dns_update_hostname_answered_404_without_the_hostname_problem_type_exits_1_api_error(string? problemType)
    {
        using var run = CliRun.Start();

        if (problemType is null)
        {
            run.Stub("PATCH", RecordPath("7"), 404);
        }
        else
        {
            run.StubRaw("PATCH", RecordPath("7"), 404, LoopbackApi.ProblemContentType, Problem(404, problemType, "Not Found", "No such DNS item."));
        }

        var result = await run.RunAsync("dns", "update-hostname", "--id", "7", "--notes", "Reviewed");

        var error = CliAssert.Failed(result, "api_error");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(run.SingleRequest().Call, Is.EqualTo($"PATCH {RecordPath("7")}"));
        });
    }

    // The hostname does not exist yet, so no 404 means it was not found. The API answers 404 for an
    // unknown system (portal DnsRecordHandlerBase.ValidateReferencedSystemsAreAllValidAsync) and for
    // a zone deleted since the CLI read it (portal DnsRecordCreateHandler.GetResponseAsync).
    [TestCase("no-such-system", "System Not Found", UnknownSystemDetail)]
    [TestCase("no-such-zone", "Zone does not exist", "Specified owning zone #4 does not exist.")]
    public async Task Dns_create_hostname_answered_404_exits_1_api_error(string problemType, string title, string detail)
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.StubRaw("POST", Records, 404, LoopbackApi.ProblemContentType, Problem(404, problemType, title, detail));

        var result = await run.RunAsync("dns", "create-hostname", "db.internal", "--systems", "ABCDE,ZZZZZ");

        var error = CliAssert.Failed(result, "api_error");
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(error.GetProperty("detail").GetString(), Is.EqualTo(detail));
            Assert.That(run.RequestsTo("POST", Records), Has.Count.EqualTo(1));
        });
    }

    // The API refuses a repeated tag (portal DnsRecordCreateValidator), and it upper-cases system IDs
    // and answers 404 when it finds fewer systems than given (portal DnsRecordCreateHandler,
    // DnsRecordHandlerBase.ValidateReferencedSystemsAreAllValidAsync), so abcde repeats ABCDE.
    [TestCase("--tags", "web,db,web", "web,db")]
    [TestCase("--systems", "ABCDE,FGHIJ,ABCDE", "ABCDE,FGHIJ")]
    [TestCase("--systems", "ABCDE,abcde", "ABCDE")]
    public async Task Dns_create_hostname_with_a_value_twice_in_a_list_exits_2_without_a_request(string option, string repeated, string once)
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("POST", Records, json: DbInternal());

        CliAssert.Rejected(run, await run.RunAsync("dns", "create-hostname", "db.internal", option, repeated));

        CliAssert.Succeeded(await run.RunAsync("dns", "create-hostname", "db.internal", option, once));
        Assert.That(run.RequestsTo("POST", Records), Has.Count.EqualTo(1));
    }

    // The patch's systems go through the same check (portal DnsRecordPatchHandler.GetResponseAsync),
    // and the API refuses a repeated automatic DNS tag (portal DnsZoneCreateValidator,
    // DnsZonePatchValidator).
    [TestCase("dns update-hostname --id 7 --set-systems ABCDE,abcde", "dns update-hostname --id 7 --set-systems ABCDE", "PATCH", "dns/records/7")]
    [TestCase("dns create-zone internal --auto-dns-tags web,web", "dns create-zone internal --auto-dns-tags web", "POST", "dns/zones")]
    [TestCase("dns update-zone --id 4 --set-auto-dns-tags web,api,web", "dns update-zone --id 4 --set-auto-dns-tags web,api", "PATCH", "dns/zones/4")]
    public async Task Dns_changes_with_a_value_twice_in_a_list_exit_2_without_a_request(string rejected, string accepted, string method, string suffix)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(suffix);
        using var run = CliRun.Start();
        run.Stub("POST", Zones, json: ApiJson.Zone(4, "internal"));
        StubEverySingleItemCall(run);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected.Split(' '), accepted.Split(' '), method, TestData.OrgPath(suffix));
    }

    [TestCase("dns show-zone --id ../records", "dns show-zone --id 4", "GET", "dns/zones/4")]
    [TestCase("dns delete-zone --id internal", "dns delete-zone --id 4", "DELETE", "dns/zones/4")]
    [TestCase("dns delete-hostname --id 7,db.internal", "dns delete-hostname --id 7,8", "DELETE", "dns/records")]
    [TestCase("dns list-hostnames --zone-id ../4", "dns list-hostnames --zone-id 4", "GET", "dns/records")]
    public async Task Dns_commands_reject_an_id_that_is_not_an_integer_without_a_request(string rejected, string accepted, string method, string suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(accepted);
        using var run = CliRun.Start();
        StubEverySingleItemCall(run);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected.Split(' '), accepted.Split(' '), method, TestData.OrgPath(suffix));
    }

    [TestCase("dns show-zone internal --id 4", "dns show-zone --id 4", "GET", "dns/zones/4")]
    [TestCase("dns update-hostname db.internal --id 7 --notes Reviewed", "dns update-hostname --id 7 --notes Reviewed", "PATCH", "dns/records/7")]
    [TestCase("dns delete-hostname db.internal --id 7", "dns delete-hostname --id 7", "DELETE", "dns/records")]
    public async Task Dns_commands_given_a_name_and_an_id_exit_2_without_a_request(string rejected, string accepted, string method, string suffix)
    {
        ArgumentNullException.ThrowIfNull(suffix);
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(accepted);
        using var run = CliRun.Start();
        StubEverySingleItemCall(run);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected.Split(' '), accepted.Split(' '), method, TestData.OrgPath(suffix));
    }

    [TestCase("dns list-zones", "dns/zones")]
    [TestCase("dns show-zone --id 4", "dns/zones/4")]
    [TestCase("dns show-hostname --id 7", "dns/records/7")]
    public async Task Dns_reads_given_dry_run_exit_2_without_a_request(string commandLine, string suffix)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(suffix);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns"), json: ApiJson.DnsSummary());
        run.Stub("GET", Zones, json: ApiJson.Page(ApiJson.Zone(4, "internal")));
        StubEverySingleItemCall(run);
        string[] accepted = commandLine.Split(' ');

        await CliAssert.RejectedThenAcceptedAsync(run, [.. accepted, "--dry-run"], accepted, "GET", TestData.OrgPath(suffix));
    }

    // None of these commands needs a read, so the fake API receives no request at all.
    [TestCase("dns create-zone internal --dry-run", "POST", "dns/zones")]
    [TestCase("dns update-zone --id 4 --notes Reviewed --dry-run", "PATCH", "dns/zones/4")]
    [TestCase("dns update-hostname --id 7 --notes Reviewed --dry-run", "PATCH", "dns/records/7")]
    public async Task Dns_changes_with_dry_run_print_the_requests_and_send_nothing(string commandLine, string method, string suffix)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();

        var result = await run.RunAsync(commandLine.Split(' '));

        DryRunOutput.RequestInTestOrg(result, method, TestData.OrgPath(suffix));
        Assert.That(run.Requests, Is.Empty);
    }

    // The zone lookup is a read the change depends on, so it runs ("Dry run").
    [Test]
    public async Task Dns_create_hostname_with_dry_run_looks_the_zone_up_and_prints_the_post_without_sending_it()
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("POST", Records, json: DbInternal());

        var result = await run.RunAsync("dns", "create-hostname", "db.internal", "--dry-run");

        var body = JsonAssert.Property(DryRunOutput.RequestInTestOrg(result, "POST", Records), "body");
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", Zones), Is.Not.Empty);
            Assert.That(run.RequestsTo("POST", Records), Is.Empty);
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("db"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("ENCLAVE"));
        });
    }

    // The problem type is a URL ending in the problem's name, as the API writes it (portal
    // ProblemResponseFactory.GetProblemTypeUrl; Enclave.Api WebStartup.Configure).
    private static string Problem(int status, string type, string title, string detail) => new JsonObject
    {
        ["type"] = $"https://api.enclave.io/problems/type/{type}",
        ["title"] = title,
        ["status"] = status,
        ["detail"] = detail,
    }.ToJsonString();

    private static string ZonePath(string id) => $"{Zones}/{id}";

    private static string RecordPath(string id) => $"{Records}/{id}";

    private static string DbInternal() => ApiJson.HostnameRecord(7, "db", 4, "internal");

    private static string WebInternal() => ApiJson.HostnameRecord(8, "web", 4, "internal");

    private static string DbLab() => ApiJson.HostnameRecord(9, "db", 5, "lab");

    private static string OldApiLab() => ApiJson.HostnameRecord(10, "old-api", 5, "lab");

    private static string OldApiInternal() => ApiJson.HostnameRecord(11, "old-api", 4, "internal");

    // internal is first in the list and eu.internal ends in it, and corp.example has two labels, so a
    // lookup that took the first zone, or matched on anything but whole labels, picks the wrong zone.
    private static void StubZones(CliRun run) =>
        run.StubPages(Zones, 200, ApiJson.Zone(4, "internal"), ApiJson.Zone(5, "lab"), ApiJson.Zone(6, "corp.example"), ApiJson.Zone(8, "eu.internal"));

    private static void StubHostnames(CliRun run) =>
        run.StubPages(Records, 200, DbInternal(), WebInternal(), DbLab());

    // Answers every single-item call on zone 4 and hostname 7, the hostname list and the bulk
    // hostname delete, so a request a rejected command sent would succeed and show in the fake API's
    // requests.
    private static void StubEverySingleItemCall(CliRun run)
    {
        run.Stub("GET", ZonePath("4"), json: ApiJson.Zone(4, "internal"));
        run.Stub("PATCH", ZonePath("4"), json: ApiJson.Zone(4, "internal"));
        run.Stub("DELETE", ZonePath("4"), json: ApiJson.Zone(4, "internal"));
        run.Stub("GET", RecordPath("7"), json: DbInternal());
        run.Stub("PATCH", RecordPath("7"), json: DbInternal());
        run.Stub("DELETE", Records, json: ApiJson.Bulk("dnsRecordsDeleted", 2));
        run.Stub("GET", Records, json: ApiJson.Page(DbInternal()));
    }
}
