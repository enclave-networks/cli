using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// The trust commands (proposed-cli-surface.md "Commands", "Command options", "Filters", "Names and
// IDs", "Details"). This part holds list, show and delete; TrustCommandTests.Create.cs and
// TrustCommandTests.Update.cs hold create and update. A trust requirement is given by its
// description, which costs one lookup call, or by --id, which costs none ("Calls per command").

/// <summary>
/// Tests for the trust commands: list, show and delete.
/// </summary>
public partial class TrustCommandTests
{
    private const string None = "(none)";

    private const string Empty = "(empty)";

    private static readonly string TrustsPath = TestData.OrgPath("trust-requirements");

    private static readonly string[] PublicIpTypeTerm = ["type:PublicIp"];

    [Test]
    public async Task Trust_list_reads_every_page_and_prints_a_trust_list()
    {
        using var run = CliRun.Start();
        run.StubPages(TrustsPath, 2, ApiJson.Trust(3), ApiJson.Trust(4), ApiJson.Trust(5));

        var result = await run.RunAsync("trust", "list");

        var items = CliAssert.List(result, "trust");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("3,4,5"));
            Assert.That(run.PagesRequested(TrustsPath), Is.EqualTo("0,1"));
            Assert.That(run.RequestsTo("GET", TrustsPath)[0].QueryValue("search"), Is.Null);
        });
    }

    // Example 59. --type public-ip is the search text type:PublicIp ("Filters", search text table):
    // the API reads TrustRequirementType names and matches nothing for a value it cannot read
    // (portal TrustRequirementSearchKeyService.BuildFilterAsync).
    [Test]
    public async Task Trust_list_with_type_public_ip_searches_for_public_ip_requirements()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(PublicIpTrust(5, "uk only", ("country", "GB", false, null))));

        var result = await run.RunAsync("trust", "list", "--type", "public-ip");

        var items = CliAssert.List(result, "trust");
        Assert.Multiple(() =>
        {
            Assert.That(SearchTerms(run.SingleRequest()), Is.EqualTo(PublicIpTypeTerm));
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("5"));
        });
    }

    // --type user-auth is the search text type:UserAuthentication ("Filters", search text table),
    // added to the --filter text.
    [Test]
    public async Task Trust_list_with_type_user_auth_adds_the_type_to_the_filter_text()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(3, "entra staff")));

        var result = await run.RunAsync("trust", "list", "--filter", "staff", "--type", "user-auth");

        CliAssert.List(result, "trust");
        var terms = SearchTerms(run.SingleRequest());
        Assert.Multiple(() =>
        {
            Assert.That(terms, Has.Length.EqualTo(2));
            Assert.That(terms, Has.Member("staff"));
            Assert.That(terms, Has.Member("type:UserAuthentication"));
        });
    }

    // TrustRequirementSortOrder has Description and RecentlyCreated (portal
    // Enclave.Configuration.Data/Modules/TrustRequirements/Enums/TrustRequirementSortOrder.cs), and
    // option values are lower-case and hyphenated ("Options on every command").
    [TestCase("description", "Description")]
    [TestCase("recently-created", "RecentlyCreated")]
    public async Task Trust_list_sends_the_sort_order_the_sort_option_names(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(3)));

        var result = await run.RunAsync("trust", "list", "--sort", value);

        CliAssert.List(result, "trust");
        Assert.That(run.SingleRequest().QueryValue("sort"), Is.EqualTo(expected));
    }

    // alphabetical is a tag sort order; the trust requirement list does not have it.
    [TestCase("--type", "wifi", "public-ip")]
    [TestCase("--sort", "alphabetical", "description")]
    public async Task Trust_list_rejects_an_option_value_outside_the_allowed_values(string option, string value, string allowed)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(3)));

        await CliAssert.RejectedThenAcceptedAsync(run, ["trust", "list", option, value], ["trust", "list", option, allowed], "GET", TrustsPath);
    }

    // An option a command does not take is unknown to it and exits 2 ("Details"); --dry-run belongs
    // to commands that change something.
    [TestCase("list")]
    [TestCase("show")]
    public async Task Trust_read_commands_reject_dry_run(string verb)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(5)));
        run.Stub("GET", TrustPath(5), json: ApiJson.Trust(5));
        string[] corrected = verb == "list" ? ["trust", "list"] : ["trust", "show", "--id", "5"];

        await CliAssert.RejectedThenAcceptedAsync(run, [.. corrected, "--dry-run"], corrected, "GET", verb == "list" ? TrustsPath : TrustPath(5));
    }

    // show prints the full model, which the GET by ID returns; the list items are summaries without
    // settings (portal TrustRequirementSummaryModel). "uk only (old)" contains the name and is not a
    // match.
    [Test]
    public async Task Trust_show_looks_up_the_description_and_prints_the_requirement_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)"), ApiJson.Trust(5, "uk only")));
        run.Stub("GET", TrustPath(4), json: ApiJson.Trust(4, "uk only (old)"));
        run.Stub("GET", TrustPath(5), json: PublicIpTrust(5, "uk only", ("country", "GB", false, null)));

        var result = await run.RunAsync("trust", "show", "uk only");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", TrustPath(5)), Has.Count.EqualTo(1));
            Assert.That(run.RequestsTo("GET", TrustPath(4)), Is.Empty);
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(5));
            Assert.That(JsonAssert.Property(result.StdoutJson, "type").GetString(), Is.EqualTo("PublicIp"));
        });
    }

    [Test]
    public async Task Trust_show_with_id_gets_the_requirement_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustPath(5), json: ApiJson.Trust(5, "uk only"));

        await CliAssert.AcceptedAsync(run, "GET", TrustPath(5), "trust", "show", "--id", "5");
    }

    // Single-item commands exit 5 for an unknown ID ("Several IDs").
    [Test]
    public async Task Trust_show_with_an_unknown_id_exits_5()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", TrustPath(5), 404, "Not Found", "Trust requirement 5 does not exist.");

        var result = await run.RunAsync("trust", "show", "--id", "5");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo(TrustPath(5)));
    }

    // Trust requirement IDs are 32-bit integers, checked before any call, because Enclave.Sdk.Api
    // 1.0.4 puts IDs into URL paths unescaped ("ID checks", "Details").
    [TestCase("show", "../policies", "5", "GET", "trust-requirements/5")]
    [TestCase("show", "4294967296", "5", "GET", "trust-requirements/5")]
    [TestCase("delete", "5,five", "5,6", "DELETE", "trust-requirements")]
    public async Task Trust_commands_reject_an_id_that_is_not_a_32_bit_integer(string verb, string badIds, string goodIds, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TrustPath(5), json: ApiJson.Trust(5));
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 2);

        await CliAssert.RejectedThenAcceptedAsync(run, ["trust", verb, "--id", badIds], ["trust", verb, "--id", goodIds], method, TestData.OrgPath(path));
    }

    // A name argument with --id contradicts itself ("Details"), and arguments are checked before
    // any call ("Errors and exit codes").
    [TestCase("show", "GET", "trust-requirements/5")]
    [TestCase("delete", "DELETE", "trust-requirements")]
    public async Task Trust_commands_reject_a_description_together_with_an_id(string verb, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(5, "uk only")));
        run.Stub("GET", TrustPath(5), json: ApiJson.Trust(5, "uk only"));
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 1);

        await CliAssert.RejectedThenAcceptedAsync(run, ["trust", verb, "uk only", "--id", "5"], ["trust", verb, "--id", "5"], method, TestData.OrgPath(path));
    }

    // A command that takes several items always makes the bulk call, also for one item ("Several
    // IDs").
    [Test]
    public async Task Trust_delete_looks_up_the_description_and_deletes_it_in_a_bulk_call()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)"), ApiJson.Trust(5, "uk only")));
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 1);

        var result = await run.RunAsync("trust", "delete", "uk only");

        CliAssert.Bulk(result, 1, 1);
        var delete = run.RequestsTo("DELETE", TrustsPath);
        Assert.That(delete, Has.Count.EqualTo(1));
        Assert.That(Ids(delete[0]), Is.EqualTo("5"));
    }

    [Test]
    public async Task Trust_delete_with_ids_sends_them_in_one_bulk_call_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 2);

        var result = await run.RunAsync("trust", "delete", "--id", "5,9");

        CliAssert.Bulk(result, 2, 2);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(Ids(request), Is.EqualTo("5,9"));
        });
    }

    // No match exits 2 invalid_argument, and the error's candidates hold nothing ("Errors and exit
    // codes").
    [Test]
    public async Task Trust_delete_with_a_description_no_requirement_has_exits_2_without_deleting_anything()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)")));
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 1);

        var result = await run.RunAsync("trust", "delete", "uk only");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(CandidateIds(error), Is.Empty);
            Assert.That(run.RequestsTo("DELETE", TrustsPath), Is.Empty);
        });
    }

    // Several matches exit 2 invalid_argument, and the error's candidates are the items that
    // matched ("Errors and exit codes").
    [Test]
    public async Task Trust_delete_with_a_description_two_requirements_share_exits_2_with_both_as_candidates()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)"), ApiJson.Trust(5, "uk only"), ApiJson.Trust(6, "UK Only")));
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 1);

        var result = await run.RunAsync("trust", "delete", "uk only");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(CandidateIds(error), Is.EqualTo("5,6"));
            Assert.That(run.RequestsTo("DELETE", TrustsPath), Is.Empty);
        });
    }

    // "-" reads a trust list and acts on its items by ID ("Several IDs").
    [Test]
    public async Task Trust_delete_reads_a_trust_list_from_stdin()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 2);
        run.StdinText = CliList.WithIds("trust", "3", "4");

        var result = await run.RunAsync("trust", "delete", "-");

        CliAssert.Bulk(result, 2, 2);
        Assert.That(Ids(run.SingleRequest()), Is.EqualTo("3,4"));
    }

    // Policies and trust requirements both have integer IDs, so the list's kind is what stops a
    // policy list deleting the trust requirements that share those numbers ("Several IDs").
    [Test]
    public async Task Trust_delete_rejects_a_policy_list_from_stdin()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 1);
        run.StdinText = CliList.WithIds("policy", "3");

        var result = await run.RunAsync("trust", "delete", "-");

        CliAssert.Rejected(run, result, "invalid_argument");
    }

    // The output form "Dry run" gives: requests is a list, and org.name is null because CliRun names
    // the organisation by ID (ENCLAVE_ORG_ID), so no lookup gives its name.
    [Test]
    [Category(TestCategory.Pending)]
    public async Task Trust_delete_with_dry_run_prints_the_delete_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TrustsPath, "requirementsDeleted", 1);

        var result = await run.RunAsync("trust", "delete", "--id", "5", "--dry-run");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var org = JsonAssert.Property(output, "org");
        var requests = JsonAssert.Property(output, "requests");
        Assert.That(requests.ValueKind, Is.EqualTo(JsonValueKind.Array), result.ToString());
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var request = requests[0];
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo("DELETE"));
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(TrustsPath));
            Assert.That(JsonRead.IntList(JsonAssert.Property(JsonAssert.Property(request, "body"), "requirementIds")), Is.EqualTo("5"));
        });
    }

    private static string TrustPath(int id) => TestData.OrgPath("trust-requirements/" + id.ToString(CultureInfo.InvariantCulture));

    // Examples give a trust requirement both ways, by its description and by --id ("Names and IDs"),
    // and the command must send the same change either way.
    private static string[] Target(bool byDescription, string description, int id) =>
        byDescription ? [description] : ["--id", id.ToString(CultureInfo.InvariantCulture)];

    // Sorted, since the order of a bulk call's IDs does not change what it does.
    private static string Ids(RecordedRequest request) =>
        string.Join(",", request.BodyIds("requirementIds").Order(StringComparer.Ordinal));

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

    // ApiJson.Trust as a public IP requirement holding these conditions, written as the API stores
    // them: every value a string, isBlocked "true" or "false" (portal TrustRequirementSettingsModel
    // keeps each condition as a string dictionary).
    private static string PublicIpTrust(int id, string description, params (string Type, string Value, bool Blocked, string? Label)[] conditions)
    {
        var list = new JsonArray();

        foreach (var (type, value, blocked, label) in conditions)
        {
            var condition = new JsonObject { ["type"] = type, ["value"] = value, ["isBlocked"] = blocked ? "true" : "false" };

            if (label is not null)
            {
                condition["description"] = label;
            }

            list.Add(condition);
        }

        return TrustWith(id, description, "PublicIp", new JsonObject(), list);
    }

    // ApiJson.Trust as a sign-in requirement with this configuration and these claim conditions,
    // in the keys the API reads (portal UserAuthenticationConstants, TrustRequirementSettingsUserAuthValidator.cs).
    private static string SignInTrust(int id, string description, JsonObject configuration, params (string Claim, string Value)[] claims)
    {
        var list = new JsonArray();

        foreach (var (claim, value) in claims)
        {
            list.Add(new JsonObject { ["claim"] = claim, ["value"] = value });
        }

        return TrustWith(id, description, "UserAuthentication", configuration, list);
    }

    private static string TrustWith(int id, string description, string type, JsonObject configuration, JsonArray conditions)
    {
        var trust = JsonNode.Parse(ApiJson.Trust(id, description))!.AsObject();
        trust["type"] = type;
        trust["settings"] = new JsonObject { ["configuration"] = configuration, ["conditions"] = conditions };
        return trust.ToJsonString();
    }

    // Sorted, since the check reads every condition whatever its position (services
    // Enclave.Discover/TrustValidators/PublicIpValidator.cs). Country codes are matched ignoring case
    // ("Command options"), so a country's code is compared in upper case.
    private static string Conditions(JsonElement conditions) =>
        Set(conditions.EnumerateArray().Select(Condition).ToArray());

    private static string Condition(JsonElement condition)
    {
        var type = Text(condition, "type");
        var value = type == "country" ? Text(condition, "value").ToUpperInvariant() : Text(condition, "value");
        return $"{type}|{value}|{Text(condition, "isBlocked")}|{Text(condition, "description")}";
    }

    // Sorted, since a sign-in token must carry every claim whatever their order.
    private static string Claims(JsonElement conditions) =>
        Set(conditions.EnumerateArray().Select(condition => $"{Text(condition, "claim")}={Text(condition, "value")}").ToArray());

    private static string Set(params string[] entries) => string.Join("; ", entries.Order(StringComparer.Ordinal));

    // Names match ignoring case, since PATCH bodies key the top level in PascalCase
    // (JsonAssert.Property). Missing and null read as (none), an unset label; an empty string reads
    // as (empty), since an empty label is sent as null ("Details"); any other JSON kind reads as
    // json:<raw>, so a boolean never passes for the string "true" the API reads
    // (TrustRequirementSettingsModel).
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
}
