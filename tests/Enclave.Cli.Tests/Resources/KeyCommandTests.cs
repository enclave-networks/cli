using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

public class KeyCommandTests
{
    // Read with TestData.LocalZone (UTC+05:30), so a time read in the wrong zone gives a different
    // expiry.
    private static readonly DateTimeOffset Now = new(2030, 3, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly string KeysPath = TestData.OrgPath("enrolment-keys");

    // Disabled keys are left out unless --include-disabled or --state disabled asks for them
    // (spec/cli-surface.md "Filters").
    [Test]
    public async Task Key_list_reads_every_page_and_prints_every_key_with_its_secret_as_a_key_list()
    {
        using var run = CliRun.Start();
        run.StubPages(KeysPath, 2, ApiJson.Key(12, "build agents", "SECRET-12"), ApiJson.Key(13, "ci runners", "SECRET-13"), ApiJson.Key(14, "laptops", "SECRET-14"));

        var result = await run.RunAsync("key", "list");

        var items = CliAssert.List(result, "key");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IntFieldList(items, "id"), Is.EqualTo("12,13,14"));
            Assert.That(JsonRead.StringFieldList(items, "key"), Is.EqualTo("SECRET-12,SECRET-13,SECRET-14"));
            Assert.That(run.PagesRequested(KeysPath), Is.EqualTo("0,1"));
            Assert.That(run.RequestsTo("GET", KeysPath).Select(request => request.QueryValue("include_disabled")), Is.Not.Empty.And.All.Null);
        });
    }

    // uses and enrolled are search keys of portal EnrolmentKeySearchKeyService. The filter leaves
    // disabled keys out, as a plain list does.
    [TestCase("build")]
    [TestCase("uses:<5 enrolled:>10")]
    public async Task Key_list_sends_the_filter_text_as_the_search_exactly_as_typed(string filter)
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", KeysPath, "key", "list", "--filter", filter);

        Assert.Multiple(() =>
        {
            Assert.That(request.QueryValue("search"), Is.EqualTo(filter));
            Assert.That(request.QueryValue("include_disabled"), Is.Null);
        });
    }

    // The API's approval key reads "gated" as Manual and matches nothing for "manual", and its state
    // key reads "nouses" (portal EnrolmentKeySearchKeyService.BuildFilterAsync). None of these flags
    // asks for disabled keys (spec/cli-surface.md "Filters").
    [TestCase("--tag ci", "tags:ci")]
    [TestCase("--tag build,linux", "tags:build,linux")]
    [TestCase("--approval automatic", "approval:automatic")]
    [TestCase("--approval manual", "approval:gated")]
    [TestCase("--state enabled", "state:enabled")]
    [TestCase("--state no-uses", "state:nouses")]
    public async Task Key_list_adds_each_search_key_flag_to_the_search_as_the_api_search_value(string flags, string search)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", KeysPath, ["key", "list", .. flags.Split(' ')]);

        Assert.Multiple(() =>
        {
            Assert.That(request.QueryValue("search"), Is.EqualTo(search));
            Assert.That(request.QueryValue("include_disabled"), Is.Null);
        });
    }

    // Example 47. The API applies each term of the search whatever their order (portal
    // BaseSearchKeyService.ParseSearchString).
    [Test]
    public async Task Key_list_approval_automatic_with_tag_adds_both_terms_to_the_search()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", KeysPath, "key", "list", "--approval", "automatic", "--tag", "ci");

        var terms = (request.QueryValue("search") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Multiple(() =>
        {
            Assert.That(terms, Has.Length.EqualTo(2));
            Assert.That(terms, Has.Exactly(1).EqualTo("tags:ci"));
            Assert.That(terms, Has.Exactly(1).EqualTo("approval:automatic"));
        });
    }

    // The API leaves disabled keys out unless include_disabled is true, whatever the search (portal
    // EnrolmentKeyRepository.BuildFilterAsync).
    [Test]
    public async Task Key_list_state_disabled_adds_the_state_to_the_search_and_includes_disabled_keys()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", KeysPath, "key", "list", "--state", "disabled");

        Assert.Multiple(() =>
        {
            Assert.That(request.QueryValue("search"), Is.EqualTo("state:disabled"));
            Assert.That(request.QueryValue("include_disabled"), Is.EqualTo("True"));
        });
    }

    // Enclave.Sdk.Api writes the flag with bool.ToString()
    // (EnrolmentKeysClient.BuildQueryString), which gives "True".
    [Test]
    public async Task Key_list_include_disabled_sends_include_disabled_true()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        var request = await CliAssert.AcceptedAsync(run, "GET", KeysPath, "key", "list", "--include-disabled");

        Assert.That(request.QueryValue("include_disabled"), Is.EqualTo("True"));
    }

    // gated is the API's search value, which the CLI writes for the user; recently-enrolled is a
    // system sort with no member in EnrolmentKeySortOrder.
    [TestCase("--approval gated")]
    [TestCase("--state expired")]
    [TestCase("--sort recently-enrolled")]
    public async Task Key_list_with_a_value_its_option_does_not_take_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "list", .. flags.Split(' ')],
            ["key", "list"],
            "GET",
            KeysPath);
    }

    // Example 13, with both commands in one sandbox as a shell pipeline runs them (portal
    // EnrolmentKeysController.DeleteBulkEnrolmentKeys). Key IDs are integers, written as JSON
    // numbers, so IntList fails on one sent as a string.
    [Test]
    public async Task Key_delete_given_the_list_of_disabled_keys_deletes_those_keys()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page(DisabledKey(21, "old laptops"), DisabledKey(22, "old servers")));
        run.StubBulk("DELETE", KeysPath, "keysDeleted", 2);

        var listed = await run.RunAsync("key", "list", "--state", "disabled");
        CliAssert.Succeeded(listed);
        run.StdinText = listed.Stdout;
        var result = await run.RunAsync("key", "delete", "-");

        CliAssert.Bulk(result, 2, 2);
        var deletes = run.RequestsTo("DELETE", KeysPath);
        Assert.That(deletes, Has.Count.EqualTo(1));
        Assert.That(JsonRead.IntList(JsonAssert.Property(deletes[0].BodyJson, "keyIds")), Is.EqualTo("21,22"));
    }

    // The lookup asks for disabled keys too, and the key found is then read by its ID
    // (spec/cli-surface.md "Calls per command"). Example 46, first command.
    [Test]
    public async Task Key_show_by_description_looks_the_key_up_and_prints_it_with_its_secret()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.BuildAgentsKeys());
        run.Stub("GET", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents", "SECRET-12"));
        run.Stub("GET", $"{KeysPath}/13", json: ApiJson.Key(13, "build agents 2026-04", "SECRET-13"));

        var result = await run.RunAsync("key", "show", "build agents");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", KeysPath).Select(request => request.QueryValue("include_disabled")), Is.All.EqualTo("True"));
            Assert.That(run.RequestsTo("GET", $"{KeysPath}/12"), Has.Count.EqualTo(1));
            Assert.That(run.RequestsTo("GET", $"{KeysPath}/13"), Is.Empty);
            Assert.That(result.StdoutJson.GetProperty("id").GetInt32(), Is.EqualTo(12));
            Assert.That(result.StdoutJson.GetProperty("key").GetString(), Is.EqualTo("SECRET-12"));
        });
    }

    // spec/cli-surface.md "Names and IDs".
    [Test]
    public async Task Key_show_matches_the_description_ignoring_case()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page(ApiJson.Key(12, "Build Agents")));
        run.Stub("GET", $"{KeysPath}/12", json: ApiJson.Key(12, "Build Agents"));

        var result = await run.RunAsync("key", "show", "build agents");

        CliAssert.Succeeded(result);
        Assert.That(run.RequestsTo("GET", $"{KeysPath}/12"), Has.Count.EqualTo(1));
    }

    // The option decides how a value is read, so "12" names the key described "12"
    // (spec/cli-surface.md "Names and IDs").
    [Test]
    public async Task Key_show_reads_a_number_given_as_the_argument_as_a_description()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page(ApiJson.Key(12, "build agents"), ApiJson.Key(40, "12")));
        run.Stub("GET", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));
        run.Stub("GET", $"{KeysPath}/40", json: ApiJson.Key(40, "12"));

        var result = await run.RunAsync("key", "show", "12");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", $"{KeysPath}/40"), Has.Count.EqualTo(1));
            Assert.That(run.RequestsTo("GET", $"{KeysPath}/12"), Is.Empty);
            Assert.That(result.StdoutJson.GetProperty("id").GetInt32(), Is.EqualTo(40));
        });
    }

    // "ci runners 2026" starts with the name given, which is not a match (spec/cli-surface.md
    // "Names and IDs").
    [Test]
    public async Task Key_show_with_a_description_that_matches_no_key_exits_2_after_the_lookup_alone()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page(ApiJson.Key(12, "build agents"), ApiJson.Key(14, "ci runners 2026")));
        run.Stub("GET", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));
        run.Stub("GET", $"{KeysPath}/14", json: ApiJson.Key(14, "ci runners 2026"));

        var result = await run.RunAsync("key", "show", "ci runners");

        CliAssert.Failed(result, "invalid_argument");
        Assert.That(run.Requests.Select(request => request.Path), Is.Not.Empty.And.All.EqualTo(KeysPath));
    }

    // The two descriptions differ only in case, and names match ignoring case (spec/cli-surface.md
    // "Names and IDs").
    [Test]
    public async Task Key_show_with_a_description_two_keys_match_exits_2_with_both_as_candidates()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.Page(ApiJson.Key(12, "build agents"), ApiJson.Key(13, "Build Agents"), ApiJson.Key(14, "ci runners")));
        run.Stub("GET", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));
        run.Stub("GET", $"{KeysPath}/13", json: ApiJson.Key(13, "Build Agents"));

        var result = await run.RunAsync("key", "show", "build agents");

        var error = CliAssert.Failed(result, "invalid_argument");
        int[] candidates = [12, 13];
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("id").GetInt32()), Is.EquivalentTo(candidates));
            Assert.That(run.Requests.Select(request => request.Path), Is.Not.Empty.And.All.EqualTo(KeysPath));
        });
    }

    // spec/cli-surface.md "Details".
    [TestCase("show", "GET", "enrolment-keys/12")]
    [TestCase("update", "PATCH", "enrolment-keys/12")]
    public async Task Key_command_given_a_description_and_id_exits_2_without_a_request(string verb, string method, string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        var path = TestData.OrgPath(pathSuffix);
        run.Stub("GET", KeysPath, json: ApiJson.BuildAgentsKeys());
        run.Stub(method, path, json: ApiJson.Key(12, "build agents"));
        string[] change = verb == "update" ? ["--notes", "rack 5"] : [];

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", verb, "build agents", "--id", "12", .. change],
            ["key", verb, "--id", "12", .. change],
            method,
            path);
    }

    // A create sends every setting, so a change to the API's defaults does not change what the
    // command does (spec/cli-surface.md "Create and update"). -1 is unlimited uses (portal
    // EnrolmentKeyCreateModel.UsesRemaining), and the API takes a retention time on ephemeral keys
    // only (portal EnrolmentKeyCreateValidator). Enclave.Sdk.Api writes every property of the create
    // model, nulls included, in camelCase (EnrolmentKeysClient.CreateAsync,
    // Constants.JsonSerializerOptions), so the names are read exactly.
    [Test]
    public async Task Key_create_sends_a_general_purpose_key_that_requires_approval_and_has_unlimited_uses()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "build agents 2026-10"));
        string[] fields =
        [
            "description", "type", "approvalMode", "usesRemaining", "tags", "ipConstraints", "disconnectedRetentionMinutes", "notes", "autoExpire",
        ];

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, "key", "create", "build agents 2026-10");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields));
            Assert.That(body.GetProperty("description").GetString(), Is.EqualTo("build agents 2026-10"));
            Assert.That(body.GetProperty("type").GetString(), Is.EqualTo("GeneralPurpose"));
            Assert.That(body.GetProperty("approvalMode").GetString(), Is.EqualTo("Manual"));
            Assert.That(body.GetProperty("usesRemaining").GetInt32(), Is.EqualTo(-1));
            Assert.That(body.GetProperty("tags").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(body.GetProperty("tags").GetArrayLength(), Is.Zero);
            Assert.That(body.GetProperty("ipConstraints").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(body.GetProperty("ipConstraints").GetArrayLength(), Is.Zero);
            Assert.That(body.GetProperty("disconnectedRetentionMinutes").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("notes").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(body.GetProperty("autoExpire").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    // Example 8 reads the secret with jq -r .key.
    [Test]
    public async Task Key_create_prints_the_created_key_with_its_secret_in_the_key_field()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(31, "ci runners", "SECRET-NEW"));

        var result = await run.RunAsync("key", "create", "ci runners");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.StdoutJson.GetProperty("id").GetInt32(), Is.EqualTo(31));
            Assert.That(result.StdoutJson.GetProperty("key").GetString(), Is.EqualTo("SECRET-NEW"));
        });
    }

    // spec/cli-surface.md "Command options".
    [TestCase("--auto-approve", "Automatic", -1)]
    [TestCase("--uses 5", "Manual", 5)]
    [TestCase("--auto-approve --uses 5", "Automatic", 5)]
    public async Task Key_create_auto_approve_and_uses_set_the_approval_and_uses_of_a_general_purpose_key(string flags, string approval, int uses)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "build agents"));

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, ["key", "create", "build agents", .. flags.Split(' ')]);

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("GeneralPurpose"));
            Assert.That(JsonAssert.Property(body, "approvalMode").GetString(), Is.EqualTo(approval));
            Assert.That(JsonAssert.Property(body, "usesRemaining").GetInt32(), Is.EqualTo(uses));
        });
    }

    // 30 minutes is the portal's default (portal-spa appConstants/enrolmentKeys.ts,
    // DEFAULT_RETENTION_MINUTES; spec/cli-surface.md "Command options").
    [Test]
    public async Task Key_create_ephemeral_sends_automatic_approval_unlimited_uses_and_30_minutes_retention()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "ci runners"));

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, "key", "create", "ci runners", "--ephemeral");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("Ephemeral"));
            Assert.That(JsonAssert.Property(body, "approvalMode").GetString(), Is.EqualTo("Automatic"));
            Assert.That(JsonAssert.Property(body, "usesRemaining").GetInt32(), Is.EqualTo(-1));
            Assert.That(JsonAssert.Property(body, "disconnectedRetentionMinutes").GetInt32(), Is.EqualTo(30));
        });
    }

    // The API takes minutes (EnrolmentKeyCreateModel.DisconnectedRetentionMinutes).
    [TestCase("10m", 10)]
    [TestCase("2h", 120)]
    [TestCase("1d", 1440)]
    public async Task Key_create_ephemeral_keep_disconnected_sends_the_retention_in_minutes(string duration, int minutes)
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "ci runners"));

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, "key", "create", "ci runners", "--ephemeral", "--keep-disconnected", duration);

        Assert.That(JsonAssert.Property(request.BodyJson, "disconnectedRetentionMinutes").GetInt32(), Is.EqualTo(minutes));
    }

    // Example 8. The range has no label, so its description is missing or null.
    [Test]
    public async Task Key_create_for_ephemeral_ci_runners_sends_every_setting_of_example_8()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(31, "ci runners", "SECRET-CI"));
        string[] arguments =
        [
            "key", "create", "ci runners",
            "--ephemeral",
            "--tags", "ci,runner",
            "--allow-ip", "198.51.100.0/24",
            "--keep-disconnected", "10m",
        ];

        var result = await run.RunAsync(arguments);

        CliAssert.Succeeded(result);
        var body = run.SingleRequest().BodyJson;
        var constraints = JsonAssert.Property(body, "ipConstraints");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "description").GetString(), Is.EqualTo("ci runners"));
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("Ephemeral"));
            Assert.That(JsonAssert.Property(body, "approvalMode").GetString(), Is.EqualTo("Automatic"));
            Assert.That(JsonAssert.Property(body, "usesRemaining").GetInt32(), Is.EqualTo(-1));
            Assert.That(JsonRead.StringList(JsonAssert.Property(body, "tags")), Is.EqualTo("ci,runner"));
            Assert.That(JsonRead.StringFieldList(constraints, "range"), Is.EqualTo("198.51.100.0/24"));
            Assert.That(constraints[0].TryGetProperty("description", out var label) ? label.ValueKind : JsonValueKind.Null, Is.EqualTo(JsonValueKind.Null));
            Assert.That(JsonAssert.Property(body, "disconnectedRetentionMinutes").GetInt32(), Is.EqualTo(10));
            Assert.That(result.StdoutJson.GetProperty("key").GetString(), Is.EqualTo("SECRET-CI"));
        });
    }

    // The second label holds a comma, which is why the flag is repeated and never a comma list.
    [Test]
    public async Task Key_create_allow_ip_sends_each_range_as_an_ip_constraint_with_its_label_as_the_description()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "build agents"));
        string[] arguments =
        [
            "key", "create", "build agents",
            "--allow-ip", "203.0.113.0/24=London office",
            "--allow-ip", "198.51.100.0/24=CI runners, shared",
        ];

        var result = await run.RunAsync(arguments);

        CliAssert.Succeeded(result);
        var constraints = JsonAssert.Property(run.SingleRequest().BodyJson, "ipConstraints");
        Assert.That(constraints.GetArrayLength(), Is.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(constraints, "range"), Is.EqualTo("203.0.113.0/24,198.51.100.0/24"));
            Assert.That(JsonAssert.Property(constraints[0], "description").GetString(), Is.EqualTo("London office"));
            Assert.That(JsonAssert.Property(constraints[1], "description").GetString(), Is.EqualTo("CI runners, shared"));
        });
    }

    // Example 9, first command.
    [Test]
    public async Task Key_create_tags_sends_the_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "build agents 2026-10"));

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, "key", "create", "build agents 2026-10", "--tags", "build,linux");

        Assert.That(JsonRead.StringList(JsonAssert.Property(request.BodyJson, "tags")), Is.EqualTo("build,linux"));
    }

    // Without --then the key is disabled at expiry (spec/cli-surface.md "Command options").
    [TestCase("--for 14d", "Disable")]
    [TestCase("--for 14d --then disable", "Disable")]
    [TestCase("--for 14d --then delete", "Delete")]
    public async Task Key_create_for_a_duration_sends_an_expiry_that_long_from_now_with_the_then_action(string flags, string action)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "contractor laptops"));

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, ["key", "create", "contractor laptops", .. flags.Split(' ')]);

        var autoExpire = JsonAssert.Property(request.BodyJson, "autoExpire");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.ExpiryDateTime(autoExpire), Is.EqualTo(Now.AddDays(14)));
            Assert.That(JsonAssert.Property(autoExpire, "expiryAction").GetString(), Is.EqualTo(action));
        });
    }

    [Test]
    public async Task Key_create_until_a_time_with_a_zone_sends_that_instant_in_utc()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "contractor laptops"));

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, "key", "create", "contractor laptops", "--until", "2036-12-01T12:00:00+02:00");

        var autoExpire = JsonAssert.Property(request.BodyJson, "autoExpire");
        var expiry = JsonRead.ExpiryDateTime(autoExpire);
        Assert.Multiple(() =>
        {
            Assert.That(expiry, Is.EqualTo(new DateTimeOffset(2036, 12, 1, 10, 0, 0, TimeSpan.Zero)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(autoExpire, "expiryAction").GetString(), Is.EqualTo("Disable"));
        });
    }

    [Test]
    public async Task Key_create_notes_sends_the_notes()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "build agents"));

        var request = await CliAssert.AcceptedAsync(run, "POST", KeysPath, "key", "create", "build agents", "--notes", "rack 4 servers");

        Assert.That(JsonAssert.Property(request.BodyJson, "notes").GetString(), Is.EqualTo("rack 4 servers"));
    }

    // An ephemeral key always approves automatically and has unlimited uses, and the API takes a
    // retention time on ephemeral keys only (portal EnrolmentKeyCreateValidator; spec/cli-surface.md
    // "Command options", "Details"). 5000000d, about 13,700 years, reaches past the last time a
    // DateTimeOffset holds.
    [TestCase("--for 5000000d", "--for 14d")]
    [TestCase("--ephemeral --auto-approve", "--ephemeral")]
    [TestCase("--ephemeral --uses 5", "--ephemeral")]
    [TestCase("--keep-disconnected 10m", "--ephemeral --keep-disconnected 10m")]
    [TestCase("--ephemeral --keep-disconnected soon", "--ephemeral --keep-disconnected 10m")]
    [TestCase("--uses five", "--uses 5")]
    [TestCase("--for 14d --then revoke", "--for 14d --then delete")]
    [TestCase("--for 14d --until 2036-12-01T10:00:00Z", "--for 14d")]
    [TestCase("--then delete", "--for 14d --then delete")]
    [TestCase("--until 2020-01-01T00:00:00Z", "--until 2036-12-01T10:00:00Z")]
    public async Task Key_create_with_conflicting_or_invalid_settings_exits_2_without_a_request(string rejected, string corrected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(corrected);
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "ci runners"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "create", "ci runners", .. rejected.Split(' ')],
            ["key", "create", "ci runners", .. corrected.Split(' ')],
            "POST",
            KeysPath);
    }

    // spec/cli-surface.md "Commands".
    [Test]
    public async Task Key_create_without_a_description_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("POST", KeysPath, json: ApiJson.Key(12, "ci runners"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "create", "--tags", "ci"],
            ["key", "create", "ci runners", "--tags", "ci"],
            "POST",
            KeysPath);
    }

    // Example 32, second command. Enclave.Sdk.Api keys a patch body by EnrolmentKeyPatchModel's
    // property names (PatchClient.Set).
    [Test]
    public async Task Key_update_id_require_approval_sends_manual_approval_as_the_only_patch_field()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", $"{KeysPath}/12", "key", "update", "--id", "12", "--require-approval");

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("ApprovalMode"));
            Assert.That(JsonAssert.Property(request.BodyJson, "ApprovalMode").GetString(), Is.EqualTo("Manual"));
        });
    }

    // Example 32, first command. The lookup asks for disabled keys too (spec/cli-surface.md
    // "Filters").
    [Test]
    public async Task Key_update_by_description_require_approval_patches_the_key_the_lookup_found()
    {
        using var run = CliRun.Start();
        run.Stub("GET", KeysPath, json: ApiJson.BuildAgentsKeys());
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));
        run.Stub("PATCH", $"{KeysPath}/13", json: ApiJson.Key(13, "build agents 2026-04"));

        var result = await run.RunAsync("key", "update", "build agents", "--require-approval");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", $"{KeysPath}/12");
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", KeysPath).Select(request => request.QueryValue("include_disabled")), Is.All.EqualTo("True"));
            Assert.That(run.RequestsTo("PATCH", $"{KeysPath}/13"), Is.Empty);
            Assert.That(JsonAssert.Property(patches[0].BodyJson, "ApprovalMode").GetString(), Is.EqualTo("Manual"));
        });
    }

    [Test]
    public async Task Key_update_auto_approve_sends_automatic_approval()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", $"{KeysPath}/12", "key", "update", "--id", "12", "--auto-approve");

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("ApprovalMode"));
            Assert.That(JsonAssert.Property(request.BodyJson, "ApprovalMode").GetString(), Is.EqualTo("Automatic"));
        });
    }

    // spec/cli-surface.md "Details".
    [Test]
    public async Task Key_update_auto_approve_with_require_approval_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "update", "--id", "12", "--auto-approve", "--require-approval"],
            ["key", "update", "--id", "12", "--require-approval"],
            "PATCH",
            $"{KeysPath}/12");
    }

    // spec/cli-surface.md "Details".
    [Test]
    public async Task Key_update_without_a_change_flag_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "update", "--id", "12"],
            ["key", "update", "--id", "12", "--notes", "rack 5"],
            "PATCH",
            $"{KeysPath}/12");
    }

    // Example 48. --set-allow-ip reads the key first to keep the labels of ranges that stay
    // (spec/cli-surface.md "Calls per command").
    [TestCase(true)]
    [TestCase(false)]
    public async Task Key_update_uses_and_set_allow_ip_send_the_uses_and_the_allowed_range(bool byDescription)
    {
        using var run = CliRun.Start();
        var path = $"{KeysPath}/12";
        run.Stub("GET", KeysPath, json: ApiJson.BuildAgentsKeys());
        run.Stub("GET", path, json: ApiJson.Key(12, "build agents"));
        run.Stub("PATCH", path, json: ApiJson.Key(12, "build agents"));
        string[] key = byDescription ? ["build agents"] : ["--id", "12"];
        string[] fields = ["UsesRemaining", "IpConstraints"];

        var result = await run.RunAsync(["key", "update", .. key, "--uses", "5", "--set-allow-ip", "203.0.113.0/24"]);

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var body = patches[0].BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields));
            Assert.That(JsonAssert.Property(body, "UsesRemaining").GetInt32(), Is.EqualTo(5));
            Assert.That(JsonRead.StringFieldList(JsonAssert.Property(body, "IpConstraints"), "range"), Is.EqualTo("203.0.113.0/24"));
            Assert.That(run.RequestsTo("GET", KeysPath), Has.Count.EqualTo(byDescription ? 1 : 0));
        });
    }

    // Example 76.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Key_update_set_allow_ip_sends_each_range_with_its_label_as_the_description(bool byDescription)
    {
        using var run = CliRun.Start();
        var path = $"{KeysPath}/12";
        run.Stub("GET", KeysPath, json: ApiJson.BuildAgentsKeys());
        run.Stub("GET", path, json: ApiJson.Key(12, "build agents"));
        run.Stub("PATCH", path, json: ApiJson.Key(12, "build agents"));
        string[] key = byDescription ? ["build agents"] : ["--id", "12"];

        var result = await run.RunAsync(["key", "update", .. key, "--set-allow-ip", "203.0.113.0/24=London office", "--set-allow-ip", "198.51.100.0/24=CI runners"]);

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var constraints = JsonAssert.Property(patches[0].BodyJson, "IpConstraints");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("IpConstraints"));
            Assert.That(JsonRead.StringFieldList(constraints, "range"), Is.EqualTo("203.0.113.0/24,198.51.100.0/24"));
            Assert.That(JsonRead.StringFieldList(constraints, "description"), Is.EqualTo("London office,CI runners"));
        });
    }

    // The API answers with another description, so the output shows it is the model returned,
    // secret included.
    [Test]
    public async Task Key_update_description_and_notes_send_only_those_fields_and_print_the_updated_key()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "from-the-api", "SECRET-12"));
        string[] fields = ["Description", "Notes"];

        var result = await run.RunAsync("key", "update", "--id", "12", "--description", "build agents eu", "--notes", "rack 5");

        CliAssert.Succeeded(result);
        var body = run.SingleRequest().BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("build agents eu"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("rack 5"));
            Assert.That(result.StdoutJson.GetProperty("description").GetString(), Is.EqualTo("from-the-api"));
            Assert.That(result.StdoutJson.GetProperty("key").GetString(), Is.EqualTo("SECRET-12"));
        });
    }

    // It replaces the whole list, so it needs no read (spec/cli-surface.md "Calls per command").
    [Test]
    public async Task Key_update_set_tags_sends_the_tags_as_the_whole_tag_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", $"{KeysPath}/12", json: ApiJson.Key(12, "build agents"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", $"{KeysPath}/12", "key", "update", "--id", "12", "--set-tags", "build,linux");

        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("Tags"));
            Assert.That(JsonRead.StringList(JsonAssert.Property(request.BodyJson, "Tags")), Is.EqualTo("build,linux"));
        });
    }

    // The API takes the whole tag list, so the CLI reads the key's tags to keep them
    // (spec/cli-surface.md "Create and update"). Tags are compared sorted, since their order does
    // not matter to the API.
    [TestCase("--add-tags", "ci", "build,ci,legacy,linux")]
    [TestCase("--remove-tags", "legacy", "build,linux")]
    public async Task Key_update_add_and_remove_tags_read_the_key_and_send_its_tags_with_the_change(string option, string tag, string tags)
    {
        using var run = CliRun.Start();
        var path = $"{KeysPath}/12";
        run.Stub("GET", path, json: ApiJson.WithUsedTags(ApiJson.Key(12, "build agents"), "build", "linux", "legacy"));
        run.Stub("PATCH", path, json: ApiJson.Key(12, "build agents"));

        var result = await run.RunAsync("key", "update", "--id", "12", option, tag);

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        var sent = JsonAssert.Strings(JsonAssert.Property(patches[0].BodyJson, "Tags")).Order(StringComparer.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(" | ", run.Calls()), Is.EqualTo($"GET {path} | PATCH {path}"));
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("Tags"));
            Assert.That(string.Join(",", sent), Is.EqualTo(tags));
        });
    }

    // The API takes minutes (EnrolmentKeyPatchModel.DisconnectedRetentionMinutes), and keeps a
    // retention time for ephemeral keys only.
    [Test]
    public async Task Key_update_keep_disconnected_sends_the_retention_in_minutes()
    {
        using var run = CliRun.Start();
        var path = $"{KeysPath}/31";
        var ephemeral = ApiJson.Altered(ApiJson.Key(31, "ci runners"), ("type", "Ephemeral"), ("disconnectedRetentionMinutes", 30));
        run.Stub("GET", path, json: ephemeral);
        run.Stub("PATCH", path, json: ephemeral);

        var result = await run.RunAsync("key", "update", "--id", "31", "--keep-disconnected", "45m");

        CliAssert.Succeeded(result);
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("DisconnectedRetentionMinutes"));
            Assert.That(JsonAssert.Property(patches[0].BodyJson, "DisconnectedRetentionMinutes").GetInt32(), Is.EqualTo(45));
        });
    }

    // The CLI leaves this check to the API (spec/cli-surface.md "Details"), which answers 422
    // retention-ephemeral-only (portal EnrolmentKeyModifyHandler.GetResponseAsync). The key is given
    // by --id, so no read tells the CLI its type.
    [Test]
    public async Task Key_update_keep_disconnected_on_a_general_purpose_key_sends_the_retention_and_reports_the_api_refusal()
    {
        using var run = CliRun.Start();
        var path = $"{KeysPath}/12";
        run.Stub("GET", path, json: ApiJson.Key(12, "build agents"));
        run.StubProblem("PATCH", path, 422, "Retention time only supported on Ephemeral keys", "A disconnected retention time can only be specified on enrolment keys of type 'Ephemeral'");

        var result = await run.RunAsync("key", "update", "--id", "12", "--keep-disconnected", "45m");

        var error = CliAssert.Failed(result, "api_error");
        var patches = run.RequestsTo("PATCH", path);
        Assert.That(patches, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(patches[0].BodyJson), Is.EqualTo("DisconnectedRetentionMinutes"));
            Assert.That(JsonAssert.Property(patches[0].BodyJson, "DisconnectedRetentionMinutes").GetInt32(), Is.EqualTo(45));
            Assert.That(error.GetProperty("status").GetInt32(), Is.EqualTo(422));
            Assert.That(error.GetProperty("title").GetString(), Is.EqualTo("Retention time only supported on Ephemeral keys"));
        });
    }

    // Key IDs go as JSON numbers (Enclave.Sdk.Api EnrolmentKeysClient: BulkEnableAsync,
    // BulkDisableAsync, BulkDeleteAsync), and IntList fails on one sent as a string.
    [TestCase("enable", "PUT", "enrolment-keys/enable", "keysModified")]
    [TestCase("disable", "PUT", "enrolment-keys/disable", "keysModified")]
    [TestCase("delete", "DELETE", "enrolment-keys", "keysDeleted")]
    public async Task Key_bulk_verb_sends_every_id_given_in_one_bulk_call_and_prints_requested_and_affected(
        string verb, string method, string pathSuffix, string resultField)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        var path = TestData.OrgPath(pathSuffix);
        run.StubBulk(method, path, resultField, 2);

        var result = await run.RunAsync("key", verb, "--id", "42,43,57");

        CliAssert.Bulk(result, 3, 2);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonRead.IntList(JsonAssert.Property(request.BodyJson, "keyIds")), Is.EqualTo("42,43,57"));
        });
    }

    // Example 9, second command. The new key's description starts with the same words and is listed
    // first, so a prefix match would pick it.
    [Test]
    public async Task Key_disable_by_description_sends_the_id_the_lookup_found_in_the_bulk_call()
    {
        using var run = CliRun.Start();
        var disablePath = TestData.OrgPath("enrolment-keys/disable");
        run.Stub("GET", KeysPath, json: ApiJson.Page(ApiJson.Key(13, "build agents 2026-10"), ApiJson.Key(12, "build agents 2026-04")));
        run.StubBulk("PUT", disablePath, "keysModified", 1);

        var result = await run.RunAsync("key", "disable", "build agents 2026-04");

        CliAssert.Bulk(result, 1, 1);
        var disables = run.RequestsTo("PUT", disablePath);
        Assert.That(disables, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", KeysPath).Select(request => request.QueryValue("include_disabled")), Is.All.EqualTo("True"));
            Assert.That(JsonRead.IntList(JsonAssert.Property(disables[0].BodyJson, "keyIds")), Is.EqualTo("12"));
        });
    }

    // Example 49, second command. The API's timed enable has no bulk form, so --for takes one key
    // and prints its model (spec/cli-surface.md "Several IDs").
    [Test]
    public async Task Key_enable_id_for_a_duration_then_delete_puts_enable_until_with_the_delete_action_and_prints_the_key()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{KeysPath}/31/enable-until";
        run.Stub("PUT", path, json: ApiJson.Key(31, "contractor laptops", "SECRET-31"));

        var result = await run.RunAsync("key", "enable", "--id", "31", "--for", "14d", "--then", "delete");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var expiry = JsonRead.ExpiryDateTime(request.BodyJson);
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(expiry, Is.EqualTo(Now.AddDays(14)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Delete"));
            Assert.That(result.StdoutJson.GetProperty("id").GetInt32(), Is.EqualTo(31));
            Assert.That(result.StdoutJson.GetProperty("key").GetString(), Is.EqualTo("SECRET-31"));
        });
    }

    // Example 49, first command. The API lists disabled keys only when include_disabled is true
    // (portal EnrolmentKeyRepository.BuildFilterAsync), so the fake gives the key only to a lookup
    // that asks for them.
    [Test]
    public async Task Key_enable_by_description_finds_a_disabled_key_and_puts_enable_until_for_it()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{KeysPath}/31/enable-until";
        run.Stub("GET", KeysPath, json: ApiJson.Page());
        run.StubWithQuery("GET", KeysPath, "include_disabled", "True", json: ApiJson.Page(DisabledKey(31, "contractor laptops")));
        run.Stub("PUT", path, json: ApiJson.Key(31, "contractor laptops"));

        var result = await run.RunAsync("key", "enable", "contractor laptops", "--for", "14d", "--then", "delete");

        CliAssert.Succeeded(result);
        var enables = run.RequestsTo("PUT", path);
        Assert.That(enables, Has.Count.EqualTo(1));
        Assert.That(JsonAssert.Property(enables[0].BodyJson, "expiryAction").GetString(), Is.EqualTo("Delete"));
    }

    // Names match ignoring case, so a description given twice, in either case, is one key
    // (spec/cli-surface.md "Several IDs").
    [Test]
    public async Task Key_enable_for_a_duration_takes_a_description_given_twice_as_one_key()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        var path = $"{KeysPath}/31/enable-until";
        run.StubWithQuery("GET", KeysPath, "include_disabled", "True", json: ApiJson.Page(DisabledKey(31, "contractor laptops")));
        run.Stub("PUT", path, json: ApiJson.Key(31, "contractor laptops"));

        var result = await run.RunAsync("key", "enable", "contractor laptops", "Contractor Laptops", "--for", "14d");

        CliAssert.Succeeded(result);
        Assert.That(run.RequestsTo("PUT", path), Has.Count.EqualTo(1));
    }

    // revoke is the systems' value for --then (spec/cli-surface.md "Command options", "Details").
    // 5000000d, about 13,700 years, reaches past the last time a DateTimeOffset holds.
    [TestCase("--for 14d --then revoke")]
    [TestCase("--for 14d --until 2036-12-01T10:00:00Z")]
    [TestCase("--then delete")]
    [TestCase("--until 2020-01-01T00:00:00Z")]
    [TestCase("--for 5000000d")]
    public async Task Key_enable_with_invalid_or_contradicting_timing_flags_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        run.Stub("PUT", $"{KeysPath}/31/enable-until", json: ApiJson.Key(31, "contractor laptops"));
        run.Stub("PUT", $"{KeysPath}/enable", json: ApiJson.Bulk("keysModified", 1));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["key", "enable", "--id", "31", .. flags.Split(' ')],
            ["key", "enable", "--id", "31", "--for", "14d", "--then", "delete"],
            "PUT",
            $"{KeysPath}/31/enable-until");
    }

    // A disabled key as the API reports one (EnrolmentKeyModel.IsEnabled and Status).
    private static string DisabledKey(int id, string description) =>
        ApiJson.Altered(ApiJson.Key(id, description), ("isEnabled", false), ("status", "Disabled"));
}
