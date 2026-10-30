using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static Enclave.Cli.Tests.Support.FieldText;

namespace Enclave.Cli.Tests.Resources;

public partial class PolicyCommandTests
{
    // An --until that has passed exits 2, so tests that give a fixed --until run on this clock, the
    // day before those times, and give the same answer whatever the date.
    private static readonly DateTimeOffset Now = new(2030, 1, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly string PoliciesPath = TestData.OrgPath("policies");

    private static readonly string TrustsPath = TestData.OrgPath("trust-requirements");

    private static readonly string OtherOrgPoliciesPath = TestData.OtherOrgPath("policies");

    private static readonly string[] TagsWebAndDb = ["tags:web,db"];

    private static readonly string[] StateDisabled = ["state:disabled"];

    private static readonly string[] StateEnabled = ["state:enabled"];

    [Test]
    public async Task Policy_list_reads_every_page_and_prints_a_policy_list_without_disabled_policies_or_a_search()
    {
        using var run = CliRun.Start();
        run.StubPages(PoliciesPath, 2, ApiJson.Policy(1), ApiJson.Policy(2), ApiJson.Policy(3));

        var result = await run.RunAsync("policy", "list");

        var items = CliAssert.List(result, "policy");
        var first = run.RequestsTo("GET", PoliciesPath)[0];
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("1,2,3"));
            Assert.That(run.PagesRequested(PoliciesPath), Is.EqualTo("0,1"));
            Assert.That(first.QueryValue("search"), Is.Null);
            Assert.That(first.QueryValue("include_disabled"), Is.Null.Or.EqualTo("false").IgnoreCase);
        });
    }

    // Example 50. Without include_disabled the API adds an enabled-only filter to the search (portal
    // PolicyRepository.BuildFilterAsync), so --state disabled must also ask for disabled policies.
    [Test]
    public async Task Policy_list_with_state_disabled_searches_for_disabled_policies_and_asks_the_api_to_include_them()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(DisabledPolicy(23, "contractors")));

        var result = await run.RunAsync("policy", "list", "--state", "disabled");

        var items = CliAssert.List(result, "policy");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(SearchTerms.Of(request.QueryValue("search")), Is.EqualTo(StateDisabled));
            Assert.That(request.QueryValue("include_disabled"), Is.EqualTo("true").IgnoreCase);
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("23"));
        });
    }

    [Test]
    public async Task Policy_list_with_state_enabled_searches_for_enabled_policies()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--state", "enabled");

        CliAssert.List(result, "policy");
        Assert.That(SearchTerms.Of(run.SingleRequest().QueryValue("search")), Is.EqualTo(StateEnabled));
    }

    // The tags key matches sender or receiver tags, and several values must all match, as --tag
    // requires (portal PolicySearchKeyService.BuildFilterAsync).
    [Test]
    public async Task Policy_list_with_tags_searches_the_tags_key_for_every_tag_given()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--tag", "web,db");

        CliAssert.List(result, "policy");
        Assert.That(SearchTerms.Of(run.SingleRequest().QueryValue("search")), Is.EqualTo(TagsWebAndDb));
    }

    [Test]
    public async Task Policy_list_adds_the_tag_and_state_filters_to_the_filter_text()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--filter", "database", "--tag", "web", "--state", "enabled");

        CliAssert.List(result, "policy");
        var terms = SearchTerms.Of(run.SingleRequest().QueryValue("search"));
        Assert.Multiple(() =>
        {
            Assert.That(terms, Has.Length.EqualTo(3));
            Assert.That(terms, Has.Member("database"));
            Assert.That(terms, Has.Member("tags:web"));
            Assert.That(terms, Has.Member("state:enabled"));
        });
    }

    // Enclave.Sdk.Api writes the flag with bool.ToString (PoliciesClient.BuildQueryString), so
    // the value is compared ignoring case.
    [Test]
    public async Task Policy_list_with_include_disabled_asks_the_api_for_disabled_policies()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1), DisabledPolicy(23, "contractors")));

        var result = await run.RunAsync("policy", "list", "--include-disabled");

        var items = CliAssert.List(result, "policy");
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().QueryValue("include_disabled"), Is.EqualTo("true").IgnoreCase);
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("1,23"));
        });
    }

    [TestCase("--sort", "newest", "description")]
    [TestCase("--state", "paused", "enabled")]
    public async Task Policy_list_rejects_an_option_value_outside_the_allowed_values(string option, string value, string allowed)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        await CliAssert.RejectedThenAcceptedAsync(run, ["policy", "list", option, value], ["policy", "list", option, allowed], "GET", PoliciesPath);
    }

    // show prints the full model, which only the GET by ID returns. "web to db (old)" contains the
    // name given and must not match.
    [Test]
    public async Task Policy_show_looks_up_the_description_and_prints_the_policy_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(40, "web to db (old)"), ApiJson.Policy(42, "web to db")));
        run.Stub("GET", PolicyPath(40), json: ApiJson.Policy(40, "web to db (old)"));
        run.Stub("GET", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        var result = await run.RunAsync("policy", "show", "web to db");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", PolicyPath(42)), Has.Count.EqualTo(1));
            Assert.That(run.RequestsTo("GET", PolicyPath(40)), Is.Empty);
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(42));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("web to db"));
        });
    }

    [TestCase("show", "../systems", "42", "GET", "policies/42")]
    [TestCase("show", "4x2", "42", "GET", "policies/42")]
    [TestCase("show", "2147483648", "42", "GET", "policies/42")]
    [TestCase("disable", "42,abc", "42,43", "PUT", "policies/disable")]
    [TestCase("delete", "17,1.5", "17,15", "DELETE", "policies")]
    public async Task Policy_commands_reject_an_id_that_is_not_a_32_bit_integer(string verb, string badIds, string goodIds, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", PolicyPath(42), json: ApiJson.Policy(42));
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 2);
        run.StubBulk("DELETE", PoliciesPath, "policiesDeleted", 2);

        await CliAssert.RejectedThenAcceptedAsync(run, ["policy", verb, "--id", badIds], ["policy", verb, "--id", goodIds], method, TestData.OrgPath(path));
    }

    [TestCase("show", "GET", "policies/42")]
    [TestCase("disable", "PUT", "policies/disable")]
    public async Task Policy_commands_reject_a_description_together_with_an_id(string verb, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.Stub("GET", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 1);

        await CliAssert.RejectedThenAcceptedAsync(run, ["policy", verb, "web to db", "--id", "42"], ["policy", verb, "--id", "42"], method, TestData.OrgPath(path));
    }

    [Test]
    public async Task Policy_show_never_puts_a_description_into_the_url_path()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));

        var result = await run.RunAsync("policy", "show", "../tags");

        CliAssert.Failed(result, "invalid_argument");
        Assert.That(run.Calls(), Is.Not.Empty.And.All.EqualTo("GET " + PoliciesPath));
    }

    // Example 31.
    [TestCase("enable")]
    [TestCase("disable")]
    public async Task Policy_enable_and_disable_look_up_the_description_and_send_its_id_in_a_bulk_call(string verb)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(40, "web to db (old)"), ApiJson.Policy(42, "web to db")));
        run.StubBulk("PUT", BulkPath(verb), "policiesUpdated", 1);

        var result = await run.RunAsync("policy", verb, "web to db");

        CliAssert.Bulk(result, 1, 1);
        var bulk = run.RequestsTo("PUT", BulkPath(verb));
        Assert.That(bulk, Has.Count.EqualTo(1));
        Assert.That(bulk[0].SortedBodyIds("policyIds"), Is.EqualTo("42"));
    }

    [Test]
    public async Task Policy_disable_matches_the_description_ignoring_case()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "disable", "Web To DB");

        CliAssert.Bulk(result, 1, 1);
        Assert.That(run.RequestsTo("PUT", BulkPath("disable"))[0].SortedBodyIds("policyIds"), Is.EqualTo("42"));
    }

    // The API leaves disabled policies out of a list unless asked (portal
    // PolicyRepository.BuildFilterAsync), and enable must find a disabled policy by name.
    [Test]
    public async Task Policy_enable_looks_the_description_up_among_disabled_policies_too()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(DisabledPolicy(23, "contractors")));
        run.StubBulk("PUT", BulkPath("enable"), "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "enable", "contractors");

        CliAssert.Bulk(result, 1, 1);
        var lookups = run.RequestsTo("GET", PoliciesPath);
        Assert.That(lookups, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(lookups.Select(lookup => lookup.QueryValue("include_disabled")), Has.All.EqualTo("true").IgnoreCase);
            Assert.That(run.RequestsTo("PUT", BulkPath("enable"))[0].SortedBodyIds("policyIds"), Is.EqualTo("23"));
        });
    }

    // "web to db (old)" contains the name and must not match.
    [Test]
    public async Task Policy_disable_with_a_description_no_policy_has_exits_2_without_disabling_anything()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(40, "web to db (old)")));
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "disable", "web to db");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(NameLookup.CandidateIds(error), Is.Empty);
            Assert.That(run.RequestsTo("PUT", BulkPath("disable")), Is.Empty);
        });
    }

    [Test]
    public async Task Policy_disable_with_a_description_two_policies_share_exits_2_with_both_as_candidates()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(40, "web to db (old)"), ApiJson.Policy(4201, "web to db"), ApiJson.Policy(4302, "Web to DB")));
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "disable", "web to db");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(NameLookup.CandidateIds(error), Is.EqualTo("4201,4302"));
            Assert.That(run.RequestsTo("PUT", BulkPath("disable")), Is.Empty);
        });
    }

    // Example 18. CliRun sets ENCLAVE_ORG_ID to the test organisation, which --org overrides.
    [Test]
    public async Task Policy_disable_with_org_acts_in_that_organisation()
    {
        using var run = CliRun.Start();
        BothOrgs.Stub(run);
        run.Stub("GET", OtherOrgPoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.StubBulk("PUT", OtherOrgPoliciesPath + "/disable", "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "disable", "web to db", "--org", TestData.OtherOrgName);

        CliAssert.Bulk(result, 1, 1);
        var bulk = run.RequestsTo("PUT", OtherOrgPoliciesPath + "/disable");
        Assert.That(bulk, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(bulk[0].SortedBodyIds("policyIds"), Is.EqualTo("42"));
            Assert.That(run.Requests.Where(request => request.Path.StartsWith(TestData.OrgPath(), StringComparison.Ordinal)), Is.Empty);
        });
    }

    // Example 18, by ID.
    [Test]
    public async Task Policy_disable_with_org_id_acts_in_that_organisation_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.StubBulk("PUT", OtherOrgPoliciesPath + "/disable", "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "disable", "--id", "42", "--org-id", TestData.OtherOrgId.ToString());

        CliAssert.Bulk(result, 1, 1);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(OtherOrgPoliciesPath + "/disable"));
            Assert.That(request.SortedBodyIds("policyIds"), Is.EqualTo("42"));
        });
    }

    // Example 6. Without --then the policy is disabled at expiry ("Command options"). The expiry is
    // bounded by the clock read before and after the run.
    [Test]
    public async Task Policy_enable_for_a_duration_enables_the_named_policy_until_then_and_prints_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(DisabledPolicy(23, "contractors")));
        run.Stub("PUT", EnableUntilPath(23), json: ApiJson.Policy(23, "contractors"));
        run.StubBulk("PUT", BulkPath("enable"), "policiesUpdated", 1);

        var before = DateTimeOffset.UtcNow;
        var result = await run.RunAsync("policy", "enable", "contractors", "--for", "8h");
        var after = DateTimeOffset.UtcNow;

        CliAssert.Succeeded(result);
        var timed = run.RequestsTo("PUT", EnableUntilPath(23));
        Assert.That(timed, Has.Count.EqualTo(1));
        var body = timed[0].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.ExpiryDateTime(body), Is.InRange(before.AddHours(8).AddSeconds(-1), after.AddHours(8).AddSeconds(1)));
            Assert.That(Text(body, "expiryAction"), Is.EqualTo("Disable"));
            Assert.That(run.RequestsTo("PUT", BulkPath("enable")), Is.Empty);
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(23));
        });
    }

    [Test]
    public async Task Policy_enable_until_a_time_with_a_zone_and_then_delete_schedules_a_delete_at_that_instant_in_utc()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("PUT", EnableUntilPath(23), json: ApiJson.Policy(23, "contractors"));

        var request = await CliAssert.AcceptedAsync(
            run, "PUT", EnableUntilPath(23), "policy", "enable", "--id", "23", "--until", "2030-01-15T09:30:00-04:00", "--then", "delete");

        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2030, 1, 15, 13, 30, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(Text(request.BodyJson, "expiryAction"), Is.EqualTo("Delete"));
        });
    }

    // The API's timed enable has no bulk form ("Several IDs").
    [Test]
    public async Task Policy_enable_for_a_duration_rejects_more_than_one_policy()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", EnableUntilPath(42), json: ApiJson.Policy(42));
        run.StubBulk("PUT", BulkPath("enable"), "policiesUpdated", 2);

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["policy", "enable", "--id", "42,43", "--for", "8h"], ["policy", "enable", "--id", "42", "--for", "8h"], "PUT", EnableUntilPath(42));
    }

    // Names match ignoring case, so both arguments name one policy, and --for takes one item.
    [TestCase("contractors", "contractors")]
    [TestCase("contractors", "CONTRACTORS")]
    public async Task Policy_enable_for_a_duration_takes_one_policy_named_twice(string first, string second)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(DisabledPolicy(23, "contractors"), ApiJson.Policy(24, "contractors (old)")));
        run.Stub("PUT", EnableUntilPath(23), json: ApiJson.Policy(23, "contractors"));
        run.StubBulk("PUT", BulkPath("enable"), "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "enable", first, second, "--for", "8h");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("PUT", EnableUntilPath(23)), Has.Count.EqualTo(1));
            Assert.That(run.RequestsTo("PUT", BulkPath("enable")), Is.Empty);
            Assert.That(result.StdoutJson.GetProperty("id").GetInt32(), Is.EqualTo(23));
        });
    }

    // Example 54.
    [Test]
    public async Task Policy_delete_looks_up_the_description_and_deletes_it_in_a_bulk_call()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(17, "old vpn"), ApiJson.Policy(18, "old vpn backup")));
        run.StubBulk("DELETE", PoliciesPath, "policiesDeleted", 1);

        var result = await run.RunAsync("policy", "delete", "old vpn");

        CliAssert.Bulk(result, 1, 1);
        var delete = run.RequestsTo("DELETE", PoliciesPath);
        Assert.That(delete, Has.Count.EqualTo(1));
        Assert.That(delete[0].SortedBodyIds("policyIds"), Is.EqualTo("17"));
    }

    private static string PolicyPath(int id) => TestData.OrgPath("policies/" + id.ToString(CultureInfo.InvariantCulture));

    private static string BulkPath(string verb) => TestData.OrgPath("policies/" + verb);

    private static string EnableUntilPath(int id) => PolicyPath(id) + "/enable-until";

    // Built on ApiJson.Policy, so the body keeps the shape HarnessTests proves Enclave.Sdk.Api reads.
    private static string PolicyWith(int id, string description, params (string Name, string Json)[] changes)
    {
        var policy = JsonNode.Parse(ApiJson.Policy(id, description))!.AsObject();

        foreach (var (name, json) in changes)
        {
            policy[name] = JsonNode.Parse(json);
        }

        return policy.ToJsonString();
    }

    private static string DisabledPolicy(int id, string description) =>
        PolicyWith(id, description, ("isEnabled", "false"), ("state", "\"Disabled\""));

    // Ports are compared without spaces, since the API reads "8000-8100" and "8000 - 8100" alike and
    // writes the second (portal AclConversionExtensions.TryParsePorts,
    // PolicyModelExtensions.FormatPorts). Sorted, since ACL order does not matter.
    private static string Acls(JsonElement acls) =>
        Set(acls.EnumerateArray()
            .Select(acl => $"{Text(acl, "protocol")}|{Text(acl, "ports").Replace(" ", string.Empty, StringComparison.Ordinal)}|{Text(acl, "description")}")
            .ToArray());

    // Sorted, since a subnet filter allows its range whatever its position in the list.
    private static string Ranges(JsonElement ranges) =>
        Set(ranges.EnumerateArray().Select(range => $"{Text(range, "ipRange")}|{Text(range, "description")}").ToArray());

    // Kept in order: with --mode ordered the order of the --gateway flags is the order the gateways
    // are tried in ("Command options").
    private static string Gateways(JsonElement gateways) =>
        string.Join("; ", gateways.EnumerateArray().Select(gateway => $"{Text(gateway, "systemId")}:{JsonRead.StringList(JsonAssert.Property(gateway, "routes"))}"));
}
