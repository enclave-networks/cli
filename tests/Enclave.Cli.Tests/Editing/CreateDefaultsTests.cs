using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// A create sends every setting that changes behaviour, so a change to the API's defaults does not
// change what a command does (spec/cli-surface.md "Create and update", "Details"). Each noun's own
// tests cover its other settings.
public class CreateDefaultsTests
{
    private static readonly string Zones = TestData.OrgPath("dns/zones");

    private static readonly string Records = TestData.OrgPath("dns/records");

    [TestCase("dns create-zone internal", "dns/zones", "autoDnsTags")]
    [TestCase("dns create-hostname db.internal", "dns/records", "tags")]
    [TestCase("dns create-hostname db.internal", "dns/records", "systems")]
    [TestCase("key create builds", "enrolment-keys", "tags")]
    public async Task Create_sends_an_empty_list_for_a_list_flag_left_out(string commandLine, string suffix, string field)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(suffix);
        using var run = CliRun.Start();
        run.StubPages(Zones, 200, ApiJson.Zone(4, "internal"));
        run.Stub("POST", Zones, json: ApiJson.Zone(4, "internal"));
        run.Stub("POST", Records, json: ApiJson.HostnameRecord(7, "db", 4, "internal"));
        run.Stub("POST", TestData.OrgPath("enrolment-keys"), json: ApiJson.Key(12, "builds"));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Succeeded(result);
        var posts = run.RequestsTo("POST", TestData.OrgPath(suffix));
        Assert.That(posts, Has.Count.EqualTo(1));
        Assert.That(JsonAssert.Strings(JsonAssert.Property(posts[0].BodyJson, field)), Is.Empty);
    }

    // The zone lookup the create depends on runs under --dry-run ("Dry run").
    [Test]
    public async Task Create_with_dry_run_prints_every_value_it_would_send()
    {
        using var run = CliRun.Start();
        run.StubPages(Zones, 200, ApiJson.Zone(4, "internal"));
        run.Stub("POST", Records, json: ApiJson.HostnameRecord(7, "db", 4, "internal"));

        var result = await run.RunAsync("dns", "create-hostname", "db.internal", "--dry-run");

        var body = JsonAssert.Property(DryRunOutput.RequestInTestOrg(result, "POST", Records), "body");
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("POST", Records), Is.Empty);
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("db"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("ENCLAVE"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "tags")), Is.Empty);
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "systems")), Is.Empty);
        });
    }
}
