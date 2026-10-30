using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// spec/cli-surface.md "Command options" and "Details". Keeping labels needs the item read first
// ("Calls per command"). A subnet filter's label is its description and a gateway subnet's is its
// name (portal PolicyGatewayAllowedIpRange, SystemGatewayRouteModel). Every gateway route the
// system has is given again, so only labels differ; system update's tests cover which routes stay,
// and Resources/PolicyCommandTests.Update.cs covers ACL labels.
public class LabelTests
{
    private const string PolicyById = "policy update --id 64 --set-subnet-filter";

    private const string PolicyByDescription = "policy update \"staff to office LAN\" --set-subnet-filter";

    private const string SystemGateway = "system update GW001 --enable-gateway-for";

    private static readonly string PolicyPath = TestData.OrgPath("policies/64");

    // Example 75. Each entry is its own flag, so the comma stays in the label.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task Each_flag_sends_one_entry_with_the_label_after_the_equals_sign_spaces_and_commas_included(string list)
    {
        var entries = await PatchedEntriesAsync(list, [], "10.0.0.0/16=Office LAN", "10.1.0.0/16=Warehouse, ground floor");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.1.0.0/16=Warehouse, ground floor"));
    }

    // Example 79.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task A_label_is_split_from_its_entry_at_the_first_equals_sign_so_it_can_hold_one(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")], "10.0.0.0/16=VLAN=20 (finance)", "10.2.0.0/16");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=VLAN=20 (finance)|10.2.0.0/16=New lab"));
    }

    // Example 77. The label was set before this update, as in the portal.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task An_entry_given_without_a_label_keeps_the_label_it_has(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN")], "10.0.0.0/16", "10.2.0.0/16=New lab");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.2.0.0/16=New lab"));
    }

    // Example 78.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task A_label_given_replaces_the_one_an_entry_has_and_an_empty_label_is_sent_as_null(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")], "10.0.0.0/16=Head office", "10.2.0.0/16=");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Head office|10.2.0.0/16"));
    }

    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    [TestCase(SystemGateway)]
    public async Task A_new_entry_without_a_label_has_none(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN")], "10.0.0.0/16", "10.3.0.0/16");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.3.0.0/16"));
    }

    // Example 80.
    [TestCase(PolicyById)]
    [TestCase(PolicyByDescription)]
    public async Task Set_subnet_filter_drops_the_ranges_left_out_and_keeps_the_labels_of_those_that_stay(string list)
    {
        var entries = await PatchedEntriesAsync(list, [("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")], "10.0.0.0/16", "10.3.0.0/16");

        Assert.That(entries, Is.EqualTo("10.0.0.0/16=Office LAN|10.3.0.0/16"));
    }

    // Example 80. The lookup and the read that keeps labels run under --dry-run ("Dry run").
    [Test]
    public async Task Dry_run_reads_the_policy_and_prints_the_entries_with_the_labels_kept_without_sending_the_change()
    {
        using var run = CliRun.Start();
        var policy = GatewayPolicy([("10.0.0.0/16", "Office LAN"), ("10.2.0.0/16", "New lab")]);
        run.StubPages(TestData.OrgPath("policies"), 200, policy);
        run.Stub("GET", PolicyPath, json: policy);
        run.Stub("PATCH", PolicyPath, json: policy);

        var result = await run.RunAsync("policy", "update", "staff to office LAN", "--set-subnet-filter", "10.0.0.0/16", "--set-subnet-filter", "10.3.0.0/16", "--dry-run");

        var printed = DryRunOutput.RequestInTestOrg(result, "PATCH", PolicyPath);
        var ranges = JsonAssert.Property(JsonAssert.Property(printed, "body"), "GatewayAllowedIpRanges");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests.Select(request => request.Method), Is.Not.Empty.And.All.EqualTo("GET"));
            Assert.That(Entries(ranges, "ipRange", "description"), Is.EqualTo("10.0.0.0/16=Office LAN|10.3.0.0/16"));
        });
    }

    // Returns the entries of the one PATCH as Entry writes them, sorted, since the spec does not fix
    // their order.
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

        // By description the command adds a lookup, which can also serve as the read, since a policy
        // list item is the whole PolicyModel ("Calls per command").
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

    // A gateway policy routing 10.0.0.0/8 through GW001 (portal PolicyModel,
    // PolicyGatewayDetailModel).
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

    // Each subnet is entered by a user (portal SystemGatewayRouteModel).
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

    // One list a flag replaces. Command is the command line up to the flag, and Item serves the item
    // with the given entries.
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
