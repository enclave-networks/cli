using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// Commands that take several IDs: the bulk call, the { requested, affected } output, IDs read from
/// stdin, and the limits on how many IDs a command takes (proposal, "Several IDs").
/// </summary>
[Category(TestCategory.Pending)]
public class SeveralIdsTests
{
    private const string Until = "2030-01-01T00:00:00Z";

    private static readonly string[] CountFields = ["requested", "affected"];

    // A multi-ID command makes the bulk call even for one ID, so its output has one shape whatever
    // the number of IDs, and a caller never has to handle two.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_makes_the_bulk_call_for_one_id(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 1);
        var id = command.Id(1);

        var result = await run.RunAsync(command.Args(id));

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(command.Method));
            Assert.That(request.Path, Is.EqualTo(command.Path));
            Assert.That(command.BodyIds(request.BodyJson), Is.EqualTo(new[] { id }));
            AssertCounts(result, requested: 1, affected: 1);
        });
    }

    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_sends_every_id_in_one_bulk_call(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 3);
        var ids = command.Ids(3);

        var result = await run.RunAsync(command.Args(ids));

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(command.Method));
            Assert.That(request.Path, Is.EqualTo(command.Path));
            Assert.That(command.BodyIds(request.BodyJson), Is.EquivalentTo(ids));
            AssertCounts(result, requested: 3, affected: 3);
        });
    }

    // The bulk calls return a count only, and the API counts only the IDs it changed: unknown IDs and
    // IDs already in the requested state are left out (portal UnapprovedSystemsController.cs:170-177
    // for approve). Exit 0 makes a re-run after a timeout safe; the counts carry the difference.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_zero_when_fewer_ids_are_affected_than_requested(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 1);

        var result = await run.RunAsync(command.Args(command.Ids(3)));

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(command.Path));
            Assert.That(result.Stderr, Is.Empty);
            AssertCounts(result, requested: 3, affected: 1);
        });
    }

    // stdin carries the output of another command, such as "list -o id", which may end lines with
    // \r\n on Windows and hold blank lines. The input repeats the first ID so that removing
    // duplicates shows in both the body and "requested".
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_reads_newline_separated_ids_from_stdin_when_the_id_is_a_dash(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 3);
        var ids = command.Ids(3);
        run.StdinText = $"{ids[0]}\r\n\r\n{ids[1]}\n{ids[0]}\n\n{ids[2]}\r\n";

        var result = await run.RunAsync(command.Args("-"));

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(command.Method));
            Assert.That(request.Path, Is.EqualTo(command.Path));
            Assert.That(command.BodyIds(request.BodyJson), Is.EquivalentTo(ids));
            AssertCounts(result, requested: 3, affected: 3);
        });
    }

    // A pipeline fed by an empty list, such as "pending list -o id | pending approve - --yes" with
    // nothing pending, succeeds and changes nothing. The bulk endpoints are not called with an empty
    // list.
    [TestCaseSource(nameof(EmptyStdinCases))]
    public async Task Bulk_command_given_a_dash_and_no_ids_on_stdin_prints_zero_counts_and_sends_nothing(BulkCommand command, string stdin)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 0);
        run.StdinText = stdin;

        var result = await run.RunAsync(command.Args("-"));

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.Stderr, Is.Empty);
            AssertCounts(result, requested: 0, affected: 0);
        });
    }

    // The CLI never waits for input (AGENTS.md, CLI contract), and an agent driving a terminal would
    // hang on a read. stdin holds valid IDs here, so a CLI that read them anyway would send the bulk
    // call. The second run gives the ID on the command line and shows the command itself works, so
    // the rejection comes from the terminal check.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_given_a_dash_exits_2_when_stdin_is_a_terminal(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 1);
        run.StdinIsTerminal = true;
        run.StdinText = BulkCommand.Lines(command.Ids(2));

        var rejected = await run.RunAsync(command.Args("-"));

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(command.Args(command.Id(1)));

        CliAssert.Succeeded(accepted);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(command.Path));
    }

    // The API's bulk limit is 200 IDs (portal Enclave.Utilities/HardLimits.cs:22, MaxBulkIds). The
    // CLI rejects more before any call, so a long list never fails half way through.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_2_for_201_ids_and_sends_200_in_one_call(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 200);

        var rejected = await run.RunAsync(command.Args(command.Ids(201)));

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(command.Args(command.Ids(200)));

        CliAssert.Succeeded(accepted);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(command.Path));
            Assert.That(command.BodyIds(request.BodyJson), Is.EquivalentTo(command.Ids(200)));
            AssertCounts(accepted, requested: 200, affected: 200);
        });
    }

    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Bulk_command_exits_2_for_201_ids_from_stdin_and_sends_200_in_one_call(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, affected: 200);
        run.StdinText = BulkCommand.Lines(command.Ids(201));

        var rejected = await run.RunAsync(command.Args("-"));

        CliAssert.Rejected(run, rejected);

        run.StdinText = BulkCommand.Lines(command.Ids(200));
        var accepted = await run.RunAsync(command.Args("-"));

        CliAssert.Succeeded(accepted);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(command.BodyIds(request.BodyJson), Is.EquivalentTo(command.Ids(200)));
            AssertCounts(accepted, requested: 200, affected: 200);
        });
    }

    // The API's timed enable has no bulk form (PUT .../{id}/enable-until), so --until takes one ID.
    // The second run shows the command accepts --until with one ID, so the rejection comes from the
    // number of IDs.
    [TestCase("system", "ABCDE", "FGHIJ")]
    [TestCase("key", "12", "13")]
    [TestCase("policy", "3", "4")]
    public async Task Enable_until_exits_2_for_several_ids_without_a_request(string noun, string id, string otherId)
    {
        using var run = CliRun.Start();
        var path = EnableUntilPath(noun, id);
        run.Stub("PUT", path, 200, ModelJson(noun, id));

        var rejected = await run.RunAsync(noun, "enable", id, otherId, "--until", Until, "--expiry-action", "Disable");

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(noun, "enable", id, "--until", Until, "--expiry-action", "Disable");

        CliAssert.Succeeded(accepted);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(path));
    }

    // With --until the command makes the single enable-until call, so it prints the updated model
    // as single-ID commands do, and the bulk counts do not apply.
    [TestCase("system", "ABCDE", "systemId")]
    [TestCase("key", "12", "id")]
    [TestCase("policy", "3", "id")]
    public async Task Enable_until_with_one_id_calls_enable_until_and_prints_the_updated_model(string noun, string id, string idField)
    {
        using var run = CliRun.Start();
        var path = EnableUntilPath(noun, id);
        run.Stub("PUT", path, 200, ModelJson(noun, id));

        var result = await run.RunAsync(noun, "enable", id, "--until", Until, "--expiry-action", "Disable");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonAssert.Property(body, "expiryAction").GetString(), Is.EqualTo("Disable"));
            Assert.That(JsonRead.ExpiryDateTime(body), Is.EqualTo(DateTimeOffset.Parse(Until, CultureInfo.InvariantCulture)));
            Assert.That(JsonAssert.Property(output, idField).ToString(), Is.EqualTo(id));
            Assert.That(output.TryGetProperty("requested", out _), Is.False);
        });
    }

    // The API has no bulk zone delete (portal DnsController.cs:145), so dns zone delete takes one ID.
    // The second run shows the command deletes one zone, so the rejection comes from the second ID.
    [Test]
    public async Task Dns_zone_delete_takes_one_id_and_exits_2_for_two_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("dns/zones/4"), 200, ApiJson.Zone(4, "example"));

        var rejected = await run.RunAsync("dns", "zone", "delete", "4", "5", "--yes");

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync("dns", "zone", "delete", "4", "--yes");

        CliAssert.Succeeded(accepted);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/zones/4")));
            Assert.That(JsonAssert.Property(accepted.StdoutJson, "id").GetInt32(), Is.EqualTo(4));
        });
    }

    // Every command reads an empty stdin; one command also reads stdin holding blank lines only,
    // which the proposal ignores, so it holds no IDs either.
    private static IEnumerable<TestCaseData> EmptyStdinCases()
    {
        foreach (var command in BulkCommand.All)
        {
            yield return new TestCaseData(command, string.Empty).SetArgDisplayNames(command.Name, "empty");
        }

        var systemDisable = BulkCommand.All.Single(command => command.Name == "system disable");
        yield return new TestCaseData(systemDisable, "\r\n\n\r\n").SetArgDisplayNames(systemDisable.Name, "blank lines");
    }

    // The enable-until routes of SystemsClient, EnrolmentKeysClient and PoliciesClient
    // (Enclave.Sdk.Api 1.0.4, EnableUntilAsync).
    private static string EnableUntilPath(string noun, string id) => noun switch
    {
        "system" => TestData.OrgPath($"systems/{id}/enable-until"),
        "key" => TestData.OrgPath($"enrolment-keys/{id}/enable-until"),
        _ => TestData.OrgPath($"policies/{id}/enable-until"),
    };

    private static string ModelJson(string noun, string id) => noun switch
    {
        "system" => ApiJson.System(id),
        "key" => ApiJson.Key(int.Parse(id, CultureInfo.InvariantCulture)),
        _ => ApiJson.Policy(int.Parse(id, CultureInfo.InvariantCulture)),
    };

    // The output is exactly { "requested": n, "affected": m } (proposal, "Several IDs").
    private static void AssertCounts(CliResult result, int requested, int affected)
    {
        var output = result.StdoutJson;

        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());
        Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(CountFields), result.ToString());
        Assert.That(output.GetProperty("requested").GetInt32(), Is.EqualTo(requested), result.ToString());
        Assert.That(output.GetProperty("affected").GetInt32(), Is.EqualTo(affected), result.ToString());
    }
}
