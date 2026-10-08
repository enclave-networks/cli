using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// Commands that take several items: the bulk call, the { requested, affected } output, calls of at
/// most 200 IDs, and the commands that take one item (proposed-cli-surface.md "Several IDs").
/// </summary>
public class SeveralIdsTests
{
    private const string Until = "2030-01-01T00:00:00Z";

    // An --until that has passed exits 2 ("Details"), so the timed enables run on a clock fixed
    // before Until, which keeps their answer the same whatever the date.
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);

    // A command that accepts several IDs makes the bulk call for one ID too, so its output has one
    // shape whatever the number of IDs, and a caller handles one form.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_makes_the_bulk_call_for_one_item(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 1);
        var id = command.Ids(1)[0];

        var result = await run.RunAsync(command.Args(id));

        CliAssert.Bulk(result, requested: 1, affected: 1);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(command.Method));
            Assert.That(request.Path, Is.EqualTo(command.Path));
            Assert.That(string.Join(",", request.BodyIds(command.BodyField)), Is.EqualTo(id));
        });
    }

    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_sends_every_item_given_in_one_call_in_the_order_given(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 3);
        var ids = command.Ids(3);

        var result = await run.RunAsync(command.Args(ids));

        CliAssert.Bulk(result, requested: 3, affected: 3);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(command.Method));
            Assert.That(request.Path, Is.EqualTo(command.Path));
            Assert.That(string.Join(",", request.BodyIds(command.BodyField)), Is.EqualTo(string.Join(",", ids)));
        });
    }

    // The bulk calls return a count only, and the API counts the items it changed: unknown IDs and
    // items already in that state are left out (portal UnapprovedSystemsController.cs:170-177 for
    // approve). Exit 0 makes a re-run after a timeout safe, and the counts carry the difference.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_0_when_fewer_items_are_affected_than_requested(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 1);

        var result = await run.RunAsync(command.Args(command.Ids(3)));

        CliAssert.Bulk(result, requested: 3, affected: 1);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(command.Path));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // The API takes at most 200 IDs per bulk call (portal Enclave.Utilities/HardLimits.cs:22,
    // MaxBulkIds). 201 IDs is the smallest split, and 450 needs a third call that is part full. Each
    // call answers a different count, so the output shows the CLI added up every call's count.
    [TestCaseSource(nameof(SplitCases))]
    public async Task Bulk_command_sends_more_than_200_items_in_calls_of_200_in_order_and_adds_up_the_counts(BulkCommand command, int count)
    {
        using var run = CliRun.Start();
        var affected = AffectedPerCall(count);
        command.StubBulk(run, affected);
        var ids = command.Ids(count);

        var result = await run.RunAsync(command.Args(ids));

        CliAssert.Bulk(result, requested: count, affected: affected.Sum());
        Assert.Multiple(() =>
        {
            Assert.That(command.IdsSent(run), Is.EqualTo(Batches(ids)));
            Assert.That(run.Requests, Has.Count.EqualTo(affected.Length));
        });
    }

    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_sends_a_list_of_more_than_200_items_from_stdin_in_calls_of_200(BulkCommand command)
    {
        using var run = CliRun.Start();
        var affected = AffectedPerCall(450);
        command.StubBulk(run, affected);
        var ids = command.Ids(450);
        run.StdinText = command.List(ids);

        var result = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(result, requested: 450, affected: affected.Sum());
        Assert.That(command.IdsSent(run), Is.EqualTo(Batches(ids)));
    }

    // A failed call stops the command, so nothing more is sent after a failure. The counts of the
    // calls that succeeded tell the caller what changed, and since items already in that state are
    // not counted, running the command again is safe (proposed-cli-surface.md "Several IDs").
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_stops_at_a_failed_call_and_its_error_carries_the_counts_of_the_calls_that_succeeded(BulkCommand command)
    {
        using var run = CliRun.Start();
        run.StubSequence(
            command.Method,
            command.Path,
            (200, ApiJson.Bulk(command.ResultField, 190)),
            (503, ApiJson.Problem(503, "Service Unavailable", "Try again later.")));
        var ids = command.Ids(450);

        var result = await run.RunAsync(command.Args(ids));

        var error = CliAssert.Failed(result, "transient");
        Assert.Multiple(() =>
        {
            Assert.That(Count(error, "requested"), Is.EqualTo(200));
            Assert.That(Count(error, "affected"), Is.EqualTo(190));
            Assert.That(command.IdsSent(run), Is.EqualTo(Batches(ids[..400])));
        });
    }

    // The error is the failed call's own, with the exit code its status maps to (AGENTS.md "CLI
    // contract"), and carries the counts of the call before it.
    [TestCase(403, "forbidden")]
    [TestCase(429, "transient")]
    public async Task Bulk_command_that_fails_part_way_exits_with_the_code_of_the_failed_call(int status, string code)
    {
        using var run = CliRun.Start();
        var command = BulkCommand.All.Single(candidate => candidate.Name == "system revoke");
        run.StubSequence(
            command.Method,
            command.Path,
            (200, ApiJson.Bulk(command.ResultField, 200)),
            (status, ApiJson.Problem(status, "Request failed")));

        var result = await run.RunAsync(command.Args(command.Ids(201)));

        var error = CliAssert.Failed(result, code);
        Assert.Multiple(() =>
        {
            Assert.That(Count(error, "requested"), Is.EqualTo(200));
            Assert.That(Count(error, "affected"), Is.EqualTo(200));
            Assert.That(command.BulkCalls(run), Has.Count.EqualTo(2));
        });
    }

    // With no call succeeded nothing changed, and the counts say so.
    [Test]
    public async Task Bulk_command_whose_first_call_fails_reports_nothing_requested_or_affected()
    {
        using var run = CliRun.Start();
        var command = BulkCommand.All.Single(candidate => candidate.Name == "policy delete");
        run.StubProblem(command.Method, command.Path, 403, "Forbidden", "The token lacks the scope this call needs.");

        var result = await run.RunAsync(command.Args(command.Ids(450)));

        var error = CliAssert.Failed(result, "forbidden");
        Assert.Multiple(() =>
        {
            Assert.That(Count(error, "requested"), Is.Zero);
            Assert.That(Count(error, "affected"), Is.Zero);
            Assert.That(command.BulkCalls(run), Has.Count.EqualTo(1));
        });
    }

    // Example 44 in proposed-cli-surface.md.
    [Test]
    public async Task System_disable_disables_three_systems_given_as_arguments_in_one_call()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/disable");
        run.StubBulk("PUT", path, "systemsUpdated", 3);

        var result = await run.RunAsync("system", "disable", "ABCDE", "FGHIJ", "KLMNO");

        CliAssert.Bulk(result, requested: 3, affected: 3);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(string.Join(",", request.BodyIds("systemIds")), Is.EqualTo("ABCDE,FGHIJ,KLMNO"));
        });
    }

    // Example 45 in proposed-cli-surface.md: the shell splits $(cat quarantine.txt) at its line
    // breaks, so each line arrives as an argument, and a long file is sent in calls of 200.
    [Test]
    public async Task System_disable_takes_the_lines_of_a_file_as_system_id_arguments()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/disable");
        run.StubBulk("PUT", path, "systemsUpdated", 200, 50);
        var ids = TestData.Ids("system", 250);
        var quarantine = string.Join("\n", ids) + "\n";

        var result = await run.RunAsync(["system", "disable", .. quarantine.Split('\n', StringSplitOptions.RemoveEmptyEntries)]);

        CliAssert.Bulk(result, requested: 250, affected: 250);
        Assert.That(
            string.Join("|", run.RequestsTo("PUT", path).Select(request => string.Join(",", request.BodyIds("systemIds")))),
            Is.EqualTo(Batches(ids)));
    }

    // Example 35 in proposed-cli-surface.md. --id gives IDs, so no lookup precedes the call.
    [Test]
    public async Task Policy_disable_takes_several_ids_after_id_separated_by_commas()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies/disable");
        run.StubBulk("PUT", path, "policiesUpdated", 3);

        var result = await run.RunAsync("policy", "disable", "--id", "42,43,57");

        CliAssert.Bulk(result, requested: 3, affected: 3);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(string.Join(",", request.BodyIds("policyIds")), Is.EqualTo("42,43,57"));
        });
    }

    // Names are looked up with one list call, and each must match one item, ignoring case
    // (proposed-cli-surface.md "Names and IDs"). The bulk call then carries the IDs the names found.
    [Test]
    public async Task Policy_disable_looks_up_several_names_with_one_list_call_and_disables_them_in_one_call()
    {
        using var run = CliRun.Start();
        var policies = TestData.OrgPath("policies");
        var disable = TestData.OrgPath("policies/disable");
        run.Stub("GET", policies, 200, ApiJson.Page(ApiJson.Policy(42, "web to db"), ApiJson.Policy(17, "Old VPN"), ApiJson.Policy(23, "contractors")));
        run.StubBulk("PUT", disable, "policiesUpdated", 2);

        var result = await run.RunAsync("policy", "disable", "web to db", "old vpn");

        CliAssert.Bulk(result, requested: 2, affected: 2);
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", policies), Has.Count.EqualTo(1));
            Assert.That(
                string.Join("|", run.RequestsTo("PUT", disable).Select(request => string.Join(",", request.BodyIds("policyIds")))),
                Is.EqualTo("42,17"));
        });
    }

    // One name that matches no policy, or several, stops the whole command: deleting the others
    // would leave the caller to work out which went. The error carries the candidates, the
    // policies the name matched, so the caller can pick one by ID without another call
    // (proposed-cli-surface.md "Names and IDs", "Errors and exit codes"). "old vpn" matches two
    // policies that differ only in case; "no such policy" matches none.
    [TestCase("no such policy", "[]")]
    [TestCase("old vpn", "[17,18]")]
    public async Task Policy_delete_exits_2_with_the_candidates_and_deletes_nothing_when_a_name_matches_no_policy_or_several(string name, string candidateIds)
    {
        using var run = CliRun.Start();
        var policies = TestData.OrgPath("policies");
        run.Stub("GET", policies, 200, ApiJson.Page(ApiJson.Policy(42, "web to db"), ApiJson.Policy(17, "old vpn"), ApiJson.Policy(18, "Old VPN")));
        run.StubBulk("DELETE", policies, "policiesDeleted", 1);

        var result = await run.RunAsync("policy", "delete", "web to db", name);

        var error = CliAssert.Failed(result, "invalid_argument");
        var candidates = JsonAssert.Property(error, "candidates");
        Assert.Multiple(() =>
        {
            Assert.That(candidates.ValueKind, Is.EqualTo(JsonValueKind.Array), result.ToString());
            Assert.That(run.RequestsTo("DELETE", policies), Is.Empty);
        });
        Assert.That(CandidateIds(candidates), Is.EqualTo(candidateIds));
    }

    // Duplicate IDs are removed whether given as arguments, with --id or through "-", and the rest
    // keep the order given (proposed-cli-surface.md "Several IDs"). Each item is acted on once and
    // counted once in requested.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_removes_duplicate_ids_given_as_arguments_and_keeps_the_order_given(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 3);
        var ids = command.Ids(3);

        var result = await run.RunAsync(command.Args(ids[1], ids[0], ids[1], ids[2], ids[0]));

        CliAssert.Bulk(result, requested: 3, affected: 3);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds(command.BodyField)), Is.EqualTo(string.Join(",", ids[1], ids[0], ids[2])));
    }

    // The API's timed enable has no bulk form (PUT .../{id}/enable-until), so --for and --until take
    // one item and print its updated model (proposed-cli-surface.md "Several IDs"). The second run
    // shows the command takes the option, so the refusal comes from the number of items.
    [TestCaseSource(nameof(TimedEnableCases))]
    public async Task Timed_enable_exits_2_for_several_items_and_prints_the_updated_model_for_one(
        string[] several,
        string[] one,
        string path,
        string response,
        string idField,
        string id)
    {
        using var run = CliRun.Start();
        run.Time = Clock;
        run.Stub("PUT", path, 200, response);

        var rejected = await run.RunAsync(several);

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(one);

        CliAssert.Succeeded(accepted);
        var request = run.SingleRequest();
        var output = accepted.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonAssert.Property(output, idField).ToString(), Is.EqualTo(id));
            Assert.That(output.TryGetProperty("requested", out _), Is.False);
        });
    }

    // The API has no bulk zone delete (portal DnsController.cs:145), so dns delete-zone takes one
    // zone and prints the deleted zone's model.
    [Test]
    public async Task Dns_delete_zone_exits_2_for_several_zones_and_deletes_one_zone_given_alone()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("dns/zones/4");
        run.Stub("DELETE", path, 200, ApiJson.Zone(4, "internal"));
        run.Stub("DELETE", TestData.OrgPath("dns/zones/5"), 200, ApiJson.Zone(5, "lab"));

        var rejected = await run.RunAsync("dns", "delete-zone", "--id", "4,5");

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync("dns", "delete-zone", "--id", "4");

        CliAssert.Succeeded(accepted);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonAssert.Property(accepted.StdoutJson, "id").GetInt32(), Is.EqualTo(4));
        });
    }

    // A bulk call reports unknown IDs through its counts. A command on one item has no count to
    // report it with, so an unknown ID is an error with its own exit code (proposed-cli-surface.md
    // "Several IDs").
    [TestCaseSource(nameof(SingleItemCases))]
    public async Task Single_item_command_exits_5_for_an_unknown_id(string[] args, string method, string path)
    {
        using var run = CliRun.Start();
        run.StubProblem(method, path, 404, "Not Found", "No item has this ID.");

        var result = await run.RunAsync(args);

        CliAssert.Failed(result, "not_found");
        Assert.That(run.RequestsTo(method, path), Has.Count.EqualTo(1));
    }

    private static IEnumerable<TestCaseData> SplitCases()
    {
        foreach (var command in BulkCommand.All)
        {
            yield return new TestCaseData(command, 201).SetArgDisplayNames(command.Name, "201");
            yield return new TestCaseData(command, 450).SetArgDisplayNames(command.Name, "450");
        }
    }

    private static IEnumerable<TestCaseData> TimedEnableCases()
    {
        yield return TimedEnable("system enable --for", ["system", "enable", "ABCDE", "FGHIJ", "--for", "8h"], ["system", "enable", "ABCDE", "--for", "8h"], "systems/ABCDE/enable-until", ApiJson.System("ABCDE"), "systemId", "ABCDE");
        yield return TimedEnable("key enable --for", ["key", "enable", "--id", "12,13", "--for", "8h"], ["key", "enable", "--id", "12", "--for", "8h"], "enrolment-keys/12/enable-until", ApiJson.Key(12), "id", "12");
        yield return TimedEnable("policy enable --until", ["policy", "enable", "--id", "42,43", "--until", Until], ["policy", "enable", "--id", "42", "--until", Until], "policies/42/enable-until", ApiJson.Policy(42), "id", "42");
    }

    private static TestCaseData TimedEnable(string name, string[] several, string[] one, string pathSuffix, string response, string idField, string id) =>
        new TestCaseData(several, one, TestData.OrgPath(pathSuffix), response, idField, id).SetArgDisplayNames(name);

    private static IEnumerable<TestCaseData> SingleItemCases()
    {
        yield return SingleItem("system show", ["system", "show", "ABCDE"], "GET", "systems/ABCDE");
        yield return SingleItem("system update", ["system", "update", "ABCDE", "--description", "web server"], "PATCH", "systems/ABCDE");
        yield return SingleItem("system enable --for", ["system", "enable", "ABCDE", "--for", "8h"], "PUT", "systems/ABCDE/enable-until");
        yield return SingleItem("key enable --for", ["key", "enable", "--id", "12", "--for", "8h"], "PUT", "enrolment-keys/12/enable-until");
        yield return SingleItem("policy update", ["policy", "update", "--id", "42", "--description", "web to db"], "PATCH", "policies/42");
        yield return SingleItem("dns delete-zone", ["dns", "delete-zone", "--id", "4"], "DELETE", "dns/zones/4");
    }

    private static TestCaseData SingleItem(string name, string[] args, string method, string pathSuffix) =>
        new TestCaseData(args, method, TestData.OrgPath(pathSuffix)).SetArgDisplayNames(name);

    // The counts successive bulk calls answer with, one per call of up to 200 IDs.
    private static int[] AffectedPerCall(int count) => count switch
    {
        201 => [190, 1],
        450 => [200, 150, 40],
        _ => throw new ArgumentOutOfRangeException(nameof(count), count, "No counts for this number of IDs."),
    };

    // The IDs of each call joined with commas, and the calls joined with "|", for IDs sent in calls
    // of 200 in the order given: the form BulkCommand.IdsSent reads the requests in.
    private static string Batches(string[] ids) =>
        string.Join("|", ids.Chunk(200).Select(batch => string.Join(",", batch)));

    // The IDs of the candidates in an error, in ascending order, as "[a,b]".
    private static string CandidateIds(JsonElement candidates) =>
        "[" + string.Join(",", candidates.EnumerateArray().Select(candidate => JsonAssert.Property(candidate, "id").GetInt32()).Order()) + "]";

    // The bulk counts sit in the error object beside "code" (proposed-cli-surface.md "Several
    // IDs"). A count that is missing or not a number reads as null, which no expected count equals.
    private static int? Count(JsonElement error, string name) =>
        error.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;
}
