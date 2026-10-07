using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// policy update (proposed-cli-surface.md "Command options" and "Create and update"). An update
// sends only the fields given, as a PolicyPatchModel; Enclave.Sdk.Api keys a PATCH body by the C#
// property name (PatchClient.Set, version 1.0.4), so the keys are PascalCase. --set-acl and
// --set-subnet-filter read the policy first so that entries which stay keep their labels ("Calls
// per command"); the other flags make no read.

/// <summary>
/// Tests for policy update.
/// </summary>
public partial class PolicyCommandTests
{
    // Example 11. A description costs one lookup and an ID none ("Calls per command").
    [TestCase(true)]
    [TestCase(false)]
    public async Task Policy_update_patches_only_the_sender_tags_given(bool byDescription)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(40, "web to db (old)"), ApiJson.Policy(42, "web to db")));
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync(["policy", "update", .. Target(byDescription, "web to db", 42), "--set-senders", "web,api"]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(42));
        Assert.That(patch, Has.Count.EqualTo(1));
        var body = patch[0].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(Keys(body), Is.EqualTo("SenderTags").IgnoreCase);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "SenderTags")), Is.EqualTo("web,api"));
            Assert.That(run.Calls(), Has.Length.EqualTo(byDescription ? 2 : 1));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(42));
        });
    }

    // Example 11, checked first: the lookup is a read, and the PATCH is printed and not sent ("Dry
    // run").
    [Test]
    [Category(TestCategory.Pending)]
    public async Task Policy_update_with_dry_run_prints_the_patch_and_sends_only_the_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync("policy", "update", "web to db", "--set-senders", "web,api", "--dry-run");

        var body = JsonAssert.Property(DryRunRequest(result, "PATCH", PolicyPath(42)), "body");
        Assert.Multiple(() =>
        {
            Assert.That(Keys(body), Is.EqualTo("SenderTags").IgnoreCase);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "SenderTags")), Is.EqualTo("web,api"));
            Assert.That(run.RequestsTo("PATCH", PolicyPath(42)), Is.Empty);
        });
    }

    [Test]
    public async Task Policy_update_patches_only_the_receiver_tags_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", PolicyPath(42), "policy", "update", "--id", "42", "--set-receivers", "db,cache");

        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo("ReceiverTags").IgnoreCase);
            Assert.That(JsonRead.StringList(JsonAssert.Property(request.BodyJson, "ReceiverTags")), Is.EqualTo("db,cache"));
        });
    }

    [Test]
    public async Task Policy_update_patches_the_description_and_notes_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to database"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", PolicyPath(42), "policy", "update", "--id", "42", "--description", "web to database", "--notes", "Reviewed");

        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo(Set("Description", "Notes")).IgnoreCase);
            Assert.That(Text(request.BodyJson, "Description"), Is.EqualTo("web to database"));
            Assert.That(Text(request.BodyJson, "Notes"), Is.EqualTo("Reviewed"));
        });
    }

    // Example 24. The read keeps the labels of the rules that stay, and the 8000-8100 rule stays
    // although the API writes its ports as "8000 - 8100" (portal PolicyModelExtensions.FormatPorts).
    // The UDP rule is not given, so it goes.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Policy_update_replaces_the_acls_and_keeps_the_labels_of_those_that_stay(bool byDescription)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(BuildToArtifactsPolicy()));
        run.Stub("GET", PolicyPath(57), json: BuildToArtifactsPolicy());
        run.Stub("PATCH", PolicyPath(57), json: ApiJson.Policy(57, "build to artifacts"));

        var result = await run.RunAsync(["policy", "update", .. Target(byDescription, "build to artifacts", 57), "--set-acl", "tcp:443", "--set-acl", "tcp:8000-8100"]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(57));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch[0].BodyJson), Is.EqualTo("Acls").IgnoreCase);
            Assert.That(Acls(JsonAssert.Property(patch[0].BodyJson, "Acls")), Is.EqualTo(Set("Tcp|443|HTTPS", "Tcp|8000-8100|Artifact store")));
        });
    }

    // Example 81: tcp:443 keeps its label, and tcp:8443 is new, so it has none ("Command options").
    [Test]
    public async Task Policy_update_gives_a_new_acl_no_label()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(BuildToArtifactsPolicy()));
        run.Stub("GET", PolicyPath(57), json: BuildToArtifactsPolicy());
        run.Stub("PATCH", PolicyPath(57), json: ApiJson.Policy(57, "build to artifacts"));

        var result = await run.RunAsync("policy", "update", "build to artifacts", "--set-acl", "tcp:443", "--set-acl", "tcp:8443");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(57));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.That(Acls(JsonAssert.Property(patch[0].BodyJson, "Acls")), Is.EqualTo(Set("Tcp|443|HTTPS", "Tcp|8443|(none)")));
    }

    // --set-trust takes descriptions, looked up like any name ("Names and IDs"), and replaces the
    // policy's sender trust requirements.
    [Test]
    public async Task Policy_update_looks_up_the_trust_requirements_named_and_patches_their_ids()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(2, "entra staff (old)"), ApiJson.Trust(3, "entra staff")));
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync("policy", "update", "--id", "42", "--set-trust", "entra staff");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(42));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch[0].BodyJson), Is.EqualTo("SenderTrustRequirements").IgnoreCase);
            Assert.That(string.Join(",", patch[0].BodyIds("SenderTrustRequirements")), Is.EqualTo("3"));
        });
    }

    [Test]
    public async Task Policy_update_with_trust_ids_patches_them_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", PolicyPath(42), "policy", "update", "--id", "42", "--set-trust-id", "3,8");

        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo("SenderTrustRequirements").IgnoreCase);
            Assert.That(string.Join(",", request.BodyIds("SenderTrustRequirements").Order(StringComparer.Ordinal)), Is.EqualTo("3,8"));
        });
    }

    // Gateways carry no labels in the patch model (PolicyGateway: systemId and routes), so
    // --set-gateway needs no read ("Calls per command").
    [Test]
    public async Task Policy_update_replaces_the_gateways_in_the_order_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(64), json: ApiJson.Policy(64, "staff to office LAN"));

        var request = await CliAssert.AcceptedAsync(
            run, "PATCH", PolicyPath(64), "policy", "update", "--id", "64", "--set-gateway", "GW002:10.0.0.0/16,10.1.0.0/16", "--set-gateway", "GW001:10.0.0.0/16");

        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo("Gateways").IgnoreCase);
            Assert.That(Gateways(JsonAssert.Property(request.BodyJson, "Gateways")), Is.EqualTo("GW002:10.0.0.0/16,10.1.0.0/16; GW001:10.0.0.0/16"));
        });
    }

    // The CLI writes Ordered where Enclave.Sdk.Api.Data 304.48.0 names the value Prioritised ("Needs
    // Enclave.Sdk.Api changes", item 9).
    [TestCase("balanced", "Balanced")]
    [TestCase("ordered", "Ordered", Category = TestCategory.Pending)]
    [TestCase("geographic", "Geographic")]
    public async Task Policy_update_patches_the_gateway_mode(string mode, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(64), json: ApiJson.Policy(64, "staff to office LAN"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", PolicyPath(64), "policy", "update", "--id", "64", "--mode", mode);

        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo("GatewayPriority").IgnoreCase);
            Assert.That(Text(request.BodyJson, "GatewayPriority"), Is.EqualTo(expected));
        });
    }

    // Example 77. 10.0.0.0/16 is given without a label, so it keeps the one it has; 10.2.0.0/16 is
    // new with its own; 10.1.0.0/16 is not given, so it goes ("Command options").
    [TestCase(true)]
    [TestCase(false)]
    public async Task Policy_update_replaces_the_subnet_filters_and_keeps_the_label_of_a_range_given_without_one(bool byDescription)
    {
        using var run = CliRun.Start();
        var policy = OfficeLanPolicy("""[{ "ipRange": "10.0.0.0/16", "description": "Office LAN" }, { "ipRange": "10.1.0.0/16", "description": "Warehouse" }]""");
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(policy));
        run.Stub("GET", PolicyPath(64), json: policy);
        run.Stub("PATCH", PolicyPath(64), json: policy);

        var result = await run.RunAsync(
            ["policy", "update", .. Target(byDescription, "staff to office LAN", 64), "--set-subnet-filter", "10.0.0.0/16", "--set-subnet-filter", "10.2.0.0/16=New lab"]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(64));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch[0].BodyJson), Is.EqualTo("GatewayAllowedIpRanges").IgnoreCase);
            Assert.That(Ranges(JsonAssert.Property(patch[0].BodyJson, "GatewayAllowedIpRanges")), Is.EqualTo(Set("10.0.0.0/16|Office LAN", "10.2.0.0/16|New lab")));
        });
    }

    // Example 78. A label given replaces the one kept, and an empty label removes it ("Command
    // options"): it is sent as a null description ("Details").
    [Test]
    public async Task Policy_update_relabels_one_subnet_filter_and_removes_the_label_of_another()
    {
        using var run = CliRun.Start();
        var policy = OfficeLanPolicy("""[{ "ipRange": "10.0.0.0/16", "description": "Office LAN" }, { "ipRange": "10.2.0.0/16", "description": "New lab" }]""");
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(policy));
        run.Stub("GET", PolicyPath(64), json: policy);
        run.Stub("PATCH", PolicyPath(64), json: policy);

        var result = await run.RunAsync("policy", "update", "staff to office LAN", "--set-subnet-filter", "10.0.0.0/16=Head office", "--set-subnet-filter", "10.2.0.0/16=");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(64));
        Assert.That(patch, Has.Count.EqualTo(1));
        var ranges = JsonAssert.Property(patch[0].BodyJson, "GatewayAllowedIpRanges");
        Assert.Multiple(() =>
        {
            Assert.That(Ranges(ranges), Is.EqualTo(Set("10.0.0.0/16|Head office", "10.2.0.0/16|(none)")));
            Assert.That(ranges.EnumerateArray().Where(range => Text(range, "ipRange") == "10.2.0.0/16").Select(range => JsonAssert.Property(range, "description").ValueKind), Has.All.EqualTo(JsonValueKind.Null));
        });
    }

    // Any --set- list flag given "" clears the list ("Details"); with no names there is nothing to
    // look up, so the patch is the only call.
    [TestCase("--set-senders", "SenderTags")]
    [TestCase("--set-receivers", "ReceiverTags")]
    [TestCase("--set-trust", "SenderTrustRequirements")]
    [TestCase("--set-trust-id", "SenderTrustRequirements")]
    public async Task Policy_update_with_an_empty_list_flag_clears_that_list(string option, string field)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", PolicyPath(42), "policy", "update", "--id", "42", option, string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo(field).IgnoreCase);
            Assert.That(JsonAssert.Property(request.BodyJson, field).GetArrayLength(), Is.Zero);
        });
    }

    // An update with no change flag exits 2 ("Details"): it has nothing to send.
    [Test]
    public async Task Policy_update_without_a_change_flag_exits_2()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        await CliAssert.RejectedThenAcceptedAsync(run, ["policy", "update", "--id", "42"], ["policy", "update", "--id", "42", "--notes", "Reviewed"], "PATCH", PolicyPath(42));
    }

    // --set-active-hours takes the same form as --active-hours ("Details").
    [Test]
    public async Task Policy_update_rejects_active_hours_outside_the_documented_form()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(61), json: ApiJson.Policy(61, "facilities tablets"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["policy", "update", "--id", "61", "--set-active-hours", "weekdays 9-5"], ["policy", "update", "--id", "61", "--set-active-hours", "mon-fri 09:00-17:00"], "PATCH", PolicyPath(61));
    }

    // Example 79. The CLI splits at the first "=", so the label is "VLAN=20 (finance)" ("Command
    // options").
    [Test]
    public async Task Policy_update_keeps_every_equals_sign_after_the_first_in_a_subnet_filter_label()
    {
        using var run = CliRun.Start();
        var policy = OfficeLanPolicy("""[{ "ipRange": "10.0.0.0/16", "description": "Office LAN" }, { "ipRange": "10.2.0.0/16", "description": "New lab" }]""");
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(policy));
        run.Stub("GET", PolicyPath(64), json: policy);
        run.Stub("PATCH", PolicyPath(64), json: policy);

        var result = await run.RunAsync("policy", "update", "staff to office LAN", "--set-subnet-filter", "10.0.0.0/16=VLAN=20 (finance)", "--set-subnet-filter", "10.2.0.0/16");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(64));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.That(Ranges(JsonAssert.Property(patch[0].BodyJson, "GatewayAllowedIpRanges")), Is.EqualTo(Set("10.0.0.0/16|VLAN=20 (finance)", "10.2.0.0/16|New lab")));
    }

    // Example 80. The reads run, so the printed request shows the kept labels, and the PATCH is not
    // sent ("Dry run").
    [Test]
    [Category(TestCategory.Pending)]
    public async Task Policy_update_with_dry_run_prints_the_subnet_filters_with_their_kept_labels_and_sends_no_change()
    {
        using var run = CliRun.Start();
        var policy = OfficeLanPolicy("""[{ "ipRange": "10.0.0.0/16", "description": "Office LAN" }, { "ipRange": "10.2.0.0/16", "description": "New lab" }]""");
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(policy));
        run.Stub("GET", PolicyPath(64), json: policy);
        run.Stub("PATCH", PolicyPath(64), json: policy);

        var result = await run.RunAsync("policy", "update", "staff to office LAN", "--set-subnet-filter", "10.0.0.0/16", "--set-subnet-filter", "10.3.0.0/16", "--dry-run");

        var body = JsonAssert.Property(DryRunRequest(result, "PATCH", PolicyPath(64)), "body");
        Assert.Multiple(() =>
        {
            Assert.That(Ranges(JsonAssert.Property(body, "GatewayAllowedIpRanges")), Is.EqualTo(Set("10.0.0.0/16|Office LAN", "10.3.0.0/16|(none)")));
            Assert.That(run.RequestsTo("PATCH", PolicyPath(64)), Is.Empty);
        });
    }

    // Example 51. An empty value removes the restriction: the patch sets ActiveHours to null, which
    // needs Enclave.Sdk.Api to accept null in a patch ("Needs Enclave.Sdk.Api changes", item 3).
    [TestCase(true)]
    [TestCase(false)]
    [Category(TestCategory.Pending)]
    public async Task Policy_update_with_empty_active_hours_removes_the_restriction(bool byDescription)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(61, "facilities tablets")));
        run.Stub("PATCH", PolicyPath(61), json: ApiJson.Policy(61, "facilities tablets"));

        var result = await run.RunAsync(["policy", "update", .. Target(byDescription, "facilities tablets", 61), "--set-active-hours", string.Empty]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(61));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch[0].BodyJson), Is.EqualTo("ActiveHours").IgnoreCase);
            Assert.That(JsonAssert.Property(patch[0].BodyJson, "ActiveHours").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task Policy_update_patches_the_active_hours_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(61), json: ApiJson.Policy(61, "facilities tablets"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", PolicyPath(61), "policy", "update", "--id", "61", "--set-active-hours", "mon-fri 07:00-19:00 Europe/Dublin");

        var hours = JsonAssert.Property(request.BodyJson, "ActiveHours");
        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo("ActiveHours").IgnoreCase);
            Assert.That(Set(JsonAssert.Strings(JsonAssert.Property(hours, "daysOfWeek"))), Is.EqualTo(Set("Monday", "Tuesday", "Wednesday", "Thursday", "Friday")));
            Assert.That(Clock(JsonAssert.Property(hours, "startTime")), Is.EqualTo("07:00"));
            Assert.That(Clock(JsonAssert.Property(hours, "endTime")), Is.EqualTo("19:00"));
            Assert.That(Text(hours, "timeZoneId"), Is.EqualTo("Europe/Dublin"));
        });
    }

    // Single-item commands exit 5 for an unknown ID ("Several IDs").
    [Test]
    public async Task Policy_update_with_an_unknown_id_exits_5()
    {
        using var run = CliRun.Start();
        run.StubProblem("PATCH", PolicyPath(42), 404, "Not Found", "Policy 42 does not exist.");

        var result = await run.RunAsync("policy", "update", "--id", "42", "--notes", "Reviewed");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo(PolicyPath(42)));
    }

    // The patch's keys, sorted, so a test can state the exact set of fields an update sends.
    private static string Keys(JsonElement body) => Set(JsonRead.PropertyNames(body));

    // The rules of examples 24 and 81: tcp:443 and the 8000-8100 range are labelled, and the API
    // writes the range with spaces (portal PolicyModelExtensions.FormatPorts).
    private static string BuildToArtifactsPolicy() => PolicyWith(
        57,
        "build to artifacts",
        ("acls", """[{ "protocol": "Tcp", "ports": "443", "description": "HTTPS" }, { "protocol": "Tcp", "ports": "8000 - 8100", "description": "Artifact store" }, { "protocol": "Udp", "ports": "53", "description": "DNS" }]"""));

    // A gateway policy as the API returns it (portal PolicyModelExtensions.ToModel), with the given
    // subnet filters.
    private static string OfficeLanPolicy(string gatewayAllowedIpRanges) => PolicyWith(
        64,
        "staff to office LAN",
        ("type", "\"Gateway\""),
        ("gateways", """[{ "systemId": "GW001", "systemName": "gateway", "machineName": "gw-01", "routes": [{ "route": "10.0.0.0/16", "gatewayWeight": 0, "gatewayName": "Office LAN" }] }]"""),
        ("gatewayTrafficDirection", "\"Exit\""),
        ("gatewayPriority", "\"Balanced\""),
        ("gatewayAllowedIpRanges", gatewayAllowedIpRanges));
}
