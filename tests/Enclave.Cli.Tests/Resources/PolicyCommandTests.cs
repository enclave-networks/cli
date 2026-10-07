using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// The policy commands (proposed-cli-surface.md "Commands", "Command options", "Filters", "Names and
// IDs", "Details"). This part holds list, show, enable, disable and delete; PolicyCommandTests.Create.cs
// and PolicyCommandTests.Update.cs hold create and update. A policy is given by its description,
// which costs one lookup call, or by --id, which costs none ("Calls per command").

/// <summary>
/// Tests for the policy commands: list, show, enable, disable and delete.
/// </summary>
public partial class PolicyCommandTests
{
    private const string None = "(none)";

    private const string Empty = "(empty)";

    private static readonly string PoliciesPath = TestData.OrgPath("policies");

    private static readonly string TrustsPath = TestData.OrgPath("trust-requirements");

    // The same path form as TestData.OrgPath: Enclave.Sdk.Api writes the organisation ID as 32 hex
    // digits (OrganisationGuid.ToString, Enclave.Sdk.Api.Data 304.48.0).
    private static readonly string OtherOrgPoliciesPath = $"/org/{TestData.OtherOrgId:N}/policies";

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

    // Example 50. Without include_disabled the API leaves disabled policies out before the search
    // applies (portal PolicyRepository.cs:381), so --state disabled has to ask for them; the
    // specification has it list them without --include-disabled ("Filters"). The search text is
    // state: and the same word ("Filters", search text table).
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
            Assert.That(SearchTerms(request), Is.EqualTo(StateDisabled));
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
        Assert.That(SearchTerms(run.SingleRequest()), Is.EqualTo(StateEnabled));
    }

    // On policy list the tags search key matches sender or receiver tags, and several values with no
    // modifier must all match (portal PolicySearchKeyService.cs, BuildFilterAsync), which is what
    // "--tag web,db lists items with every tag given" asks for ("Filters").
    [Test]
    public async Task Policy_list_with_tags_searches_the_tags_key_for_every_tag_given()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--tag", "web,db");

        CliAssert.List(result, "policy");
        Assert.That(SearchTerms(run.SingleRequest()), Is.EqualTo(TagsWebAndDb));
    }

    // --filter is sent as typed and the filter flags are added to it as search keys, so they
    // combine ("Filters").
    [Test]
    public async Task Policy_list_adds_the_tag_and_state_filters_to_the_filter_text()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--filter", "database", "--tag", "web", "--state", "enabled");

        CliAssert.List(result, "policy");
        var terms = SearchTerms(run.SingleRequest());
        Assert.Multiple(() =>
        {
            Assert.That(terms, Has.Length.EqualTo(3));
            Assert.That(terms, Has.Member("database"));
            Assert.That(terms, Has.Member("tags:web"));
            Assert.That(terms, Has.Member("state:enabled"));
        });
    }

    // Enclave.Sdk.Api 1.0.4 writes the flag with bool.ToString (PoliciesClient.BuildQueryString), so
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

    // Enum option values are lower-case and hyphenated, matched ignoring case, and the API receives
    // its own name ("Options on every command"). PolicySortOrder has Description and
    // RecentlyCreated (portal Enclave.Configuration.Data/Modules/Policies/Enums/PolicySortOrder.cs).
    [TestCase("description", "Description")]
    [TestCase("recently-created", "RecentlyCreated")]
    [TestCase("Recently-Created", "RecentlyCreated")]
    public async Task Policy_list_sends_the_sort_order_the_sort_option_names(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--sort", value);

        CliAssert.List(result, "policy");
        Assert.That(run.SingleRequest().QueryValue("sort"), Is.EqualTo(expected));
    }

    [TestCase("--sort", "newest", "description")]
    [TestCase("--state", "paused", "enabled")]
    public async Task Policy_list_rejects_an_option_value_outside_the_allowed_values(string option, string value, string allowed)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(1)));

        await CliAssert.RejectedThenAcceptedAsync(run, ["policy", "list", option, value], ["policy", "list", option, allowed], "GET", PoliciesPath);
    }

    // An option a command does not take is unknown to it and exits 2 ("Details"); --dry-run belongs
    // to commands that change something.
    [TestCase("list")]
    [TestCase("show")]
    public async Task Policy_read_commands_reject_dry_run(string verb)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42)));
        run.Stub("GET", PolicyPath(42), json: ApiJson.Policy(42));
        string[] corrected = verb == "list" ? ["policy", "list"] : ["policy", "show", "--id", "42"];

        await CliAssert.RejectedThenAcceptedAsync(run, [.. corrected, "--dry-run"], corrected, "GET", verb == "list" ? PoliciesPath : PolicyPath(42));
    }

    // show prints the full model, which the GET by ID returns, so a description costs the lookup
    // and then that GET. "web to db (old)" contains the name given, and a lookup must match the
    // whole description ("Names and IDs").
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

    [Test]
    public async Task Policy_show_with_id_gets_the_policy_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PolicyPath(42), json: ApiJson.Policy(42, "web to db"));

        await CliAssert.AcceptedAsync(run, "GET", PolicyPath(42), "policy", "show", "--id", "42");
    }

    // Single-item commands exit 5 for an unknown ID ("Several IDs").
    [Test]
    public async Task Policy_show_with_an_unknown_id_exits_5()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", PolicyPath(42), 404, "Not Found", "Policy 42 does not exist.");

        var result = await run.RunAsync("policy", "show", "--id", "42");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo(PolicyPath(42)));
    }

    // Policy IDs are 32-bit integers, checked before any call, because Enclave.Sdk.Api 1.0.4 puts
    // IDs into URL paths unescaped ("ID checks", "Details").
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

    // A name argument with --id contradicts itself ("Details"), and arguments are checked before
    // any call ("Errors and exit codes").
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

    // A name goes to the lookup and never into a URL path ("ID checks"), so a description that
    // looks like a path is only ever searched for.
    [Test]
    public async Task Policy_show_never_puts_a_description_into_the_url_path()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));

        var result = await run.RunAsync("policy", "show", "../tags");

        CliAssert.Failed(result, "invalid_argument");
        Assert.That(run.Calls(), Has.All.EqualTo("GET " + PoliciesPath));
    }

    // Example 31. A command that takes several items always makes the bulk call, also for one item,
    // and prints { requested, affected } ("Several IDs").
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
        Assert.That(Ids(bulk[0]), Is.EqualTo("42"));
    }

    // Names match exactly, ignoring case ("Names and IDs").
    [Test]
    public async Task Policy_disable_matches_the_description_ignoring_case()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "disable", "Web To DB");

        CliAssert.Bulk(result, 1, 1);
        Assert.That(Ids(run.RequestsTo("PUT", BulkPath("disable"))[0]), Is.EqualTo("42"));
    }

    // Name lookups ask for disabled items too, since enable takes a disabled item by name
    // ("Filters"); the API leaves disabled policies out of a list unless asked (portal
    // PolicyRepository.cs:381).
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
            Assert.That(Ids(run.RequestsTo("PUT", BulkPath("enable"))[0]), Is.EqualTo("23"));
        });
    }

    [Test]
    public async Task Policy_disable_sends_every_policy_named_in_one_bulk_call()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(17, "old vpn"), ApiJson.Policy(42, "web to db"), ApiJson.Policy(57, "build to artifacts")));
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 2);

        var result = await run.RunAsync("policy", "disable", "web to db", "old vpn");

        CliAssert.Bulk(result, 2, 2);
        var bulk = run.RequestsTo("PUT", BulkPath("disable"));
        Assert.That(bulk, Has.Count.EqualTo(1));
        Assert.That(Ids(bulk[0]), Is.EqualTo("17,42"));
    }

    // Example 35. --id takes a comma list and makes no lookup.
    [TestCase("enable")]
    [TestCase("disable")]
    public async Task Policy_enable_and_disable_with_ids_send_them_in_one_bulk_call_without_a_lookup(string verb)
    {
        using var run = CliRun.Start();
        run.StubBulk("PUT", BulkPath(verb), "policiesUpdated", 3);

        var result = await run.RunAsync("policy", verb, "--id", "42,43,57");

        CliAssert.Bulk(result, 3, 3);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(BulkPath(verb)));
            Assert.That(Ids(request), Is.EqualTo("42,43,57"));
        });
    }

    // No match exits 2 invalid_argument, and the error's candidates hold nothing ("Errors and exit
    // codes"); "web to db (old)" contains the name and is not a match.
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
            Assert.That(CandidateIds(error), Is.Empty);
            Assert.That(run.RequestsTo("PUT", BulkPath("disable")), Is.Empty);
        });
    }

    // Several matches exit 2 invalid_argument, and the error's candidates are the items that
    // matched, so the caller can choose by --id without another call ("Errors and exit codes").
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
            Assert.That(CandidateIds(error), Is.EqualTo("4201,4302"));
            Assert.That(run.RequestsTo("PUT", BulkPath("disable")), Is.Empty);
        });
    }

    // Example 18. --org names the organisation over ENCLAVE_ORG_ID, which CliRun sets to Acme, and a
    // name costs one lookup ("Context"). The policy lookup and the change go to Globex.
    [Test]
    public async Task Policy_disable_with_org_acts_in_that_organisation()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", OtherOrgPoliciesPath, json: ApiJson.Page(ApiJson.Policy(42, "web to db")));
        run.StubBulk("PUT", OtherOrgPoliciesPath + "/disable", "policiesUpdated", 1);

        var result = await run.RunAsync("policy", "disable", "web to db", "--org", TestData.OtherOrgName);

        CliAssert.Bulk(result, 1, 1);
        var bulk = run.RequestsTo("PUT", OtherOrgPoliciesPath + "/disable");
        Assert.That(bulk, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Ids(bulk[0]), Is.EqualTo("42"));
            Assert.That(run.Requests.Where(request => request.Path.StartsWith(TestData.OrgPath(), StringComparison.Ordinal)), Is.Empty);
        });
    }

    // Example 18, by ID: --org-id takes a GUID and costs no lookup ("Context").
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
            Assert.That(Ids(request), Is.EqualTo("42"));
        });
    }

    // Example 6. --for uses the API's timed enable, which takes one policy and returns it, and the
    // policy is disabled when the time is up unless --then says otherwise ("Command options"). The
    // expiry is bracketed by the clock before and after the run.
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
    public async Task Policy_enable_with_id_for_a_duration_makes_one_timed_enable_call()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", EnableUntilPath(23), json: ApiJson.Policy(23, "contractors"));

        var before = DateTimeOffset.UtcNow;
        var request = await CliAssert.AcceptedAsync(run, "PUT", EnableUntilPath(23), "policy", "enable", "--id", "23", "--for", "8h");
        var after = DateTimeOffset.UtcNow;

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.ExpiryDateTime(request.BodyJson), Is.InRange(before.AddHours(8).AddSeconds(-1), after.AddHours(8).AddSeconds(1)));
            Assert.That(Text(request.BodyJson, "expiryAction"), Is.EqualTo("Disable"));
        });
    }

    // --until with a zone is that instant (RFC 3339), sent as a UTC instant ("Details"), and --then
    // delete deletes the policy at expiry; the API names the actions Disable and Delete (portal
    // Enclave.Configuration.Data/Enums/ExpiryAction.cs).
    [Test]
    public async Task Policy_enable_until_a_time_with_a_zone_and_then_delete_schedules_a_delete_at_that_instant_in_utc()
    {
        using var run = CliRun.Start();
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

    // The API's timed enable has no bulk form, so --for and --until take one item ("Several IDs").
    [Test]
    public async Task Policy_enable_for_a_duration_rejects_more_than_one_policy()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", EnableUntilPath(42), json: ApiJson.Policy(42));
        run.StubBulk("PUT", BulkPath("enable"), "policiesUpdated", 2);

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["policy", "enable", "--id", "42,43", "--for", "8h"], ["policy", "enable", "--id", "42", "--for", "8h"], "PUT", EnableUntilPath(42));
    }

    // --then takes disable or delete on policies; revoke is the system's name for deleting
    // ("Command options").
    [Test]
    public async Task Policy_enable_rejects_then_revoke()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", EnableUntilPath(23), json: ApiJson.Policy(23));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["policy", "enable", "--id", "23", "--for", "8h", "--then", "revoke"], ["policy", "enable", "--id", "23", "--for", "8h", "--then", "delete"], "PUT", EnableUntilPath(23));
    }

    // --for with --until, and --then without either, contradict each other ("Details"); a time in
    // the past cannot be an expiry ("Details").
    [TestCase("--for", "8h", "--until", "2030-01-15T09:30:00Z")]
    [TestCase("--then", "delete", null, null)]
    [TestCase("--until", "2020-01-15T09:30:00Z", null, null)]
    public async Task Policy_enable_rejects_contradicting_or_past_expiry_options(string option, string value, string? otherOption, string? otherValue)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", EnableUntilPath(23), json: ApiJson.Policy(23));
        string[] rejected = otherOption is null ? ["policy", "enable", "--id", "23", option, value] : ["policy", "enable", "--id", "23", option, value, otherOption, otherValue!];

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, ["policy", "enable", "--id", "23", "--until", "2030-01-15T09:30:00Z"], "PUT", EnableUntilPath(23));
    }

    [Test]
    public async Task Policy_enable_for_a_duration_with_an_unknown_id_exits_5()
    {
        using var run = CliRun.Start();
        run.StubProblem("PUT", EnableUntilPath(23), 404, "Not Found", "Policy 23 does not exist.");

        var result = await run.RunAsync("policy", "enable", "--id", "23", "--for", "8h");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo(EnableUntilPath(23)));
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
        Assert.That(Ids(delete[0]), Is.EqualTo("17"));
    }

    // Example 54. affected below requested is still success: the IDs were unknown or already gone,
    // and a re-run is safe ("Several IDs").
    [Test]
    public async Task Policy_delete_with_ids_reports_what_the_api_deleted_and_exits_0()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", PoliciesPath, "policiesDeleted", 1);

        var result = await run.RunAsync("policy", "delete", "--id", "17,18");

        CliAssert.Bulk(result, 2, 1);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(Ids(request), Is.EqualTo("17,18"));
        });
    }

    // Example 54. The lookup is a read a change depends on, so it still runs; only the delete is
    // withheld ("Dry run").
    [Test]
    [Category(TestCategory.Pending)]
    public async Task Policy_delete_by_description_with_dry_run_prints_the_delete_and_sends_only_the_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PoliciesPath, json: ApiJson.Page(ApiJson.Policy(17, "old vpn")));
        run.StubBulk("DELETE", PoliciesPath, "policiesDeleted", 1);

        var result = await run.RunAsync("policy", "delete", "old vpn", "--dry-run");

        var request = DryRunRequest(result, "DELETE", PoliciesPath);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntList(JsonAssert.Property(JsonAssert.Property(request, "body"), "policyIds")), Is.EqualTo("17"));
            Assert.That(run.Calls(), Has.All.EqualTo("GET " + PoliciesPath));
        });
    }

    [Test]
    [Category(TestCategory.Pending)]
    public async Task Policy_delete_with_id_and_dry_run_prints_the_delete_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", PoliciesPath, "policiesDeleted", 1);

        var result = await run.RunAsync("policy", "delete", "--id", "17", "--dry-run");

        var request = DryRunRequest(result, "DELETE", PoliciesPath);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntList(JsonAssert.Property(JsonAssert.Property(request, "body"), "policyIds")), Is.EqualTo("17"));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // "-" reads a policy list and acts on its items by ID, the id property of each policy
    // ("Several IDs").
    [Test]
    public async Task Policy_disable_reads_a_policy_list_from_stdin()
    {
        using var run = CliRun.Start();
        run.StubBulk("PUT", BulkPath("disable"), "policiesUpdated", 2);
        run.StdinText = CliList.WithIds("policy", "42", "43");

        var result = await run.RunAsync("policy", "disable", "-");

        CliAssert.Bulk(result, 2, 2);
        Assert.That(Ids(run.SingleRequest()), Is.EqualTo("42,43"));
    }

    // Keys and policies both have integer IDs, so the list's kind is what stops a key list deleting
    // the policies that share those numbers ("Several IDs").
    [Test]
    public async Task Policy_delete_rejects_a_key_list_from_stdin()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", PoliciesPath, "policiesDeleted", 1);
        run.StdinText = CliList.WithIds("key", "42");

        var result = await run.RunAsync("policy", "delete", "-");

        CliAssert.Rejected(run, result, "invalid_argument");
    }

    private static string PolicyPath(int id) => TestData.OrgPath("policies/" + id.ToString(CultureInfo.InvariantCulture));

    private static string BulkPath(string verb) => TestData.OrgPath("policies/" + verb);

    private static string EnableUntilPath(int id) => PolicyPath(id) + "/enable-until";

    // Examples give a policy both ways, by its description and by --id ("Names and IDs"), and the
    // command must send the same change either way.
    private static string[] Target(bool byDescription, string description, int id) =>
        byDescription ? [description] : ["--id", id.ToString(CultureInfo.InvariantCulture)];

    // ApiJson.Policy with some properties replaced, each given as JSON text, so the body keeps the
    // shape HarnessTests proves Enclave.Sdk.Api reads.
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

    // Sorted, since the order of a bulk call's IDs does not change what it does.
    private static string Ids(RecordedRequest request) =>
        string.Join(",", request.BodyIds("policyIds").Order(StringComparer.Ordinal));

    // The API splits the search text at whitespace (portal BaseSearchKeyService.GetTokens), so the
    // order of the terms is not part of the requirement; each term is the exact search text the
    // "Filters" table gives.
    private static string[] SearchTerms(RecordedRequest request) =>
        (request.QueryValue("search") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // The IDs of the candidates a name lookup error carries, sorted. For no match the candidates
    // hold nothing ("Errors and exit codes"), so a missing, null or empty list all read as "".
    private static string CandidateIds(JsonElement error)
    {
        if (!error.TryGetProperty("candidates", out var candidates) || candidates.ValueKind == JsonValueKind.Null)
        {
            return string.Empty;
        }

        return string.Join(",", candidates.EnumerateArray().Select(candidate => JsonAssert.Property(candidate, "id").GetInt32()).Order().Select(id => id.ToString(CultureInfo.InvariantCulture)));
    }

    // The output form "Dry run" gives: { "dryRun": true, "org": { id, name }, "requests": [...] },
    // with requests a list. Each command here sends one change, so the list holds one request.
    // CliRun names the organisation by ID (ENCLAVE_ORG_ID), and then no lookup gives its name, which
    // is null.
    private static JsonElement DryRunRequest(CliResult result, string method, string path)
    {
        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var org = JsonAssert.Property(output, "org");
        var requests = JsonAssert.Property(output, "requests");
        Assert.That(requests.ValueKind, Is.EqualTo(JsonValueKind.Array), result.ToString());
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var request = requests[0];

        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo(method));
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(path));
        });

        return request;
    }

    // Ports are compared without spaces: the API reads "8000-8100" and "8000 - 8100" alike and
    // writes the second (portal AclConversionExtensions.TryParsePorts,
    // PolicyModelExtensions.FormatPorts). ACLs allow traffic whatever their order, so the entries
    // are sorted.
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

    private static string Set(params string[] entries) => string.Join("; ", entries.Order(StringComparer.Ordinal));

    // Names match ignoring case, since PATCH bodies key the top level in PascalCase
    // (JsonAssert.Property). Missing and null read as (none), an unset label; an empty string reads
    // as (empty), since an empty label is sent as null ("Details"); any other JSON kind reads as
    // json:<raw>, so a boolean or number never passes for text.
    private static string Text(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return property.Value.ValueKind switch
            {
                JsonValueKind.Null => None,
                JsonValueKind.String => property.Value.GetString() is { Length: > 0 } text ? text : Empty,
                _ => "json:" + property.Value.GetRawText(),
            };
        }

        return None;
    }

    // Absent, null and [] all leave a setting unset. A general policy must have no gateways, subnet
    // filters or traffic direction (portal PolicyCreateModelValidator.cs:47-52).
    private static bool IsUnset(JsonElement body, string name) =>
        Text(body, name) == None
        || (body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0);
}
