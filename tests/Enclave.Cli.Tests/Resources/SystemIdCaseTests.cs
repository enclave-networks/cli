using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// System IDs are upper-case: the API makes each one from an alphabet of upper-case letters and
// digits (portal Enclave.Configuration/HashIdProvider.cs:39, with the sdk repository's
// Enclave.Sdk.Security.Abstractions/SecurityConstants.cs:13, CertificateNameKeyspaceAlphabet). The
// API upper-cases the ID its own system routes are given (portal
// Enclave.Api/Modules/SystemManagement/Systems/Handlers/SingleSystemQueryHandler.cs:45,
// BulkSystemUpdateHandler.cs:56, MultipleSystemDeletionHandler.cs:110,
// UnapprovedSystems/Handlers/BulkUnapprovedSystemDeleteHandler.cs:82), and compares a policy
// gateway's system ID exactly (Policies/Validators/PolicyGatewayValidator.cs:49). The CLI
// upper-cases a system ID when it reads it, so every request and every duplicate check sees the
// form the API stores (spec/cli-surface.md "Names and IDs").
public class SystemIdCaseTests
{
    private static readonly DateTimeOffset Now = new(2030, 3, 14, 20, 0, 0, TimeSpan.Zero);

    // Each command reads the system ID from its argument; enable --for takes one system the way the
    // bulk verbs take several, so it covers the argument that holds a list. A CLI that sends the ID
    // as typed asks for systems/abcde.
    [TestCase("system show abcde", "GET", "systems/ABCDE")]
    [TestCase("system show abcde --pending", "GET", "unapproved-systems/ABCDE")]
    [TestCase("system update abcde --notes rack", "PATCH", "systems/ABCDE")]
    [TestCase("system enable abcde --for 1h", "PUT", "systems/ABCDE/enable-until")]
    public async Task A_system_id_given_in_lower_case_goes_into_the_path_upper_case(string command, string method, string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = TestData.OrgPath(pathSuffix);
        run.Stub(method, path, json: pathSuffix.StartsWith("unapproved", StringComparison.Ordinal) ? ApiJson.PendingSystem("ABCDE") : ApiJson.System("ABCDE"));

        await CliAssert.AcceptedAsync(run, method, path, command.Split(' '));
    }

    // The API upper-cases each ID of a bulk call (portal BulkSystemUpdateHandler.cs:56), so abcde and
    // ABCDE name one system, and a CLI that sends both reports two requested where one system was
    // asked for. The duplicate is removed ("Several IDs"). The lower-case form comes first, so a CLI
    // that removes duplicates ignoring case and keeps the first form sends abcde.
    [Test]
    public async Task System_disable_given_one_id_in_two_cases_sends_it_once_upper_case()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/disable");
        run.StubBulk("PUT", path, "systemsUpdated", 1);

        var result = await run.RunAsync("system", "disable", "abcde", "ABCDE");

        CliAssert.Bulk(result, 1, 1);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds("systemIds")), Is.EqualTo("ABCDE"));
    }

    // The policy gateway check finds the gateway system by an exact comparison (portal
    // PolicyGatewayValidator.cs:49), so a gateway named gw001 is refused with 400 though system
    // GW001 exists. The ID is sent upper-case.
    [Test]
    public async Task Policy_create_sends_a_gateway_system_id_given_in_lower_case_upper_case()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies");
        run.Stub("POST", path, json: ApiJson.Policy(64, "staff to office LAN"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", path, "policy", "create", "staff to office LAN", "--senders", "staff", "--gateway", "gw001:10.0.0.0/16", "--acl", "any");

        Assert.That(JsonRead.StringFieldList(request.BodyJson.GetProperty("gateways"), "systemId"), Is.EqualTo("GW001"));
    }

    // A hostname's systems are system IDs too, which the API upper-cases before it looks them up and
    // stores them (portal Dns/Handlers/DnsRecordPatchHandler.cs:128, DnsRecordCreateHandler.cs:104),
    // so they are sent in that form.
    [Test]
    public async Task Dns_update_hostname_sends_the_systems_given_in_lower_case_upper_case()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("dns/records/7");
        run.Stub("GET", path, json: ApiJson.HostnameRecord(7, "db", 4, "internal"));
        run.Stub("PATCH", path, json: ApiJson.HostnameRecord(7, "db", 4, "internal"));

        var result = await run.RunAsync("dns", "update-hostname", "--id", "7", "--set-systems", "abcde,FghiJ");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.That(JsonRead.StringList(JsonAssert.Property(patches[0].BodyJson, "Systems")), Is.EqualTo("ABCDE,FGHIJ"));
    }
}
