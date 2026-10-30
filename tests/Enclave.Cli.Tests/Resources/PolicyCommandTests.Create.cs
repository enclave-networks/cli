using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static Enclave.Cli.Tests.Support.FieldText;

namespace Enclave.Cli.Tests.Resources;

public partial class PolicyCommandTests
{
    // Every field of the API's PolicyCreateModel.
    private static readonly string[] PolicyCreateFields =
    [
        "type", "description", "isEnabled", "senderTags", "receiverTags", "acls", "gatewayAllowedIpRanges", "gateways",
        "gatewayTrafficDirection", "gatewayPriority", "notes", "senderTrustRequirements", "autoExpire", "activeHours",
    ];

    // Example 21. A flag left out is sent as an empty list or null ("Create and update"). The API
    // rejects gateway settings on a general policy (portal PolicyCreateModelValidator), and --mode
    // applies to gateway policies only, so every gateway field is empty or null.
    [Test]
    public async Task Policy_create_sends_every_setting_of_a_general_policy()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "POST", PoliciesPath, "policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(PolicyCreateFields));
            Assert.That(body.GetProperty("type").GetString(), Is.EqualTo("General"));
            Assert.That(body.GetProperty("description").GetString(), Is.EqualTo("web to db"));
            Assert.That(body.GetProperty("isEnabled").GetBoolean(), Is.True);
            Assert.That(JsonRead.StringList(body.GetProperty("senderTags")), Is.EqualTo("web"));
            Assert.That(JsonRead.StringList(body.GetProperty("receiverTags")), Is.EqualTo("db"));
            Assert.That(Acls(body.GetProperty("acls")), Is.EqualTo("Tcp|5432|(none)"));
            Assert.That(body.GetProperty("senderTrustRequirements").GetRawText(), Is.EqualTo(new JsonArray().ToJsonString()));
            Assert.That(body.GetProperty("gateways").GetRawText(), Is.EqualTo(new JsonArray().ToJsonString()));
            Assert.That(body.GetProperty("gatewayAllowedIpRanges").GetRawText(), Is.EqualTo(new JsonArray().ToJsonString()));
            Assert.That(body.GetProperty("gatewayTrafficDirection").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("gatewayPriority").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("notes").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("autoExpire").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("activeHours").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task Policy_create_prints_the_policy_the_api_created()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync("policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(42));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("web to db"));
        });
    }

    // Example 21, checked first.
    [Test]
    public async Task Policy_create_with_dry_run_prints_the_create_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync("policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432", "--dry-run");

        var body = JsonAssert.Property(DryRunOutput.RequestInTestOrg(result, "POST", PoliciesPath), "body");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(Text(body, "type"), Is.EqualTo("General"));
            Assert.That(JsonAssert.Property(body, "isEnabled").GetBoolean(), Is.True);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "senderTags")), Is.EqualTo("web"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "receiverTags")), Is.EqualTo("db"));
            Assert.That(Acls(JsonAssert.Property(body, "acls")), Is.EqualTo("Tcp|5432|(none)"));
        });
    }

    [Test]
    public async Task Policy_create_with_disabled_creates_the_policy_disabled()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: DisabledPolicy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "POST", PoliciesPath, "policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432", "--disabled");

        Assert.That(JsonAssert.Property(request.BodyJson, "isEnabled").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Policy_create_sends_the_notes_given()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432", "--notes", "Ticket 1234, primary path");

        Assert.That(Text(request.BodyJson, "notes"), Is.EqualTo("Ticket 1234, primary path"));
    }

    // The API accepts a policy with no ACLs, which then carries no traffic (portal
    // PolicyModelExtensions.ToModel, state InactiveNoAcls; fabric
    // StateTracker.DoesPolicyAllowTraffic).
    [Test]
    public async Task Policy_create_without_an_acl_exits_2()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "web to db", "--senders", "web", "--receivers", "db"],
            ["policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "any"],
            "POST",
            PoliciesPath);
    }

    // The API names the protocols Any, Tcp, Udp and Icmp (PolicyAclProtocol, Enclave.Sdk.Api.Data).
    [TestCase("any", "Any|(none)|(none)")]
    [TestCase("icmp", "Icmp|(none)|(none)")]
    [TestCase("tcp:22", "Tcp|22|(none)")]
    [TestCase("udp:53", "Udp|53|(none)")]
    [TestCase("tcp:8000-8100", "Tcp|8000-8100|(none)")]
    [TestCase("tcp:65535", "Tcp|65535|(none)")]
    [TestCase("udp:1-65535", "Udp|1-65535|(none)")]
    public async Task Policy_create_sends_an_acl_as_its_protocol_and_ports(string acl, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "POST", PoliciesPath, "policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", acl);

        Assert.That(Acls(JsonAssert.Property(request.BodyJson, "acls")), Is.EqualTo(expected));
    }

    // any and icmp take no ports and tcp and udp need one (portal PolicyAclModelValidator); a port
    // is 1 to 65535, and a range's low end is no greater than its high end ("Details").
    [TestCase("tcp")]
    [TestCase("udp")]
    [TestCase("icmp:80")]
    [TestCase("any:443")]
    [TestCase("smtp:25")]
    [TestCase("tcp:0")]
    [TestCase("tcp:65536")]
    [TestCase("tcp:9000-8000")]
    [TestCase("udp:1-65536")]
    [TestCase("tcp:https")]
    public async Task Policy_create_rejects_an_acl_outside_the_protocols_and_ports_it_allows(string acl)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", acl],
            ["policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432"],
            "POST",
            PoliciesPath);
    }

    // Example 52.
    [Test]
    public async Task Policy_create_sends_every_acl_given()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "monitoring"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "monitoring", "--senders", "monitoring", "--receivers", "app", "--acl", "icmp", "--acl", "udp:8125-8126");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(Acls(JsonAssert.Property(body, "acls")), Is.EqualTo(Set("Icmp|(none)|(none)", "Udp|8125-8126|(none)")));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "senderTags")), Is.EqualTo("monitoring"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "receiverTags")), Is.EqualTo("app"));
        });
    }

    // Example 74. --acl repeats in place of a comma list, so a label can hold a comma.
    [Test]
    public async Task Policy_create_sends_each_acl_label_as_its_description()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "reports to db"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "reports to db", "--senders", "reports", "--receivers", "db", "--acl", "tcp:5432=PostgreSQL", "--acl", "icmp=Ping, for monitoring");

        Assert.That(Acls(JsonAssert.Property(request.BodyJson, "acls")), Is.EqualTo(Set("Tcp|5432|PostgreSQL", "Icmp|(none)|Ping, for monitoring")));
    }

    // Example 23. "entra staff (old)" contains the name and must not match.
    [Test]
    public async Task Policy_create_looks_up_the_trust_requirement_named_and_sends_its_id()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(2, "entra staff (old)"), ApiJson.Trust(3, "entra staff"), ApiJson.Trust(8, "contractors")));
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "office to build farm"));

        var result = await run.RunAsync(
            "policy", "create", "office to build farm", "--senders", "office,laptop", "--receivers", "build", "--acl", "udp:53", "--acl", "tcp:443", "--trust", "entra staff");

        CliAssert.Succeeded(result);
        var create = run.RequestsTo("POST", PoliciesPath);
        Assert.That(create, Has.Count.EqualTo(1));
        var body = create[0].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", create[0].BodyIds("senderTrustRequirements")), Is.EqualTo("3"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "senderTags")), Is.EqualTo("office,laptop"));
            Assert.That(Acls(JsonAssert.Property(body, "acls")), Is.EqualTo(Set("Udp|53|(none)", "Tcp|443|(none)")));
        });
    }

    // Example 23, by ID.
    [Test]
    public async Task Policy_create_with_trust_ids_sends_them_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "office to build farm"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "office to build farm", "--senders", "office,laptop", "--receivers", "build", "--acl", "udp:53", "--acl", "tcp:443", "--trust-id", "3,8");

        Assert.That(string.Join(",", request.BodyIds("senderTrustRequirements").Order(StringComparer.Ordinal)), Is.EqualTo("3,8"));
    }

    [Test]
    public async Task Policy_create_rejects_a_trust_id_that_is_not_an_integer()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "office to build farm"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "office to build farm", "--senders", "office", "--receivers", "build", "--acl", "tcp:443", "--trust-id", "3,x"],
            ["policy", "create", "office to build farm", "--senders", "office", "--receivers", "build", "--acl", "tcp:443", "--trust-id", "3"],
            "POST",
            PoliciesPath);
    }

    [Test]
    public async Task Policy_create_looks_up_every_trust_requirement_in_a_comma_list()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(3, "entra staff"), ApiJson.Trust(8, "contractors"), ApiJson.Trust(9, "uk only")));
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "office to build farm"));

        var result = await run.RunAsync("policy", "create", "office to build farm", "--senders", "office", "--receivers", "build", "--acl", "tcp:443", "--trust", "entra staff,uk only");

        CliAssert.Succeeded(result);
        var create = run.RequestsTo("POST", PoliciesPath);
        Assert.That(create, Has.Count.EqualTo(1));
        Assert.That(string.Join(",", create[0].BodyIds("senderTrustRequirements").Order(StringComparer.Ordinal)), Is.EqualTo("3,9"));
    }

    [Test]
    public async Task Policy_create_with_a_trust_requirement_no_requirement_has_exits_2_without_creating_the_policy()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(2, "entra staff (old)")));
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "office to build farm"));

        var result = await run.RunAsync("policy", "create", "office to build farm", "--senders", "office", "--receivers", "build", "--acl", "tcp:443", "--trust", "entra staff");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(NameLookup.CandidateIds(error), Is.Empty);
            Assert.That(run.RequestsTo("POST", PoliciesPath), Is.Empty);
        });
    }

    // Example 25. Exit is the only traffic direction the API accepts, and it rejects receiver tags on
    // an exit gateway policy (portal PolicyCreateModelValidator, the rules on
    // GatewayTrafficDirection and ReceiverTags). --mode defaults to balanced ("Command options").
    [Test]
    public async Task Policy_create_with_a_gateway_sends_a_gateway_policy_with_every_gateway_setting()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(64, "staff to office LAN"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "staff to office LAN", "--senders", "staff", "--gateway", "GW001:10.0.0.0/16", "--subnet-filter", "10.0.0.0/16", "--acl", "any");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(PolicyCreateFields));
            Assert.That(Text(body, "type"), Is.EqualTo("Gateway"));
            Assert.That(JsonAssert.Property(body, "isEnabled").GetBoolean(), Is.True);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "senderTags")), Is.EqualTo("staff"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "receiverTags")), Is.Empty);
            Assert.That(Gateways(JsonAssert.Property(body, "gateways")), Is.EqualTo("GW001:10.0.0.0/16"));
            Assert.That(Text(body, "gatewayTrafficDirection"), Is.EqualTo("Exit"));
            Assert.That(Text(body, "gatewayPriority"), Is.EqualTo("Balanced"));
            Assert.That(Ranges(JsonAssert.Property(body, "gatewayAllowedIpRanges")), Is.EqualTo("10.0.0.0/16|(none)"));
            Assert.That(Acls(JsonAssert.Property(body, "acls")), Is.EqualTo("Any|(none)|(none)"));
            Assert.That(request.BodyIds("senderTrustRequirements"), Is.Empty);
        });
    }

    [Test]
    public async Task Policy_create_sends_every_route_of_a_gateway()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(64, "staff to offices"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "staff to offices", "--senders", "staff", "--gateway", "GW001:10.0.0.0/16,10.1.0.0/16", "--acl", "any");

        Assert.That(Gateways(JsonAssert.Property(request.BodyJson, "gateways")), Is.EqualTo("GW001:10.0.0.0/16,10.1.0.0/16"));
    }

    // Example 53. The CLI sends Ordered where Enclave.Sdk.Api.Data names the value Prioritised
    // (spec/cli-surface.md "`Enclave.Sdk.Api` changes", item 9; sdk GatewayPriorityType).
    [TestCase("balanced", "Balanced")]
    [TestCase("ordered", "Ordered")]
    [TestCase("geographic", "Geographic")]
    [TestCase("Ordered", "Ordered")]
    public async Task Policy_create_sends_the_gateway_mode_and_the_gateways_in_the_order_given(string mode, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(65, "office LAN"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "office LAN", "--senders", "staff", "--gateway", "GW001:10.0.0.0/16", "--gateway", "GW002:10.0.0.0/16", "--mode", mode, "--acl", "any");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(Text(body, "gatewayPriority"), Is.EqualTo(expected));
            Assert.That(Gateways(JsonAssert.Property(body, "gateways")), Is.EqualTo("GW001:10.0.0.0/16; GW002:10.0.0.0/16"));
            Assert.That(Text(body, "type"), Is.EqualTo("Gateway"));
            Assert.That(Text(body, "gatewayTrafficDirection"), Is.EqualTo("Exit"));
        });
    }

    [Test]
    public async Task Policy_create_rejects_a_mode_outside_the_allowed_values()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(65, "office LAN"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "office LAN", "--senders", "staff", "--gateway", "GW001:10.0.0.0/16", "--mode", "random", "--acl", "any"],
            ["policy", "create", "office LAN", "--senders", "staff", "--gateway", "GW001:10.0.0.0/16", "--mode", "geographic", "--acl", "any"],
            "POST",
            PoliciesPath);
    }

    // The API rejects allowed ranges on a general policy (portal PolicyCreateModelValidator, the rule
    // on GatewayAllowedIpRanges).
    [TestCase("--mode", "ordered")]
    [TestCase("--subnet-filter", "10.0.0.0/16")]
    public async Task Policy_create_rejects_a_gateway_option_without_a_gateway(string option, string value)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(64, "staff to office LAN"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "staff to office LAN", "--senders", "staff", "--receivers", "printers", option, value, "--acl", "any"],
            ["policy", "create", "staff to office LAN", "--senders", "staff", "--gateway", "GW001:10.0.0.0/16", option, value, "--acl", "any"],
            "POST",
            PoliciesPath);
    }

    // The API rejects receiver tags on an exit gateway policy (portal PolicyCreateModelValidator, the
    // rule on ReceiverTags).
    [Test]
    public async Task Policy_create_rejects_a_gateway_with_receivers()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(64, "staff to office LAN"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "staff to office LAN", "--senders", "staff", "--receivers", "printers", "--gateway", "GW001:10.0.0.0/16", "--acl", "any"],
            ["policy", "create", "staff to office LAN", "--senders", "staff", "--gateway", "GW001:10.0.0.0/16", "--acl", "any"],
            "POST",
            PoliciesPath);
    }

    [Test]
    public async Task Policy_create_sends_each_subnet_filter_with_its_label()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(66, "staff to Exchange"));

        var request = await CliAssert.AcceptedAsync(
            run,
            "POST",
            PoliciesPath,
            "policy",
            "create",
            "staff to Exchange",
            "--senders",
            "staff",
            "--gateway",
            "GW001:104.47.0.0/17,40.92.0.0/15",
            "--subnet-filter",
            "104.47.0.0/17=Exchange Online",
            "--subnet-filter",
            "40.92.0.0/15",
            "--acl",
            "tcp:443");

        Assert.That(Ranges(JsonAssert.Property(request.BodyJson, "gatewayAllowedIpRanges")), Is.EqualTo(Set("104.47.0.0/17|Exchange Online", "40.92.0.0/15|(none)")));
    }

    // Example 30 is the first case. The API holds the days as DayOfWeek names and the times as hours
    // and minutes (portal ActiveHours, HoursMinutes).
    [TestCase("mon-fri 08:00-18:00 Europe/London", "Monday,Tuesday,Wednesday,Thursday,Friday", "08:00", "18:00", "Europe/London")]
    [TestCase("sat-sun 10:00-16:00 Europe/Dublin", "Saturday,Sunday", "10:00", "16:00", "Europe/Dublin")]
    [TestCase("mon,wed,fri 09:00-17:30 America/New_York", "Monday,Wednesday,Friday", "09:00", "17:30", "America/New_York")]
    [TestCase("sun 00:00-23:59 Asia/Tokyo", "Sunday", "00:00", "23:59", "Asia/Tokyo")]
    public async Task Policy_create_sends_active_hours_as_days_times_and_the_time_zone(string activeHours, string days, string start, string end, string zone)
    {
        ArgumentNullException.ThrowIfNull(days);
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(61, "facilities tablets"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "facilities tablets", "--senders", "tablets", "--receivers", "printers", "--acl", "tcp:9100", "--active-hours", activeHours);

        var hours = JsonAssert.Property(request.BodyJson, "activeHours");
        Assert.Multiple(() =>
        {
            Assert.That(Set(JsonAssert.Strings(JsonAssert.Property(hours, "daysOfWeek"))), Is.EqualTo(Set(days.Split(','))));
            Assert.That(Clock(JsonAssert.Property(hours, "startTime")), Is.EqualTo(start));
            Assert.That(Clock(JsonAssert.Property(hours, "endTime")), Is.EqualTo(end));
            Assert.That(Text(hours, "timeZoneId"), Is.EqualTo(zone));
            Assert.That(Acls(JsonAssert.Property(request.BodyJson, "acls")), Is.EqualTo("Tcp|9100|(none)"));
        });
    }

    // "UTC" and "Etc/UTC" both name UTC in the IANA database.
    [Test]
    public async Task Policy_create_sends_active_hours_in_utc_when_no_time_zone_is_given()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(61, "facilities tablets"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "facilities tablets", "--senders", "tablets", "--receivers", "printers", "--acl", "tcp:9100", "--active-hours", "mon-fri 09:00-17:30");

        var hours = JsonAssert.Property(request.BodyJson, "activeHours");
        Assert.Multiple(() =>
        {
            Assert.That(Text(hours, "timeZoneId"), Is.EqualTo("UTC").Or.EqualTo("Etc/UTC"));
            Assert.That(Clock(JsonAssert.Property(hours, "startTime")), Is.EqualTo("09:00"));
            Assert.That(Clock(JsonAssert.Property(hours, "endTime")), Is.EqualTo("17:30"));
        });
    }

    // A create has no restriction to remove, so "" and white space mean nothing here ("Details").
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("mon-fri")]
    [TestCase("mon-fri 08:00")]
    [TestCase("weekdays 08:00-18:00")]
    [TestCase("mon-fri 8:00-18:00")]
    [TestCase("mon-fri 08:00-24:00")]
    [TestCase("mon-fri 6pm-8pm")]
    [TestCase("mon-fri 08:00-18:00 Europe/London extra")]
    public async Task Policy_create_rejects_active_hours_outside_the_documented_form(string activeHours)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(61, "facilities tablets"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "facilities tablets", "--senders", "tablets", "--receivers", "printers", "--acl", "tcp:9100", "--active-hours", activeHours],
            ["policy", "create", "facilities tablets", "--senders", "tablets", "--receivers", "printers", "--acl", "tcp:9100", "--active-hours", "mon-fri 08:00-18:00"],
            "POST",
            PoliciesPath);
    }

    // Such hours are never active: the check needs a time at or after the start and before the end
    // (services SystemIsopActiveHoursExtensions.IsWithinActiveHours), so 00:00-00:00 is not the
    // whole day.
    [TestCase("mon 08:00-08:00")]
    [TestCase("mon-fri 00:00-00:00 Europe/London")]
    public async Task Policy_create_rejects_active_hours_that_start_and_end_at_the_same_time(string activeHours)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(61, "facilities tablets"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "facilities tablets", "--senders", "tablets", "--receivers", "printers", "--acl", "tcp:9100", "--active-hours", activeHours],
            ["policy", "create", "facilities tablets", "--senders", "tablets", "--receivers", "printers", "--acl", "tcp:9100", "--active-hours", "mon 08:00-18:00"],
            "POST",
            PoliciesPath);
    }

    // The API refuses a repeated sender tag, receiver tag or trust requirement ID (portal
    // PolicyCreateModelValidator, MustNotHaveDuplicates).
    [TestCase("--senders", "web,api,web", "web,api")]
    [TestCase("--receivers", "db,db", "db")]
    [TestCase("--trust-id", "3,8,3", "3,8")]
    public async Task Policy_create_with_a_value_twice_in_a_list_exits_2_without_a_request(string option, string repeated, string once)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["policy", "create", "web to db", "--acl", "tcp:5432", option, repeated], ["policy", "create", "web to db", "--acl", "tcp:5432", option, once], "POST", PoliciesPath);
    }

    [Test]
    public async Task Policy_create_with_a_flag_repeated_with_the_same_value_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "web to db", "--acl", "tcp:5432", "--senders", "web", "--senders", "web"],
            ["policy", "create", "web to db", "--acl", "tcp:5432", "--senders", "web", "--senders", "api"],
            "POST",
            PoliciesPath);
    }

    // Names match ignoring case, so "Entra Staff" repeats "entra staff" and would send its ID twice.
    [Test]
    public async Task Policy_create_with_a_trust_requirement_named_twice_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(3, "entra staff"), ApiJson.Trust(9, "uk only")));
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "office to build farm"));
        string[] create = ["policy", "create", "office to build farm", "--senders", "office", "--receivers", "build", "--acl", "tcp:443", "--trust"];

        CliAssert.Rejected(run, await run.RunAsync([.. create, "entra staff,uk only,Entra Staff"]));

        CliAssert.Succeeded(await run.RunAsync([.. create, "entra staff,uk only"]));
        var posts = run.RequestsTo("POST", PoliciesPath);
        Assert.That(posts, Has.Count.EqualTo(1));
        Assert.That(string.Join(",", posts[0].BodyIds("senderTrustRequirements").Order(StringComparer.Ordinal)), Is.EqualTo("3,9"));
    }

    // The API accepts a repeated ACL, gateway system or subnet filter (portal
    // PolicyCreateModelValidator, PolicyGatewayValidator, PolicyCreateHandler), so the CLI sends both.
    [TestCase("--senders web --receivers db --acl tcp:443 --acl tcp:443", "acls")]
    [TestCase("--senders staff --gateway GW001:10.0.0.0/16 --gateway GW001:10.1.0.0/16 --acl any", "gateways")]
    [TestCase("--senders staff --gateway GW001:10.0.0.0/16 --subnet-filter 10.0.0.0/16 --subnet-filter 10.0.0.0/16 --acl any", "gatewayAllowedIpRanges")]
    public async Task Policy_create_sends_a_repeated_acl_gateway_system_or_subnet_filter_as_given(string options, string field)
    {
        ArgumentNullException.ThrowIfNull(options);
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "POST", PoliciesPath, ["policy", "create", "web to db", .. options.Split(' ')]);

        Assert.That(JsonAssert.Property(request.BodyJson, field).GetArrayLength(), Is.EqualTo(2));
    }

    // Example 22. The clock reads 01:30 on the 15th in the test zone (UTC+05:30), so 18:00 is later
    // that day, 12:30 UTC; reading 18:00 as UTC, or using the system's clock or zone, gives another
    // instant.
    [Test]
    public async Task Policy_create_until_a_clock_time_expires_at_its_next_local_occurrence_and_disables_the_policy()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(70, "support ssh"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22", "--until", "18:00");

        var autoExpire = JsonAssert.Property(request.BodyJson, "autoExpire");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.ExpiryDateTime(autoExpire), Is.EqualTo(new DateTimeOffset(2030, 1, 15, 12, 30, 0, TimeSpan.Zero)));
            Assert.That(JsonRead.ExpiryDateTime(autoExpire).Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(Text(autoExpire, "expiryAction"), Is.EqualTo("Disable"));
            Assert.That(Acls(JsonAssert.Property(request.BodyJson, "acls")), Is.EqualTo("Tcp|22|(none)"));
        });
    }

    [Test]
    public async Task Policy_create_for_a_duration_then_delete_schedules_the_delete()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(70, "support ssh"));

        var before = DateTimeOffset.UtcNow;
        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22", "--for", "8h", "--then", "delete");
        var after = DateTimeOffset.UtcNow;

        var autoExpire = JsonAssert.Property(request.BodyJson, "autoExpire");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.ExpiryDateTime(autoExpire), Is.InRange(before.AddHours(8).AddSeconds(-1), after.AddHours(8).AddSeconds(1)));
            Assert.That(Text(autoExpire, "expiryAction"), Is.EqualTo("Delete"));
        });
    }

    [Test]
    public async Task Policy_create_until_a_time_with_a_zone_expires_at_that_instant_in_utc()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(70, "support ssh"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22", "--until", "2030-01-15T09:30:00-04:00");

        var autoExpire = JsonAssert.Property(request.BodyJson, "autoExpire");
        var expiry = JsonRead.ExpiryDateTime(autoExpire);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2030, 1, 15, 13, 30, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(Text(autoExpire, "expiryAction"), Is.EqualTo("Disable"));
        });
    }

    [TestCase("--for", "8h", "--until", "2030-01-15T09:30:00Z")]
    [TestCase("--then", "delete", null, null)]
    [TestCase("--until", "2020-01-15T09:30:00Z", null, null)]
    public async Task Policy_create_rejects_contradicting_or_past_expiry_options(string option, string value, string? otherOption, string? otherValue)
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(70, "support ssh"));
        string[] create = ["policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22"];
        string[] rejected = otherOption is null ? [.. create, option, value] : [.. create, option, value, otherOption, otherValue!];

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, [.. create, "--until", "2030-01-15T09:30:00Z"], "POST", PoliciesPath);
    }

    // The CLI sends the empty lists itself, so the API's defaults never decide who the policy applies
    // to.
    [Test]
    public async Task Policy_create_sends_an_empty_list_for_each_list_flag_left_out()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(71, "web outbound"));

        var request = await CliAssert.AcceptedAsync(run, "POST", PoliciesPath, "policy", "create", "web outbound", "--senders", "web", "--acl", "any");

        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Strings(JsonAssert.Property(request.BodyJson, "receiverTags")), Is.Empty);
            Assert.That(request.BodyIds("senderTrustRequirements"), Is.Empty);
            Assert.That(JsonRead.StringList(JsonAssert.Property(request.BodyJson, "senderTags")), Is.EqualTo("web"));
        });
    }

    [Test]
    public async Task Policy_create_rejects_then_revoke()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(70, "support ssh"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22", "--for", "8h", "--then", "revoke"],
            ["policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22", "--for", "8h", "--then", "disable"],
            "POST",
            PoliciesPath);
    }

    private static string Clock(JsonElement time) => string.Create(
        CultureInfo.InvariantCulture,
        $"{JsonAssert.Property(time, "hours").GetInt32():D2}:{JsonAssert.Property(time, "minutes").GetInt32():D2}");
}
