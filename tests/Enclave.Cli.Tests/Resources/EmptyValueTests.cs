using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// An empty value usually comes from an unset shell variable, so where it means nothing it exits 2
// before the token and any call, and is never read as the flag left out (spec/cli-surface.md
// "Details"). The run with a token answers every read the command could make, so a CLI that went on
// sends a request; the run without a token shows the check comes before the token.
public class EmptyValueTests
{
    private const string Blank = " ";

    private static readonly string Empty = string.Empty;

    [TestCaseSource(nameof(BlankNames))]
    public Task An_empty_or_blank_name_exits_2_before_the_token_and_any_call(string[] arguments, string key) =>
        RejectedBeforeTheTokenAsync(arguments, key);

    [TestCaseSource(nameof(TagEditsOfNothing))]
    public Task Adding_or_removing_no_tag_exits_2_before_the_token_and_any_call(string[] arguments, string key) =>
        RejectedBeforeTheTokenAsync(arguments, key);

    [TestCaseSource(nameof(CreateListsOfNothing))]
    public Task A_list_flag_of_a_create_given_no_entries_exits_2_before_the_token_and_any_call(string[] arguments, string key) =>
        RejectedBeforeTheTokenAsync(arguments, key);

    // The trust requirement list is answered, so a CLI that looked a name up shows a second request.
    [TestCase("--trust")]
    [TestCase("--trust-id")]
    public async Task Tag_set_trust_given_an_empty_value_patches_no_trust_requirements_without_a_lookup(string option)
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("tags/prod");
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(5, "uk only")));
        run.Stub("PATCH", path, json: ApiJson.Tag("prod"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", path, "tag", "set", "prod", option, Empty);

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("TrustRequirements").IgnoreCase);
            Assert.That(request.BodyIds("TrustRequirements"), Is.Empty);
        });
    }

    [TestCaseSource(nameof(BlankUpdateTexts))]
    public Task An_update_given_a_blank_value_the_api_refuses_exits_2_before_the_token_and_any_call(string[] arguments, string key) =>
        RejectedBeforeTheTokenAsync(arguments, key);

    // The API takes an empty description or notes here (portal SystemPatchModelValidator,
    // UnapprovedSystemPatchModelValidator, EnrolmentKeyPatchModelValidator: MaximumLength only).
    [TestCase("system update ABCDE --description", "systems/ABCDE", "Description")]
    [TestCase("system update XYZ12 --pending --description", "unapproved-systems/XYZ12", "Description")]
    [TestCase("key update --id 12 --notes", "enrolment-keys/12", "Notes")]
    public async Task An_update_given_an_empty_value_for_optional_text_sends_it_to_clear_the_text(string command, string suffix, string field)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        var path = TestData.OrgPath(suffix);
        run.Stub("PATCH", path, json: ItemAt(suffix));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", path, [.. command.Split(' '), Empty]);

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo(field).IgnoreCase);
            Assert.That(JsonAssert.Property(request.BodyJson, field).GetString(), Is.Empty);
        });
    }

    [TestCaseSource(nameof(EmptyEntriesAmongOthers))]
    public Task A_list_value_with_an_empty_entry_exits_2_before_the_token_and_any_call(string[] arguments, string key) =>
        RejectedBeforeTheTokenAsync(arguments, key);

    [TestCaseSource(nameof(BlankLabelledValuesAmongOthers))]
    public Task A_set_flag_given_a_blank_value_among_others_exits_2_before_the_token_and_any_call(string[] arguments, string key) =>
        RejectedBeforeTheTokenAsync(arguments, key);

    // A cleared list has no labels to keep, so there is no read. The fake answers the read, so a CLI
    // that read the item shows a second request.
    [TestCase("key update --id 12 --set-allow-ip", "enrolment-keys/12", "IpConstraints")]
    [TestCase("policy update --id 64 --set-subnet-filter", "policies/64", "GatewayAllowedIpRanges")]
    [TestCase("policy update --id 64 --set-gateway", "policies/64", "Gateways")]
    public async Task A_set_flag_given_only_white_space_clears_its_list_without_a_read(string command, string suffix, string field)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        var path = TestData.OrgPath(suffix);
        run.Stub("GET", path, json: ItemAt(suffix));
        run.Stub("PATCH", path, json: ItemAt(suffix));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", path, [.. command.Split(' '), " \t "]);

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo(field).IgnoreCase);
            Assert.That(JsonAssert.Property(request.BodyJson, field).GetArrayLength(), Is.Zero);
        });
    }

    // The API refuses a blank description or name (portal EnrolmentKeyCreateValidator,
    // EnrolmentKeyPatchModelValidator, PolicyCreateModelValidator, PolicyPatchModelValidator,
    // TrustRequirementCreateModelValidator, TrustRequirementPatchBasicValidator,
    // DnsZoneCreateValidator, DnsRecordCreateValidator: NotEmpty), so no item has one. Dropped, a
    // blank among several names would leave the command acting on the others.
    private static IEnumerable<TestCaseData> BlankNames()
    {
        yield return Case(["key", "show", Empty], "key");
        yield return Case(["key", "show", Blank], "key");
        yield return Case(["key", "update", Empty, "--notes", "rotated"], "key");
        yield return Case(["key", "enable", Empty], "key");
        yield return Case(["key", "disable", Blank], "key");
        yield return Case(["key", "delete", "build agents", Empty], "key");
        yield return Case(["key", "create", Empty], "description");
        yield return Case(["key", "create", " \t "], "description");
        yield return Case(["policy", "show", Empty], "policy");
        yield return Case(["policy", "update", Blank, "--notes", "reviewed"], "policy");
        yield return Case(["policy", "enable", Empty], "policy");
        yield return Case(["policy", "disable", Empty], "policy");
        yield return Case(["policy", "delete", Blank], "policy");
        yield return Case(["policy", "delete", "web to db", Blank], "policy");
        yield return Case(["policy", "create", Empty, "--acl", "any"], "description");
        yield return Case(["trust", "show", Empty], "trust");
        yield return Case(["trust", "update", Empty, "--notes", "reviewed"], "trust");
        yield return Case(["trust", "delete", Blank], "trust");
        yield return Case(["trust", "delete", "uk only", Empty], "trust");
        yield return Case(["trust", "create", Empty, "--authority", "portal"], "description");
        yield return Case(["dns", "show-zone", Empty], "zone");
        yield return Case(["dns", "update-zone", Blank, "--notes", "reviewed"], "zone");
        yield return Case(["dns", "delete-zone", Empty], "zone");
        yield return Case(["dns", "create-zone", Empty], "zone");
        yield return Case(["dns", "show-hostname", Empty], "hostname");
        yield return Case(["dns", "update-hostname", Blank, "--notes", "reviewed"], "hostname");
        yield return Case(["dns", "delete-hostname", Empty], "hostname");
        yield return Case(["dns", "delete-hostname", "db.internal", Blank], "hostname");
        yield return Case(["dns", "create-hostname", Empty], "hostname");
        yield return Case(["dns", "update-hostname", "--id", "7", "--name", Empty], "--name");
        yield return Case(["dns", "update-hostname", "--id", "7", "--name", Blank], "--name");
    }

    // Accepted, the update would read the item and patch its tag list back unchanged. "", white
    // space and "," each hold no tag.
    private static IEnumerable<TestCaseData> TagEditsOfNothing()
    {
        yield return Case(["system", "update", "ABCDE", "--add-tags", Empty], "--add-tags");
        yield return Case(["system", "update", "ABCDE", "--remove-tags", ","], "--remove-tags");
        yield return Case(["system", "update", "XYZ12", "--pending", "--add-tags", Blank], "--add-tags");
        yield return Case(["system", "update", "XYZ12", "--pending", "--remove-tags", Empty], "--remove-tags");
        yield return Case(["key", "update", "--id", "12", "--add-tags", Empty], "--add-tags");
        yield return Case(["key", "update", "--id", "12", "--remove-tags", " , "], "--remove-tags");
        yield return Case(["dns", "update-hostname", "--id", "7", "--add-tags", ","], "--add-tags");
        yield return Case(["dns", "update-hostname", "--id", "7", "--remove-tags", Empty], "--remove-tags");
    }

    // On a create a list flag with no entries would be the flag left out, and left out, --trust,
    // --allow-ip or --block-ip lets in more than was meant.
    private static IEnumerable<TestCaseData> CreateListsOfNothing()
    {
        yield return Case(["key", "create", "ci runners", "--tags", Empty], "--tags");
        yield return Case(["key", "create", "ci runners", "--allow-ip", Empty], "--allow-ip");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--senders", Empty], "--senders");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--receivers", ","], "--receivers");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--trust", Empty], "--trust");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--trust", " , "], "--trust");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--trust-id", Empty], "--trust-id");
        yield return Case(["policy", "create", "web to db", "--acl", Empty], "--acl");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--gateway", Empty], "--gateway");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--gateway", "GW001:10.0.0.0/16", "--subnet-filter", Empty], "--subnet-filter");
        yield return Case(["trust", "create", "staff", "--authority", "portal", "--claim", Empty], "--claim");
        yield return Case(["trust", "create", "office only", "--allow-ip", Empty], "--allow-ip");
        yield return Case(["trust", "create", "office only", "--block-ip", Empty], "--block-ip");
        yield return Case(["trust", "create", "office only", "--allow-country", Empty], "--allow-country");
        yield return Case(["trust", "create", "office only", "--block-country", Empty], "--block-country");
        yield return Case(["dns", "create-zone", "internal", "--auto-dns-tags", Empty], "--auto-dns-tags");
        yield return Case(["dns", "create-hostname", "db.internal", "--tags", Empty], "--tags");
        yield return Case(["dns", "create-hostname", "db.internal", "--systems", ","], "--systems");
        yield return Case(["system", "update", "ABCDE", "--enable-gateway-for", Empty], "--enable-gateway-for");
    }

    // The API refuses each of these when set blank (portal EnrolmentKeyPatchModelValidator,
    // PolicyPatchModelValidator, TrustRequirementPatchBasicValidator, DnsZonePatchValidator,
    // TagPatchModelValidator), except an organisation name of white space
    // (OrganisationPatchModelValidator), which --org could then not choose by name.
    private static IEnumerable<TestCaseData> BlankUpdateTexts()
    {
        yield return Case(["key", "update", "--id", "12", "--description", Empty], "--description");
        yield return Case(["key", "update", "--id", "12", "--description", Blank], "--description");
        yield return Case(["policy", "update", "--id", "42", "--description", Empty], "--description");
        yield return Case(["trust", "update", "--id", "5", "--description", Blank], "--description");
        yield return Case(["dns", "update-zone", "--id", "4", "--name", Empty], "--name");
        yield return Case(["org", "update", "--name", Empty], "--name");
        yield return Case(["org", "update", "--name", " \t "], "--name");
        yield return Case(["tag", "set", "prod", "--name", Empty], "--name");
        yield return Case(["tag", "set", "prod", "--colour", Empty], "--colour");
        yield return Case(["tag", "set", "prod", "--colour", Blank], "--colour");
    }

    // Dropped, an empty entry would shorten the list; it usually comes from an unset shell variable,
    // as in `--trust "uk only,$EXTRA"`. Each kind of comma list is here.
    private static IEnumerable<TestCaseData> EmptyEntriesAmongOthers()
    {
        yield return Case(["system", "list", "--tag", "web,"], "--tag");
        yield return Case(["key", "list", "--tag", " ,web"], "--tag");
        yield return Case(["policy", "list", "--tag", "web, ,db"], "--tag");
        yield return Case(["log", "--level", "warning,"], "--level");
        yield return Case(["system", "update", "ABCDE", "--set-tags", "web,,db"], "--set-tags");
        yield return Case(["system", "update", "ABCDE", "--set-tags", ","], "--set-tags");
        yield return Case(["system", "update", "ABCDE", "--add-tags", "web,"], "--add-tags");
        yield return Case(["key", "update", "--id", "12", "--remove-tags", "web,,"], "--remove-tags");
        yield return Case(["key", "create", "ci runners", "--tags", "ci,"], "--tags");
        yield return Case(["policy", "update", "--id", "42", "--set-senders", "web,"], "--set-senders");
        yield return Case(["policy", "update", "--id", "42", "--set-trust", "uk only,"], "--set-trust");
        yield return Case(["policy", "update", "--id", "42", "--set-trust-id", "5, "], "--set-trust-id");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--trust", "uk only, "], "--trust");
        yield return Case(["tag", "set", "prod", "--trust", ",uk only"], "--trust");
        yield return Case(["tag", "set", "prod", "--trust-id", "5,"], "--trust-id");
        yield return Case(["dns", "update-zone", "--id", "4", "--set-auto-dns-tags", "web,"], "--set-auto-dns-tags");
        yield return Case(["dns", "update-hostname", "--id", "7", "--set-systems", "ABCDE,"], "--set-systems");
        yield return Case(["dns", "create-hostname", "db.internal", "--systems", "ABCDE,,FGHIJ"], "--systems");
        yield return Case(["key", "delete", "--id", "1,,2"], "--id");
        yield return Case(["policy", "disable", "--id", "42,"], "--id");
        yield return Case(["trust", "delete", "--id", "5", "--id", Empty], "--id");
        yield return Case(["policy", "create", "web to db", "--acl", "any", "--gateway", "GW001:10.0.0.0/16,"], "--gateway");
    }

    // Dropped, a blank value would shorten the list, and sent, the API refuses it (portal
    // EnrolmentKeyIpConstraintInputValidator, the rule on Range). "=office" is blank too.
    private static IEnumerable<TestCaseData> BlankLabelledValuesAmongOthers()
    {
        yield return Case(["key", "update", "--id", "12", "--set-allow-ip", "10.0.0.0/8", "--set-allow-ip", Empty], "--set-allow-ip");
        yield return Case(["key", "update", "--id", "12", "--set-allow-ip", "=office"], "--set-allow-ip");
        yield return Case(["policy", "update", "--id", "42", "--set-acl", "tcp:443", "--set-acl", Empty], "--set-acl");
        yield return Case(["policy", "update", "--id", "42", "--set-subnet-filter", "10.0.0.0/8", "--set-subnet-filter", Empty], "--set-subnet-filter");
        yield return Case(["trust", "update", "--id", "5", "--set-allow-ip", "10.0.0.0/8", "--set-allow-ip", Blank], "--set-allow-ip");
        yield return Case(["trust", "update", "--id", "5", "--set-block-country", Empty, "--set-block-country", "RU"], "--set-block-country");
    }

    // The item each path answers with, as the API models it, so the CLI can read the answer.
    private static string ItemAt(string suffix) => suffix switch
    {
        _ when suffix.StartsWith("systems/", StringComparison.Ordinal) => ApiJson.System("ABCDE"),
        _ when suffix.StartsWith("unapproved-systems/", StringComparison.Ordinal) => ApiJson.PendingSystem("XYZ12"),
        _ when suffix.StartsWith("policies/", StringComparison.Ordinal) => ApiJson.Policy(64, "staff to office LAN"),
        _ => ApiJson.Key(12, "build agents"),
    };

    private static async Task RejectedBeforeTheTokenAsync(string[] arguments, string key)
    {
        using var withToken = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");
        StubEveryRead(withToken);

        var first = await withToken.RunAsync(arguments);
        var second = await withoutToken.RunAsync(arguments);

        CliAssert.Rejected(withToken, first);
        CliAssert.Rejected(withoutToken, second);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(first.Error.GetProperty("errors")), Is.EqualTo(key), first.ToString());
            Assert.That(second.Error.GetRawText(), Is.EqualTo(first.Error.GetRawText()));
        });
    }

    // The lists hold the items the cases name, so a lookup of the names that are not blank succeeds,
    // and the reads give the items the update cases act on.
    private static void StubEveryRead(CliRun run)
    {
        run.Stub("GET", TestData.OrgPath("enrolment-keys"), json: ApiJson.Page(ApiJson.Key(12, "build agents")));
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(5, "uk only")));
        run.Stub("GET", TestData.OrgPath("dns/zones"), json: ApiJson.Page(ApiJson.Zone(4, "internal")));
        run.Stub("GET", TestData.OrgPath("dns/records"), json: ApiJson.Page(ApiJson.HostnameRecord(7, "db", 4, "internal")));
        run.Stub("GET", TestData.OrgPath("systems/ABCDE"), json: ApiJson.WithUsedTags(ApiJson.System("ABCDE"), "web"));
        run.Stub("GET", TestData.OrgPath("unapproved-systems/XYZ12"), json: ApiJson.WithUsedTags(ApiJson.PendingSystem("XYZ12"), "web"));
        run.Stub("GET", TestData.OrgPath("enrolment-keys/12"), json: ApiJson.WithUsedTags(ApiJson.Key(12, "build agents"), "web"));
        run.Stub("GET", TestData.OrgPath("dns/records/7"), json: ApiJson.HostnameRecord(7, "db", 4, "internal", "web"));
        run.Stub("GET", TestData.OrgPath("policies/42"), json: ApiJson.Policy(42, "web to db"));
        run.Stub("GET", TestData.OrgPath("trust-requirements/5"), json: ApiJson.Trust(5, "uk only"));
        run.Stub("GET", TestData.OrgPath("tags/prod"), json: ApiJson.Tag("prod"));
        run.Stub("GET", TestData.OrgPath("logs"), json: ApiJson.Page());
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("GET", TestData.OrgPath("tags"), json: ApiJson.Page(ApiJson.Tag("prod")));
    }

    // A blank argument is shown quoted, so each case's name differs from the others.
    private static TestCaseData Case(string[] arguments, string key) =>
        new TestCaseData(arguments, key).SetArgDisplayNames(string.Join(" ", arguments.Select(argument => argument.Trim().Length == 0 ? $"\"{argument}\"" : argument)));
}
