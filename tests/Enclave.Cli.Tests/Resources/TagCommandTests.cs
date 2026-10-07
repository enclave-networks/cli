using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// The tag commands (proposed-cli-surface.md "Commands", "Command options", "Names and IDs", "ID
// checks", "Details"). Tags are given by name only, and a tag name goes into the URL path of show
// and set, so each is checked against the API's tag rule, ^([a-z0-9]+[-.])*[a-z0-9]+$ (portal
// TagValidationExtensions.cs:13), before any call. An argument starting "ref:" is not a tag
// reference; it fails that rule like any other name.
//
// tag set updates a tag that exists and creates one that does not; the API has separate calls
// ("Command options"). It may read the tag to choose ("Dry run" names that read), so the tag set
// tests serve the tag's GET as well as its PATCH, and require exactly one change whatever reads
// come first.

/// <summary>
/// Tests for the tag commands: list, show, set and delete.
/// </summary>
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

    // --filter is sent as typed ("Filters").
    [Test]
    public async Task Tag_list_sends_the_filter_text_as_the_search()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagsPath, json: ApiJson.Page(ApiJson.Tag("web")));

        var result = await run.RunAsync("tag", "list", "--filter", "web");

        CliAssert.List(result, "tag");
        Assert.That(run.SingleRequest().QueryValue("search")?.Split(' ', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(WebFilter));
    }

    // TagQuerySortOrder has Alphabetical, RecentlyUsed and ReferencedSystems (portal
    // Enclave.Configuration.Data/Modules/Tags/Enums/TagQuerySortOrder.cs), and option values are
    // lower-case and hyphenated, matched ignoring case ("Options on every command").
    [TestCase("alphabetical", "Alphabetical")]
    [TestCase("recently-used", "RecentlyUsed")]
    [TestCase("referenced-systems", "ReferencedSystems")]
    [TestCase("Recently-Used", "RecentlyUsed")]
    public async Task Tag_list_sends_the_sort_order_the_sort_option_names(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagsPath, json: ApiJson.Page(ApiJson.Tag("web")));

        var result = await run.RunAsync("tag", "list", "--sort", value);

        CliAssert.List(result, "tag");
        Assert.That(run.SingleRequest().QueryValue("sort"), Is.EqualTo(expected));
    }

    // recently-created is a sort order of policies and trust requirements; the tag list does not
    // have it.
    [Test]
    public async Task Tag_list_rejects_a_sort_order_the_tag_list_does_not_have()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagsPath, json: ApiJson.Page(ApiJson.Tag("web")));

        await CliAssert.RejectedThenAcceptedAsync(run, ["tag", "list", "--sort", "recently-created"], ["tag", "list", "--sort", "recently-used"], "GET", TagsPath);
    }

    // An option a command does not take is unknown to it and exits 2 ("Details"); --dry-run belongs
    // to commands that change something.
    [TestCase("list")]
    [TestCase("show")]
    public async Task Tag_read_commands_reject_dry_run(string verb)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagsPath, json: ApiJson.Page(ApiJson.Tag("web")));
        run.Stub("GET", TagPath("web"), json: ApiJson.Tag("web"));
        string[] corrected = verb == "list" ? ["tag", "list"] : ["tag", "show", "web"];

        await CliAssert.RejectedThenAcceptedAsync(run, [.. corrected, "--dry-run"], corrected, "GET", verb == "list" ? TagsPath : TagPath("web"));
    }

    [Test]
    public async Task Tag_show_gets_the_tag_by_name_and_prints_its_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagPath("web-servers"), json: ApiJson.Tag("web-servers"));

        var result = await run.RunAsync("tag", "show", "web-servers");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(TagPath("web-servers")));
            Assert.That(JsonAssert.Property(result.StdoutJson, "tag").GetString(), Is.EqualTo("web-servers"));
        });
    }

    // Single-item commands exit 5 for an unknown name ("Several IDs").
    [Test]
    public async Task Tag_show_with_an_unknown_tag_exits_5()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", TagPath("web"), 404, "Not Found", "Tag web does not exist.");

        var result = await run.RunAsync("tag", "show", "web");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.SingleRequest().Path, Is.EqualTo(TagPath("web")));
    }

    // Enclave.Sdk.Api 1.0.4 puts the tag into the URL path unescaped (TagsClient.GetAsync and
    // Update), so a name outside the tag rule never reaches it ("ID checks").
    [TestCase("Web", "web")]
    [TestCase("web_servers", "web-servers")]
    [TestCase("../systems", "web")]
    [TestCase(TagRefText, "web")]
    public async Task Tag_show_rejects_a_name_outside_the_tag_rule(string badName, string goodName)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TagPath(goodName), json: ApiJson.Tag(goodName));

        await CliAssert.RejectedThenAcceptedAsync(run, ["tag", "show", badName], ["tag", "show", goodName], "GET", TagPath(goodName));
    }

    // The patch holds only the fields given ("Create and update"). Enclave.Sdk.Api keys a PATCH
    // body by the C# property name (PatchClient.Set, version 1.0.4).
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

    // Example 55. --name renames the tag: TagPatchModel.Tag holds the new name (portal
    // Enclave.Api/Modules/SystemManagement/Tags/Models/TagPatchModel.cs).
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
    // TagModifyHandler.GetResponseAsync:58), so tag set creates it ("Command options", "Calls per
    // command"). A create sends an empty list for a list flag left out, so the trust requirements go
    // as [] ("Details").
    [Test]
    public async Task Tag_set_on_a_tag_that_does_not_exist_creates_it_and_prints_it()
    {
        using var run = CliRun.Start();
        StubMissingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed", "--notes", "Web servers, all regions");

        CliAssert.Succeeded(result);
        var create = SingleChange(run, "POST", TagsPath);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(create.BodyJson, "tag").GetString(), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(create.BodyJson, "colour").GetString(), Is.EqualTo("#2f80ed"));
            Assert.That(JsonAssert.Property(create.BodyJson, "notes").GetString(), Is.EqualTo("Web servers, all regions"));
            Assert.That(create.BodyIds("trustRequirements"), Is.Empty);
            Assert.That(JsonAssert.Property(result.StdoutJson, "tag").GetString(), Is.EqualTo("web"));
        });
    }

    // --name renames, so the tag must exist ("Command options"): a rename of an unknown tag is the
    // single-item not found, and creating a tag under either name would not be what was asked.
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

    // Only a tag that does not exist is created; any other failure of the update is reported as it
    // is (here 403, exit 4, "Errors and exit codes").
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

    // An update with no change flag exits 2, tag set included ("Details").
    [Test]
    public async Task Tag_set_without_a_change_flag_exits_2()
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "web");

        CliAssert.Rejected(run, await run.RunAsync("tag", "set", "web"), "invalid_argument");

        CliAssert.Succeeded(await run.RunAsync("tag", "set", "web", "--notes", "Web servers"));
        Assert.That(Keys(SingleChange(run, "PATCH", TagPath("web")).BodyJson), Is.EqualTo("Notes").IgnoreCase);
    }

    // Example 28. --trust takes trust requirement descriptions, looked up like any name ("Names and
    // IDs"), and replaces the tag's trust requirements. "uk only (old)" contains the name and is
    // not a match.
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

    // Example 28, by ID: --trust-id makes no lookup ("Names and IDs").
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

    // Trust requirement IDs are 32-bit integers, and every ID is checked before any call ("ID
    // checks", "Details").
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

    // No match exits 2 invalid_argument, and the error's candidates hold nothing ("Errors and exit
    // codes").
    [Test]
    public async Task Tag_set_with_a_trust_requirement_no_requirement_has_exits_2_without_a_change()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)")));
        StubExistingTag(run, "prod");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("prod"));

        var result = await run.RunAsync("tag", "set", "prod", "--trust", "uk only");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(error.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array ? candidates.GetArrayLength() : 0, Is.Zero);
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

    // The read tag set makes to choose between update and create still runs under --dry-run, and
    // the change it chose is printed and not sent ("Dry run").
    [Test]
    [Category(TestCategory.Pending)]
    public async Task Tag_set_with_dry_run_on_a_tag_that_exists_prints_the_update_and_sends_no_change()
    {
        using var run = CliRun.Start();
        StubExistingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed", "--dry-run");

        var request = DryRunRequest(result, "PATCH", TagPath("web"));
        Assert.Multiple(() =>
        {
            Assert.That(Keys(JsonAssert.Property(request, "body")), Is.EqualTo("Colour").IgnoreCase);
            Assert.That(run.Requests.Where(sent => sent.Method != "GET"), Is.Empty);
        });
    }

    [Test]
    [Category(TestCategory.Pending)]
    public async Task Tag_set_with_dry_run_on_a_tag_that_does_not_exist_prints_the_create_and_sends_no_change()
    {
        using var run = CliRun.Start();
        StubMissingTag(run, "web");
        run.Stub("POST", TagsPath, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--colour", "#2f80ed", "--dry-run");

        var body = JsonAssert.Property(DryRunRequest(result, "POST", TagsPath), "body");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "tag").GetString(), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(body, "colour").GetString(), Is.EqualTo("#2f80ed"));
            Assert.That(JsonAssert.Property(body, "trustRequirements").GetArrayLength(), Is.Zero);
            Assert.That(run.Requests.Where(sent => sent.Method != "GET"), Is.Empty);
        });
    }

    // A command that takes several items always makes the bulk call, and the tag bulk delete takes
    // names in a "tags" array (Enclave.Sdk.Api 1.0.4, TagsClient.DeleteTagsAsync). affected below
    // requested is still success ("Several IDs").
    [Test]
    public async Task Tag_delete_sends_every_tag_named_in_one_bulk_call()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TagsPath, "tagsDeleted", 1);

        var result = await run.RunAsync("tag", "delete", "web", "db");

        CliAssert.Bulk(result, 2, 1);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TagsPath));
            Assert.That(Names(request), Is.EqualTo("db,web"));
        });
    }

    // One bad name stops the whole command before any call ("ID checks").
    [TestCase("Db")]
    [TestCase(TagRefText)]
    public async Task Tag_delete_rejects_a_name_outside_the_tag_rule(string badName)
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TagsPath, "tagsDeleted", 2);

        await CliAssert.RejectedThenAcceptedAsync(run, ["tag", "delete", "web", badName], ["tag", "delete", "web", "db"], "DELETE", TagsPath);
    }

    // A tag list's items are identified by their name (TagSummaryModel.Tag), which the bulk delete
    // takes ("Several IDs").
    [Test]
    public async Task Tag_delete_reads_a_tag_list_from_stdin()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TagsPath, "tagsDeleted", 2);
        run.StdinText = CliList.WithIds("tag", "web", "db");

        var result = await run.RunAsync("tag", "delete", "-");

        CliAssert.Bulk(result, 2, 2);
        Assert.That(Names(run.SingleRequest()), Is.EqualTo("db,web"));
    }

    [Test]
    [Category(TestCategory.Pending)]
    public async Task Tag_delete_with_dry_run_prints_the_delete_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.StubBulk("DELETE", TagsPath, "tagsDeleted", 1);

        var result = await run.RunAsync("tag", "delete", "web", "--dry-run");

        var request = DryRunRequest(result, "DELETE", TagsPath);
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

    // tag set makes one change, the update or the create, whatever reads come first ("Calls per
    // command"). An update answered 404 is how the API says the tag does not exist (portal
    // TagModifyHandler.GetResponseAsync:58), so a create may follow a PATCH, and nothing may follow
    // an update that succeeded.
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

    // The output form "Dry run" gives: { "dryRun": true, "org": { id, name }, "requests": [...] },
    // with requests a list holding the one change here. CliRun names the organisation by ID
    // (ENCLAVE_ORG_ID), and then no lookup gives its name, which is null.
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

    // Sorted, so a test can state the exact set of fields an update sends.
    private static string Keys(JsonElement body) => string.Join("; ", JsonRead.PropertyNames(body).Order(StringComparer.OrdinalIgnoreCase));

    // Sorted, since the order of a bulk call's names does not change what it does.
    private static string Names(RecordedRequest request) =>
        string.Join(",", request.BodyIds("tags").Order(StringComparer.Ordinal));
}
