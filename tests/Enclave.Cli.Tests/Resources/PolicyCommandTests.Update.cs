using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static Enclave.Cli.Tests.Support.FieldText;

namespace Enclave.Cli.Tests.Resources;

// Enclave.Sdk.Api keys a PATCH body by the C# property name (PatchClient.Set), so the keys are
// PascalCase. The label rules of --set-subnet-filter are tested in Editing/LabelTests.cs.

/// <summary>
/// Tests for policy update.
/// </summary>
public partial class PolicyCommandTests
{
    // Example 11.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Policy_update_patches_only_the_sender_tags_given(bool byDescription)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(40, "web to db (old)"), ApiJson.Policy(42, "web to db")));
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync(["policy", "update", .. NameLookup.Target(byDescription, "web to db", 42), "--set-senders", "web,api"]);

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

    // Example 11, checked first.
    [Test]
    public async Task Policy_update_with_dry_run_prints_the_patch_and_sends_only_the_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync("policy", "update", "web to db", "--set-senders", "web,api", "--dry-run");

        var body = JsonAssert.Property(DryRunOutput.RequestInTestOrg(result, "PATCH", PolicyPath(42)), "body");
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

    // Example 24. The 8000-8100 rule stays although the API writes its ports as "8000 - 8100"
    // (portal PolicyModelExtensions.FormatPorts).
    [TestCase(true)]
    [TestCase(false)]
    public async Task Policy_update_replaces_the_acls_and_keeps_the_labels_of_those_that_stay(bool byDescription)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(BuildToArtifactsPolicy()));
        run.Stub("GET", PolicyPath(57), json: BuildToArtifactsPolicy());
        run.Stub("PATCH", PolicyPath(57), json: ApiJson.Policy(57, "build to artifacts"));

        var result = await run.RunAsync(["policy", "update", .. NameLookup.Target(byDescription, "build to artifacts", 57), "--set-acl", "tcp:443", "--set-acl", "tcp:8000-8100"]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(57));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch[0].BodyJson), Is.EqualTo("Acls").IgnoreCase);
            Assert.That(Acls(JsonAssert.Property(patch[0].BodyJson, "Acls")), Is.EqualTo(Set("Tcp|443|HTTPS", "Tcp|8000-8100|Artifact store")));
        });
    }

    // Example 81. A new rule has no label, sent as a null description ("Details").
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
        var acls = JsonAssert.Property(patch[0].BodyJson, "Acls");
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch[0].BodyJson), Is.EqualTo("Acls").IgnoreCase);
            Assert.That(Acls(acls), Is.EqualTo(Set("Tcp|443|HTTPS", "Tcp|8443|(none)")));
            Assert.That(
                acls.EnumerateArray().Where(acl => Text(acl, "ports") == "8443").Select(acl => JsonAssert.Property(acl, "description").ValueKind),
                Is.Not.Empty.And.All.EqualTo(JsonValueKind.Null));
        });
    }

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

    // The CLI sends Ordered where Enclave.Sdk.Api.Data names the value Prioritised
    // (spec/cli-surface.md "`Enclave.Sdk.Api` changes", item 9).
    [TestCase("balanced", "Balanced")]
    [TestCase("ordered", "Ordered")]
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

    // With no names there is nothing to look up, so the patch is the only call.
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

    [Test]
    public async Task Policy_update_without_a_change_flag_exits_2()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        await CliAssert.RejectedThenAcceptedAsync(run, ["policy", "update", "--id", "42"], ["policy", "update", "--id", "42", "--notes", "Reviewed"], "PATCH", PolicyPath(42));
    }

    [Test]
    public async Task Policy_update_rejects_active_hours_outside_the_documented_form()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(61), json: ApiJson.Policy(61, "facilities tablets"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["policy", "update", "--id", "61", "--set-active-hours", "weekdays 9-5"], ["policy", "update", "--id", "61", "--set-active-hours", "mon-fri 09:00-17:00"], "PATCH", PolicyPath(61));
    }

    // Such hours are never active (services SystemIsopActiveHoursExtensions.IsWithinActiveHours).
    [Test]
    public async Task Policy_update_rejects_active_hours_that_start_and_end_at_the_same_time()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(61), json: ApiJson.Policy(61, "facilities tablets"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["policy", "update", "--id", "61", "--set-active-hours", "mon 08:00-08:00"], ["policy", "update", "--id", "61", "--set-active-hours", "mon 08:00-18:00"], "PATCH", PolicyPath(61));
    }

    // The API refuses a repeated sender tag, receiver tag or trust requirement ID (portal
    // PolicyPatchModelValidator, MustNotHaveDuplicates).
    [TestCase("--set-senders", "web,api,web", "web,api")]
    [TestCase("--set-receivers", "db,db", "db")]
    [TestCase("--set-trust-id", "3,8,3", "3,8")]
    public async Task Policy_update_with_a_value_twice_in_a_list_exits_2_without_a_request(string option, string repeated, string once)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        await CliAssert.RejectedThenAcceptedAsync(run, ["policy", "update", "--id", "42", option, repeated], ["policy", "update", "--id", "42", option, once], "PATCH", PolicyPath(42));
    }

    // Names match ignoring case, so "Entra Staff" repeats "entra staff".
    [Test]
    public async Task Policy_update_with_a_trust_requirement_named_twice_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(3, "entra staff")));
        run.Stub("PATCH", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        CliAssert.Rejected(run, await run.RunAsync("policy", "update", "--id", "42", "--set-trust", "entra staff,Entra Staff"));

        CliAssert.Succeeded(await run.RunAsync("policy", "update", "--id", "42", "--set-trust", "entra staff"));
        var patch = run.RequestsTo("PATCH", PolicyPath(42));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.That(string.Join(",", patch[0].BodyIds("SenderTrustRequirements")), Is.EqualTo("3"));
    }

    // Example 51. PatchClient.Set sends the null as JSON null (spec/cli-surface.md "`Enclave.Sdk.Api`
    // changes", item 3).
    [TestCase(true)]
    [TestCase(false)]
    public async Task Policy_update_with_empty_active_hours_removes_the_restriction(bool byDescription)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(61, "facilities tablets")));
        run.Stub("PATCH", PolicyPath(61), json: ApiJson.Policy(61, "facilities tablets"));

        var result = await run.RunAsync(["policy", "update", .. NameLookup.Target(byDescription, "facilities tablets", 61), "--set-active-hours", string.Empty]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", PolicyPath(61));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch[0].BodyJson), Is.EqualTo("ActiveHours").IgnoreCase);
            Assert.That(JsonAssert.Property(patch[0].BodyJson, "ActiveHours").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [TestCase(" ")]
    [TestCase("   ")]
    [TestCase("\t")]
    public async Task Policy_update_with_active_hours_of_only_white_space_removes_the_restriction(string activeHours)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", PolicyPath(61), json: ApiJson.Policy(61, "facilities tablets"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", PolicyPath(61), "policy", "update", "--id", "61", "--set-active-hours", activeHours);

        Assert.Multiple(() =>
        {
            Assert.That(Keys(request.BodyJson), Is.EqualTo("ActiveHours").IgnoreCase);
            Assert.That(JsonAssert.Property(request.BodyJson, "ActiveHours").ValueKind, Is.EqualTo(JsonValueKind.Null));
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

    // The policy of examples 24 and 81, with the range written as the API writes it.
    private static string BuildToArtifactsPolicy() => PolicyWith(
        57,
        "build to artifacts",
        ("acls", """[{ "protocol": "Tcp", "ports": "443", "description": "HTTPS" }, { "protocol": "Tcp", "ports": "8000 - 8100", "description": "Artifact store" }, { "protocol": "Udp", "ports": "53", "description": "DNS" }]"""));
}
