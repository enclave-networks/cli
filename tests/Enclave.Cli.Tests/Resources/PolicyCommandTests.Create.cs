using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// policy create <description> (proposed-cli-surface.md "Command options" and "Create and update").
// A create sends every setting that changes behaviour, with the documented value when a flag is
// left out, so a change to the API's defaults does not change what the command does. The body is
// the API's PolicyCreateModel, which the portal fills the same way (portal-spa
// redux/sagas/business/policies/createPolicySaga.ts, createPolicyDetailsSaga.ts).

/// <summary>
/// Tests for policy create.
/// </summary>
public partial class PolicyCommandTests
{
    // Example 21. A general policy has no gateway settings, and the API rejects a general policy
    // that has any (portal PolicyCreateModelValidator.cs:47-52).
    [Test]
    public async Task Policy_create_sends_every_setting_of_a_general_policy()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var request = await CliAssert.AcceptedAsync(run, "POST", PoliciesPath, "policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(Text(body, "type"), Is.EqualTo("General"));
            Assert.That(Text(body, "description"), Is.EqualTo("web to db"));
            Assert.That(JsonAssert.Property(body, "isEnabled").GetBoolean(), Is.True);
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "senderTags")), Is.EqualTo("web"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "receiverTags")), Is.EqualTo("db"));
            Assert.That(Acls(JsonAssert.Property(body, "acls")), Is.EqualTo("Tcp|5432|(none)"));
            Assert.That(request.BodyIds("senderTrustRequirements"), Is.Empty);
            Assert.That(IsUnset(body, "gateways"), Is.True, "gateways");
            Assert.That(IsUnset(body, "gatewayAllowedIpRanges"), Is.True, "gatewayAllowedIpRanges");
            Assert.That(IsUnset(body, "gatewayTrafficDirection"), Is.True, "gatewayTrafficDirection");
            Assert.That(IsUnset(body, "autoExpire"), Is.True, "autoExpire");
            Assert.That(IsUnset(body, "activeHours"), Is.True, "activeHours");
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

    // Example 21, checked first: --dry-run prints the POST with every value it would send and sends
    // nothing ("Dry run", "Create and update").
    [Test]
    public async Task Policy_create_with_dry_run_prints_the_create_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync("policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432", "--dry-run");

        var body = JsonAssert.Property(DryRunRequest(result, "POST", PoliciesPath), "body");
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

    // policy create requires at least one --acl, so the command always states what traffic the
    // policy allows; the API accepts a policy with no ACLs, and it then carries no traffic (fabric
    // StateTracker.cs:909-921, portal PolicyModelExtensions.cs:25-29).
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

    // --acl takes a protocol and, for TCP and UDP, a port or range ("Command options"). The API
    // names the protocols Any, Tcp, Udp and Icmp (PolicyAclProtocol, Enclave.Sdk.Api.Data
    // 304.48.0).
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

    // any and icmp take no ports, tcp and udp need a port or range (portal
    // PolicyAclModelValidator.cs), and the protocols are any, tcp, udp and icmp ("Command options").
    // A port is 1 to 65535, and a range low-high has low no greater than high ("Details").
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

    // Example 74. The label after "=" is the ACL's description, and --acl repeats, so a label can
    // hold a comma ("Command options").
    [Test]
    public async Task Policy_create_sends_each_acl_label_as_its_description()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "reports to db"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "reports to db", "--senders", "reports", "--receivers", "db", "--acl", "tcp:5432=PostgreSQL", "--acl", "icmp=Ping, for monitoring");

        Assert.That(Acls(JsonAssert.Property(request.BodyJson, "acls")), Is.EqualTo(Set("Tcp|5432|PostgreSQL", "Icmp|(none)|Ping, for monitoring")));
    }

    // Example 23. --trust takes trust requirement descriptions, looked up like any name, and the
    // API takes their IDs as senderTrustRequirements ("Names and IDs"). "entra staff (old)"
    // contains the name and is not a match.
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

    // Example 23, by ID: --trust-id makes no lookup ("Names and IDs").
    [Test]
    public async Task Policy_create_with_trust_ids_sends_them_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(42, "office to build farm"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "office to build farm", "--senders", "office,laptop", "--receivers", "build", "--acl", "udp:53", "--acl", "tcp:443", "--trust-id", "3,8");

        Assert.That(string.Join(",", request.BodyIds("senderTrustRequirements").Order(StringComparer.Ordinal)), Is.EqualTo("3,8"));
    }

    // Trust requirement IDs are integers, and every ID is checked before any call ("ID checks").
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
            Assert.That(CandidateIds(error), Is.Empty);
            Assert.That(run.RequestsTo("POST", PoliciesPath), Is.Empty);
        });
    }

    // Example 25. --gateway makes a gateway policy whose senders reach the routes through that
    // system, with the traffic direction Exit, the only one the API accepts (portal
    // PolicyCreateModelValidator.cs:56), and the mode Balanced when --mode is left out ("Command
    // options"). The API rejects receiver tags on an exit gateway policy (same file, 61).
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

    // Example 53. --mode takes the API's GatewayPriority values, matched ignoring case, and the
    // CLI writes Ordered where Enclave.Sdk.Api.Data 304.48.0 names the value Prioritised
    // ("`Enclave.Sdk.Api` changes", item 9; sdk
    // Enclave.Sdk.Network/NetworkPolicy/GatewayPriorityType.cs).
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

    // --mode and --subnet-filter apply to gateway policies only, and the API accepts allowed ranges
    // on gateway policies only (portal PolicyCreateModelValidator.cs:47), so without --gateway they
    // exit 2 ("Command options").
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

    // The senders of a gateway policy reach the gateway's routes, and the API rejects receiver tags
    // on it (portal PolicyCreateModelValidator.cs:61), so --gateway with --receivers exits 2
    // ("Command options").
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

    // The label after "=" is the range's description in gatewayAllowedIpRanges ("Command options");
    // the flag repeats, and the API sets no limit on how many there are.
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

    // Example 30 is the first case. --active-hours takes <days> <start>-<end> [<zone>]: days are mon
    // to sun, a range or a comma list, and times are 24-hour HH:MM ("Details"). The API's
    // ActiveHours holds the days as DayOfWeek names, the start and end as hours and minutes, and an
    // IANA or Windows time zone ID (portal Enclave.Configuration.Data/ActiveHours.cs,
    // HoursMinutes.cs; portal-spa types/api.ts ActiveHours, DayOfWeek).
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

    // The time zone is UTC when left out ("Command options"); "UTC" and "Etc/UTC" both name it in
    // the IANA database.
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

    // Anything outside <days> <start>-<end> [<zone>], with days mon to sun and 24-hour HH:MM times,
    // exits 2 ("Details").
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

    // Example 22. A clock time without a zone means its next occurrence in the machine's time zone,
    // and the policy is disabled then unless --then says otherwise ("Command options", --until); the
    // expiry is sent as a UTC instant ("Details"). The CLI runs in this process, so
    // TimeZoneInfo.Local is the zone it reads; the next occurrence is taken from the clock before
    // and after the run.
    [Test]
    public async Task Policy_create_until_a_clock_time_expires_at_its_next_local_occurrence_and_disables_the_policy()
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(70, "support ssh"));

        var before = DateTimeOffset.Now;
        var request = await CliAssert.AcceptedAsync(
            run, "POST", PoliciesPath, "policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22", "--until", "18:00");
        var after = DateTimeOffset.Now;

        var autoExpire = JsonAssert.Property(request.BodyJson, "autoExpire");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.ExpiryDateTime(autoExpire), Is.InRange(NextLocalOccurrence(before, 18, 0), NextLocalOccurrence(after, 18, 0)));
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

    // RFC 3339 with a zone is that instant, whatever the machine's zone ("Command options"), sent as
    // a UTC instant ("Details").
    [Test]
    public async Task Policy_create_until_a_time_with_a_zone_expires_at_that_instant_in_utc()
    {
        using var run = CliRun.Start();
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

    // --for with --until, and --then without either, contradict each other, and a time in the past
    // cannot be an expiry ("Details").
    [TestCase("--for", "8h", "--until", "2030-01-15T09:30:00Z")]
    [TestCase("--then", "delete", null, null)]
    [TestCase("--until", "2020-01-15T09:30:00Z", null, null)]
    public async Task Policy_create_rejects_contradicting_or_past_expiry_options(string option, string value, string? otherOption, string? otherValue)
    {
        using var run = CliRun.Start();
        run.Stub("POST", PoliciesPath, json: ApiJson.Policy(70, "support ssh"));
        string[] create = ["policy", "create", "support ssh", "--senders", "support", "--receivers", "prod", "--acl", "tcp:22"];
        string[] rejected = otherOption is null ? [.. create, option, value] : [.. create, option, value, otherOption, otherValue!];

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, [.. create, "--until", "2030-01-15T09:30:00Z"], "POST", PoliciesPath);
    }

    // A create sends an empty list for a list flag left out ("Details"), so the API's own default
    // never decides who the policy applies to.
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

    // --then takes disable or delete on policies ("Command options").
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

    private static DateTimeOffset NextLocalOccurrence(DateTimeOffset after, int hour, int minute)
    {
        var local = TimeZoneInfo.ConvertTime(after, TimeZoneInfo.Local);
        var next = local.Date.AddHours(hour).AddMinutes(minute);

        if (next <= local.DateTime)
        {
            next = next.AddDays(1);
        }

        return new DateTimeOffset(next, TimeZoneInfo.Local.GetUtcOffset(next));
    }
}
