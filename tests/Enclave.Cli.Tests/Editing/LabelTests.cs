using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// Subnet filters, gateway subnets, IP ranges, countries and ACL rules take an optional label after
// "=", which the API stores with each entry (proposed-cli-surface.md "Command options"). The CLI
// splits at the first "=", so a label can hold "=", and the flags are repeated, never comma lists,
// so a label can hold a comma. A flag that replaces a list keeps the label of every entry that
// stays, which needs the item read first ("Calls per command"); a label given replaces it, an empty
// one is sent as null, and a new entry without a label has none ("Command options", "Details").
//
// The rules are checked on policy update --set-subnet-filter, with the policy given by ID and by
// description, and on system update --enable-gateway-for. A subnet filter's label is the
// description of its gatewayAllowedIpRanges entry, and a gateway subnet's is the name of its
// gatewayRoutes entry (portal PolicyGatewayAllowedIpRange.cs, SystemGatewayRouteModel.cs). Which
// gateway routes stay, and their weights, are tested with system update; here every route the
// system has is given again, so only labels differ.
[Category(TestCategory.Pending)]
public class LabelTests
{
    private const string PolicyById = "policy update --id 64 --set-subnet-filter";

    private const string PolicyByDescription = "policy update \"staff to office LAN\" --set-subnet-filter";

    private const string SystemGateway = "system update GW001 --enable-gateway-for";

    private static readonly string PolicyPath = TestData.OrgPath("policies/64");

    // Example 75 in proposed-cli-surface.md. Two entries, each with the label after its "="; the
    // comma stays in the label because each entry is its own flag.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task Each_flag_sends_one_entry_with_the_label_after_the_equals_sign_spaces_and_commas_included(string list)
    {
        var entries = await PatchedEntriesAsync(list, [], "10.0.0.0/16=Office LAN", "10.1.0.0/16=Warehouse, ground floor");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.1.0.0/16=Warehouse, ground floor"));
    }

