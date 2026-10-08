using System.Globalization;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// Every ID is checked against its format before any call, and one bad ID stops the command with
/// exit 2 (proposed-cli-surface.md "ID checks"). A parse error also exits 2, so each test runs the
/// command again with valid IDs and checks it then sends its request.
/// </summary>
public class IdCheckTests
{
    private const string Until = "2030-01-01T00:00:00Z";

    private const string AllZeroGuid = "00000000-0000-0000-0000-000000000000";

    // An --until that has passed exits 2 ("Details"), so the commands run on a clock fixed before
    // Until, which keeps each case's answer the same whatever the date.
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

    private static readonly Guid AccountId = new("5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25");

    private static readonly Guid CustomerOrgId = new("6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b");

    // proposed-cli-surface.md "ID checks": every ID is checked before any call, so a malformed one
    // exits 2 naming it and sends nothing. Put into a URL path unescaped, this ID would send DELETE
    // org/<id>/systems/ABCDE and revoke system ABCDE, since .NET resolves ".." when it combines the
    // path with the base address; Enclave.Sdk.Api 1.1.0 escapes it (ClientBase.PathSegment), and the
    // check does not depend on that. Every route the ID could reach is answered, so any request it
    // caused would be recorded.
    [Test]
    public async Task System_decline_exits_2_for_a_path_traversal_id_and_sends_nothing()
    {
        using var run = CliRun.Start();
        var decline = TestData.OrgPath("unapproved-systems");
        run.StubBulk("DELETE", decline, "systemsDeclined", 1);
        run.Stub("DELETE", TestData.OrgPath("systems"), 200, ApiJson.Bulk("systemsRevoked", 1));
        run.Stub("DELETE", TestData.OrgPath("systems/ABCDE"), 200, ApiJson.System("ABCDE"));
        run.Stub("DELETE", TestData.OrgPath("unapproved-systems/ABCDE"), 200, ApiJson.PendingSystem("ABCDE"));

        var rejected = await run.RunAsync("system", "decline", "../systems/ABCDE");

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync("system", "decline", "ABCDE");

        CliAssert.Bulk(accepted, requested: 1, affected: 1);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(decline));
            Assert.That(string.Join(",", request.BodyIds("systemIds")), Is.EqualTo("ABCDE"));
        });
    }

    // One malformed ID among several stops the whole command: sending the valid ones would leave the
    // caller to work out which items changed.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_2_and_sends_nothing_when_one_id_given_is_malformed(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 2);
        var ids = command.Ids(2);

        var rejected = await run.RunAsync(command.Args(ids[0], command.MalformedId, ids[1]));

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(command.Args(ids));

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(command.Path));
    }

    // A list on stdin is checked as arguments are: its IDs go into the same calls. The bad item sits
    // between two good ones, so a CLI that sent the good items would show a request.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_2_and_sends_nothing_when_one_id_in_a_list_on_stdin_is_malformed(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 2);
        var ids = command.Ids(2);
        run.StdinText = ListWithMalformedItem(command, ids);

        var rejected = await run.RunAsync(command.StdinArgs());

        CliAssert.Rejected(run, rejected);

        run.StdinText = command.List(ids);
        var accepted = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds(command.BodyField)), Is.EqualTo(string.Join(",", ids)));
    }

    // The formats are those of the "ID checks" table, wherever the ID is given: as the command's own
    // item, after an option naming another item, or in a list of systems a hostname points at.
    [TestCaseSource(nameof(MalformedIdCases))]
    public async Task Command_exits_2_and_sends_nothing_for_a_malformed_id(
        string[] rejectedArgs,
        string[] acceptedArgs,
        string method,
        string path,
        string? otherPath,
        string? response,
        string? readPath,
        string? readResponse)
    {
        using var run = CliRun.Start();
        run.Time = Clock;
        run.Stub(method, path, 200, response);

        if (otherPath is not null)
        {
            run.Stub(method, otherPath, 200, response);
        }

        if (readPath is not null)
        {
            run.Stub("GET", readPath, 200, readResponse);
        }

        var rejected = await run.RunAsync(rejectedArgs);

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(acceptedArgs);

        CliAssert.Succeeded(accepted);
        var requests = run.Requests;
        var calls = string.Join(", ", run.Calls());
        Assert.Multiple(() =>
        {
            Assert.That(requests.Count(request => request.Method == method && (request.Path == path || request.Path == otherPath)), Is.EqualTo(1), calls);
            Assert.That(requests.Count(request => request.Method != "GET"), Is.EqualTo(method == "GET" ? 0 : 1), calls);
        });
    }

    // org use saves the organisation as the default for later commands, so an ID that is not a GUID
    // is refused before anything is saved.
    [Test]
    public async Task Org_use_exits_2_for_an_id_that_is_not_a_guid_and_saves_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));

        var rejected = await run.RunAsync("org", "use", "--id", "not-a-guid");

        CliAssert.Rejected(run, rejected);
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.False);

        var accepted = await run.RunAsync("org", "use", "--id", TestData.OtherOrgId.ToString());

        CliAssert.Succeeded(accepted);
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.True);
    }

    // partner use saves the partner as the default for later commands, and partners are given by
    // GUID (proposed-cli-surface.md "ID checks"), so an ID that is not a GUID is refused before
    // anything is saved. partner use makes no call either way ("Login, logout and status").
    [Test]
    public async Task Partner_use_exits_2_for_an_id_that_is_not_a_guid_and_saves_nothing()
    {
        using var run = CliRun.Start();

        var rejected = await run.RunAsync("partner", "use", "--id", "12");

        CliAssert.Rejected(run, rejected);
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.False);

        var accepted = await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString());

        CliAssert.Succeeded(accepted);
        Assert.Multiple(() =>
        {
            Assert.That(run.Files.Exists(run.CliConfigPath), Is.True);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // The all-zero GUID names no organisation or partner, so org use and partner use refuse it
    // before saving it as the default every later command would use. The corrected run proves the
    // option and the file.
    [TestCase("org")]
    [TestCase("partner")]
    public async Task Use_exits_2_for_the_all_zero_guid_and_saves_nothing(string noun)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        var valid = noun == "org" ? TestData.OtherOrgId : TestData.PartnerId;

        var rejected = await run.RunAsync(noun, "use", "--id", AllZeroGuid);

        CliAssert.Rejected(run, rejected);
        Assert.That(run.Files.Exists(run.CliConfigPath), Is.False);

        var accepted = await run.RunAsync(noun, "use", "--id", valid.ToString());

        CliAssert.Succeeded(accepted);
        var saved = JsonAssert.Property(JsonRead.Parse(run.Files.ReadText(run.CliConfigPath) ?? throw new AssertionException($"{noun} use saved no cli.json.")), noun);
        Assert.That(Guid.Parse(JsonAssert.Property(saved, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(valid));
    }

    // The organisation and partner IDs that come from the environment or from cli.json are checked
    // as the options' are ("ID checks"), so the all-zero GUID there exits 2 before any call. It
    // comes after the token in the order of checks ("Errors and exit codes"), and a saved default
    // names the file it came from, so the caller knows what to fix. The corrected run, with a real
    // ID in the same place, proves the source is read.
    [TestCase("ENCLAVE_ORG_ID")]
    [TestCase("ENCLAVE_PARTNER_ID")]
    [TestCase("cli.json org")]
    [TestCase("cli.json partner")]
    public async Task The_all_zero_guid_in_the_environment_or_a_saved_default_exits_2_before_any_call(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var run = CliRun.Start();
        PartnerApiFake.StubPartnerApi(run);
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));
        run.Environment.Remove("ENCLAVE_ORG_ID");
        var partner = source is "ENCLAVE_PARTNER_ID" or "cli.json partner";
        string[] command = partner ? ["partner", "customer", "list"] : ["system", "list"];
        var call = partner ? $"GET {TestData.PartnerPath("customers")}" : $"GET {TestData.OrgPath("systems")}";
        var valid = partner ? TestData.PartnerId : TestData.OrgId;

        Choose(run, source, AllZeroGuid);
        var rejected = await run.RunAsync(command);

        CliAssert.Rejected(run, rejected);

        if (source.StartsWith("cli.json", StringComparison.Ordinal))
        {
            Assert.That(rejected.Error.GetProperty("detail").GetString(), Does.Contain(run.CliConfigPath));
        }

        Choose(run, source, valid.ToString());
        var accepted = await run.RunAsync(command);

        CliAssert.Succeeded(accepted);
        Assert.That(run.Calls(), Is.EqualTo(new[] { call }));
    }

    // Partners and customers are given by GUID (proposed-cli-surface.md "ID checks"), and arguments
    // are checked before anything else ("Errors and exit codes"), so a bad ID exits 2 and sends
    // nothing. The second run, with GUIDs, passes the check and makes the command's one call, on the
    // partner API route of the partner and customer it was given.
    [TestCaseSource(nameof(PartnerIdCases))]
    public async Task Partner_customer_command_exits_2_for_a_partner_or_customer_id_that_is_not_a_guid(string[] rejectedArgs, string[] acceptedArgs, string call)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubPartnerApi(run);

        var rejected = await run.RunAsync(rejectedArgs);

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(acceptedArgs);

        PartnerApiFake.AssertSentToThePartnerApi(run, accepted);
        Assert.That(run.Calls(), Is.EqualTo(new[] { call }), accepted.ToString());
    }

    // "Every ID is checked before any call" includes the lookup --org <name> makes, so a malformed
    // ID costs no call at all.
    [Test]
    public async Task Id_check_runs_before_the_organisation_lookup()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/disable");
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.StubBulk("PUT", path, "systemsUpdated", 1);

        var rejected = await run.RunAsync("system", "disable", "ABCDE", "AB/CD", "--org", TestData.OrgName);

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync("system", "disable", "ABCDE", "--org", TestData.OrgName);

        CliAssert.Bulk(accepted, requested: 1, affected: 1);
        Assert.That(string.Join(",", run.Calls()), Is.EqualTo($"GET /account/orgs,PUT {path}"));
    }

    // The same holds for the lookup a name needs: the hostname's systems are checked before the
    // hostname is looked up.
    [Test]
    public async Task Id_check_runs_before_a_name_lookup()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("dns/records/7");
        run.Stub("GET", TestData.OrgPath("dns/zones"), 200, ApiJson.Page(ApiJson.Zone(1, "enclave")));
        run.Stub("GET", TestData.OrgPath("dns/records"), 200, ApiJson.Page(ApiJson.Record(7, "db")));
        run.Stub("PATCH", path, 200, ApiJson.Record(7, "db"));

        var rejected = await run.RunAsync("dns", "update-hostname", "db.enclave", "--set-systems", "ABCDE,AB/CD");

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync("dns", "update-hostname", "db.enclave", "--set-systems", "ABCDE,FGHIJ");

        CliAssert.Succeeded(accepted);
        Assert.That(run.RequestsTo("PATCH", path), Has.Count.EqualTo(1));
    }

    // A dry run prints the request the command would send, so it applies the same checks.
    [Test]
    public async Task Id_check_applies_under_dry_run()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/disable");
        run.StubBulk("PUT", path, "systemsUpdated", 1);

        var rejected = await run.RunAsync("system", "disable", "AB/CD", "--dry-run");

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync("system", "disable", "ABCDE", "--dry-run");

        CliAssert.Succeeded(accepted);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(accepted.StdoutJson, "dryRun").GetBoolean(), Is.True);
            Assert.That(run.RequestsTo("PUT", path), Is.Empty);
        });
    }

    private static IEnumerable<TestCaseData> MalformedIdCases()
    {
        var otherOrgSystems = TestData.OtherOrgPath("systems");

        // Systems: letters and digits.
        yield return Case("system show ../policies/3", ["system", "show", "../policies/3"], ["system", "show", "ABCDE"], "GET", "systems/ABCDE", ApiJson.System("ABCDE"));
        yield return Case("system update ABC-DE", ["system", "update", "ABC-DE", "--description", "web server"], ["system", "update", "ABCDE", "--description", "web server"], "PATCH", "systems/ABCDE", ApiJson.System("ABCDE", "web server"));
        yield return Case("system update --pending ../systems/ABCDE", ["system", "update", "../systems/ABCDE", "--pending", "--description", "kiosk"], ["system", "update", "ABCDE", "--pending", "--description", "kiosk"], "PATCH", "unapproved-systems/ABCDE", ApiJson.PendingSystem("ABCDE", "kiosk"));
        yield return Case("system enable --for ../systems/ABCDE", ["system", "enable", "../systems/ABCDE", "--for", "8h"], ["system", "enable", "ABCDE", "--for", "8h"], "PUT", "systems/ABCDE/enable-until", ApiJson.System("ABCDE"));
        yield return Case("dns create-hostname --systems ../systems/ABCDE", ["dns", "create-hostname", "db.internal", "--systems", "FGHIJ,../systems/ABCDE"], ["dns", "create-hostname", "db.internal", "--systems", "FGHIJ,ABCDE"], "POST", "dns/records", ApiJson.Record(7, "db"), readPathSuffix: "dns/zones", readResponse: ApiJson.Page(ApiJson.Zone(4, "internal")));
        yield return Case("dns update-hostname --set-systems AB/CD", ["dns", "update-hostname", "--id", "7", "--set-systems", "ABCDE,AB/CD"], ["dns", "update-hostname", "--id", "7", "--set-systems", "ABCDE,FGHIJ"], "PATCH", "dns/records/7", ApiJson.Record(7, "db"));

        // Tags: the API's tag rule (portal TagValidationExtensions.cs:13).
        yield return Case("tag show Web", ["tag", "show", "Web"], ["tag", "show", "web"], "GET", "tags/web", ApiJson.Tag("web"));
        yield return Case("tag show web/db", ["tag", "show", "web/db"], ["tag", "show", "web"], "GET", "tags/web", ApiJson.Tag("web"));
        yield return Case("tag show web..db", ["tag", "show", "web..db"], ["tag", "show", "web.db"], "GET", "tags/web.db", ApiJson.Tag("web.db"));
        yield return Case("tag show web-", ["tag", "show", "web-"], ["tag", "show", "web-1"], "GET", "tags/web-1", ApiJson.Tag("web-1"));
        yield return Case("tag set ../systems/ABCDE", ["tag", "set", "../systems/ABCDE", "--notes", "front end"], ["tag", "set", "web", "--notes", "front end"], "PATCH", "tags/web", ApiJson.Tag("web"), readPathSuffix: "tags/web", readResponse: ApiJson.Tag("web"));

        // Keys, policies, zones, hostnames and trust requirements: integers after --id or an option
        // naming the item. Integer IDs are 32-bit (proposed-cli-surface.md "Details"; portal
        // Enclave.Configuration.Data/Identifiers, IdBackingType.Int), so 99999999999 is refused.
        yield return Case("key show --id 12a", ["key", "show", "--id", "12a"], ["key", "show", "--id", "12"], "GET", "enrolment-keys/12", ApiJson.Key(12));
        yield return Case("key update --id ../policies/3", ["key", "update", "--id", "../policies/3", "--description", "build agents"], ["key", "update", "--id", "12", "--description", "build agents"], "PATCH", "enrolment-keys/12", ApiJson.Key(12, "build agents"));
        yield return Case("key enable --for --id 1.5", ["key", "enable", "--id", "1.5", "--for", "8h"], ["key", "enable", "--id", "12", "--for", "8h"], "PUT", "enrolment-keys/12/enable-until", ApiJson.Key(12));
        yield return Case("system list --key-id 12a", ["system", "list", "--key-id", "12a"], ["system", "list", "--key-id", "12"], "GET", "systems", ApiJson.Page(ApiJson.System("ABCDE")));
        yield return Case("system list --pending --key-id ../3", ["system", "list", "--pending", "--key-id", "../3"], ["system", "list", "--pending", "--key-id", "3"], "GET", "unapproved-systems", ApiJson.Page(ApiJson.PendingSystem("XYZ12")));
        yield return Case("policy show --id 0x1F", ["policy", "show", "--id", "0x1F"], ["policy", "show", "--id", "31"], "GET", "policies/31", ApiJson.Policy(31));
        yield return Case("policy update --id ../3", ["policy", "update", "--id", "../3", "--description", "web to db"], ["policy", "update", "--id", "3", "--description", "web to db"], "PATCH", "policies/3", ApiJson.Policy(3, "web to db"));
        yield return Case("policy enable --until --id three", ["policy", "enable", "--id", "three", "--until", Until], ["policy", "enable", "--id", "3", "--until", Until], "PUT", "policies/3/enable-until", ApiJson.Policy(3));
        yield return Case("policy create --trust-id 3a", ["policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432", "--trust-id", "3a"], ["policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432", "--trust-id", "3"], "POST", "policies", ApiJson.Policy(42, "web to db"));
        yield return Case("tag set --trust-id ../5", ["tag", "set", "prod", "--trust-id", "../5"], ["tag", "set", "prod", "--trust-id", "5"], "PATCH", "tags/prod", ApiJson.Tag("prod"), readPathSuffix: "tags/prod", readResponse: ApiJson.Tag("prod"));
        yield return Case("dns show-zone --id ../records/7", ["dns", "show-zone", "--id", "../records/7"], ["dns", "show-zone", "--id", "4"], "GET", "dns/zones/4", ApiJson.Zone(4, "internal"));
        yield return Case("dns update-zone --id 4a", ["dns", "update-zone", "--id", "4a", "--notes", "office"], ["dns", "update-zone", "--id", "4", "--notes", "office"], "PATCH", "dns/zones/4", ApiJson.Zone(4, "internal"));
        yield return Case("dns delete-zone --id 99999999999", ["dns", "delete-zone", "--id", "99999999999"], ["dns", "delete-zone", "--id", "4"], "DELETE", "dns/zones/4", ApiJson.Zone(4, "internal"));
        yield return Case("dns list-hostnames --zone-id 4a", ["dns", "list-hostnames", "--zone-id", "4a"], ["dns", "list-hostnames", "--zone-id", "4"], "GET", "dns/records", ApiJson.Page(ApiJson.Record(7, "db")));
        yield return Case("dns show-hostname --id 7a", ["dns", "show-hostname", "--id", "7a"], ["dns", "show-hostname", "--id", "7"], "GET", "dns/records/7", ApiJson.Record(7, "db"));
        yield return Case("dns update-hostname --id ../zones/4", ["dns", "update-hostname", "--id", "../zones/4", "--notes", "office"], ["dns", "update-hostname", "--id", "7", "--notes", "office"], "PATCH", "dns/records/7", ApiJson.Record(7, "db"));
        yield return Case("trust show --id five", ["trust", "show", "--id", "five"], ["trust", "show", "--id", "5"], "GET", "trust-requirements/5", ApiJson.Trust(5));
        yield return Case("trust update --id ../5", ["trust", "update", "--id", "../5", "--description", "uk only"], ["trust", "update", "--id", "5", "--description", "uk only"], "PATCH", "trust-requirements/5", ApiJson.Trust(5, "uk only"));

        // Organisations and accounts: GUIDs. RemoveUserAsync takes the account ID as a string
        // (Enclave.Sdk.Api 1.1.0, OrganisationScopedClient.RemoveUserAsync), so the path carries
        // whichever GUID form the CLI passes.
        yield return Case("system list --org-id not-a-guid", ["system", "list", "--org-id", "not-a-guid"], ["system", "list", "--org-id", TestData.OtherOrgId.ToString()], "GET", otherOrgSystems, ApiJson.Page(ApiJson.System("ABCDE")));
        yield return Case("system list --org-id ../account/orgs", ["system", "list", "--org-id", "../account/orgs"], ["system", "list", "--org-id", TestData.OtherOrgId.ToString()], "GET", otherOrgSystems, ApiJson.Page(ApiJson.System("ABCDE")));
        yield return Case("org remove-user --id not-a-guid", ["org", "remove-user", "--id", "not-a-guid"], ["org", "remove-user", "--id", AccountId.ToString()], "DELETE", $"users/{AccountId:N}", ApiJson.User(AccountId, "sam@example.com"), otherPathSuffix: $"users/{AccountId:D}");
        yield return Case("org remove-user --id ../invites", ["org", "remove-user", "--id", "../invites"], ["org", "remove-user", "--id", AccountId.ToString()], "DELETE", $"users/{AccountId:N}", ApiJson.User(AccountId, "sam@example.com"), otherPathSuffix: $"users/{AccountId:D}");

        // The all-zero GUID has a GUID's form and names nothing: no organisation or account has it.
        // It is refused in either form a GUID is read in, with hyphens and without.
        yield return Case("system list --org-id all zeros", ["system", "list", "--org-id", AllZeroGuid], ["system", "list", "--org-id", TestData.OtherOrgId.ToString()], "GET", otherOrgSystems, ApiJson.Page(ApiJson.System("ABCDE")));
        yield return Case("system list --org-id 32 zeros", ["system", "list", "--org-id", Guid.Empty.ToString("N")], ["system", "list", "--org-id", TestData.OtherOrgId.ToString()], "GET", otherOrgSystems, ApiJson.Page(ApiJson.System("ABCDE")));
        yield return Case("org remove-user --id all zeros", ["org", "remove-user", "--id", AllZeroGuid], ["org", "remove-user", "--id", AccountId.ToString()], "DELETE", $"users/{AccountId:N}", ApiJson.User(AccountId, "sam@example.com"), otherPathSuffix: $"users/{AccountId:D}");
    }

    // A path starting with "/" is a full path; any other is below the test organisation's path.
    private static TestCaseData Case(
        string name,
        string[] rejectedArgs,
        string[] acceptedArgs,
        string method,
        string pathOrSuffix,
        string? response,
        string? readPathSuffix = null,
        string? readResponse = null,
        string? otherPathSuffix = null)
    {
        var path = pathOrSuffix.StartsWith('/') ? pathOrSuffix : TestData.OrgPath(pathOrSuffix);
        var otherPath = otherPathSuffix is null ? null : TestData.OrgPath(otherPathSuffix);
        var readPath = readPathSuffix is null ? null : TestData.OrgPath(readPathSuffix);

        return new TestCaseData(rejectedArgs, acceptedArgs, method, path, otherPath, response, readPath, readResponse).SetArgDisplayNames(name);
    }

    private static IEnumerable<TestCaseData> PartnerIdCases()
    {
        var partner = TestData.PartnerId.ToString();
        var customer = CustomerOrgId.ToString();

        yield return PartnerCase("--partner-id not-a-guid", ["partner", "customer", "list", "--partner-id", "not-a-guid"], ["partner", "customer", "list", "--partner-id", partner], $"GET {TestData.PartnerPath("customers")}");
        yield return PartnerCase("--partner-id ../customers", ["partner", "customer", "list", "--partner-id", "../customers"], ["partner", "customer", "list", "--partner-id", partner], $"GET {TestData.PartnerPath("customers")}");
        yield return PartnerCase("partner customer show --org-id 12", ["partner", "customer", "show", "--org-id", "12", "--partner-id", partner], ["partner", "customer", "show", "--org-id", customer, "--partner-id", partner], $"GET {TestData.CustomerPath(customer)}");
        yield return PartnerCase("partner customer convert --org-id ../admins", ["partner", "customer", "convert", "--org-id", "../admins", "--billing-months", "12", "--partner-id", partner], ["partner", "customer", "convert", "--org-id", customer, "--billing-months", "12", "--partner-id", partner], $"PUT {TestData.CustomerPath(customer, "convert")}");

        // The all-zero GUID names no partner, customer or account.
        yield return PartnerCase("--partner-id all zeros", ["partner", "customer", "list", "--partner-id", AllZeroGuid], ["partner", "customer", "list", "--partner-id", partner], $"GET {TestData.PartnerPath("customers")}");
        yield return PartnerCase("partner customer show --org-id all zeros", ["partner", "customer", "show", "--org-id", AllZeroGuid, "--partner-id", partner], ["partner", "customer", "show", "--org-id", customer, "--partner-id", partner], $"GET {TestData.CustomerPath(customer)}");
        yield return PartnerCase("partner customer add-admin --user-id all zeros", ["partner", "customer", "add-admin", "--org-id", customer, "--user-id", AllZeroGuid, "--partner-id", partner], ["partner", "customer", "add-admin", "--org-id", customer, "--user-id", TestData.PartnerStaffAccountId, "--partner-id", partner], $"PUT {TestData.CustomerAdminPath(customer, TestData.PartnerStaffAccountId)}");
    }

    private static TestCaseData PartnerCase(string name, string[] rejectedArgs, string[] acceptedArgs, string call) =>
        new TestCaseData(rejectedArgs, acceptedArgs, call).SetArgDisplayNames(name);

    // Puts the ID where the case names: in its environment variable, or as the default in cli.json
    // in the form org use and partner use save ("Login, logout and status").
    private static void Choose(CliRun run, string source, string id)
    {
        switch (source)
        {
            case "ENCLAVE_ORG_ID":
            case "ENCLAVE_PARTNER_ID":
                run.Environment[source] = id;
                break;
            case "cli.json org":
                run.Files.WriteText(run.CliConfigPath, $$"""{ "org": { "id": "{{id}}", "name": "{{TestData.OrgName}}" } }""", privateToUser: false);
                break;
            default:
                run.Files.WriteText(run.CliConfigPath, $$"""{ "partner": { "id": "{{id}}" } }""", privateToUser: false);
                break;
        }
    }

    // A copy of the list's first item with its ID replaced by the malformed one, placed between the
    // two good items. The malformed ID is written as a JSON number where it reads as one, the type
    // an integer ID has in a list, and as text otherwise.
    private static string ListWithMalformedItem(BulkCommand command, string[] ids)
    {
        var items = JsonNode.Parse(command.List(ids))!["items"]!.AsArray();
        var first = items[0]!.ToJsonString();
        var second = items[1]!.ToJsonString();
        var malformed = JsonNode.Parse(first)!;
        malformed[CliList.IdField(command.Kind)] = IdValue(command.MalformedId);

        return CliList.Of(command.Kind, first, malformed.ToJsonString(), second);
    }

    private static JsonValue? IdValue(string id)
    {
        if (long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            return JsonValue.Create(whole);
        }

        if (decimal.TryParse(id, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fraction))
        {
            return JsonValue.Create(fraction);
        }

        return JsonValue.Create(id);
    }
}
