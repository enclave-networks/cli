using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// A create sends a value for every setting that changes behaviour, so a change to the API's own
// defaults does not change what a command does, and --dry-run shows every value sent
// (proposed-cli-surface.md "Create and update"). A list flag left out is sent as an empty list
// ("Details"). The values each noun documents for its other settings are tested with that noun.
[Category(TestCategory.Pending)]
public class CreateDefaultsTests
{
    private static readonly string Zones = TestData.OrgPath("dns/zones");

    private static readonly string Records = TestData.OrgPath("dns/records");

    // The create carries the field with an empty list, so what the command sends does not rest on
    // the API's default for a field left out.
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

    // --dry-run shows every value sent ("Create and update"), the values for flags left out
    // included, and the zone lookup the create depends on runs ("Dry run").
    [Test]
    public async Task Create_with_dry_run_prints_every_value_it_would_send()
    {
        using var run = CliRun.Start();
        run.StubPages(Zones, 200, ApiJson.Zone(4, "internal"));
        run.Stub("POST", Records, json: ApiJson.HostnameRecord(7, "db", 4, "internal"));

        var result = await run.RunAsync("dns", "create-hostname", "db.internal", "--dry-run");

        CliAssert.Succeeded(result);
        var requests = JsonAssert.Property(result.StdoutJson, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var body = JsonAssert.Property(requests[0], "body");
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("POST", Records), Is.Empty);
            Assert.That(JsonAssert.Property(requests[0], "method").GetString(), Is.EqualTo("POST"));
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("db"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("ENCLAVE"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "tags")), Is.Empty);
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "systems")), Is.Empty);
        });
    }
}
