using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

[Category(TestCategory.Pending)]
public class SystemCommandTests
{
    private static readonly string SystemsPath = TestData.OrgPath("systems");

    private static readonly string[] ListFilterParameters = ["search", "enrolment_key", "include_disabled", "sort", "dns"];

    // A list sends per_page equal to the limit, 100 by default, and sends a filter only when its
    // option is given.
    [Test]
    public async Task System_list_gets_the_systems_with_per_page_100_and_no_filters_by_default()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(SystemsPath));
            Assert.That(request.Query.GetValueOrDefault("per_page"), Is.EqualTo("100"));
            Assert.That(request.Query.Keys.Intersect(ListFilterParameters), Is.Empty);
        });
    }

    [Test]
    public async Task System_list_prints_the_systems_in_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page(ApiJson.System("AAAAA"), ApiJson.System("BBBBB")));

        var result = await run.RunAsync("system", "list");

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
    // the ones Enclave.Sdk.Api 1.0.4 sends (SystemsClient.BuildQueryString).
    [TestCase("--search", "web", "search", "web")]
    [TestCase("--key", "12", "enrolment_key", "12")]
    [TestCase("--dns-name", "web.internal", "dns", "web.internal")]
    public async Task System_list_sends_each_filter_option_as_its_query_parameter(string option, string value, string parameter, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list", option, value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(SystemsPath));
            Assert.That(request.Query.GetValueOrDefault(parameter), Is.EqualTo(expected));
        });
    }

    // Enclave.Sdk.Api 1.0.4 formats the flag with bool.ToString() (SystemsClient.BuildQueryString),
    // which gives "True".
    [Test]
    public async Task System_list_include_disabled_sends_include_disabled_true()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list", "--include-disabled");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        Assert.That(run.SingleRequest().Query.GetValueOrDefault("include_disabled"), Is.EqualTo("True"));
    }

    // Enum values take the API's names, matched ignoring case (proposal "Options on every
    // command"). The names are SystemQuerySortMode's members (Enclave.Configuration.Data).
    [TestCase("RecentlyEnrolled", "RecentlyEnrolled")]
    [TestCase("recentlyconnected", "RecentlyConnected")]
    [TestCase("DESCRIPTION", "Description")]
    [TestCase("descriptionOrHostname", "DescriptionOrHostname")]
    [TestCase("enrolmentkeyused", "EnrolmentKeyUsed")]
    public async Task System_list_sort_sends_the_api_sort_name_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        var result = await run.RunAsync("system", "list", "--sort", value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        Assert.That(run.SingleRequest().Query.GetValueOrDefault("sort"), Is.EqualTo(expected));
    }

    // "1" is SystemQuerySortMode's underlying value for RecentlyConnected. The option takes names
    // only, so a parser that accepts numeric enum values fails this test.
    [TestCase("Newest")]
    [TestCase("1")]
    public async Task System_list_with_a_sort_value_that_is_not_an_api_sort_name_exits_2_without_a_request(string value)
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--sort", value],
            ["system", "list", "--sort", "RecentlyConnected"],
            "GET",
            SystemsPath);
    }

    // A key ID is an integer (proposal "ID checks").
    [Test]
    public async Task System_list_with_a_key_that_is_not_an_integer_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", SystemsPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "list", "--key", "abc"],
            ["system", "list", "--key", "12"],
            "GET",
            SystemsPath);
    }

    [Test]
    public async Task System_show_gets_the_system_by_id_and_prints_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE", "web-01"));

        var result = await run.RunAsync("system", "show", "ABCDE");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo($"{SystemsPath}/ABCDE"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "systemId").GetString(), Is.EqualTo("ABCDE"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("web-01"));
        });
    }

    // A patch sends only the fields it sets; the API leaves absent fields as they are (proposal
    // "Create and update"). Enclave.Sdk.Api 1.0.4 keys the patch body by SystemPatchModel's
    // property names (PatchClient.Set).
    [TestCase("--description", "web-02", "Description")]
    [TestCase("--notes", "rack 4", "Notes")]
    public async Task System_update_sends_the_flag_as_the_only_patch_field(string option, string value, string field)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE", "web-02"));

        var result = await run.RunAsync("system", "update", "ABCDE", option, value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo($"{SystemsPath}/ABCDE"));
            Assert.That(body.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(JsonAssert.Property(body, field).GetString(), Is.EqualTo(value));
        });
    }

    [Test]
    public async Task System_update_set_tags_sends_the_whole_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE"));
        string[] expectedTags = ["web", "prod"];

        var result = await run.RunAsync("system", "update", "ABCDE", "--set-tags", "web,prod");

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
    public async Task System_update_set_tags_with_an_empty_value_sends_an_empty_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "update", "ABCDE", "--set-tags", string.Empty);

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
    public async Task System_update_sends_every_flag_in_one_patch_and_prints_the_updated_system()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE", "from-the-api"));
        string[] expectedTags = ["web"];

        var result = await run.RunAsync("system", "update", "ABCDE", "--description", "web-02", "--notes", "rack 4", "--set-tags", "web");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(body.EnumerateObject().Count(), Is.EqualTo(3));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("web-02"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("rack 4"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "Tags")), Is.EqualTo(expectedTags));
            Assert.That(JsonAssert.Property(result.StdoutJson, "systemId").GetString(), Is.EqualTo("ABCDE"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("from-the-api"));
        });
    }

    // An update with nothing to change would send an empty patch; exiting 2 tells the caller its
    // flags were lost.
    [Test]
    public async Task System_update_without_a_field_to_change_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{SystemsPath}/ABCDE", json: ApiJson.System("ABCDE"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "update", "ABCDE"],
            ["system", "update", "ABCDE", "--description", "web-02"],
            "PATCH",
            $"{SystemsPath}/ABCDE");
    }

    // The proposal ("Several IDs"): a command that accepts several IDs makes the bulk call for one
    // ID too, so its output always has the bulk shape.
    [TestCaseSource(nameof(BulkCommands))]
    public async Task System_bulk_command_with_one_id_sends_the_bulk_request_and_prints_requested_and_affected(
        string verb, bool needsYes, string method, string path, string resultField)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: ApiJson.Bulk(resultField, 1));
        string[] ids = ["ABCDE"];

        var result = await run.RunAsync(["system", verb, .. ids, .. Args.YesIf(needsYes)]);

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

    // The API reports one of the two IDs as affected, so the output shows affected is the API's
    // count and requested is the number of IDs given.
    [TestCaseSource(nameof(BulkCommands))]
    public async Task System_bulk_command_with_two_ids_sends_both_in_one_request_and_prints_the_api_count_as_affected(
        string verb, bool needsYes, string method, string path, string resultField)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: ApiJson.Bulk(resultField, 1));
        string[] ids = ["ABCDE", "FGHIJ"];

        var result = await run.RunAsync(["system", verb, .. ids, .. Args.YesIf(needsYes)]);

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

    // The API's timed enable has no bulk form (proposal "Several IDs"), so --until puts to the
    // system's own enable-until route and prints the system it returns. The expiry action defaults
    // to Disable: AutoExpireModel gives ExpiryAction no default of its own, Disable is the enum's
    // default value (Enclave.Configuration.Data.Enums.ExpiryAction), and Disable loses nothing.
    [Test]
    public async Task System_enable_until_an_iso_8601_time_puts_enable_until_with_that_time_in_utc_and_the_disable_action()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{SystemsPath}/ABCDE/enable-until", json: ApiJson.System("ABCDE", "web-01"));

        var result = await run.RunAsync("system", "enable", "ABCDE", "--until", "2026-12-01T10:00:00Z");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo($"{SystemsPath}/ABCDE/enable-until"));
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Disable"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "systemId").GetString(), Is.EqualTo("ABCDE"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("web-01"));
        });
    }

    // AGENTS.md "CLI contract": times are ISO 8601 UTC. A time given with an offset names the same
    // instant, sent in UTC.
    [Test]
    public async Task System_enable_until_a_time_with_an_offset_sends_the_same_instant_in_utc()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{SystemsPath}/ABCDE/enable-until", json: ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "enable", "ABCDE", "--until", "2026-12-01T12:00:00+02:00");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var expiry = JsonRead.ExpiryDateTime(run.SingleRequest().BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    // AGENTS.md "CLI contract": --until accepts a relative time such as 2h or 7d. The expected
    // window spans the run, with a second either side for a CLI that rounds to whole seconds.
    [TestCase("2h", 2 * 60)]
    [TestCase("7d", 7 * 24 * 60)]
    public async Task System_enable_until_a_relative_time_sends_that_long_from_now_in_utc(string until, int minutes)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{SystemsPath}/ABCDE/enable-until", json: ApiJson.System("ABCDE"));
        var span = TimeSpan.FromMinutes(minutes);

        var before = DateTimeOffset.UtcNow;
        var result = await run.RunAsync("system", "enable", "ABCDE", "--until", until);
        var after = DateTimeOffset.UtcNow;

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo($"{SystemsPath}/ABCDE/enable-until"));
            Assert.That(expiry, Is.InRange(before + span - TimeSpan.FromSeconds(1), after + span + TimeSpan.FromSeconds(1)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    // Enum values take the API's names, matched ignoring case. Delete schedules a deletion, so
    // every case passes --yes (proposal "Confirmation").
    [TestCase("Delete", "Delete")]
    [TestCase("delete", "Delete")]
    [TestCase("DISABLE", "Disable")]
    public async Task System_enable_until_sends_the_expiry_action_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{SystemsPath}/ABCDE/enable-until", json: ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "enable", "ABCDE", "--until", "2026-12-01T10:00:00Z", "--expiry-action", value, "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        Assert.That(JsonAssert.Property(run.SingleRequest().BodyJson, "expiryAction").GetString(), Is.EqualTo(expected));
    }

    // --until takes one ID (proposal "Several IDs"). --expiry-action has no effect without --until,
    // and accepting it alone would leave the caller believing an expiry was set. Both routes are
    // stubbed so a request the CLI sends by mistake gets a success response and shows up here as a
    // request. Each case's correction is a timed enable of one system.
    [TestCaseSource(nameof(InvalidEnableArguments))]
    public async Task System_enable_with_invalid_timed_enable_arguments_exits_2_without_a_request(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{SystemsPath}/ABCDE/enable-until", json: ApiJson.System("ABCDE"));
        run.Stub("PUT", $"{SystemsPath}/enable", json: ApiJson.Bulk("systemsUpdated", 1));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "enable", .. rejected],
            ["system", "enable", .. corrected],
            "PUT",
            $"{SystemsPath}/ABCDE/enable-until");
    }

    // Each bulk route and its result field, from Enclave.Sdk.Api 1.0.4 SystemsClient
    // (BulkEnableAsync, BulkDisableAsync, RevokeSystemsAsync). Revoke needs --yes (proposal
    // "Confirmation").
    private static IEnumerable<TestCaseData> BulkCommands()
    {
        yield return new TestCaseData("enable", false, "PUT", TestData.OrgPath("systems/enable"), "systemsUpdated").SetArgDisplayNames("system enable");
        yield return new TestCaseData("disable", false, "PUT", TestData.OrgPath("systems/disable"), "systemsUpdated").SetArgDisplayNames("system disable");
        yield return new TestCaseData("revoke", true, "DELETE", TestData.OrgPath("systems"), "systemsRevoked").SetArgDisplayNames("system revoke");
    }

    private static IEnumerable<TestCaseData> InvalidEnableArguments()
    {
        yield return Correction(
            "until is not a time",
            ["ABCDE", "--until", "tomorrow"],
            ["ABCDE", "--until", "2026-12-01T10:00:00Z"]);
        yield return Correction(
            "unknown expiry action",
            ["ABCDE", "--until", "2026-12-01T10:00:00Z", "--expiry-action", "Archive"],
            ["ABCDE", "--until", "2026-12-01T10:00:00Z", "--expiry-action", "Disable"]);
        yield return Correction(
            "expiry action without until",
            ["ABCDE", "--expiry-action", "Disable"],
            ["ABCDE", "--until", "2026-12-01T10:00:00Z", "--expiry-action", "Disable"]);
        yield return Correction(
            "until with two ids",
            ["ABCDE", "FGHIJ", "--until", "2026-12-01T10:00:00Z"],
            ["ABCDE", "--until", "2026-12-01T10:00:00Z"]);
    }

    private static TestCaseData Correction(string name, string[] rejected, string[] corrected) =>
        new TestCaseData(rejected, corrected).SetArgDisplayNames(name);
}
