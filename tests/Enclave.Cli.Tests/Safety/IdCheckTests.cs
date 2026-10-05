using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// Every ID is checked against its format before any call, and one malformed ID stops the command
/// with exit 2 (proposal, "ID checks"). An unknown command also exits 2, so each test also runs the
/// command with a valid ID and checks it sends the request.
/// </summary>
public class IdCheckTests
{
    private const string Until = "2030-01-01T00:00:00Z";

    private static readonly Guid AccountId = new("5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25");

    private static readonly string[] OneSystem = ["ABCDE"];

    private static readonly string[] LookupOnly = ["GET /account/orgs"];

    // Enclave.Sdk.Api 1.0.4 puts IDs into URL paths unescaped, and .NET resolves ".." when it
    // combines the path with the base address, so the bulk decline's single counterpart
    // (UnapprovedSystemsClient.cs:95, DeclineAsync) would send DELETE org/<id>/systems/ABCDE and
    // revoke system ABCDE. This is the proposal's own example. The CLI rejects the ID before any
    // call, so neither the decline nor a revoke reaches the API.
    [Test]
    public async Task Pending_decline_exits_2_for_a_path_traversal_id_and_sends_no_request()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("unapproved-systems"), 200, ApiJson.Bulk("systemsDeclined", 1));
        run.Stub("DELETE", TestData.OrgPath("systems"), 200, ApiJson.Bulk("systemsRevoked", 1));
        run.Stub("DELETE", TestData.OrgPath("systems/ABCDE"), 200, ApiJson.System("ABCDE"));

        var rejected = await run.RunAsync("pending", "decline", "../systems/ABCDE", "--yes");

        AssertRejected(rejected, "../systems/ABCDE");
        Assert.That(run.Requests, Is.Empty);

        var accepted = await run.RunAsync("pending", "decline", "ABCDE", "--yes");

        AssertSucceeded(accepted);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("unapproved-systems")));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(request.BodyJson, "systemIds")), Is.EqualTo(OneSystem));
        });
    }

    // TagsClient.Update puts the tag into the PATCH path unescaped (Enclave.Sdk.Api 1.0.4,
    // TagsClient.cs, Update(string tag)), so "../systems/ABCDE" would resolve to
    // org/<id>/systems/ABCDE and set the notes of system ABCDE.
    [Test]
    public async Task Tag_update_exits_2_for_a_path_traversal_tag_and_sends_no_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("tags/web"), 200, ApiJson.Tag("web"));
        run.Stub("PATCH", TestData.OrgPath("systems/ABCDE"), 200, ApiJson.System("ABCDE"));

        var rejected = await run.RunAsync("tag", "update", "../systems/ABCDE", "--notes", "x");

        AssertRejected(rejected, "../systems/ABCDE");
        Assert.That(run.Requests, Is.Empty);

        var accepted = await run.RunAsync("tag", "update", "web", "--notes", "x");

        AssertSucceeded(accepted);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags/web")));
        });
    }

    // Formats from the proposal's "ID checks" table: system IDs are letters and digits; tags follow
    // the API's rule ^([a-z0-9]+[-.])*[a-z0-9]+$ (portal TagValidationExtensions.cs:13); account
    // IDs are GUIDs; key, policy, zone, record and trust requirement IDs are integers (portal
    // Enclave.Configuration.Data/Identifiers, IdBackingType.Int, so 99999999999 is out of range).
    // IDs given through options (--key, --zone, --set-systems) are checked the same way.
    [TestCaseSource(nameof(MalformedIdCases))]
    public async Task Command_exits_2_for_a_malformed_id_and_sends_no_request(
        string[] rejectedArgs,
        string malformedId,
        string[] acceptedArgs,
        string method,
        string path,
        string? response)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, 200, response);

        var rejected = await run.RunAsync(rejectedArgs);

        AssertRejected(rejected, malformedId);
        Assert.That(run.Requests, Is.Empty);

        var accepted = await run.RunAsync(acceptedArgs);

        AssertSucceeded(accepted);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
        });
    }

    // One malformed ID among several stops the whole command: sending the valid ones would leave the
    // caller to work out which IDs were changed.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_2_when_one_id_argument_is_malformed_and_sends_none(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 2);

        var rejected = await run.RunAsync(command.Args(command.Id(1), command.MalformedId, command.Id(2)));

        AssertRejected(rejected, command.MalformedId);
        Assert.That(run.Requests, Is.Empty);

        var accepted = await run.RunAsync(command.Args(command.Id(1), command.Id(2)));

        AssertSucceeded(accepted);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(command.Path));
    }

    // IDs piped from another command are checked in the same way as IDs on the command line, and
    // all of them are checked before the bulk call.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_2_when_one_id_on_stdin_is_malformed_and_sends_none(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 2);
        run.StdinText = BulkCommand.Lines([command.Id(1), command.MalformedId, command.Id(2)]);

        var rejected = await run.RunAsync(command.Args("-"));

        AssertRejected(rejected, command.MalformedId);
        Assert.That(run.Requests, Is.Empty);

        run.StdinText = BulkCommand.Lines([command.Id(1), command.Id(2)]);
        var accepted = await run.RunAsync(command.Args("-"));

        AssertSucceeded(accepted);
        Assert.That(command.BodyIds(run.SingleRequest().BodyJson), Is.EquivalentTo(command.Ids(2)));
    }

    // "Every ID is checked before any call" includes the organisation lookup that --org <name>
    // makes, so a malformed ID costs no API call at all.
    [Test]
    public async Task Id_check_runs_before_the_organisation_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("PUT", TestData.OrgPath("systems/disable"), 200, ApiJson.Bulk("systemsUpdated", 1));

        var rejected = await run.RunAsync("system", "disable", "ABCDE", "AB/CD", "--org", TestData.OrgName);

        AssertRejected(rejected, "AB/CD");
        Assert.That(run.Requests, Is.Empty);

        var accepted = await run.RunAsync("system", "disable", "ABCDE", "--org", TestData.OrgName);

        AssertSucceeded(accepted);
        string[] expected = ["GET /account/orgs", $"PUT {TestData.OrgPath("systems/disable")}"];
        Assert.That(run.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(expected));
    }

    // A dry run prints the request a command would send, so a command that would be rejected is
    // rejected under --dry-run too, before the dry run's own organisation lookup.
    [Test]
    public async Task Id_check_applies_under_dry_run()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var rejected = await run.RunAsync("system", "disable", "AB/CD", "--dry-run");

        AssertRejected(rejected, "AB/CD");
        Assert.That(run.Requests, Is.Empty);

        var accepted = await run.RunAsync("system", "disable", "ABCDE", "--dry-run");

        AssertSucceeded(accepted);
        Assert.That(JsonAssert.Property(accepted.StdoutJson, "dryRun").GetBoolean(), Is.True);
    }

    // --org takes an organisation ID or name. A value that is not a GUID is a name, and only the
    // GUID of a matching organisation from the lookup goes into a URL path. A name that matches no
    // organisation exits 2 with no_org after the lookup, with no other request; the error lists the
    // organisations the token sees, as Auth/OrgContextTests requires.
    [Test]
    public async Task Org_option_that_is_neither_a_guid_nor_a_known_name_exits_2_after_the_lookup_alone()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("PUT", TestData.OrgPath("systems/disable"), 200, ApiJson.Bulk("systemsUpdated", 1));

        var rejected = await run.RunAsync("system", "disable", "ABCDE", "--org", "../systems");

        Assert.That(rejected.ExitCode, Is.EqualTo(2), rejected.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(rejected.Error, "code").GetString(), Is.EqualTo("no_org"));
            Assert.That(run.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(LookupOnly));
        });

        var accepted = await run.RunAsync("system", "disable", "ABCDE", "--org", TestData.OrgName);

        AssertSucceeded(accepted);
        Assert.That(run.Requests.Count(request => request.Method == "PUT"), Is.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> MalformedIdCases()
    {
        var accountPath = TestData.OrgPath($"users/{AccountId}");

        yield return Case("system show ../policies/3", "../policies/3", ["system", "show", "../policies/3"], ["system", "show", "ABCDE"], "GET", "systems/ABCDE", ApiJson.System("ABCDE"));
        yield return Case("system update ABC-DE", "ABC-DE", ["system", "update", "ABC-DE", "--description", "x"], ["system", "update", "ABCDE", "--description", "x"], "PATCH", "systems/ABCDE", ApiJson.System("ABCDE", "x"));
        yield return Case("system enable --until ../systems/ABCDE", "../systems/ABCDE", ["system", "enable", "../systems/ABCDE", "--until", Until, "--expiry-action", "Disable"], ["system", "enable", "ABCDE", "--until", Until, "--expiry-action", "Disable"], "PUT", "systems/ABCDE/enable-until", ApiJson.System("ABCDE"));
        yield return Case("pending show AB CD", "AB CD", ["pending", "show", "AB CD"], ["pending", "show", "ABCDE"], "GET", "unapproved-systems/ABCDE", ApiJson.PendingSystem("ABCDE"));
        yield return Case("pending update ../systems/ABCDE", "../systems/ABCDE", ["pending", "update", "../systems/ABCDE", "--description", "x"], ["pending", "update", "ABCDE", "--description", "x"], "PATCH", "unapproved-systems/ABCDE", ApiJson.PendingSystem("ABCDE", "x"));

        yield return Case("tag show Web", "Web", ["tag", "show", "Web"], ["tag", "show", "web"], "GET", "tags/web", ApiJson.Tag("web"));
        yield return Case("tag show web/db", "web/db", ["tag", "show", "web/db"], ["tag", "show", "web"], "GET", "tags/web", ApiJson.Tag("web"));
        yield return Case("tag show web..db", "web..db", ["tag", "show", "web..db"], ["tag", "show", "web.db"], "GET", "tags/web.db", ApiJson.Tag("web.db"));
        yield return Case("tag show web-", "web-", ["tag", "show", "web-"], ["tag", "show", "web-1"], "GET", "tags/web-1", ApiJson.Tag("web-1"));

        yield return Case("org user remove not-a-guid", "not-a-guid", ["org", "user", "remove", "not-a-guid", "--yes"], ["org", "user", "remove", AccountId.ToString(), "--yes"], "DELETE", accountPath, null);
        yield return Case("org user remove ../invites", "../invites", ["org", "user", "remove", "../invites", "--yes"], ["org", "user", "remove", AccountId.ToString(), "--yes"], "DELETE", accountPath, null);

        yield return Case("key show 12a", "12a", ["key", "show", "12a"], ["key", "show", "12"], "GET", "enrolment-keys/12", ApiJson.Key(12));
        yield return Case("key update ../policies/3", "../policies/3", ["key", "update", "../policies/3", "--description", "x"], ["key", "update", "12", "--description", "x"], "PATCH", "enrolment-keys/12", ApiJson.Key(12, "x"));
        yield return Case("key enable --until 1.5", "1.5", ["key", "enable", "1.5", "--until", Until, "--expiry-action", "Disable"], ["key", "enable", "12", "--until", Until, "--expiry-action", "Disable"], "PUT", "enrolment-keys/12/enable-until", ApiJson.Key(12));
        yield return Case("policy show 0x1F", "0x1F", ["policy", "show", "0x1F"], ["policy", "show", "3"], "GET", "policies/3", ApiJson.Policy(3));
        yield return Case("policy update ../3", "../3", ["policy", "update", "../3", "--description", "x"], ["policy", "update", "3", "--description", "x"], "PATCH", "policies/3", ApiJson.Policy(3, "x"));
        yield return Case("policy enable --until three", "three", ["policy", "enable", "three", "--until", Until, "--expiry-action", "Disable"], ["policy", "enable", "3", "--until", Until, "--expiry-action", "Disable"], "PUT", "policies/3/enable-until", ApiJson.Policy(3));
        yield return Case("dns zone show ../records/7", "../records/7", ["dns", "zone", "show", "../records/7"], ["dns", "zone", "show", "4"], "GET", "dns/zones/4", ApiJson.Zone(4, "example"));
        yield return Case("dns zone update 4a", "4a", ["dns", "zone", "update", "4a", "--notes", "x"], ["dns", "zone", "update", "4", "--notes", "x"], "PATCH", "dns/zones/4", ApiJson.Zone(4, "example"));
        yield return Case("dns zone delete 99999999999", "99999999999", ["dns", "zone", "delete", "99999999999", "--yes"], ["dns", "zone", "delete", "4", "--yes"], "DELETE", "dns/zones/4", ApiJson.Zone(4, "example"));
        yield return Case("dns record show 7a", "7a", ["dns", "record", "show", "7a"], ["dns", "record", "show", "7"], "GET", "dns/records/7", ApiJson.Record(7, "www"));
        yield return Case("dns record update ../zones/4", "../zones/4", ["dns", "record", "update", "../zones/4", "--notes", "x"], ["dns", "record", "update", "7", "--notes", "x"], "PATCH", "dns/records/7", ApiJson.Record(7, "www"));
        yield return Case("trust show five", "five", ["trust", "show", "five"], ["trust", "show", "5"], "GET", "trust-requirements/5", ApiJson.Trust(5));
        yield return Case("trust update ../5", "../5", ["trust", "update", "../5", "--description", "x"], ["trust", "update", "5", "--description", "x"], "PATCH", "trust-requirements/5", ApiJson.Trust(5, "x"));

        yield return Case("system list --key 12a", "12a", ["system", "list", "--key", "12a"], ["system", "list", "--key", "12"], "GET", "systems", ApiJson.Page());
        yield return Case("pending list --key ../3", "../3", ["pending", "list", "--key", "../3"], ["pending", "list", "--key", "3"], "GET", "unapproved-systems", ApiJson.Page());
        yield return Case("dns record list --zone 4a", "4a", ["dns", "record", "list", "--zone", "4a"], ["dns", "record", "list", "--zone", "4"], "GET", "dns/records", ApiJson.Page());
        yield return Case("dns record create --zone ../4", "../4", ["dns", "record", "create", "www", "--zone", "../4"], ["dns", "record", "create", "www", "--zone", "4"], "POST", "dns/records", ApiJson.Record(7, "www"));
        yield return Case("dns record update --set-systems AB/CD", "AB/CD", ["dns", "record", "update", "7", "--set-systems", "ABCDE,AB/CD"], ["dns", "record", "update", "7", "--set-systems", "ABCDE,FGHIJ"], "PATCH", "dns/records/7", ApiJson.Record(7, "www"));
    }

    // pathOrSuffix is a full path when it starts with "/", otherwise a suffix of the test
    // organisation's path.
    private static TestCaseData Case(string name, string malformedId, string[] rejectedArgs, string[] acceptedArgs, string method, string pathOrSuffix, string? response)
    {
        var path = pathOrSuffix.StartsWith('/') ? pathOrSuffix : TestData.OrgPath(pathOrSuffix);

        return new TestCaseData(rejectedArgs, malformedId, acceptedArgs, method, path, response).SetArgDisplayNames(name);
    }

    private static void AssertSucceeded(CliResult result) =>
        Assert.That(result.ExitCode, Is.Zero, result.ToString());

    // The error names the malformed ID, so a caller who passed 200 IDs can find the one at fault.
    private static void AssertRejected(CliResult result, string malformedId)
    {
        Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());

        var error = result.Error;
        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(DetailOf(error), Does.Contain(malformedId));
        });
    }

    private static string DetailOf(JsonElement error) =>
        error.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String
            ? detail.GetString()!
            : string.Empty;
}
