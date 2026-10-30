using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// The patch models take the whole tag list (portal SystemPatchModel.Tags,
// DnsRecordPatchModel.Tags), so --add-tags and --remove-tags read the item to keep the tags it has
// (spec/cli-surface.md "Create and update", "Calls per command").
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

    // A patch without the field leaves the list as it is (portal PatchModel.WasSet), so clearing
    // sends an empty list ("Details").
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

    // Example 10.
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

    // Example 42.
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

    // The API refuses a tag list that holds a tag twice (portal SystemPatchModelValidator,
    // UnapprovedSystemPatchModelValidator). The tags are compared as a collection that counts each
    // copy, so a list with web twice fails.
    [TestCaseSource(nameof(TagLists))]
    public async Task Set_tags_given_a_tag_twice_sends_it_once(string[] command, string path, string item)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, json: item);
        run.Stub("PATCH", path, json: item);

        var result = await run.RunAsync([.. command, "--set-tags", "web,db,web"]);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        string[] tags = ["web", "db"];
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(request.BodyJson, "Tags")), Is.EquivalentTo(tags));
        });
    }

    // As above, for a tag the item already has.
    [TestCaseSource(nameof(TagLists))]
    public async Task Add_tags_given_a_tag_the_item_has_sends_it_once(string[] command, string path, string item)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, json: item);
        run.Stub("PATCH", path, json: item);

        var result = await run.RunAsync([.. command, "--add-tags", "web"]);

        CliAssert.Succeeded(result);
        Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {path}|PATCH {path}"));
        string[] tags = ["web", "legacy", "prod"];
        Assert.That(JsonAssert.Strings(JsonAssert.Property(run.Requests[^1].BodyJson, "Tags")), Is.EquivalentTo(tags));
    }

    // update is a single-ID command ("Several IDs"), and the failed read leaves no list to patch.
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

    // The read runs under --dry-run ("Dry run"), so the printed body holds every tag the patch would
    // send.
    [Test]
    public async Task Add_tags_with_dry_run_reads_the_item_and_prints_the_whole_list_it_would_send()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/ABCDE");
        var system = ApiJson.WithUsedTags(ApiJson.System("ABCDE"), "web", "prod");
        run.Stub("GET", path, json: system);
        run.Stub("PATCH", path, json: system);

        var result = await run.RunAsync("system", "update", "ABCDE", "--add-tags", "monitoring", "--dry-run");

        var request = DryRunOutput.RequestInTestOrg(result, "PATCH", path);
        string[] tags = ["web", "prod", "monitoring"];
        Assert.Multiple(() =>
        {
            Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {path}"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(JsonAssert.Property(request, "body"), "Tags")), Is.EquivalentTo(tags));
        });
    }

    // The update commands with the tag flags ("Command options"), each item tagged web, legacy and
    // prod.
    private static IEnumerable<TestCaseData> TagLists()
    {
        yield return TagList("system update", ["system", "update", "ABCDE"], "systems/ABCDE", ApiJson.System("ABCDE"));
        yield return TagList("system update --pending", ["system", "update", "XYZ12", "--pending"], "unapproved-systems/XYZ12", ApiJson.PendingSystem("XYZ12"));
        yield return TagList("key update", ["key", "update", "--id", "12"], "enrolment-keys/12", ApiJson.Key(12, "build agents"));
        yield return TagList("dns update-hostname", ["dns", "update-hostname", "--id", "7"], "dns/records/7", ApiJson.HostnameRecord(7, "db", 4, "internal"));
    }

    // Each --set- list flag given "", with the PATCH field that holds its list.
    private static IEnumerable<TestCaseData> ClearedLists()
    {
        yield return Cleared("system update --set-tags", ["system", "update", "ABCDE", "--set-tags", string.Empty], "systems/ABCDE", ApiJson.System("ABCDE"), "Tags");
        yield return Cleared("system update --pending --set-tags", ["system", "update", "XYZ12", "--pending", "--set-tags", string.Empty], "unapproved-systems/XYZ12", ApiJson.PendingSystem("XYZ12"), "Tags");
        yield return Cleared("key update --set-tags", ["key", "update", "--id", "12", "--set-tags", string.Empty], "enrolment-keys/12", ApiJson.Key(12, "build agents"), "Tags");
        yield return Cleared("dns update-hostname --set-tags", ["dns", "update-hostname", "--id", "7", "--set-tags", string.Empty], "dns/records/7", ApiJson.HostnameRecord(7, "db", 4, "internal"), "Tags");
        yield return Cleared("dns update-hostname --set-systems", ["dns", "update-hostname", "--id", "7", "--set-systems", string.Empty], "dns/records/7", ApiJson.HostnameRecord(7, "db", 4, "internal"), "Systems");
        yield return Cleared("dns update-zone --set-auto-dns-tags", ["dns", "update-zone", "--id", "4", "--set-auto-dns-tags", string.Empty], "dns/zones/4", ApiJson.Zone(4, "internal"), "AutoDnsTags");

        // A list cleared has no entries whose labels a read would keep, so --set-acl,
        // --set-subnet-filter and key update --set-allow-ip given "" make no read ("Calls per
        // command").
        yield return Cleared("key update --set-allow-ip", ["key", "update", "--id", "12", "--set-allow-ip", string.Empty], "enrolment-keys/12", ApiJson.Key(12, "build agents"), "IpConstraints");
        yield return Cleared("policy update --set-acl", ["policy", "update", "--id", "64", "--set-acl", string.Empty], "policies/64", ApiJson.Policy(64, "staff to office LAN"), "Acls");
        yield return Cleared("policy update --set-subnet-filter", ["policy", "update", "--id", "64", "--set-subnet-filter", string.Empty], "policies/64", ApiJson.Policy(64, "staff to office LAN"), "GatewayAllowedIpRanges");
        yield return Cleared("policy update --set-gateway", ["policy", "update", "--id", "64", "--set-gateway", string.Empty], "policies/64", ApiJson.Policy(64, "staff to office LAN"), "Gateways");
    }

    private static TestCaseData TagList(string name, string[] command, string suffix, string item) =>
        new TestCaseData(command, TestData.OrgPath(suffix), ApiJson.WithUsedTags(item, "web", "legacy", "prod")).SetArgDisplayNames(name);

    private static TestCaseData Cleared(string name, string[] command, string suffix, string item, string field) =>
        new TestCaseData(command, TestData.OrgPath(suffix), item, field).SetArgDisplayNames(name);
}
