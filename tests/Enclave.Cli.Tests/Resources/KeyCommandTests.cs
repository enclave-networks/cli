using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

[Category(TestCategory.Pending)]
public class KeyCommandTests
{
    private static readonly string KeysPath = TestData.OrgPath("enrolment-keys");

    private static readonly string[] ListFilterParameters = ["search", "include_disabled", "sort"];

    // A list sends per_page equal to the limit, 100 by default, and sends a filter only when its
    // option is given.
    [Test]
    public async Task Key_list_gets_the_enrolment_keys_with_per_page_100_and_no_filters_by_default()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var result = await run.RunAsync("key", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(KeysPath));
            Assert.That(request.Query.GetValueOrDefault("per_page"), Is.EqualTo("100"));
            Assert.That(request.Query.Keys.Intersect(ListFilterParameters), Is.Empty);
        });
    }

    [Test]
    public async Task Key_list_prints_the_enrolment_keys_in_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page(ApiJson.Key(12, "servers"), ApiJson.Key(13, "laptops")));

        var result = await run.RunAsync("key", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var output = result.StdoutJson;
        var items = output.GetProperty("items");
        Assert.Multiple(() =>
        {
            Assert.That(items.GetArrayLength(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(items[0], "id").GetInt32(), Is.EqualTo(12));
            Assert.That(JsonAssert.Property(items[1], "id").GetInt32(), Is.EqualTo(13));
            Assert.That(output.GetProperty("total").GetInt32(), Is.EqualTo(2));
            Assert.That(output.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // The query parameter name is the one Enclave.Sdk.Api 1.0.4 sends
    // (EnrolmentKeysClient.BuildQueryString).
    [Test]
    public async Task Key_list_search_sends_the_search_query_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var result = await run.RunAsync("key", "list", "--search", "servers");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(KeysPath));
            Assert.That(request.Query.GetValueOrDefault("search"), Is.EqualTo("servers"));
        });
    }

    // Enclave.Sdk.Api 1.0.4 formats the flag with bool.ToString()
    // (EnrolmentKeysClient.BuildQueryString), which gives "True".
    [Test]
    public async Task Key_list_include_disabled_sends_include_disabled_true()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var result = await run.RunAsync("key", "list", "--include-disabled");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        Assert.That(run.SingleRequest().Query.GetValueOrDefault("include_disabled"), Is.EqualTo("True"));
    }

    // Enum values take the API's names, matched ignoring case (proposal "Options on every
    // command"). The names are EnrolmentKeySortOrder's members (Enclave.Configuration.Data).
    [TestCase("Description", "Description")]
    [TestCase("lastused", "LastUsed")]
    [TestCase("APPROVALMODE", "ApprovalMode")]
    [TestCase("usesRemaining", "UsesRemaining")]
    public async Task Key_list_sort_sends_the_api_sort_name_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var result = await run.RunAsync("key", "list", "--sort", value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        Assert.That(run.SingleRequest().Query.GetValueOrDefault("sort"), Is.EqualTo(expected));
    }

    // RecentlyEnrolled is a system sort with no member in EnrolmentKeySortOrder, so a command that
    // shares the system list's sort values fails this test. "1" is EnrolmentKeySortOrder's
    // underlying value for LastUsed, and the option takes names only.
    [TestCase("RecentlyEnrolled")]
    [TestCase("1")]
    public async Task Key_list_with_a_sort_value_that_is_not_a_key_sort_name_exits_2_without_a_request(string value)
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "list", "--sort", value],
            ["key", "list", "--sort", "LastUsed"],
            "GET",
            KeysPath);
    }

    // The enrolment key list has no key or DNS filter (EnrolmentKeysClient.GetEnrolmentKeysAsync,
    // Enclave.Sdk.Api 1.0.4), so these system list options are unknown here. The correction drops
    // the option.
    [TestCase("--key", "12")]
    [TestCase("--dns-name", "web.internal")]
    public async Task Key_list_rejects_the_system_list_options_it_lacks_with_exit_2_and_no_request(params string[] option)
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "list", .. option],
            ["key", "list"],
            "GET",
            KeysPath);
    }

    // Enrolment key output includes the key's secret (proposal "Output"); the caller needs it to
    // enrol a system.
    [Test]
    public async Task Key_show_gets_the_key_by_id_and_prints_it_with_its_secret()
    {
        using var run = CliRun.Start();
        run.Stub("GET", $"{KeysPath}/12", json: ApiJson.Key(12, "servers", "SECRET-KEY-VALUE"));

        var result = await run.RunAsync("key", "show", "12");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo($"{KeysPath}/12"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(12));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("servers"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "key").GetString(), Is.EqualTo("SECRET-KEY-VALUE"));
        });
    }

    // A create body is the Enclave.Sdk.Api create model serialised in camelCase
    // (EnrolmentKeysClient.CreateAsync with Constants.JsonSerializerOptions, version 1.0.4).
    [Test]
    public async Task Key_create_posts_the_description_and_prints_the_created_key_with_its_secret()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "servers", "SECRET-KEY-VALUE"));

        var result = await run.RunAsync("key", "create", "--description", "servers");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(KeysPath));
            Assert.That(JsonAssert.Property(request.BodyJson, "description").GetString(), Is.EqualTo("servers"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(12));
            Assert.That(JsonAssert.Property(result.StdoutJson, "key").GetString(), Is.EqualTo("SECRET-KEY-VALUE"));
        });
    }

    // Enum values take the API's names, matched ignoring case, and are sent as the member names
    // Enclave.Sdk.Api's JsonStringEnumConverter writes (EnrolmentKeyType, ApprovalMode). The create
    // model's defaults are GeneralPurpose and Manual, so the Ephemeral and Automatic cases are the
    // ones that show the flag reached the body.
    [TestCase("--type", "Ephemeral", "type", "Ephemeral")]
    [TestCase("--type", "ephemeral", "type", "Ephemeral")]
    [TestCase("--type", "GENERALPURPOSE", "type", "GeneralPurpose")]
    [TestCase("--approval-mode", "Automatic", "approvalMode", "Automatic")]
    [TestCase("--approval-mode", "automatic", "approvalMode", "Automatic")]
    [TestCase("--approval-mode", "MANUAL", "approvalMode", "Manual")]
    [TestCase("--notes", "rack 4 servers", "notes", "rack 4 servers")]
    public async Task Key_create_sends_each_flag_as_its_create_model_field(string option, string value, string field, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "servers"));

        var result = await run.RunAsync("key", "create", "--description", "servers", option, value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, field).GetString(), Is.EqualTo(expected));
            Assert.That(JsonAssert.Property(body, "description").GetString(), Is.EqualTo("servers"));
        });
    }

    // The create model's default is -1, no limit (EnrolmentKeyCreateModel.UsesRemaining), so 5
    // shows the flag reached the body.
    [Test]
    public async Task Key_create_uses_remaining_sends_the_number_of_uses()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "servers"));

        var result = await run.RunAsync("key", "create", "--description", "servers", "--uses-remaining", "5");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var usesRemaining = JsonAssert.Property(run.SingleRequest().BodyJson, "usesRemaining");
        Assert.Multiple(() =>
        {
            Assert.That(usesRemaining.ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(usesRemaining.GetInt32(), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Key_create_tags_sends_the_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "servers"));
        string[] expectedTags = ["servers", "prod"];

        var result = await run.RunAsync("key", "create", "--description", "servers", "--tags", "servers,prod");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var tags = JsonAssert.Property(run.SingleRequest().BodyJson, "tags");
        Assert.That(JsonAssert.Strings(tags), Is.EqualTo(expectedTags));
    }

    [Test]
    public async Task Key_create_sends_every_flag_in_one_request()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "servers"));
        string[] expectedTags = ["servers", "prod"];
        string[] arguments =
        [
            "key", "create",
            "--description", "servers",
            "--type", "Ephemeral",
            "--approval-mode", "Automatic",
            "--uses-remaining", "5",
            "--tags", "servers,prod",
            "--notes", "rack 4 servers",
        ];

        var result = await run.RunAsync(arguments);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "description").GetString(), Is.EqualTo("servers"));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("Ephemeral"));
            Assert.That(JsonAssert.Property(body, "approvalMode").GetString(), Is.EqualTo("Automatic"));
            Assert.That(JsonAssert.Property(body, "usesRemaining").GetInt32(), Is.EqualTo(5));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "tags")), Is.EqualTo(expectedTags));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("rack 4 servers"));
        });
    }

    // The description is required (proposal "Command options"), enum options take the API's names
    // only, and --uses-remaining takes an integer. Each case's correction supplies the description
    // or a valid value.
    [TestCaseSource(nameof(InvalidCreateArguments))]
    public async Task Key_create_with_invalid_arguments_exits_2_without_a_request(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "servers"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "create", .. rejected],
            ["key", "create", .. corrected],
            "POST",
            KeysPath);
    }

    // A patch sends only the fields it sets (proposal "Create and update"). Enclave.Sdk.Api 1.0.4
    // keys the patch body by EnrolmentKeyPatchModel's property names (PatchClient.Set).
    [TestCase("--description", "servers-eu", "Description")]
    [TestCase("--notes", "rack 5", "Notes")]
    public async Task Key_update_sends_the_flag_as_the_only_patch_field(string option, string value, string field)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "servers-eu"));

        var result = await run.RunAsync("key", "update", "12", option, value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo($"{KeysPath}/12"));
            Assert.That(body.EnumerateObject().Count(), Is.EqualTo(1));
            Assert.That(JsonAssert.Property(body, field).GetString(), Is.EqualTo(value));
        });
    }

    [Test]
    public async Task Key_update_set_tags_sends_the_whole_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "servers"));
        string[] expectedTags = ["servers", "prod"];

        var result = await run.RunAsync("key", "update", "12", "--set-tags", "servers,prod");

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
    public async Task Key_update_set_tags_with_an_empty_value_sends_an_empty_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "servers"));

        var result = await run.RunAsync("key", "update", "12", "--set-tags", string.Empty);

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
    public async Task Key_update_sends_every_flag_in_one_patch_and_prints_the_updated_key()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "from-the-api"));
        string[] expectedTags = ["servers"];

        var result = await run.RunAsync("key", "update", "12", "--description", "servers-eu", "--notes", "rack 5", "--set-tags", "servers");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(body.EnumerateObject().Count(), Is.EqualTo(3));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("servers-eu"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("rack 5"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "Tags")), Is.EqualTo(expectedTags));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(12));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("from-the-api"));
        });
    }

    // An update with nothing to change would send an empty patch; exiting 2 tells the caller its
    // flags were lost.
    [Test]
    public async Task Key_update_without_a_field_to_change_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "servers"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "update", "12"],
            ["key", "update", "12", "--description", "servers-eu"],
            "PATCH",
            $"{KeysPath}/12");
    }

    // The proposal ("Several IDs"): a command that accepts several IDs makes the bulk call for one
    // ID too, so its output always has the bulk shape. Key IDs are JSON numbers in the API's body
    // (Enclave.Sdk.Api 1.0.4 serialises EnrolmentKeyId as a number).
    [TestCaseSource(nameof(BulkCommands))]
    public async Task Key_bulk_command_with_one_id_sends_the_bulk_request_and_prints_requested_and_affected(
        string verb, bool needsYes, string method, string path, string resultField)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: ApiJson.Bulk(resultField, 1));
        string[] ids = ["12"];

        var result = await run.RunAsync(["key", verb, .. ids, .. Args.YesIf(needsYes)]);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(KeyIdsOf(request), Is.EqualTo(ids));
            Assert.That(result.StdoutJson.GetProperty("requested").GetInt32(), Is.EqualTo(1));
            Assert.That(result.StdoutJson.GetProperty("affected").GetInt32(), Is.EqualTo(1));
        });
    }

    // The API reports one of the two IDs as affected, so the output shows affected is the API's
    // count and requested is the number of IDs given.
    [TestCaseSource(nameof(BulkCommands))]
    public async Task Key_bulk_command_with_two_ids_sends_both_in_one_request_and_prints_the_api_count_as_affected(
        string verb, bool needsYes, string method, string path, string resultField)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: ApiJson.Bulk(resultField, 1));
        string[] ids = ["12", "13"];

        var result = await run.RunAsync(["key", verb, .. ids, .. Args.YesIf(needsYes)]);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(KeyIdsOf(request), Is.EqualTo(ids));
            Assert.That(result.StdoutJson.GetProperty("requested").GetInt32(), Is.EqualTo(2));
            Assert.That(result.StdoutJson.GetProperty("affected").GetInt32(), Is.EqualTo(1));
        });
    }

    // The API's timed enable has no bulk form (proposal "Several IDs"), so --until puts to the key's
    // own enable-until route and prints the key it returns. The expiry action defaults to Disable:
    // AutoExpireModel gives ExpiryAction no default of its own, Disable is the enum's default value
    // (Enclave.Configuration.Data.Enums.ExpiryAction), and Disable loses nothing.
    [Test]
    public async Task Key_enable_until_an_iso_8601_time_puts_enable_until_with_that_time_in_utc_and_the_disable_action()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{KeysPath}/12/enable-until", json: ApiJson.Key(12, "servers"));

        var result = await run.RunAsync("key", "enable", "12", "--until", "2026-12-01T10:00:00Z");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo($"{KeysPath}/12/enable-until"));
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Disable"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(12));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("servers"));
        });
    }

    // AGENTS.md "CLI contract": --until accepts a relative time such as 2h or 7d. The expected
    // window spans the run, with a second either side for a CLI that rounds to whole seconds.
    [TestCase("2h", 2 * 60)]
    [TestCase("7d", 7 * 24 * 60)]
    public async Task Key_enable_until_a_relative_time_sends_that_long_from_now_in_utc(string until, int minutes)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{KeysPath}/12/enable-until", json: ApiJson.Key(12, "servers"));
        var span = TimeSpan.FromMinutes(minutes);

        var before = DateTimeOffset.UtcNow;
        var result = await run.RunAsync("key", "enable", "12", "--until", until);
        var after = DateTimeOffset.UtcNow;

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo($"{KeysPath}/12/enable-until"));
            Assert.That(expiry, Is.InRange(before + span - TimeSpan.FromSeconds(1), after + span + TimeSpan.FromSeconds(1)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
        });
    }

    // Enum values take the API's names, matched ignoring case. Delete schedules a deletion, so
    // every case passes --yes (proposal "Confirmation").
    [TestCase("Delete", "Delete")]
    [TestCase("delete", "Delete")]
    [TestCase("disable", "Disable")]
    public async Task Key_enable_until_sends_the_expiry_action_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{KeysPath}/12/enable-until", json: ApiJson.Key(12, "servers"));

        var result = await run.RunAsync("key", "enable", "12", "--until", "2026-12-01T10:00:00Z", "--expiry-action", value, "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        Assert.That(JsonAssert.Property(run.SingleRequest().BodyJson, "expiryAction").GetString(), Is.EqualTo(expected));
    }

    // --until takes one ID (proposal "Several IDs"). --expiry-action has no effect without --until,
    // and accepting it alone would leave the caller believing an expiry was set. Both routes are
    // stubbed so a request the CLI sends by mistake gets a success response and shows up here as a
    // request. Each case's correction is a timed enable of one key.
    [TestCaseSource(nameof(InvalidEnableArguments))]
    public async Task Key_enable_with_invalid_timed_enable_arguments_exits_2_without_a_request(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", $"{KeysPath}/12/enable-until", json: ApiJson.Key(12, "servers"));
        run.Stub("PUT", $"{KeysPath}/enable", json: ApiJson.Bulk("keysModified", 1));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "enable", .. rejected],
            ["key", "enable", .. corrected],
            "PUT",
            $"{KeysPath}/12/enable-until");
    }

    // Each bulk route and its result field. Enable and disable come from Enclave.Sdk.Api 1.0.4
    // EnrolmentKeysClient (BulkEnableAsync, BulkDisableAsync). Enclave.Sdk.Api 1.0.4 has no key
    // delete, so the delete row encodes the API's bulk route (portal EnrolmentKeysController.cs:295).
    // Delete needs --yes (proposal "Confirmation").
    private static IEnumerable<TestCaseData> BulkCommands()
    {
        yield return new TestCaseData("enable", false, "PUT", TestData.OrgPath("enrolment-keys/enable"), "keysModified").SetArgDisplayNames("key enable");
        yield return new TestCaseData("disable", false, "PUT", TestData.OrgPath("enrolment-keys/disable"), "keysModified").SetArgDisplayNames("key disable");
        yield return new TestCaseData("delete", true, "DELETE", TestData.OrgPath("enrolment-keys"), "keysDeleted").SetArgDisplayNames("key delete");
    }

    private static IEnumerable<TestCaseData> InvalidCreateArguments()
    {
        yield return Correction(
            "no description",
            ["--type", "GeneralPurpose"],
            ["--description", "servers", "--type", "GeneralPurpose"]);
        yield return Correction(
            "unknown type",
            ["--description", "servers", "--type", "Permanent"],
            ["--description", "servers", "--type", "Ephemeral"]);
        yield return Correction(
            "numeric type",
            ["--description", "servers", "--type", "1"],
            ["--description", "servers", "--type", "Ephemeral"]);
        yield return Correction(
            "unknown approval mode",
            ["--description", "servers", "--approval-mode", "Auto"],
            ["--description", "servers", "--approval-mode", "Automatic"]);
        yield return Correction(
            "uses remaining not a number",
            ["--description", "servers", "--uses-remaining", "five"],
            ["--description", "servers", "--uses-remaining", "5"]);
    }

    private static IEnumerable<TestCaseData> InvalidEnableArguments()
    {
        yield return Correction(
            "until is not a time",
            ["12", "--until", "tomorrow"],
            ["12", "--until", "2026-12-01T10:00:00Z"]);
        yield return Correction(
            "unknown expiry action",
            ["12", "--until", "2026-12-01T10:00:00Z", "--expiry-action", "Archive"],
            ["12", "--until", "2026-12-01T10:00:00Z", "--expiry-action", "Disable"]);
        yield return Correction(
            "expiry action without until",
            ["12", "--expiry-action", "Disable"],
            ["12", "--until", "2026-12-01T10:00:00Z", "--expiry-action", "Disable"]);
        yield return Correction(
            "until with two ids",
            ["12", "13", "--until", "2026-12-01T10:00:00Z"],
            ["12", "--until", "2026-12-01T10:00:00Z"]);
    }

    private static TestCaseData Correction(string name, string[] rejected, string[] corrected) =>
        new TestCaseData(rejected, corrected).SetArgDisplayNames(name);

    // The raw JSON text of each ID: a number reads 12, and an ID sent as a string reads "12" with
    // its quotes, so it fails the comparison.
    private static string[] KeyIdsOf(RecordedRequest request)
    {
        var keyIds = JsonAssert.Property(request.BodyJson, "keyIds");

        return [.. keyIds.EnumerateArray().Select(id => id.GetRawText())];
    }
}
