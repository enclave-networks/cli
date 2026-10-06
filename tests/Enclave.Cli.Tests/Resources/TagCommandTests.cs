using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

[Category(TestCategory.Pending)]
public class TagCommandTests
{
    [Test]
    public async Task Tag_list_gets_one_page_of_tags_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags"), json: ApiJson.Page(ApiJson.Tag("web"), ApiJson.Tag("db")));

        var result = await run.RunAsync("tag", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags")));
            Assert.That(request.QueryValue("per_page"), Is.EqualTo("100"));
            Assert.That(request.QueryValue("search"), Is.Null);
            Assert.That(request.QueryValue("sort"), Is.Null);
            Assert.That(JsonRead.StringFieldList(JsonAssert.Property(output, "items"), "tag"), Is.EqualTo("web,db"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task Tag_list_sends_the_search_option_as_the_search_query_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags"), json: ApiJson.Page(ApiJson.Tag("web")));

        var result = await run.RunAsync("tag", "list", "--search", "we");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags")));
            Assert.That(request.QueryValue("search"), Is.EqualTo("we"));
        });
    }

    // Enum option values take the API's names, matched ignoring case, and the API receives its
    // own spelling (proposal, "Options on every command"). TagQuerySortOrder has the values
    // Alphabetical, RecentlyUsed and ReferencedSystems (Enclave.Configuration.Data,
    // Modules/Tags/Enums).
    [TestCase("Alphabetical", "Alphabetical")]
    [TestCase("alphabetical", "Alphabetical")]
    [TestCase("RecentlyUsed", "RecentlyUsed")]
    [TestCase("recentlyused", "RecentlyUsed")]
    [TestCase("ReferencedSystems", "ReferencedSystems")]
    [TestCase("REFERENCEDSYSTEMS", "ReferencedSystems")]
    public async Task Tag_list_sends_the_sort_order_named_by_the_sort_option_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags"), json: ApiJson.Page(ApiJson.Tag("web")));

