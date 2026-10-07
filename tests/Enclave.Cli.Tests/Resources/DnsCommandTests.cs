using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// The dns commands of proposed-cli-surface.md "Commands" and "Command options". Zones and hostnames
// are given by name, looked up with one list call, or by ID after --id, which makes no lookup
// ("Names and IDs"). A hostname is written in full, and the API keeps db.internal as the record
// "db" in the zone "internal" (portal DnsRecordModel: name, zoneId, fqdn). A hostname's zone is the
// longest zone name it ends in, at a label boundary, and the record name is the rest ("Details").
public class DnsCommandTests
{
    private static readonly string Zones = TestData.OrgPath("dns/zones");

    private static readonly string Records = TestData.OrgPath("dns/records");

    [Test]
    public async Task Dns_show_prints_the_organisation_dns_summary()
    {
        using var run = CliRun.Start();
        var summary = JsonRead.Parse(ApiJson.DnsSummary());
        run.Stub("GET", TestData.OrgPath("dns"), json: ApiJson.DnsSummary());

        var result = await run.RunAsync("dns", "show");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns")));
            Assert.That(JsonAssert.Property(output, "zoneCount").GetInt32(), Is.EqualTo(JsonAssert.Property(summary, "zoneCount").GetInt32()));
            Assert.That(JsonAssert.Property(output, "totalRecordCount").GetInt32(), Is.EqualTo(JsonAssert.Property(summary, "totalRecordCount").GetInt32()));
        });
    }

    // Three zones two to a page make the CLI follow nextPage to a second page. A list reads every
    // page before it prints, 200 to a page, the most the API returns (proposed-cli-surface.md
    // "Output" and "Calls per command").
    [Test]
    public async Task Dns_list_zones_reads_every_page_and_prints_a_zone_list()
    {
        using var run = CliRun.Start();
        run.StubPages(Zones, 2, ApiJson.Zone(4, "internal"), ApiJson.Zone(5, "lab"), ApiJson.Zone(6, "corp.example"));

        var result = await run.RunAsync("dns", "list-zones");

        var items = CliAssert.List(result, "zone");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("4,5,6"));
            Assert.That(JsonRead.StringFieldList(items, "name"), Is.EqualTo("internal,lab,corp.example"));
            Assert.That(run.Requests.Select(request => request.Path), Is.All.EqualTo(Zones));
            Assert.That(run.PagesRequested(Zones), Is.EqualTo("0,1"));
            Assert.That(run.Requests.Select(request => request.QueryValue("per_page")), Is.All.EqualTo("200"));
        });
    }

    // lab is the second zone the lookup reads, so a lookup that took the first zone gets the wrong
    // one. Names match ignoring case (proposed-cli-surface.md "Names and IDs").
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

    [Test]
    public async Task Dns_show_zone_with_id_gets_the_zone_without_a_lookup()
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("GET", ZonePath("4"), json: ApiJson.Zone(4, "internal"));

        var result = await run.RunAsync("dns", "show-zone", "--id", "4");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(ZonePath("4")));
            Assert.That(JsonAssert.Property(result.StdoutJson, "name").GetString(), Is.EqualTo("internal"));
        });
    }

    // A name that matches nothing exits 2 invalid_argument, and the error's candidates are the
    // items that matched, none here (proposed-cli-surface.md "Errors and exit codes"). The argument
    // "4" is a name, since the option decides how a value is read and the CLI never inspects it to
    // guess its type ("Commands"); zone 4 exists, so treating the argument as an ID would print it.
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

    // Example 29 in proposed-cli-surface.md. A zone is created by name, so there is nothing to look
    // up.
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
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("internal"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "autoDnsTags")), Is.EqualTo("web"));
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
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("lab"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "autoDnsTags")), Is.EqualTo("web,api"));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("Lab systems"));
        });
    }

    // Example 60 in proposed-cli-surface.md, by name and by ID. Only the auto DNS tags are sent, so
    // the zone's name and notes stay as they are ("Create and update").
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

    [Test]
    public async Task Dns_update_zone_sends_the_name_and_notes_given_and_prints_the_updated_zone()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", ZonePath("4"), json: ApiJson.Zone(4, "corp"));

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
            Assert.That(JsonAssert.Property(result.StdoutJson, "name").GetString(), Is.EqualTo("corp"));
        });
    }

    // The API deletes one zone per call and has no bulk zone delete (portal DnsController.cs:145), so
    // delete-zone prints the deleted zone, as single-ID commands do (proposed-cli-surface.md "Several
    // IDs").
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

    // delete-zone takes one zone (proposed-cli-surface.md "Several IDs"). The single-zone command run
    // next in the same sandbox shows the command exists and the rejection comes from the second
    // zone.
    [TestCase("dns delete-zone --id 4,5")]
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

    // Three hostnames two to a page make the CLI follow nextPage. Without --zone or --filter the
    // list asks for every hostname.
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

    // Example 62 in proposed-cli-surface.md. --zone takes the zone's name, looked up with one call,
    // and --zone-id its ID, which needs no lookup ("Names and IDs"); either way every page asks the
    // API for that zone's hostnames (DnsClient.GetRecordsAsync sends zoneId, Enclave.Sdk.Api 1.1.0).
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

    // Example 16 in proposed-cli-surface.md. --filter is sent as typed, as the API's search
    // ("Filters"; DnsClient.GetRecordsAsync sends it as search, Enclave.Sdk.Api 1.1.0).
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

    // A zone name that matches nothing exits 2 with no candidates (proposed-cli-surface.md "Errors
    // and exit codes"), before any hostname is read.
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

    // db.internal and db.lab are both the record "db", so only a lookup by the full hostname picks
    // one of them, and names match ignoring case (proposed-cli-surface.md "Names and IDs").
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

    [Test]
    public async Task Dns_show_hostname_with_id_gets_the_hostname_without_a_lookup()
    {
        using var run = CliRun.Start();
        StubHostnames(run);
        run.Stub("GET", RecordPath("7"), json: DbInternal());

        var result = await run.RunAsync("dns", "show-hostname", "--id", "7");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(RecordPath("7")));
            Assert.That(JsonAssert.Property(result.StdoutJson, "fqdn").GetString(), Is.EqualTo("db.internal"));
        });
    }

    // A hostname is its full name (proposed-cli-surface.md "Names and IDs"): "db" is the record name
    // of db.internal and db.lab, and the full name of neither. A name that matches nothing exits 2
    // with no candidates ("Errors and exit codes").
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
            Assert.That(run.Requests.Select(request => request.Method), Is.All.EqualTo("GET"));
            Assert.That(run.Requests.Select(request => request.Path), Has.None.EqualTo(RecordPath("7")).And.None.EqualTo(RecordPath("9")));
        });
    }

    // Example 15 in proposed-cli-surface.md. The zone is found with one zone list call; the API
    // takes the record name within the zone and the zone's ID (portal DnsRecordCreateModel), and
    // ENCLAVE is the only record type it has (portal DnsRecordTypeFormatConverter.cs:12).
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
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("db"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("ENCLAVE"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "systems")), Is.EqualTo("ABCDE,FGHIJ"));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("primary database pair"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(7));
        });
    }

    // A hostname's zone is the longest zone name it ends in, at a label boundary, and the record
    // name is the rest, which may contain dots (proposed-cli-surface.md "Details"). internal comes
    // before eu.internal in the zone list, so a lookup that took the first zone the hostname ends
    // in puts db.eu.internal in the wrong zone; corp.example has two labels, so its zone is more
    // than the hostname's last label.
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

    // A hostname with no zone, or equal to a zone's name, exits 2 (proposed-cli-surface.md
    // "Details"). No zone is named nowhere; xinternal ends in the text "internal" but not at a label
    // boundary; internal and eu.internal are zone names, and eu.internal would be the record "eu"
    // in internal if the zone were not matched whole.
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

    // Example 61 in proposed-cli-surface.md, by name and by ID. --set-systems replaces the whole list
    // ("Create and update"), and nothing else is sent.
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

    // --name takes a full hostname in the hostname's own zone, and the API takes the record name
    // within the zone (portal DnsRecordPatchModel.Name; proposed-cli-surface.md "Details"). The
    // record name may contain dots. The reads that find the hostname's zone are not fixed, so the
    // test checks the one PATCH it sends.
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

    // The API cannot move a record between zones, so a new name in another zone exits 2
    // (proposed-cli-surface.md "Details"). db.lab is in lab, db2 is in no zone, and db2.eu.internal
    // is in eu.internal, the longest zone it ends in, although it also ends in internal.
    [TestCase("db.lab")]
    [TestCase("db2")]
    [TestCase("db2.eu.internal")]
    public async Task Dns_update_hostname_name_outside_the_hostname_zone_exits_2_without_a_change(string hostname)
    {
        using var run = CliRun.Start();
        StubZones(run);
        StubHostnames(run);
        run.Stub("GET", RecordPath("7"), json: DbInternal());
        run.Stub("PATCH", RecordPath("7"), json: DbInternal());

        var result = await run.RunAsync("dns", "update-hostname", "--id", "7", "--name", hostname);

        CliAssert.Failed(result, "invalid_argument");
        Assert.That(run.RequestsTo("PATCH", RecordPath("7")), Is.Empty);
    }

    // A command that takes several items looks each name up with one list call and sends one bulk
    // call, printing { requested, affected } (proposed-cli-surface.md "Several IDs").
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

    // --id gives IDs and makes no lookup, and one ID goes to the bulk call too (proposed-cli-surface.md
    // "Names and IDs" and "Several IDs").
    [TestCase("7,8", 2)]
    [TestCase("7", 1)]
    public async Task Dns_delete_hostname_with_id_makes_the_bulk_call_without_a_lookup(string ids, int count)
    {
        using var run = CliRun.Start();
        StubHostnames(run);
        run.StubBulk("DELETE", Records, "dnsRecordsDeleted", count);

        var result = await run.RunAsync("dns", "delete-hostname", "--id", ids);

        CliAssert.Bulk(result, requested: count, affected: count);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(Records));
            Assert.That(string.Join(",", request.BodyIds("recordIds")), Is.EqualTo(ids));
        });
    }

    // Example 16 in proposed-cli-surface.md: the list list-hostnames prints, read from stdin, carries
    // each hostname's ID, so no lookup is made ("Names and IDs").
    [Test]
    public async Task Dns_delete_hostname_deletes_the_hostnames_of_a_list_read_from_stdin()
    {
        using var run = CliRun.Start();
        run.StdinText = CliList.Of("hostname", OldApiLab(), OldApiInternal());
        run.StubBulk("DELETE", Records, "dnsRecordsDeleted", 2);

        var result = await run.RunAsync("dns", "delete-hostname", "-");

        CliAssert.Bulk(result, requested: 2, affected: 2);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(Records));
            Assert.That(string.Join(",", request.BodyIds("recordIds")), Is.EqualTo("10,11"));
        });
    }

    // Zones and hostnames both have integer IDs, so the list's kind is what stops a zone list
    // deleting the hostnames that share its numbers (proposed-cli-surface.md "Several IDs"). The
    // hostname list run next in the same sandbox shows the rejection comes from the kind.
    [Test]
    public async Task Dns_delete_hostname_given_a_zone_list_on_stdin_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", Records, "dnsRecordsDeleted", 2);
        run.StdinText = CliList.WithIds("zone", "10", "11");

        CliAssert.Rejected(run, await run.RunAsync("dns", "delete-hostname", "-"));

        run.StdinText = CliList.WithIds("hostname", "10", "11");
        var request = await CliAssert.AcceptedAsync(run, "DELETE", Records, "dns", "delete-hostname", "-");
        Assert.That(string.Join(",", request.BodyIds("recordIds")), Is.EqualTo("10,11"));
    }

    // Single-ID commands exit 5 for an unknown ID, dns delete-zone among them (proposed-cli-surface.md
    // "Several IDs").
    [TestCase("dns show-zone --id 4", "GET", "dns/zones/4")]
    [TestCase("dns update-zone --id 4 --notes Reviewed", "PATCH", "dns/zones/4")]
    [TestCase("dns delete-zone --id 4", "DELETE", "dns/zones/4")]
    [TestCase("dns show-hostname --id 7", "GET", "dns/records/7")]
    [TestCase("dns update-hostname --id 7 --notes Reviewed", "PATCH", "dns/records/7")]
    public async Task Dns_single_id_commands_exit_5_for_an_id_the_api_does_not_know(string commandLine, string method, string suffix)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(suffix);
        using var run = CliRun.Start();
        run.StubProblem(method, TestData.OrgPath(suffix), 404, "Not Found", "No such DNS item.");

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Failed(result, "not_found");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath(suffix)));
        });
    }

    // Zone and hostname IDs are integers, and every ID is checked before any call, so a malformed
    // one exits 2 naming it and sends nothing (proposed-cli-surface.md "ID checks").
    // The same command with integer IDs run next shows the rejection comes from the ID.
    [TestCase("dns show-zone --id ../records", "dns show-zone --id 4", "GET", "dns/zones/4")]
    [TestCase("dns update-zone --id 4a --notes Reviewed", "dns update-zone --id 4 --notes Reviewed", "PATCH", "dns/zones/4")]
    [TestCase("dns delete-zone --id internal", "dns delete-zone --id 4", "DELETE", "dns/zones/4")]
    [TestCase("dns show-hostname --id 7a", "dns show-hostname --id 7", "GET", "dns/records/7")]
    [TestCase("dns update-hostname --id ../zones/4 --notes Reviewed", "dns update-hostname --id 7 --notes Reviewed", "PATCH", "dns/records/7")]
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

    // A name argument with --id contradicts itself and exits 2 before any call
    // (proposed-cli-surface.md "Details"). The command with --id alone run next shows the rejection
    // comes from the pair.
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

    // --dry-run is an option of commands that change something, and a read does not take it
    // (proposed-cli-surface.md "Details"). The read without it run next shows the rejection comes
    // from the option.
    [TestCase("dns show", "dns")]
    [TestCase("dns list-zones", "dns/zones")]
    [TestCase("dns show-zone --id 4", "dns/zones/4")]
    [TestCase("dns list-hostnames", "dns/records")]
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

    // --dry-run prints the requests the change would send and sends none of them; org.name is null
    // when the organisation was given by ID, as CliRun gives it (proposed-cli-surface.md "Dry run").
    // None of these commands needs a read, so the fake API receives no request at all, and each
    // change is one call.
    [TestCase("dns create-zone internal --dry-run", "POST", "dns/zones")]
    [TestCase("dns update-zone --id 4 --notes Reviewed --dry-run", "PATCH", "dns/zones/4")]
    [TestCase("dns delete-zone --id 4 --dry-run", "DELETE", "dns/zones/4")]
    [TestCase("dns update-hostname --id 7 --notes Reviewed --dry-run", "PATCH", "dns/records/7")]
    [TestCase("dns delete-hostname --id 7,8 --dry-run", "DELETE", "dns/records")]
    public async Task Dns_changes_with_dry_run_print_the_requests_and_send_nothing(string commandLine, string method, string suffix)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var org = JsonAssert.Property(output, "org");
        var requests = JsonAssert.Property(output, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(JsonAssert.Property(requests[0], "method").GetString(), Is.EqualTo(method));
            Assert.That(JsonAssert.Property(requests[0], "url").GetString(), Does.EndWith(TestData.OrgPath(suffix)));
        });
    }

    // The zone lookup is a read the change depends on, so it runs, and only the POST is withheld
    // (proposed-cli-surface.md "Dry run"). The printed body carries the zone the lookup found and the
    // record type.
    [Test]
    public async Task Dns_create_hostname_with_dry_run_looks_the_zone_up_and_prints_the_post_without_sending_it()
    {
        using var run = CliRun.Start();
        StubZones(run);
        run.Stub("POST", Records, json: DbInternal());

        var result = await run.RunAsync("dns", "create-hostname", "db.internal", "--dry-run");

        CliAssert.Succeeded(result);
        var requests = JsonAssert.Property(result.StdoutJson, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var body = JsonAssert.Property(requests[0], "body");
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", Zones), Is.Not.Empty);
            Assert.That(run.RequestsTo("POST", Records), Is.Empty);
            Assert.That(JsonAssert.Property(requests[0], "method").GetString(), Is.EqualTo("POST"));
            Assert.That(JsonAssert.Property(requests[0], "url").GetString(), Does.EndWith(Records));
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("db"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("ENCLAVE"));
        });
    }

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
