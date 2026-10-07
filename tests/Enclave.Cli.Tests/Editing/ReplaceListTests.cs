using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// --set-tags replaces the whole tag list, and any --set- list flag given "" clears its list;
// --add-tags and --remove-tags change the list in place: the CLI reads the item, then patches the
// whole list (proposed-cli-surface.md "Create and update" and "Details"). The patch models take the
// whole list (portal SystemPatchModel.Tags, DnsRecordPatchModel.Tags), so the API has no call that
// adds or removes one tag, and the read is what keeps the tags the item has. --set- flags need no
// read; the add and remove flags add one ("Calls per command").
public class ReplaceListTests
{
    [TestCaseSource(nameof(TagLists))]
    public async Task Set_tags_sends_the_tags_given_as_the_whole_list_without_reading_the_item(string[] command, string path, string item)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, json: item);
        run.Stub("PATCH", path, json: item);

        var result = await run.RunAsync([.. command, "--set-tags", "web,db"]);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("Tags").IgnoreCase);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "Tags")), Is.EqualTo("web,db"));
        });
    }

    // Any --set- list flag given "" clears its list (proposed-cli-surface.md "Details"). The patch
    // clears it by sending the field as an empty list; a patch without the field leaves the list as
    // it is (portal Enclave.Api.Scaffolding/Models/PatchModel.cs, WasSet).
    [TestCaseSource(nameof(ClearedLists))]
    public async Task Set_list_flag_given_an_empty_value_sends_an_empty_list(string[] command, string path, string item, string field)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, json: item);
        run.Stub("PATCH", path, json: item);

        var result = await run.RunAsync(command);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo(field).IgnoreCase);
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, field)), Is.Empty);
        });
    }

    // Example 10 in proposed-cli-surface.md, on each command with the tag flags. The item has the
    // tags web, legacy and prod, which the patch keeps.
    [TestCaseSource(nameof(TagLists))]
    public async Task Add_tags_reads_the_item_then_patches_the_whole_list_with_the_tag_added(string[] command, string path, string item)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, json: item);
        run.Stub("PATCH", path, json: item);

        var result = await run.RunAsync([.. command, "--add-tags", "monitoring"]);

        CliAssert.Succeeded(result);
        Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {path}|PATCH {path}"));
        var body = run.Requests[^1].BodyJson;
        string[] tags = ["web", "legacy", "prod", "monitoring"];
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("Tags").IgnoreCase);
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "Tags")), Is.EquivalentTo(tags));
        });
    }

    // Example 42 in proposed-cli-surface.md, on each command with the tag flags.
    [TestCaseSource(nameof(TagLists))]
    public async Task Remove_tags_reads_the_item_then_patches_the_whole_list_without_the_tag(string[] command, string path, string item)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, json: item);
        run.Stub("PATCH", path, json: item);

        var result = await run.RunAsync([.. command, "--remove-tags", "legacy"]);

        CliAssert.Succeeded(result);
        Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {path}|PATCH {path}"));
        var body = run.Requests[^1].BodyJson;
        string[] tags = ["web", "prod"];
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("Tags").IgnoreCase);
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "Tags")), Is.EquivalentTo(tags));
        });
    }

    // update is a single-ID command, which exits 5 for an unknown ID (proposed-cli-surface.md
    // "Several IDs"). The read fails, so there is no list to patch.
    [Test]
    public async Task Add_tags_on_a_system_the_api_does_not_know_exits_5_and_patches_nothing()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/ABCDE");
        run.StubProblem("GET", path, 404, "Not Found", "No such system.");
        run.Stub("PATCH", path, json: ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "update", "ABCDE", "--add-tags", "monitoring");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.RequestsTo("PATCH", path), Is.Empty);
    }

    // --dry-run prints the requests and sends none of them, and the reads a change depends on run
    // (proposed-cli-surface.md "Dry run"): the read gives the whole list, so the printed body holds
    // every tag the patch would send.
    [Test]
    [Category(TestCategory.Pending)]
    public async Task Add_tags_with_dry_run_reads_the_item_and_prints_the_whole_list_it_would_send()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/ABCDE");
        var system = ApiJson.WithUsedTags(ApiJson.System("ABCDE"), "web", "prod");
        run.Stub("GET", path, json: system);
        run.Stub("PATCH", path, json: system);

        var result = await run.RunAsync("system", "update", "ABCDE", "--add-tags", "monitoring", "--dry-run");

        CliAssert.Succeeded(result);
        var requests = JsonAssert.Property(result.StdoutJson, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        string[] tags = ["web", "prod", "monitoring"];
        Assert.Multiple(() =>
        {
            Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {path}"));
            Assert.That(JsonAssert.Property(requests[0], "method").GetString(), Is.EqualTo("PATCH"));
            Assert.That(JsonAssert.Property(requests[0], "url").GetString(), Does.EndWith(path));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(JsonAssert.Property(requests[0], "body"), "Tags")), Is.EquivalentTo(tags));
        });
    }

    // The update commands with the tag flags (proposed-cli-surface.md "Command options") that are
    // the vehicles for these rules, with the path each reads and patches and the item the fake API
    // answers with, tagged web, legacy and prod.
    private static IEnumerable<TestCaseData> TagLists()
    {
        yield return TagList("system update", ["system", "update", "ABCDE"], "systems/ABCDE", ApiJson.System("ABCDE"));
        yield return TagList("system update --pending", ["system", "update", "XYZ12", "--pending"], "unapproved-systems/XYZ12", ApiJson.PendingSystem("XYZ12"));
        yield return TagList("dns update-hostname", ["dns", "update-hostname", "--id", "7"], "dns/records/7", ApiJson.HostnameRecord(7, "db", 4, "internal"));
    }

    // Each --set- list flag given "", with the PATCH field that holds its list.
    private static IEnumerable<TestCaseData> ClearedLists()
    {
        yield return Cleared("system update --set-tags", ["system", "update", "ABCDE", "--set-tags", string.Empty], "systems/ABCDE", ApiJson.System("ABCDE"), "Tags");
        yield return Cleared("system update --pending --set-tags", ["system", "update", "XYZ12", "--pending", "--set-tags", string.Empty], "unapproved-systems/XYZ12", ApiJson.PendingSystem("XYZ12"), "Tags");
        yield return Cleared("dns update-hostname --set-tags", ["dns", "update-hostname", "--id", "7", "--set-tags", string.Empty], "dns/records/7", ApiJson.HostnameRecord(7, "db", 4, "internal"), "Tags");
        yield return Cleared("dns update-hostname --set-systems", ["dns", "update-hostname", "--id", "7", "--set-systems", string.Empty], "dns/records/7", ApiJson.HostnameRecord(7, "db", 4, "internal"), "Systems");
        yield return Cleared("dns update-zone --set-auto-dns-tags", ["dns", "update-zone", "--id", "4", "--set-auto-dns-tags", string.Empty], "dns/zones/4", ApiJson.Zone(4, "internal"), "AutoDnsTags");
    }

    private static TestCaseData TagList(string name, string[] command, string suffix, string item) =>
        new TestCaseData(command, TestData.OrgPath(suffix), ApiJson.WithUsedTags(item, "web", "legacy", "prod")).SetArgDisplayNames(name);

    private static TestCaseData Cleared(string name, string[] command, string suffix, string item, string field) =>
        new TestCaseData(command, TestData.OrgPath(suffix), item, field).SetArgDisplayNames(name);
}