    // Example 79 in proposed-cli-surface.md. The label of 10.0.0.0/16 is "VLAN=20 (finance)", and
    // 10.2.0.0/16, given without a label, keeps the one it has.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task A_label_is_split_from_its_entry_at_the_first_equals_sign_so_it_can_hold_one(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")], "10.0.0.0/16=VLAN=20 (finance)", "10.2.0.0/16");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=VLAN=20 (finance)|10.2.0.0/16=New lab"));
    }

    // Example 77 in proposed-cli-surface.md. The label of 10.0.0.0/16 was set before this update,
    // as in the portal, and survives it.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task An_entry_given_without_a_label_keeps_the_label_it_has(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN")], "10.0.0.0/16", "10.2.0.0/16=New lab");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.2.0.0/16=New lab"));
    }

    // Example 78 in proposed-cli-surface.md. Both ranges stay; one is relabelled, and the other's
    // empty label is sent as null ("Details").
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task A_label_given_replaces_the_one_an_entry_has_and_an_empty_label_is_sent_as_null(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")], "10.0.0.0/16=Head office", "10.2.0.0/16=");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Head office|10.2.0.0/16"));
    }

    // A new entry without a label has none (proposed-cli-surface.md "Command options"), and the entry
    // that stays keeps its label beside it.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task A_new_entry_without_a_label_has_none(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN")], "10.0.0.0/16", "10.3.0.0/16");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.3.0.0/16"));
    }

    // Example 80 in proposed-cli-surface.md, sent. --set-subnet-filter replaces the list, so
    // 10.2.0.0/16, left out, goes, and the range that stays keeps its label.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    public async Task Set_subnet_filter_drops_the_ranges_left_out_and_keeps_the_labels_of_those_that_stay(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")], "10.0.0.0/16", "10.3.0.0/16");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.3.0.0/16"));
    }

    // Example 80 in proposed-cli-surface.md. --dry-run prints the requests and sends none of them,
    // and the reads the change depends on run ("Dry run"): the lookup and the read that keeps
    // the labels, so the printed body holds the kept label.
    [Test]
    public async Task Dry_run_reads_the_policy_and_prints_the_entries_with_the_labels_kept_without_sending_the_change()
    {
        using var run = CliRun.Start();
        var policy = GatewayPolicy([("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")]);
        run.StubPages(TestData.OrgPath("policies"), 200, policy);
        run.Stub("GET", PolicyPath, json: policy);
        run.Stub("PATCH", PolicyPath, json: policy);

        var result = await run.RunAsync("policy", "update", "staff to office LAN", "--set-subnet-filter", "10.0.0.0/16", "--set-subnet-filter", "10.3.0.0/16", "--dry-run");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var requests = JsonAssert.Property(output, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var ranges = JsonAssert.Property(JsonAssert.Property(requests[0], "body"), "GatewayAllowedIpRanges");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests.Select(request => request.Method), Is.Not.Empty.And.All.EqualTo("GET"));
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(JsonAssert.Property(requests[0], "method").GetString(), Is.EqualTo("PATCH"));
            Assert.That(JsonAssert.Property(requests[0], "url").GetString(), Does.EndWith(PolicyPath));
            Assert.That(Entries(ranges, "ipRange", "description"), Is.EqualTo("10.0.0.0/16=Office LAN|10.3.0.0/16"));
        });
    }

    // Example 81 in proposed-cli-surface.md. An ACL rule stays when its protocol and ports are given
    // again, so tcp:443 keeps the label "HTTPS"; tcp:8443 is new and has none, and tcp:8000-8100 is
    // left out and goes.
    [Test]
    public async Task Set_acl_keeps_the_label_of_a_rule_that_stays_and_a_new_rule_has_none()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies/57");
        var policy = ArtifactsPolicy();
        run.StubPages(TestData.OrgPath("policies"), 200, policy);
        run.Stub("GET", path, json: policy);
        run.Stub("PATCH", path, json: policy);

        var result = await run.RunAsync("policy", "update", "build to artifacts", "--set-acl", "tcp:443", "--set-acl", "tcp:8443");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var body = patches[0].BodyJson;
        var rules = JsonAssert.Property(body, "Acls").EnumerateArray()
            .Select(rule => Entry($"{JsonAssert.Property(rule, "protocol").GetString()!.ToUpperInvariant()}:{JsonAssert.Property(rule, "ports").GetString()}", rule, "description"))
            .Order(StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo("Acls").IgnoreCase);
            Assert.That(string.Join("|", rules), Is.EqualTo("TCP:443=HTTPS|TCP:8443"));
        });
    }

    // Serves the item with the given entries, runs the flag once for each argument, checks the
    // update sent one PATCH holding the list alone, and returns that list's entries as Entry writes
    // them, sorted and joined with "|". The entries are compared without their order, which the
    // specification does not fix for these lists.
    private static async Task<string> PatchedEntriesAsync(string name, (string Value, string? Label)[] existing, params string[] arguments)
    {
        var list = ListNamed(name);
        using var run = CliRun.Start();
        var item = list.Item(existing);
        run.StubPages(TestData.OrgPath("policies"), 200, item);
        run.Stub("GET", list.Path, json: item);
        run.Stub("PATCH", list.Path, json: item);

        var result = await run.RunAsync([.. list.Command, .. arguments.SelectMany(argument => new[] { list.Flag, argument })]);

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", list.Path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.That(run.Requests[^1].Method, Is.EqualTo("PATCH"));

        // Given by ID, the command makes the read that keeps labels and the PATCH, and no lookup
        // ("Names and IDs", "Calls per command"). Given by description it adds a lookup, which can
        // also serve as the read, since a policy list item is the whole PolicyModel.
        if (!list.ByDescription)
        {
            Assert.That(string.Join("|", run.Calls()), Is.EqualTo($"GET {list.Path}|PATCH {list.Path}"));
        }

        var body = patches[0].BodyJson;
        Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo(list.Field).IgnoreCase);
        return Entries(JsonAssert.Property(body, list.Field), list.ValueKey, list.LabelKey);
    }

    private static string Entries(JsonElement array, string valueKey, string labelKey)
    {
        var entries = array.EnumerateArray()
            .Select(entry => Entry(JsonAssert.Property(entry, valueKey).GetString()!, entry, labelKey))
            .Order(StringComparer.Ordinal);

        return string.Join("|", entries);
    }

    // An entry as "value=label", or "value" alone when its label is null, the form an entry without
    // a label is sent in ("Details"). A label the entry leaves out, or one that is neither a string
    // nor null, fails the test.
    private static string Entry(string value, JsonElement entry, string labelKey)
    {
        var label = JsonAssert.Property(entry, labelKey);

        return label.ValueKind switch
        {
            JsonValueKind.String => $"{value}={label.GetString()}",
            JsonValueKind.Null => value,
            _ => throw new AssertionException($"Expected the label \"{labelKey}\" of {entry} to be a string or null."),
        };
    }

    private static LabelledList ListNamed(string name) => name switch
    {
        PolicyById => new(["policy", "update", "--id", "64"], "--set-subnet-filter", PolicyPath, false, "GatewayAllowedIpRanges", "ipRange", "description", GatewayPolicy),
        PolicyByDescription => new(["policy", "update", "staff to office LAN"], "--set-subnet-filter", PolicyPath, true, "GatewayAllowedIpRanges", "ipRange", "description", GatewayPolicy),
        SystemGateway => new(["system", "update", "GW001"], "--enable-gateway-for", TestData.OrgPath("systems/GW001"), false, "GatewayRoutes", "subnet", "name", GatewaySystem),
        _ => throw new ArgumentException($"No labelled list named \"{name}\".", nameof(name)),
    };

    // Policy 64, "staff to office LAN": a gateway policy routing 10.0.0.0/8 through GW001, in the
    // shape PolicyModel returns (portal PolicyModel.cs, PolicyGatewayDetailModel.cs), with the given
    // subnet filter entries.
    private static string GatewayPolicy((string Value, string? Label)[] ranges)
    {
        var policy = JsonNode.Parse(ApiJson.Policy(64, "staff to office LAN"))!.AsObject();
        var allowed = new JsonArray();

        foreach (var (range, label) in ranges)
        {
            allowed.Add(new JsonObject { ["ipRange"] = range, ["description"] = label });
        }

        policy["type"] = "Gateway";
        policy["gateways"] = new JsonArray(new JsonObject
        {
            ["systemId"] = "GW001",
            ["systemName"] = "gateway-01",
            ["machineName"] = "gw01",
            ["routes"] = new JsonArray(new JsonObject { ["route"] = "10.0.0.0/8", ["gatewayWeight"] = 0, ["gatewayName"] = null }),
        });
        policy["gatewayAllowedIpRanges"] = allowed;
        policy["gatewayTrafficDirection"] = "Exit";
        policy["gatewayPriority"] = "Balanced";
        return policy.ToJsonString();
    }

    // System GW001 acting as a gateway for the given subnets, each entered by a user, in the shape
    // SystemModel returns (portal SystemGatewayRouteModel.cs).
    private static string GatewaySystem((string Value, string? Label)[] subnets)
    {
        var system = JsonNode.Parse(ApiJson.System("GW001", "gateway-01"))!.AsObject();
        var routes = new JsonArray();

        foreach (var (subnet, label) in subnets)
        {
            routes.Add(new JsonObject { ["subnet"] = subnet, ["userEntered"] = true, ["weight"] = 0, ["name"] = label });
        }

        system["gatewayRoutes"] = routes;
        return system.ToJsonString();
    }

    // Policy 57, "build to artifacts", allowing TCP 443 labelled "HTTPS" and TCP 8000-8100 labelled
    // "Artifact uploads" (portal PolicyAclModel.cs; the protocol is written by its enum name).
    private static string ArtifactsPolicy()
    {
        var policy = JsonNode.Parse(ApiJson.Policy(57, "build to artifacts"))!.AsObject();
        policy["acls"] = new JsonArray(
            new JsonObject { ["protocol"] = "Tcp", ["ports"] = "443", ["description"] = "HTTPS" },
            new JsonObject { ["protocol"] = "Tcp", ["ports"] = "8000-8100", ["description"] = "Artifact uploads" });
        return policy.ToJsonString();
    }

    // One list a flag replaces: the command up to the flag, the flag, the path the item is read
    // and patched at, whether the item is given by description, the PATCH field holding the list,
    // the keys of an entry's value and label, and the item the fake API serves for given entries.
    private sealed record LabelledList(
        string[] Command,
        string Flag,
        string Path,
        bool ByDescription,
        string Field,
        string ValueKey,
        string LabelKey,
        Func<(string Value, string? Label)[], string> Item);
}