        var result = await run.RunAsync("tag", "list", "--sort", value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags")));
            Assert.That(request.QueryValue("sort"), Is.EqualTo(expected));
        });
    }

    // RecentlyCreated is a sort order of policies and trust requirements; the tag list does not
    // have it.
    [Test]
    public async Task Tag_list_rejects_a_sort_order_the_api_does_not_have_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags"), json: ApiJson.Page(ApiJson.Tag("web")));

        var result = await run.RunAsync("tag", "list", "--sort", "RecentlyCreated");

        CliAssert.Rejected(run, result);
        var request = await CliAssert.AcceptedAsync(run, "GET", TestData.OrgPath("tags"), "tag", "list", "--sort", "RecentlyUsed");
        Assert.That(request.QueryValue("sort"), Is.EqualTo("RecentlyUsed"));
    }

    // A tag is identified by its name, which is what tag show, update and delete take, so -o id
    // prints names and `tag list -o id | tag delete - --yes` works.
    [Test]
    public async Task Tag_list_with_output_id_prints_one_tag_name_per_line()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags"), json: ApiJson.Page(ApiJson.Tag("web"), ApiJson.Tag("db")));

        var result = await run.RunAsync("tag", "list", "-o", "id");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags")));
            Assert.That(string.Join(",", result.StdoutLines), Is.EqualTo("web,db"));
        });
    }

    [Test]
    public async Task Tag_show_gets_the_tag_by_name_and_prints_its_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags/web-servers"), json: ApiJson.Tag("web-servers"));

        var result = await run.RunAsync("tag", "show", "web-servers");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags/web-servers")));
            Assert.That(JsonAssert.Property(result.StdoutJson, "tag").GetString(), Is.EqualTo("web-servers"));
        });
    }

    // The flags set the TagCreateModel fields of the same name (proposal, "Shape and naming").
    [Test]
    public async Task Tag_create_posts_the_tag_colour_and_notes_and_prints_the_created_tag()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("tags"), json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "create", "web", "--colour", "#3a7bd5", "--notes", "Web servers");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags")));
            Assert.That(JsonAssert.Property(body, "tag").GetString(), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(body, "colour").GetString(), Is.EqualTo("#3a7bd5"));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("Web servers"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "tag").GetString(), Is.EqualTo("web"));
        });
    }

    // --name renames the tag: TagPatchModel.Tag holds the new name (portal
    // Enclave.Api/Modules/SystemManagement/Tags/Models/TagPatchModel.cs), and the flag is called
    // --name because the positional argument names the tag being changed.
    [Test]
    public async Task Tag_update_patches_the_new_name_colour_and_notes_on_the_named_tag()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("tags/web"), json: ApiJson.Tag("web-servers"));

        var result = await run.RunAsync("tag", "update", "web", "--name", "web-servers", "--colour", "#000000", "--notes", "Renamed");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags/web")));
            Assert.That(JsonAssert.Property(body, "Tag").GetString(), Is.EqualTo("web-servers"));
            Assert.That(JsonAssert.Property(body, "Colour").GetString(), Is.EqualTo("#000000"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("Renamed"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "tag").GetString(), Is.EqualTo("web-servers"));
        });
    }

    // A patch sets the fields present and leaves absent fields as they are (proposal, "Create and
    // update"). A Tag field the caller did not ask for would rename the tag.
    [Test]
    public async Task Tag_update_sends_only_the_fields_whose_options_are_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("tags/web"), json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "update", "web", "--colour", "#000000");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("Colour").IgnoreCase);
    }

    // tag update takes --name, --colour and --notes (proposal, "Command options"); an update with
    // none of them has nothing to send.
    [Test]
    public async Task Tag_update_without_any_field_option_exits_2_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("tags/web"), json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "update", "web");

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "PATCH", TestData.OrgPath("tags/web"), "tag", "update", "web", "--notes", "Reviewed");
    }

    // A command that accepts several IDs always makes the bulk call, also for one ID (proposal,
    // "Several IDs"). The tag bulk delete takes names in a "tags" array (Enclave.Sdk.Api 1.0.4,
    // TagsClient.DeleteTagsAsync).
    [Test]
    public async Task Tag_delete_with_yes_sends_one_name_to_the_bulk_delete()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("tags"), json: ApiJson.Bulk("tagsDeleted", 1));

        var result = await run.RunAsync("tag", "delete", "web", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags")));
            Assert.That(JsonRead.StringList(JsonAssert.Property(request.BodyJson, "tags")), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(1));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(1));
        });
    }

    // The API reports one tag deleted of two, so the output shows the difference and the exit code
    // stays 0 (proposal, "Several IDs").
    [Test]
    public async Task Tag_delete_with_yes_sends_every_name_in_one_bulk_delete()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("tags"), json: ApiJson.Bulk("tagsDeleted", 1));

        var result = await run.RunAsync("tag", "delete", "web", "db", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("tags")));
            Assert.That(JsonRead.StringList(JsonAssert.Property(request.BodyJson, "tags")), Is.EqualTo("web,db"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(1));
        });
    }

    // delete cannot be undone, so it needs --yes (proposal, "Confirmation").
    [Test]
    public async Task Tag_delete_without_yes_exits_6_naming_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("tags"), json: ApiJson.Bulk("tagsDeleted", 1));

        var result = await run.RunAsync("tag", "delete", "web");

        CliAssert.Rejected(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    // --dry-run prints the request Enclave.Sdk.Api would send and sends nothing (proposal, "Dry
    // run").
    [TestCase("tag create web --dry-run", "POST", "tags")]
    [TestCase("tag update web --notes x --dry-run", "PATCH", "tags/web")]
    [TestCase("tag delete web --dry-run", "DELETE", "tags")]
    public async Task Tag_change_commands_with_dry_run_print_the_request_and_send_nothing(string commandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();

        var result = await run.RunAsync(commandLine.Split(' '));

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var output = result.StdoutJson;
        var request = JsonAssert.Property(output, "request");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo(method));
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(TestData.OrgPath(path)));
        });
    }

    // A tag goes into the URL path of show and update, and Enclave.Sdk.Api 1.0.4 does not escape
    // it, so every tag is checked against the API's tag rule before any call (proposal, "ID
    // checks"; portal TagValidationExtensions.cs:13). The same command with a valid tag is then
    // sent.
    [TestCase("tag show Web", "tag show web", "GET", "tags/web")]
    [TestCase("tag show web_servers", "tag show web-servers", "GET", "tags/web-servers")]
    [TestCase("tag show ../systems", "tag show web", "GET", "tags/web")]
    [TestCase("tag update ../systems --notes x", "tag update web --notes x", "PATCH", "tags/web")]
    [TestCase("tag delete web Db --yes", "tag delete web db --yes", "DELETE", "tags")]
    [TestCase("tag delete web db. --yes", "tag delete web db --yes", "DELETE", "tags")]
    public async Task Tag_commands_reject_a_name_outside_the_api_tag_rule_without_sending_a_request(
        string commandLine, string acceptedCommandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags/web"), json: ApiJson.Tag("web"));
        run.Stub("GET", TestData.OrgPath("tags/web-servers"), json: ApiJson.Tag("web-servers"));
        run.Stub("PATCH", TestData.OrgPath("tags/web"), json: ApiJson.Tag("web"));
        run.Stub("DELETE", TestData.OrgPath("tags"), json: ApiJson.Bulk("tagsDeleted", 2));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, method, TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }

    // Single-ID commands exit 5 for an unknown ID (proposal, "Several IDs").
    [TestCase("tag show web", "GET")]
    [TestCase("tag update web --notes x", "PATCH")]
    public async Task Tag_single_id_commands_exit_5_when_the_api_reports_not_found(string commandLine, string method)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.StubProblem(method, TestData.OrgPath("tags/web"), 404, "Not Found", "Tag web does not exist.");

        var result = await run.RunAsync(commandLine.Split(' '));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(5), result.Stderr);
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("not_found"));
            Assert.That(run.Requests, Has.Count.EqualTo(1));
        });
    }

    // --dry-run and --yes exist only on commands that change something; elsewhere they are unknown
    // options, which exit 2. The same command without the option is then sent.
    [TestCase("tag list --yes", "tag list", "tags")]
    [TestCase("tag list --dry-run", "tag list", "tags")]
    [TestCase("tag show web --dry-run", "tag show web", "tags/web")]
    public async Task Tag_read_commands_reject_change_options_without_sending_a_request(string commandLine, string acceptedCommandLine, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("tags"), json: ApiJson.Page(ApiJson.Tag("web")));
        run.Stub("GET", TestData.OrgPath("tags/web"), json: ApiJson.Tag("web"));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "GET", TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }
}
