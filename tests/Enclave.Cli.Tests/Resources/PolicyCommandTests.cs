using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

public class PolicyCommandTests
{
    // A PolicyCreateModel with the fields a general policy needs, for the create that follows a
    // rejected create without --from-file.
    private const string PolicyCreateFile = """
        {
          "type": "General",
          "description": "Developers to web servers",
          "isEnabled": true,
          "senderTags": ["developers"],
          "receiverTags": ["web-servers"],
          "acls": [{ "protocol": "Tcp", "ports": "443", "description": "HTTPS" }],
          "notes": "Created from a file"
        }
        """;

    [Test]
    public async Task Policy_list_gets_one_page_of_policies_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(1), ApiJson.Policy(2)));

        var result = await run.RunAsync("policy", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies")));
            Assert.That(QueryValue(request, "per_page"), Is.EqualTo("100"));
            Assert.That(QueryValue(request, "search"), Is.Null);
            Assert.That(QueryValue(request, "sort"), Is.Null);
            Assert.That(QueryValue(request, "include_disabled"), Is.Null.Or.EqualTo("false").IgnoreCase);
            Assert.That(IntField(JsonAssert.Property(output, "items"), "id"), Is.EqualTo("1,2"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task Policy_list_sends_the_search_option_as_the_search_query_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--search", "web");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies")));
            Assert.That(QueryValue(request, "search"), Is.EqualTo("web"));
        });
    }

    // Enclave.Sdk.Api 1.0.4 writes the flag as include_disabled=True (PoliciesClient.BuildQueryString
    // uses bool.ToString), so the value is compared ignoring case.
    [Test]
    public async Task Policy_list_asks_for_disabled_policies_when_include_disabled_is_given()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--include-disabled");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies")));
            Assert.That(QueryValue(request, "include_disabled"), Is.EqualTo("true").IgnoreCase);
        });
    }

    // Enum option values take the API's names, matched ignoring case, and the API receives its
    // own spelling (proposal, "Options on every command"). PolicySortOrder has the values
    // Description and RecentlyCreated (Enclave.Configuration.Data, Modules/Policies/Enums).
    [TestCase("Description", "Description")]
    [TestCase("description", "Description")]
    [TestCase("RecentlyCreated", "RecentlyCreated")]
    [TestCase("RECENTLYCREATED", "RecentlyCreated")]
    public async Task Policy_list_sends_the_sort_order_named_by_the_sort_option_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--sort", value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies")));
            Assert.That(QueryValue(request, "sort"), Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task Policy_list_rejects_a_sort_order_the_api_does_not_have_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(1)));

        var result = await run.RunAsync("policy", "list", "--sort", "Newest");

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        var request = await RunAcceptedAsync(run, "GET", TestData.OrgPath("policies"), "policy", "list", "--sort", "RecentlyCreated");
        Assert.That(QueryValue(request, "sort"), Is.EqualTo("RecentlyCreated"));
    }

    // -o id feeds pipelines such as `policy list -o id | policy disable - --yes`, so it prints the
    // policy ID that the other policy commands take.
    [Test]
    public async Task Policy_list_with_output_id_prints_one_policy_id_per_line()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(1), ApiJson.Policy(2)));

        var result = await run.RunAsync("policy", "list", "-o", "id");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies")));
            Assert.That(string.Join(",", result.StdoutLines), Is.EqualTo("1,2"));
        });
    }

    [Test]
    public async Task Policy_show_gets_the_policy_and_prints_its_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies/7"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "show", "7");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies/7")));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(7));
        });
    }

    [Test]
    public async Task Policy_update_patches_the_description_and_notes_and_prints_the_updated_policy()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("policies/7"), json: ApiJson.Policy(7, "Web access"));

        var result = await run.RunAsync("policy", "update", "7", "--description", "Web access", "--notes", "Reviewed");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies/7")));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("Web access"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("Reviewed"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("Web access"));
        });
    }

    // A patch sets the fields present and leaves absent fields as they are (proposal, "Create and
    // update"), so a flag that is not given must not appear in the body at all.
    [Test]
    public async Task Policy_update_sends_only_the_fields_whose_options_are_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("policies/7"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "update", "7", "--notes", "Reviewed");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.That(PropertyNames(request.BodyJson), Is.EqualTo("Notes").IgnoreCase);
    }

    // The proposal gives policy update only --description and --notes (and --from-file); an update
    // with none of them has nothing to send.
    [Test]
    public async Task Policy_update_without_any_field_option_exits_2_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("policies/7"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "update", "7");

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, "PATCH", TestData.OrgPath("policies/7"), "policy", "update", "7", "--notes", "Reviewed");
    }

    // A command that accepts several IDs always makes the bulk call, also for one ID, and prints
    // { requested, affected } (proposal, "Several IDs").
    [TestCase("enable")]
    [TestCase("disable")]
    public async Task Policy_enable_and_disable_send_a_single_id_to_the_bulk_route(string verb)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/" + verb), json: ApiJson.Bulk("policiesUpdated", 1));

        var result = await run.RunAsync("policy", verb, "7");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies/" + verb)));
            Assert.That(IntList(JsonAssert.Property(request.BodyJson, "policyIds")), Is.EqualTo("7"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(1));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(1));
        });
    }

    [TestCase("enable")]
    [TestCase("disable")]
    public async Task Policy_enable_and_disable_send_every_id_in_one_bulk_call(string verb)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/" + verb), json: ApiJson.Bulk("policiesUpdated", 2));

        var result = await run.RunAsync("policy", verb, "7", "8");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies/" + verb)));
            Assert.That(IntList(JsonAssert.Property(request.BodyJson, "policyIds")), Is.EqualTo("7,8"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(2));
        });
    }

    // affected below requested means some IDs were unknown or already in that state; the exit code
    // stays 0 so that a re-run after a timeout is safe (proposal, "Several IDs").
    [Test]
    public async Task Policy_disable_reports_fewer_affected_than_requested_and_exits_zero()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/disable"), json: ApiJson.Bulk("policiesUpdated", 1));

        var result = await run.RunAsync("policy", "disable", "7", "8");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies/disable")));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(1));
        });
    }

    // --until uses the API's timed enable, which has no bulk form, and prints the updated model
    // (proposal, "Several IDs"). The body is the API's AutoExpireModel; expiryAction is written by
    // name because Enclave.Sdk.Api 1.0.4 adds JsonStringEnumConverter (Constants.cs).
    [TestCase("Disable", "Disable")]
    [TestCase("disable", "Disable")]
    [TestCase("DISABLE", "Disable")]
    public async Task Policy_enable_until_puts_the_expiry_time_and_action_to_the_enable_until_route(string action, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/7/enable-until"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "enable", "7", "--until", "2026-12-01T09:30:00Z", "--expiry-action", action);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies/7/enable-until")));
            Assert.That(ExpiryTime(body), Is.EqualTo(new DateTimeOffset(2026, 12, 1, 9, 30, 0, TimeSpan.Zero)));
            Assert.That(JsonAssert.Property(body, "expiryAction").GetString(), Is.EqualTo(expected));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(7));
        });
    }

    // --until accepts a relative time such as 7d (AGENTS.md, "CLI contract"). The expected instant
    // is bracketed by the clock before and after the run, so the test does not depend on how long
    // the run takes.
    [Test]
    public async Task Policy_enable_until_accepts_a_relative_time_counted_from_now()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/7/enable-until"), json: ApiJson.Policy(7));

        var before = DateTimeOffset.UtcNow;
        var result = await run.RunAsync("policy", "enable", "7", "--until", "7d", "--expiry-action", "Disable");
        var after = DateTimeOffset.UtcNow;

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var expiry = ExpiryTime(run.SingleRequest().BodyJson);
        Assert.That(expiry, Is.InRange(before.AddDays(7).AddSeconds(-1), after.AddDays(7).AddSeconds(1)));
    }

    // Without --expiry-action the timed enable disables the policy when it expires: the
    // non-destructive action, and the one that needs no --yes.
    [Test]
    public async Task Policy_enable_until_without_an_expiry_action_schedules_a_disable()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/7/enable-until"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "enable", "7", "--until", "2026-12-01T09:30:00Z");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var body = run.SingleRequest().BodyJson;
        Assert.That(JsonAssert.Property(body, "expiryAction").GetString(), Is.EqualTo("Disable"));
    }

    // `enable --until ... --expiry-action Delete` schedules a deletion, so it needs --yes
    // (proposal, "Confirmation").
    [Test]
    public async Task Policy_enable_until_with_the_delete_action_exits_6_without_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/7/enable-until"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "enable", "7", "--until", "2026-12-01T09:30:00Z", "--expiry-action", "Delete");

        AssertRejectedWithoutRequest(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    [Test]
    public async Task Policy_enable_until_with_the_delete_action_and_yes_schedules_a_delete()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/7/enable-until"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "enable", "7", "--until", "2026-12-01T09:30:00Z", "--expiry-action", "delete", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies/7/enable-until")));
            Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo("Delete"));
        });
    }

    // The API's timed enable takes one policy (proposal, "Several IDs": --until takes one ID).
    [Test]
    public async Task Policy_enable_until_with_two_ids_exits_2_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/7/enable-until"), json: ApiJson.Policy(7));
        run.Stub("PUT", TestData.OrgPath("policies/enable"), json: ApiJson.Bulk("policiesUpdated", 2));

        var result = await run.RunAsync("policy", "enable", "7", "8", "--until", "2026-12-01T09:30:00Z");

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, "PUT", TestData.OrgPath("policies/7/enable-until"), "policy", "enable", "7", "--until", "2026-12-01T09:30:00Z");
    }

    // An expiry action describes what happens when --until expires. Without --until it has no
    // meaning, and running a plain enable would drop the caller's intent without a word.
    [Test]
    public async Task Policy_enable_with_an_expiry_action_and_no_until_exits_2_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("policies/enable"), json: ApiJson.Bulk("policiesUpdated", 1));
        run.Stub("PUT", TestData.OrgPath("policies/7/enable-until"), json: ApiJson.Policy(7));

        var result = await run.RunAsync("policy", "enable", "7", "--expiry-action", "Disable");

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(
            run, "PUT", TestData.OrgPath("policies/7/enable-until"), "policy", "enable", "7", "--until", "2026-12-01T09:30:00Z", "--expiry-action", "Disable");
    }

    [Test]
    public async Task Policy_delete_with_yes_sends_every_id_in_one_bulk_delete()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("policies"), json: ApiJson.Bulk("policiesDeleted", 2));

        var result = await run.RunAsync("policy", "delete", "7", "8", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("policies")));
            Assert.That(IntList(JsonAssert.Property(request.BodyJson, "policyIds")), Is.EqualTo("7,8"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(2));
        });
    }

    // delete cannot be undone, so it needs --yes, and the error names --yes so the caller knows
    // what to add (proposal, "Confirmation"; AGENTS.md, "CLI contract").
    [Test]
    public async Task Policy_delete_without_yes_exits_6_naming_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("policies"), json: ApiJson.Bulk("policiesDeleted", 1));

        var result = await run.RunAsync("policy", "delete", "7");

        AssertRejectedWithoutRequest(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    // --dry-run is checked before --yes and prints the request Enclave.Sdk.Api would send, without
    // sending it (proposal, "Dry run" and "Confirmation").
    [Test]
    public async Task Policy_delete_with_dry_run_prints_the_bulk_delete_request_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("policies"), json: ApiJson.Bulk("policiesDeleted", 1));

        var result = await run.RunAsync("policy", "delete", "7", "--dry-run");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var output = result.StdoutJson;
        var request = JsonAssert.Property(output, "request");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo("DELETE"));
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(TestData.OrgPath("policies")));
            Assert.That(IntList(JsonAssert.Property(JsonAssert.Property(request, "body"), "policyIds")), Is.EqualTo("7"));
        });
    }

    // Policies are created from a file only: their content is nested, and flags can express only
    // part of it (proposal, "Command options"). The same create with --from-file is then sent.
    [TestCase("policy create")]
    [TestCase("policy create --description Web")]
    public async Task Policy_create_without_from_file_exits_2_without_sending_a_request(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("policies"), json: ApiJson.Policy(7));
        var filePath = run.WriteFile("policy.json", PolicyCreateFile);

        var result = await run.RunAsync(commandLine.Split(' '));

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, "POST", TestData.OrgPath("policies"), "policy", "create", "--from-file", filePath);
    }

    // Policy IDs are integers, and every ID is checked before any call because Enclave.Sdk.Api
    // 1.0.4 puts IDs into URL paths unescaped (proposal, "ID checks"). The same command with
    // integer IDs is then sent.
    [TestCase("policy show abc", "policy show 7", "GET", "policies/7")]
    [TestCase("policy show ../systems", "policy show 7", "GET", "policies/7")]
    [TestCase("policy update ../tags --notes x", "policy update 7 --notes x", "PATCH", "policies/7")]
    [TestCase("policy disable 7 x8", "policy disable 7 8", "PUT", "policies/disable")]
    [TestCase("policy delete 7 1.5 --yes", "policy delete 7 15 --yes", "DELETE", "policies")]
    public async Task Policy_commands_reject_an_id_that_is_not_an_integer_without_sending_a_request(
        string commandLine, string acceptedCommandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies/7"), json: ApiJson.Policy(7));
        run.Stub("PATCH", TestData.OrgPath("policies/7"), json: ApiJson.Policy(7));
        run.Stub("PUT", TestData.OrgPath("policies/disable"), json: ApiJson.Bulk("policiesUpdated", 2));
        run.Stub("DELETE", TestData.OrgPath("policies"), json: ApiJson.Bulk("policiesDeleted", 2));

        var result = await run.RunAsync(commandLine.Split(' '));

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, method, TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }

    // Single-ID commands exit 5 for an unknown ID (proposal, "Several IDs").
    [TestCase("policy show 7", "GET", "policies/7")]
    [TestCase("policy update 7 --notes x", "PATCH", "policies/7")]
    [TestCase("policy enable 7 --until 2026-12-01T09:30:00Z", "PUT", "policies/7/enable-until")]
    public async Task Policy_single_id_commands_exit_5_when_the_api_reports_not_found(string commandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.StubProblem(method, TestData.OrgPath(path), 404, "Not Found", "Policy 7 does not exist.");

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
    [TestCase("policy list --yes", "policy list", "policies")]
    [TestCase("policy list --dry-run", "policy list", "policies")]
    [TestCase("policy show 7 --yes", "policy show 7", "policies/7")]
    [TestCase("policy show 7 --dry-run", "policy show 7", "policies/7")]
    public async Task Policy_read_commands_reject_change_options_without_sending_a_request(string commandLine, string acceptedCommandLine, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies"), json: ApiJson.Page(ApiJson.Policy(7)));
        run.Stub("GET", TestData.OrgPath("policies/7"), json: ApiJson.Policy(7));

        var result = await run.RunAsync(commandLine.Split(' '));

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, "GET", TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }

    private static void AssertRejectedWithoutRequest(CliRun run, CliResult result, int exitCode, string code)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.Stderr);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo(code));
        });
    }

    // An unknown command or option also exits 2 without a request, so each exit-2 test then runs a
    // corrected command in the same sandbox and checks it reaches the API. That proves the command
    // exists and the rejection came from the input the test changed.
    private static async Task<RecordedRequest> RunAcceptedAsync(CliRun run, string method, string path, params string[] args)
    {
        var accepted = await run.RunAsync(args);

        Assert.That(accepted.ExitCode, Is.Zero, accepted.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
        });
        return request;
    }

    private static string? QueryValue(RecordedRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) ? value : null;

    // Lists are compared as comma-joined strings, which keeps constant arrays out of the
    // assertions (CA1861) and prints both sides readably on failure.
    private static string PropertyNames(JsonElement obj) =>
        string.Join(",", obj.EnumerateObject().Select(property => property.Name));

    private static string IntList(JsonElement array) =>
        string.Join(",", array.EnumerateArray().Select(item => item.GetInt32()));

    private static string IntField(JsonElement array, string name) =>
        string.Join(",", array.EnumerateArray().Select(item => JsonAssert.Property(item, name).GetInt32()));

    private static DateTimeOffset ExpiryTime(JsonElement body) =>
        DateTimeOffset.Parse(JsonAssert.Property(body, "expiryDateTime").GetString()!, CultureInfo.InvariantCulture);
}
