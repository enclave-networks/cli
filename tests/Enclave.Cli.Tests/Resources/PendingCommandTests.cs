using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

public class PendingCommandTests
{
    private static readonly string PendingPath = TestData.OrgPath("unapproved-systems");

    private static readonly string[] ListFilterParameters = ["search", "enrolment_key", "sort"];

    // A list sends per_page equal to the limit, 100 by default, and sends a filter only when its
    // option is given.
    [Test]
    public async Task Pending_list_gets_the_unapproved_systems_with_per_page_100_and_no_filters_by_default()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        var result = await run.RunAsync("pending", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(PendingPath));
            Assert.That(request.Query.GetValueOrDefault("per_page"), Is.EqualTo("100"));
            Assert.That(request.Query.Keys.Intersect(ListFilterParameters), Is.Empty);
        });
    }

    [Test]
    public async Task Pending_list_prints_the_unapproved_systems_in_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page(ApiJson.PendingSystem("AAAAA"), ApiJson.PendingSystem("BBBBB")));

        var result = await run.RunAsync("pending", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var output = result.StdoutJson;
        var items = output.GetProperty("items");
        Assert.Multiple(() =>
        {
            Assert.That(items.GetArrayLength(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(items[0], "systemId").GetString(), Is.EqualTo("AAAAA"));
            Assert.That(JsonAssert.Property(items[1], "systemId").GetString(), Is.EqualTo("BBBBB"));
            Assert.That(output.GetProperty("total").GetInt32(), Is.EqualTo(2));
            Assert.That(output.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // The flag names follow the proposal's "Command options" table; the query parameter names are
    // the ones Enclave.Sdk.Api 1.0.4 sends (UnapprovedSystemsClient.BuildQueryString).
    [TestCase("--search", "laptop", "search", "laptop")]
    [TestCase("--key", "12", "enrolment_key", "12")]
    public async Task Pending_list_sends_each_filter_option_as_its_query_parameter(string option, string value, string parameter, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        var result = await run.RunAsync("pending", "list", option, value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(PendingPath));
            Assert.That(request.Query.GetValueOrDefault(parameter), Is.EqualTo(expected));
        });
    }

    // Enum values take the API's names, matched ignoring case (proposal "Options on every
    // command"). The names are UnapprovedSystemQuerySortMode's members.
    [TestCase("RecentlyEnrolled", "RecentlyEnrolled")]
    [TestCase("description", "Description")]
    [TestCase("ENROLMENTKEYUSED", "EnrolmentKeyUsed")]
    public async Task Pending_list_sort_sends_the_api_sort_name_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        var result = await run.RunAsync("pending", "list", "--sort", value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        Assert.That(run.SingleRequest().Query.GetValueOrDefault("sort"), Is.EqualTo(expected));
    }

    // RecentlyConnected is a sort for approved systems (SystemQuerySortMode) with no member in
    // UnapprovedSystemQuerySortMode, so a command that shares the system list's sort values fails
    // this test. "1" is UnapprovedSystemQuerySortMode's underlying value for Description, and the
    // option takes names only.
    [TestCase("RecentlyConnected")]
    [TestCase("Newest")]
    [TestCase("1")]
    public async Task Pending_list_with_a_sort_value_that_is_not_a_pending_sort_name_exits_2_without_a_request(string value)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        await AssertRejectedThenCorrectedAsync(
            run,
            ["pending", "list", "--sort", value],
            ["pending", "list", "--sort", "RecentlyEnrolled"],
            "GET",
            PendingPath);
    }

    // The unapproved systems list has no disabled state or DNS filter
    // (UnapprovedSystemsClient.GetSystemsAsync, Enclave.Sdk.Api 1.0.4), so these system list
    // options are unknown here. The correction drops the option.
    [TestCase("--include-disabled")]
    [TestCase("--dns-name", "web.internal")]
    public async Task Pending_list_rejects_the_system_list_options_it_lacks_with_exit_2_and_no_request(params string[] option)
    {
        using var run = CliRun.Start();
        run.Stub("GET", PendingPath, json: ApiJson.Page());

        await AssertRejectedThenCorrectedAsync(
            run,
            ["pending", "list", .. option],
            ["pending", "list"],
            "GET",
            PendingPath);
    }

    [Test]
    public async Task Pending_show_gets_the_unapproved_system_by_id_and_prints_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", $"{PendingPath}/ABCDE", json: ApiJson.PendingSystem("ABCDE", "laptop-01"));

        var result = await run.RunAsync("pending", "show", "ABCDE");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo($"{PendingPath}/ABCDE"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "systemId").GetString(), Is.EqualTo("ABCDE"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("laptop-01"));
        });
    }

    // A patch sends only the fields it sets (proposal "Create and update"). Enclave.Sdk.Api 1.0.4
    // keys the patch body by UnapprovedSystemPatchModel's property names (PatchClient.Set).
    [TestCase("--description", "laptop-02", "Description")]
    [TestCase("--notes", "awaiting IT", "Notes")]
    public async Task Pending_update_sends_the_flag_as_the_only_patch_field(string option, string value, string field)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/ABCDE", json: ApiJson.PendingSystem("ABCDE"));

        var result = await run.RunAsync("pending", "update", "ABCDE", option, value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo($"{PendingPath}/ABCDE"));
            Assert.That(body.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(JsonAssert.Property(body, field).GetString(), Is.EqualTo(value));
        });
    }

    [Test]
    public async Task Pending_update_set_tags_sends_the_whole_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/ABCDE", json: ApiJson.PendingSystem("ABCDE"));
        string[] expectedTags = ["laptops", "staff"];

        var result = await run.RunAsync("pending", "update", "ABCDE", "--set-tags", "laptops,staff");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(body.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "Tags")), Is.EqualTo(expectedTags));
        });
    }

    // The proposal ("Create and update"): `--set-tags ""` clears the list.
    [Test]
    public async Task Pending_update_set_tags_with_an_empty_value_sends_an_empty_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/ABCDE", json: ApiJson.PendingSystem("ABCDE"));

        var result = await run.RunAsync("pending", "update", "ABCDE", "--set-tags", string.Empty);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var tags = JsonAssert.Property(run.SingleRequest().BodyJson, "Tags");
        Assert.Multiple(() =>
        {
            Assert.That(tags.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(tags.GetArrayLength(), Is.Zero);
        });
    }

    // The output is the model the API returned, so the stub's description differs from every value
    // the command sends.
    [Test]
    public async Task Pending_update_sends_every_flag_in_one_patch_and_prints_the_updated_system()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/ABCDE", json: ApiJson.PendingSystem("ABCDE", "from-the-api"));
        string[] expectedTags = ["laptops"];

        var result = await run.RunAsync("pending", "update", "ABCDE", "--description", "laptop-02", "--notes", "awaiting IT", "--set-tags", "laptops");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(body.EnumerateObject().Count(), Is.EqualTo(3));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("laptop-02"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("awaiting IT"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "Tags")), Is.EqualTo(expectedTags));
            Assert.That(JsonAssert.Property(result.StdoutJson, "systemId").GetString(), Is.EqualTo("ABCDE"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("from-the-api"));
        });
    }

    // An update with nothing to change would send an empty patch; exiting 2 tells the caller its
    // flags were lost.
    [Test]
    public async Task Pending_update_without_a_field_to_change_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{PendingPath}/ABCDE", json: ApiJson.PendingSystem("ABCDE"));

        await AssertRejectedThenCorrectedAsync(
            run,
            ["pending", "update", "ABCDE"],
            ["pending", "update", "ABCDE", "--description", "laptop-02"],
            "PATCH",
            $"{PendingPath}/ABCDE");
    }

    // The proposal ("Several IDs"): a command that accepts several IDs makes the bulk call for one
    // ID too, so its output always has the bulk shape. Approve and decline both need --yes
    // (proposal "Confirmation").
    [TestCaseSource(nameof(BulkCommands))]
    public async Task Pending_bulk_command_with_one_id_sends_the_bulk_request_and_prints_requested_and_affected(
        string verb, string method, string path, string resultField)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: ApiJson.Bulk(resultField, 1));
        string[] ids = ["ABCDE"];

        var result = await run.RunAsync(["pending", verb, .. ids, "--yes"]);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(request.BodyJson, "systemIds")), Is.EqualTo(ids));
            Assert.That(result.StdoutJson.GetProperty("requested").GetInt32(), Is.EqualTo(1));
            Assert.That(result.StdoutJson.GetProperty("affected").GetInt32(), Is.EqualTo(1));
        });
    }

    // Bulk approve succeeds when some IDs were not approved (portal UnapprovedSystemsController.cs:
    // 170-177). The API here reports one of the two IDs as affected, and the output carries that
    // count.
    [TestCaseSource(nameof(BulkCommands))]
    public async Task Pending_bulk_command_with_two_ids_sends_both_in_one_request_and_prints_the_api_count_as_affected(
        string verb, string method, string path, string resultField)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: ApiJson.Bulk(resultField, 1));
        string[] ids = ["ABCDE", "FGHIJ"];

        var result = await run.RunAsync(["pending", verb, .. ids, "--yes"]);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(request.BodyJson, "systemIds")), Is.EqualTo(ids));
            Assert.That(result.StdoutJson.GetProperty("requested").GetInt32(), Is.EqualTo(2));
            Assert.That(result.StdoutJson.GetProperty("affected").GetInt32(), Is.EqualTo(1));
        });
    }

    // Each bulk route and its result field, from Enclave.Sdk.Api 1.0.4 UnapprovedSystemsClient
    // (ApproveSystemsAsync, DeclineSystems).
    private static IEnumerable<TestCaseData> BulkCommands()
    {
        yield return new TestCaseData("approve", "PUT", TestData.OrgPath("unapproved-systems/approve"), "systemsApproved").SetArgDisplayNames("pending approve");
        yield return new TestCaseData("decline", "DELETE", TestData.OrgPath("unapproved-systems"), "systemsDeclined").SetArgDisplayNames("pending decline");
    }

    // Parse errors exit 2 with invalid_argument and send nothing (proposal "Errors and exit
    // codes"), and an unknown command is a parse error, so a rejection alone does not show that the
    // command checked the input. The corrected command runs next in the same sandbox and must
    // succeed with exactly one request, the one the rejection withheld.
    private static async Task AssertRejectedThenCorrectedAsync(CliRun run, string[] rejected, string[] corrected, string method, string path)
    {
        var rejectedResult = await run.RunAsync(rejected);

        Assert.That(rejectedResult.ExitCode, Is.EqualTo(2), rejectedResult.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(rejectedResult.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(rejectedResult.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(run.Requests, Is.Empty);
        });

        var correctedResult = await run.RunAsync(corrected);

        Assert.That(correctedResult.ExitCode, Is.Zero, correctedResult.ToString());
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
        });
    }
}
