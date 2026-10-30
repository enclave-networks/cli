using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static Enclave.Cli.Tests.Support.FieldText;

namespace Enclave.Cli.Tests.Resources;

// A tag name goes into the URL path of show and set, so the CLI checks it against the API's tag
// rule before any call (portal TagValidationExtensions, _tagRegex). An argument starting "ref:" is
// a name like any other and fails that rule.
//
// tag set may read the tag to choose between the API's update and create calls
// (spec/cli-surface.md "Dry run"), so its tests serve the GET as well as the PATCH.
public class TagCommandTests
{
    private const string TagRefText = "ref:0123456789abcdef0123456789abcdef";

    private static readonly string TagsPath = TestData.OrgPath("tags");

    private static readonly string TrustsPath = TestData.OrgPath("trust-requirements");

    private static readonly string[] WebFilter = ["web"];

    [Test]
    public async Task Tag_list_reads_every_page_and_prints_a_tag_list()
    {
        using var run = CliRun.Start();
        run.StubPages(TagsPath, 2, ApiJson.Tag("web"), ApiJson.Tag("db"), ApiJson.Tag("api"));

        var result = await run.RunAsync("tag", "list");

        var items = CliAssert.List(result, "tag");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "tag"), Is.EqualTo("web,db,api"));
            Assert.That(run.PagesRequested(TagsPath), Is.EqualTo("0,1"));
            Assert.That(run.RequestsTo("GET", TagsPath)[0].QueryValue("search"), Is.Null);
        });
    }

    [Test]
    public async Task Tag_list_sends_the_filter_text_as_the_search()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagsPath, json: ApiJson.Page(ApiJson.Tag("web")));

        var result = await run.RunAsync("tag", "list", "--filter", "web");

        CliAssert.List(result, "tag");
        Assert.That(run.SingleRequest().QueryValue("search")?.Split(' ', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(WebFilter));
    }

    // recently-created belongs to policies and trust requirements.
    [Test]
    public async Task Tag_list_rejects_a_sort_order_the_tag_list_does_not_have()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagsPath, json: ApiJson.Page(ApiJson.Tag("web")));

        await CliAssert.RejectedThenAcceptedAsync(run, ["tag", "list", "--sort", "recently-created"], ["tag", "list", "--sort", "recently-used"], "GET", TagsPath);
    }

    // The name goes into the URL path (Enclave.Sdk.Api TagsClient.GetAsync and Update), so
    // ../systems would reach another route (spec/cli-surface.md "ID checks").
    [TestCase("web_servers", "web-servers")]
    [TestCase("../systems", "web")]
    [TestCase(TagRefText, "web")]
    public async Task Tag_show_rejects_a_name_outside_the_tag_rule(string badName, string goodName)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagPath(goodName), json: ApiJson.Tag(goodName));

        await CliAssert.RejectedThenAcceptedAsync(run, ["tag", "show", badName], ["tag", "show", goodName], "GET", TagPath(goodName));
    }

    // Enclave.Sdk.Api keys a PATCH body by the C# property name (PatchClient.Set).
    [Test]
    public async Task Tag_set_on_a_tag_that_exists_patches_only_the_fields_given_and_prints_the_tag()
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed", "--notes", "Web servers, all regions");

        CliAssert.Succeeded(result);
        var patch = SingleChange(run, "PATCH", TagPath("web"));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch.BodyJson), Is.EqualTo("Colour; Notes").IgnoreCase);
            Assert.That(JsonAssert.Property(patch.BodyJson, "Colour").GetString(), Is.EqualTo("#2f80ed"));
            Assert.That(JsonAssert.Property(patch.BodyJson, "Notes").GetString(), Is.EqualTo("Web servers, all regions"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "tag").GetString(), Is.EqualTo("web"));
        });
    }

    // Example 55. TagPatchModel.Tag holds the new name (portal TagPatchModel).
    [Test]
    public async Task Tag_set_with_name_renames_the_tag_and_sets_its_colour()
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "web", ApiJson.Tag("frontend"));

        var result = await run.RunAsync("tag", "set", "web", "--name", "frontend", "--colour", "#2f80ed");

        CliAssert.Succeeded(result);
        var patch = SingleChange(run, "PATCH", TagPath("web"));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch.BodyJson), Is.EqualTo("Colour; Tag").IgnoreCase);
            Assert.That(JsonAssert.Property(patch.BodyJson, "Tag").GetString(), Is.EqualTo("frontend"));
            Assert.That(JsonAssert.Property(patch.BodyJson, "Colour").GetString(), Is.EqualTo("#2f80ed"));
        });
    }

    // The update answers 404 for a tag the API does not hold (portal
    // TagModifyHandler.GetResponseAsync). A create sends every setting, so the trust requirements go
    // as [] (spec/cli-surface.md "Create and update"). Enclave.Sdk.Api writes every property of
    // TagCreateModel in camelCase (TagsClient.CreateAsync, Constants.JsonSerializerOptions), so the
    // names are read exactly. The API answers with another colour and notes, so the output shows it
    // is the model returned.
    [Test]
    public async Task Tag_set_on_a_tag_that_does_not_exist_creates_it_and_prints_it()
    {
        using var run = CliRun.Start();
        StubMissingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Altered(ApiJson.Tag("web"), ("colour", "#000000"), ("notes", "from-the-api")));
        string[] fields = ["tag", "colour", "notes", "trustRequirements"];

        var result = await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed", "--notes", "Web servers, all regions");

        CliAssert.Succeeded(result);
        var create = SingleChange(run, "POST", TagsPath);
        var body = create.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields));
            Assert.That(body.GetProperty("tag").GetString(), Is.EqualTo("web"));
            Assert.That(body.GetProperty("colour").GetString(), Is.EqualTo("#2f80ed"));
            Assert.That(body.GetProperty("notes").GetString(), Is.EqualTo("Web servers, all regions"));
            Assert.That(body.GetProperty("trustRequirements").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(body.GetProperty("trustRequirements").GetArrayLength(), Is.Zero);
            Assert.That(result.StdoutJson.GetProperty("tag").GetString(), Is.EqualTo("web"));
            Assert.That(result.StdoutJson.GetProperty("colour").GetString(), Is.EqualTo("#000000"));
            Assert.That(result.StdoutJson.GetProperty("notes").GetString(), Is.EqualTo("from-the-api"));
        });
    }

    // Creating a tag under either name would not be what was asked (spec/cli-surface.md "Command
    // options").
    [Test]
    public async Task Tag_set_with_name_on_a_tag_that_does_not_exist_exits_5_without_creating_a_tag()
    {
        using var run = CliRun.Start();
        StubMissingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("frontend"));

        var result = await run.RunAsync("tag", "set", "web", "--name", "frontend");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.RequestsTo("POST", TagsPath), Is.Empty);
    }

    // The 403 is reported as the API gave it.
    [Test]
    public async Task Tag_set_does_not_create_the_tag_when_the_update_fails_for_another_reason()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagPath("web"), json: ApiJson.Tag("web"));
        run.StubProblem("PATCH", TagPath("web"), 403, "Forbidden", "The token lacks the WriteTags scope.");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--notes", "Web servers");

        CliAssert.Failed(result, "forbidden");
        Assert.That(run.RequestsTo("POST", TagsPath), Is.Empty);
    }

    // spec/cli-surface.md "Details".
    [Test]
    public async Task Tag_set_without_a_change_flag_exits_2()
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "web");

        CliAssert.Rejected(run, await run.RunAsync("tag", "set", "web"), "invalid_argument");

        CliAssert.Succeeded(await run.RunAsync("tag", "set", "web", "--notes", "Web servers"));
        Assert.That(Keys(SingleChange(run, "PATCH", TagPath("web")).BodyJson), Is.EqualTo("Notes").IgnoreCase);
    }

    // Example 28. "uk only (old)" contains the name and is not a match (spec/cli-surface.md "Names
    // and IDs").
    [Test]
    public async Task Tag_set_with_trust_looks_up_the_requirement_and_patches_its_id()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)"), ApiJson.Trust(5, "uk only")));
        StubExistingTag(run, "prod");

        var result = await run.RunAsync("tag", "set", "prod", "--trust", "uk only");

        CliAssert.Succeeded(result);
        var patch = SingleChange(run, "PATCH", TagPath("prod"));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch.BodyJson), Is.EqualTo("TrustRequirements").IgnoreCase);
            Assert.That(string.Join(",", patch.BodyIds("TrustRequirements")), Is.EqualTo("5"));
        });
    }

    // Example 28, by ID.
    [Test]
    public async Task Tag_set_with_trust_ids_patches_them_without_a_lookup()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(5, "uk only")));
        StubExistingTag(run, "prod");

        var result = await run.RunAsync("tag", "set", "prod", "--trust-id", "5,9");

        CliAssert.Succeeded(result);
        var patch = SingleChange(run, "PATCH", TagPath("prod"));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(patch.BodyJson), Is.EqualTo("TrustRequirements").IgnoreCase);
            Assert.That(string.Join(",", patch.BodyIds("TrustRequirements").Order(StringComparer.Ordinal)), Is.EqualTo("5,9"));
            Assert.That(run.RequestsTo("GET", TrustsPath), Is.Empty);
        });
    }

    // The API refuses a trust requirement listed twice (portal TagCreateModelValidator and
    // TagPatchModelValidator, MustNotHaveDuplicates on TrustRequirements). Names match ignoring
    // case, so "UK Only" repeats "uk only" (spec/cli-surface.md "Names and IDs").
    [TestCase("--trust-id", "5,9,5", "5,9", "5,9")]
    [TestCase("--trust", "uk only,UK Only", "uk only", "5")]
    public async Task Tag_set_with_a_trust_requirement_given_twice_exits_2_without_a_request(string option, string repeated, string once, string ids)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(5, "uk only")));
        StubExistingTag(run, "prod");

        CliAssert.Rejected(run, await run.RunAsync("tag", "set", "prod", option, repeated));

        CliAssert.Succeeded(await run.RunAsync("tag", "set", "prod", option, once));
        var patch = SingleChange(run, "PATCH", TagPath("prod"));
        Assert.That(string.Join(",", patch.BodyIds("TrustRequirements").Order(StringComparer.Ordinal)), Is.EqualTo(ids));
    }

    // 2147483648 is one past the largest 32-bit integer (spec/cli-surface.md "ID checks").
    [TestCase("5,five")]
    [TestCase("2147483648")]
    public async Task Tag_set_rejects_a_trust_id_that_is_not_a_32_bit_integer(string badIds)
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "prod");

        CliAssert.Rejected(run, await run.RunAsync("tag", "set", "prod", "--trust-id", badIds), "invalid_argument");

        CliAssert.Succeeded(await run.RunAsync("tag", "set", "prod", "--trust-id", "5"));
        Assert.That(string.Join(",", SingleChange(run, "PATCH", TagPath("prod")).BodyIds("TrustRequirements")), Is.EqualTo("5"));
    }

    [Test]
    public async Task Tag_set_creates_a_tag_that_does_not_exist_with_the_trust_requirements_given()
    {
        using var run = CliRun.Start();
        StubMissingTag(run, "prod");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("prod"));

        var result = await run.RunAsync("tag", "set", "prod", "--trust-id", "5");

        CliAssert.Succeeded(result);
        var create = SingleChange(run, "POST", TagsPath);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(create.BodyJson, "tag").GetString(), Is.EqualTo("prod"));
            Assert.That(string.Join(",", create.BodyIds("trustRequirements")), Is.EqualTo("5"));
        });
    }

    // The error's candidates are empty (spec/cli-surface.md "Errors and exit codes").
    [Test]
    public async Task Tag_set_with_a_trust_requirement_no_requirement_has_exits_2_without_a_change()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)")));
        StubExistingTag(run, "prod");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("prod"));

        var result = await run.RunAsync("tag", "set", "prod", "--trust", "uk only");

        var error = CliAssert.Failed(result, "invalid_argument");
        var candidates = error.GetProperty("candidates");
        Assert.Multiple(() =>
        {
            Assert.That(candidates.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(candidates.EnumerateArray(), Is.Empty);
            Assert.That(run.Requests.Where(request => request.Method != "GET"), Is.Empty);
        });
    }

    [TestCase("Web")]
    [TestCase("../systems")]
    [TestCase(TagRefText)]
    public async Task Tag_set_rejects_a_name_outside_the_tag_rule(string badName)
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "web");

        CliAssert.Rejected(run, await run.RunAsync("tag", "set", badName, "--colour", "#2f80ed"), "invalid_argument");

        CliAssert.Succeeded(await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed"));
        Assert.That(Keys(SingleChange(run, "PATCH", TagPath("web")).BodyJson), Is.EqualTo("Colour").IgnoreCase);
    }

    // spec/cli-surface.md "Dry run".
    [Test]
    public async Task Tag_set_with_dry_run_on_a_tag_that_exists_prints_the_update_and_sends_no_change()
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed", "--dry-run");

        var request = DryRunOutput.RequestInTestOrg(result, "PATCH", TagPath("web"));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(JsonAssert.Property(request, "body")), Is.EqualTo("Colour").IgnoreCase);
            Assert.That(run.Requests.Where(sent => sent.Method != "GET"), Is.Empty);
        });
    }

    [Test]
    public async Task Tag_set_with_dry_run_on_a_tag_that_does_not_exist_prints_the_create_and_sends_no_change()
    {
        using var run = CliRun.Start();
        StubMissingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed", "--dry-run");

        var body = JsonAssert.Property(DryRunOutput.RequestInTestOrg(result, "POST", TagsPath), "body");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "tag").GetString(), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(body, "colour").GetString(), Is.EqualTo("#2f80ed"));
            Assert.That(JsonAssert.Property(body, "trustRequirements").GetArrayLength(), Is.Zero);
            Assert.That(run.Requests.Where(sent => sent.Method != "GET"), Is.Empty);
        });
    }

    // One bad name stops the whole command (spec/cli-surface.md "ID checks").
    [TestCase("Db")]
    [TestCase(TagRefText)]
    public async Task Tag_delete_rejects_a_name_outside_the_tag_rule(string badName)
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TagsPath, "tagsDeleted", 2);

        await CliAssert.RejectedThenAcceptedAsync(run, ["tag", "delete", "web", badName], ["tag", "delete", "web", "db"], "DELETE", TagsPath);
    }

    [Test]
    public async Task Tag_delete_with_dry_run_prints_the_delete_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TagsPath, "tagsDeleted", 1);

        var result = await run.RunAsync("tag", "delete", "web", "--dry-run");

        var request = DryRunOutput.RequestInTestOrg(result, "DELETE", TagsPath);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringList(JsonAssert.Property(JsonAssert.Property(request, "body"), "tags")), Is.EqualTo("web"));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    private static string TagPath(string tag) => TestData.OrgPath("tags/" + tag);

    private static void StubExistingTag(CliRun run, string tag, string? patched = null)
    {
        run.Stub("GET", TagsPath, json: ApiJson.Page(ApiJson.Tag(tag)));
        run.Stub("GET", TagPath(tag), json: ApiJson.Tag(tag));
        run.Stub("PATCH", TagPath(tag), json: patched ?? ApiJson.Tag(tag));
    }

    private static void StubMissingTag(CliRun run, string tag)
    {
        run.Stub("GET", TagsPath, json: ApiJson.Page());
        run.StubProblem("GET", TagPath(tag), 404, "Not Found", $"Tag {tag} does not exist.");
        run.StubProblem("PATCH", TagPath(tag), 404, "Not Found", $"Tag {tag} does not exist.");
    }

    // A create may follow a PATCH answered 404, which is how the API says the tag does not exist
    // (portal TagModifyHandler.GetResponseAsync); nothing may follow an update that succeeded
    // (spec/cli-surface.md "Calls per command").
    private static RecordedRequest SingleChange(CliRun run, string method, string path)
    {
        var changes = run.RequestsTo(method, path);
        Assert.That(changes, Has.Count.EqualTo(1), string.Join(Environment.NewLine, run.Calls()));

        if (method == "PATCH")
        {
            Assert.That(run.RequestsTo("POST", TagsPath), Is.Empty);
        }

        return changes[0];
    }
}
