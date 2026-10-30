using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static Enclave.Cli.Tests.Support.FieldText;

namespace Enclave.Cli.Tests.Resources;

/// <summary>
/// Tests for the trust commands: list, show and delete.
/// </summary>
public partial class TrustCommandTests
{
    private static readonly string TrustsPath = TestData.OrgPath("trust-requirements");

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

    // The API reads TrustRequirementType names and matches nothing for a value it cannot read
    // (portal TrustRequirementSearchKeyService.BuildFilterAsync).
    [Test]
    public async Task Trust_list_with_type_user_auth_adds_the_type_to_the_filter_text()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(3, "entra staff")));

        var result = await run.RunAsync("trust", "list", "--filter", "staff", "--type", "user-auth");

        CliAssert.List(result, "trust");
        var terms = SearchTerms.Of(run.SingleRequest().QueryValue("search"));
        Assert.Multiple(() =>
        {
            Assert.That(terms, Has.Length.EqualTo(2));
            Assert.That(terms, Has.Member("staff"));
            Assert.That(terms, Has.Member("type:UserAuthentication"));
        });
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

    [TestCase("list")]
    public async Task Trust_read_commands_reject_dry_run(string verb)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(5)));
        run.Stub("GET", TrustPath(5), json: ApiJson.Trust(5));
        string[] corrected = verb == "list" ? ["trust", "list"] : ["trust", "show", "--id", "5"];

        await CliAssert.RejectedThenAcceptedAsync(run, [.. corrected, "--dry-run"], corrected, "GET", verb == "list" ? TrustsPath : TrustPath(5));
    }

    // The list items are summaries without settings (portal TrustRequirementSummaryModel), so show
    // reads the model by ID. "uk only (old)" contains the name and must not match.
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

    [TestCase("../policies")]
    [TestCase("4294967296")]
    public async Task Trust_show_rejects_an_id_that_is_not_a_32_bit_integer(string badId)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustPath(5), json: ApiJson.Trust(5));

        await CliAssert.RejectedThenAcceptedAsync(run, ["trust", "show", "--id", badId], ["trust", "show", "--id", "5"], "GET", TrustPath(5));
    }

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
        Assert.That(delete[0].SortedBodyIds("requirementIds"), Is.EqualTo("5"));
    }

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
            Assert.That(NameLookup.CandidateIds(error), Is.Empty);
            Assert.That(run.RequestsTo("DELETE", TrustsPath), Is.Empty);
        });
    }

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
            Assert.That(NameLookup.CandidateIds(error), Is.EqualTo("5,6"));
            Assert.That(run.RequestsTo("DELETE", TrustsPath), Is.Empty);
        });
    }

    private static string TrustPath(int id) => TestData.OrgPath("trust-requirements/" + id.ToString(CultureInfo.InvariantCulture));

    // Conditions are written as the API stores them, every value a string (portal
    // TrustRequirementSettingsModel).
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

    // Sorted, since condition order does not matter (services PublicIpValidator). Country codes match
    // ignoring case, so they are compared in upper case.
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
}
