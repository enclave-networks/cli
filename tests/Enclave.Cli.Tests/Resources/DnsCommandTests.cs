using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

[Category(TestCategory.Pending)]
public class DnsCommandTests
{
    // The stubbed summary is parsed back so the test compares each field the API sent with the
    // CLI's output, whatever values the fixture holds.
    [Test]
    public async Task Dns_show_gets_the_organisation_dns_summary_and_prints_it()
    {
        using var run = CliRun.Start();
        var summary = ApiJson.DnsSummary();
        run.Stub("GET", TestData.OrgPath("dns"), json: summary);

        var result = await run.RunAsync("dns", "show");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        using var expected = JsonDocument.Parse(summary);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns")));
            Assert.That(
                JsonAssert.Property(output, "zoneCount").GetInt32(),
                Is.EqualTo(JsonAssert.Property(expected.RootElement, "zoneCount").GetInt32()));
            Assert.That(
                JsonAssert.Property(output, "totalRecordCount").GetInt32(),
                Is.EqualTo(JsonAssert.Property(expected.RootElement, "totalRecordCount").GetInt32()));
        });
    }

    [Test]
    public async Task Dns_zone_list_gets_one_page_of_zones_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/zones"), json: ApiJson.Page(ApiJson.Zone(4, "corp.example"), ApiJson.Zone(5, "lab.example")));

        var result = await run.RunAsync("dns", "zone", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/zones")));
            Assert.That(request.QueryValue("per_page"), Is.EqualTo("100"));
            Assert.That(JsonRead.IntFieldList(JsonAssert.Property(output, "items"), "id"), Is.EqualTo("4,5"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task Dns_zone_list_with_output_id_prints_one_zone_id_per_line()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/zones"), json: ApiJson.Page(ApiJson.Zone(4, "corp.example"), ApiJson.Zone(5, "lab.example")));

        var result = await run.RunAsync("dns", "zone", "list", "-o", "id");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/zones")));
            Assert.That(string.Join(",", result.StdoutLines), Is.EqualTo("4,5"));
        });
    }

    [Test]
    public async Task Dns_zone_show_gets_the_zone_and_prints_its_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));

        var result = await run.RunAsync("dns", "zone", "show", "4");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/zones/4")));
            Assert.That(JsonAssert.Property(output, "id").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(output, "name").GetString(), Is.EqualTo("corp.example"));
        });
    }

    // The positional name and --notes set the DnsZoneCreateModel fields of the same name
    // (proposal, "Shape and naming").
    [Test]
    public async Task Dns_zone_create_posts_the_name_and_notes_and_prints_the_created_zone()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("dns/zones"), json: ApiJson.Zone(4, "corp.example"));

        var result = await run.RunAsync("dns", "zone", "create", "corp.example", "--notes", "Corporate");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/zones")));
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("corp.example"));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("Corporate"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Dns_zone_update_patches_the_name_and_notes_and_prints_the_updated_zone()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example.com"));

        var result = await run.RunAsync("dns", "zone", "update", "4", "--name", "corp.example.com", "--notes", "Renamed");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/zones/4")));
            Assert.That(JsonAssert.Property(body, "Name").GetString(), Is.EqualTo("corp.example.com"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("Renamed"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "name").GetString(), Is.EqualTo("corp.example.com"));
        });
    }

    // A patch sets the fields present and leaves absent fields as they are (proposal, "Create and
    // update"); a Name field the caller did not ask for would rename the zone.
    [Test]
    public async Task Dns_zone_update_sends_only_the_fields_whose_options_are_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));

        var result = await run.RunAsync("dns", "zone", "update", "4", "--notes", "Reviewed");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("Notes").IgnoreCase);
    }

    // dns zone update takes --name and --notes (proposal, "Command options"); an update with
    // neither has nothing to send.
    [Test]
    public async Task Dns_zone_update_without_any_field_option_exits_2_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));

        var result = await run.RunAsync("dns", "zone", "update", "4");

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "PATCH", TestData.OrgPath("dns/zones/4"), "dns", "zone", "update", "4", "--notes", "Reviewed");
    }

    // The API has no bulk zone delete (portal DnsController.cs:145), so dns zone delete takes one
    // ID, calls the single delete and prints the deleted zone (proposal, "Several IDs").
    [Test]
    public async Task Dns_zone_delete_with_yes_deletes_the_one_zone_and_prints_it()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));

        var result = await run.RunAsync("dns", "zone", "delete", "4", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/zones/4")));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(4));
        });
    }

    // delete cannot be undone, so it needs --yes (proposal, "Confirmation").
    [Test]
    public async Task Dns_zone_delete_without_yes_exits_6_naming_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));

        var result = await run.RunAsync("dns", "zone", "delete", "4");

        CliAssert.Rejected(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    // dns zone delete takes one ID because the API has no bulk zone delete (proposal, "Several
    // IDs"). The same delete with one ID is then sent.
    [Test]
    public async Task Dns_zone_delete_with_two_ids_exits_2_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));
        run.Stub("DELETE", TestData.OrgPath("dns/zones/5"), json: ApiJson.Zone(5, "lab.example"));

        var result = await run.RunAsync("dns", "zone", "delete", "4", "5", "--yes");

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "DELETE", TestData.OrgPath("dns/zones/4"), "dns", "zone", "delete", "4", "--yes");
    }

    [Test]
    public async Task Dns_record_list_gets_one_page_of_records_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/records"), json: ApiJson.Page(ApiJson.Record(9, "web"), ApiJson.Record(10, "db")));

        var result = await run.RunAsync("dns", "record", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(request.QueryValue("per_page"), Is.EqualTo("100"));
            Assert.That(request.QueryValue("zoneId"), Is.Null);
            Assert.That(request.QueryValue("search"), Is.Null);
            Assert.That(JsonRead.IntFieldList(JsonAssert.Property(output, "items"), "id"), Is.EqualTo("9,10"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    // --zone filters by zone through the API's zoneId query parameter (Enclave.Sdk.Api 1.0.4,
    // DnsClient.GetRecordsAsync).
    [Test]
    public async Task Dns_record_list_sends_the_zone_and_search_options_as_query_parameters()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/records"), json: ApiJson.Page(ApiJson.Record(9, "web")));

        var result = await run.RunAsync("dns", "record", "list", "--zone", "4", "--search", "web");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(request.QueryValue("zoneId"), Is.EqualTo("4"));
            Assert.That(request.QueryValue("search"), Is.EqualTo("web"));
        });
    }

    [Test]
    public async Task Dns_record_list_with_output_id_prints_one_record_id_per_line()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/records"), json: ApiJson.Page(ApiJson.Record(9, "web"), ApiJson.Record(10, "db")));

        var result = await run.RunAsync("dns", "record", "list", "-o", "id");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(string.Join(",", result.StdoutLines), Is.EqualTo("9,10"));
        });
    }

    [Test]
    public async Task Dns_record_show_gets_the_record_and_prints_its_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "web"));

        var result = await run.RunAsync("dns", "record", "show", "9");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records/9")));
            Assert.That(JsonAssert.Property(output, "id").GetInt32(), Is.EqualTo(9));
            Assert.That(JsonAssert.Property(output, "name").GetString(), Is.EqualTo("web"));
        });
    }

    // The flags set the DnsRecordCreateModel fields of the same name (proposal, "Shape and
    // naming"); zoneId is a number because DnsZoneId is an integer-backed ID.
    [Test]
    public async Task Dns_record_create_posts_every_given_field_and_prints_the_created_record()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("dns/records"), json: ApiJson.Record(9, "web"));

        var result = await run.RunAsync(
            "dns", "record", "create", "web", "--zone", "4", "--type", "ENCLAVE", "--tags", "web,db", "--systems", "AB12C,XY34Z", "--notes", "Front end");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("ENCLAVE"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "tags")), Is.EqualTo("web,db"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "systems")), Is.EqualTo("AB12C,XY34Z"));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("Front end"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(9));
        });
    }

    [Test]
    public async Task Dns_record_create_with_only_a_zone_posts_the_name_and_zone()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("dns/records"), json: ApiJson.Record(9, "web"));

        var result = await run.RunAsync("dns", "record", "create", "web", "--zone", "4");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
        });
    }

    // A record belongs to a zone, so --zone is required (proposal, "Command options"); a zone ID is
    // an integer (proposal, "ID checks"). The same create with an integer zone is then sent.
    [TestCase("dns record create web")]
    [TestCase("dns record create web --zone corp")]
    [TestCase("dns record create web --zone ../4")]
    public async Task Dns_record_create_without_an_integer_zone_exits_2_without_sending_a_request(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("dns/records"), json: ApiJson.Record(9, "web"));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        var request = await CliAssert.AcceptedAsync(run, "POST", TestData.OrgPath("dns/records"), "dns", "record", "create", "web", "--zone", "4");
        Assert.That(JsonAssert.Property(request.BodyJson, "zoneId").GetInt32(), Is.EqualTo(4));
    }

    // --set-tags and --set-systems replace the whole list (proposal, "Create and update").
    [Test]
    public async Task Dns_record_update_patches_the_name_tags_systems_and_notes_and_prints_the_updated_record()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "api"));

        var result = await run.RunAsync(
            "dns", "record", "update", "9", "--name", "api", "--set-tags", "web,db", "--set-systems", "AB12C", "--notes", "Moved");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records/9")));
            Assert.That(JsonAssert.Property(body, "Name").GetString(), Is.EqualTo("api"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "Tags")), Is.EqualTo("web,db"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "Systems")), Is.EqualTo("AB12C"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("Moved"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "name").GetString(), Is.EqualTo("api"));
        });
    }

    // --set-tags "" clears the list (proposal, "Create and update"): the body carries an empty Tags
    // array and no other field.
    [Test]
    public async Task Dns_record_update_with_empty_set_tags_sends_an_empty_tag_list_and_nothing_else()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "web"));

        var result = await run.RunAsync("dns", "record", "update", "9", "--set-tags", string.Empty);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("Tags").IgnoreCase);
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "Tags")), Is.Empty);
        });
    }

    // dns record update takes --set-tags and --set-systems, whose names say they replace the list
    // (proposal, "Create and update"); the create flags --tags, --systems and --zone are unknown
    // there, and an update with no field has nothing to send. The update with a valid field option
    // is then sent.
    [TestCase("dns record update 9 --tags web", "dns record update 9 --set-tags web")]
    [TestCase("dns record update 9 --systems AB12C", "dns record update 9 --set-systems AB12C")]
    [TestCase("dns record update 9 --zone 4", "dns record update 9 --notes Moved")]
    [TestCase("dns record update 9", "dns record update 9 --notes Moved")]
    public async Task Dns_record_update_without_a_valid_field_option_exits_2_without_sending_a_request(string commandLine, string acceptedCommandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "web"));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "PATCH", TestData.OrgPath("dns/records/9"), acceptedCommandLine.Split(' '));
    }

    // A command that accepts several IDs always makes the bulk call, also for one ID, and prints
    // { requested, affected } (proposal, "Several IDs").
    [Test]
    public async Task Dns_record_delete_with_yes_sends_one_id_to_the_bulk_delete()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("dns/records"), json: ApiJson.Bulk("dnsRecordsDeleted", 1));

        var result = await run.RunAsync("dns", "record", "delete", "9", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(JsonRead.IntList(JsonAssert.Property(request.BodyJson, "recordIds")), Is.EqualTo("9"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(1));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Dns_record_delete_with_yes_sends_every_id_in_one_bulk_delete()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("dns/records"), json: ApiJson.Bulk("dnsRecordsDeleted", 2));

        var result = await run.RunAsync("dns", "record", "delete", "9", "10", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(JsonRead.IntList(JsonAssert.Property(request.BodyJson, "recordIds")), Is.EqualTo("9,10"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Dns_record_delete_without_yes_exits_6_naming_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("dns/records"), json: ApiJson.Bulk("dnsRecordsDeleted", 1));

        var result = await run.RunAsync("dns", "record", "delete", "9");

        CliAssert.Rejected(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    // --dry-run prints the request Enclave.Sdk.Api would send and sends nothing (proposal, "Dry
    // run"). dns zone delete is included because it is the one delete with a single-ID route.
    [TestCase("dns zone create corp.example --dry-run", "POST", "dns/zones")]
    [TestCase("dns zone update 4 --notes x --dry-run", "PATCH", "dns/zones/4")]
    [TestCase("dns zone delete 4 --dry-run", "DELETE", "dns/zones/4")]
    [TestCase("dns record create web --zone 4 --dry-run", "POST", "dns/records")]
    [TestCase("dns record update 9 --notes x --dry-run", "PATCH", "dns/records/9")]
    [TestCase("dns record delete 9 --dry-run", "DELETE", "dns/records")]
    public async Task Dns_change_commands_with_dry_run_print_the_request_and_send_nothing(string commandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();

        var result = await run.RunAsync(commandLine.Split(' '));

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var output = result.StdoutJson;
        var request = JsonAssert.Property(output, "request");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo(method));
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(TestData.OrgPath(path)));
        });
    }

    // Zone and record IDs are integers, and every ID is checked before any call because
    // Enclave.Sdk.Api 1.0.4 puts IDs into URL paths unescaped (proposal, "ID checks"). The same
    // command with integer IDs is then sent.
    [TestCase("dns zone show abc", "dns zone show 4", "GET", "dns/zones/4")]
    [TestCase("dns zone show ../records", "dns zone show 4", "GET", "dns/zones/4")]
    [TestCase("dns zone update x4 --notes x", "dns zone update 4 --notes x", "PATCH", "dns/zones/4")]
    [TestCase("dns zone delete ../records --yes", "dns zone delete 4 --yes", "DELETE", "dns/zones/4")]
    [TestCase("dns record show abc", "dns record show 9", "GET", "dns/records/9")]
    [TestCase("dns record update ../zones/4 --notes x", "dns record update 9 --notes x", "PATCH", "dns/records/9")]
    [TestCase("dns record delete 9 nine --yes", "dns record delete 9 10 --yes", "DELETE", "dns/records")]
    [TestCase("dns record list --zone corp", "dns record list --zone 4", "GET", "dns/records")]
    public async Task Dns_commands_reject_an_id_that_is_not_an_integer_without_sending_a_request(
        string commandLine, string acceptedCommandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));
        run.Stub("PATCH", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));
        run.Stub("DELETE", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));
        run.Stub("GET", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "web"));
        run.Stub("PATCH", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "web"));
        run.Stub("DELETE", TestData.OrgPath("dns/records"), json: ApiJson.Bulk("dnsRecordsDeleted", 2));
        run.Stub("GET", TestData.OrgPath("dns/records"), json: ApiJson.Page(ApiJson.Record(9, "web")));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, method, TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }

    // Single-ID commands, dns zone delete among them, exit 5 for an unknown ID (proposal, "Several
    // IDs").
    [TestCase("dns zone show 4", "GET", "dns/zones/4")]
    [TestCase("dns zone update 4 --notes x", "PATCH", "dns/zones/4")]
    [TestCase("dns zone delete 4 --yes", "DELETE", "dns/zones/4")]
    [TestCase("dns record show 9", "GET", "dns/records/9")]
    [TestCase("dns record update 9 --notes x", "PATCH", "dns/records/9")]
    public async Task Dns_single_id_commands_exit_5_when_the_api_reports_not_found(string commandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.StubProblem(method, TestData.OrgPath(path), 404, "Not Found", "The DNS item does not exist.");

        var result = await run.RunAsync(commandLine.Split(' '));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(5), result.Stderr);
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("not_found"));
            Assert.That(run.Requests, Has.Count.EqualTo(1));
        });
    }

    // --dry-run and --yes exist only on commands that change something; elsewhere they are unknown
    // options, which exit 2. The same command without the option is then sent.
    [TestCase("dns show --yes", "dns show", "dns")]
    [TestCase("dns show --dry-run", "dns show", "dns")]
    [TestCase("dns zone list --dry-run", "dns zone list", "dns/zones")]
    [TestCase("dns zone show 4 --yes", "dns zone show 4", "dns/zones/4")]
    [TestCase("dns record list --yes", "dns record list", "dns/records")]
    [TestCase("dns record show 9 --dry-run", "dns record show 9", "dns/records/9")]
    public async Task Dns_read_commands_reject_change_options_without_sending_a_request(string commandLine, string acceptedCommandLine, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("dns"), json: ApiJson.DnsSummary());
        run.Stub("GET", TestData.OrgPath("dns/zones"), json: ApiJson.Page(ApiJson.Zone(4, "corp.example")));
        run.Stub("GET", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "corp.example"));
        run.Stub("GET", TestData.OrgPath("dns/records"), json: ApiJson.Page(ApiJson.Record(9, "web")));
        run.Stub("GET", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "web"));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "GET", TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }
}
