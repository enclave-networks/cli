using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

// spec/cli-surface.md "Several IDs".
public class StdinListTests
{
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dash_acts_on_the_items_of_a_list_of_the_commands_kind_on_stdin(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 3);
        var ids = command.Ids(3);
        run.StdinText = command.List(ids);

        var result = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(result, requested: 3, affected: 3);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(command.Method));
            Assert.That(request.Path, Is.EqualTo(command.Path));
            Assert.That(string.Join(",", request.BodyIds(command.BodyField)), Is.EqualTo(string.Join(",", ids)));
        });
    }

    // Several kinds share an ID format, so a list of the wrong kind would act on whatever items share
    // its IDs. The refused list holds IDs the command would accept, so only the kind check can
    // refuse it.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dash_exits_2_and_sends_nothing_for_a_list_of_another_kind(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 2);
        var ids = TestData.Ids(command.OtherKind, 2);
        run.StdinText = CliList.WithIds(command.OtherKind, ids);

        var rejected = await run.RunAsync(command.StdinArgs());

        CliAssert.Rejected(run, rejected);

        run.StdinText = command.List(ids);
        var accepted = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds(command.BodyField)), Is.EqualTo(string.Join(",", ids)));
    }

    [Test]
    public async Task Policy_delete_refuses_a_key_list_so_key_ids_never_delete_policies()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies");
        run.StubBulk("DELETE", path, "policiesDeleted", 2);
        run.StdinText = CliList.Of("key", ApiJson.Key(12, "build agents"), ApiJson.Key(13, "contractor laptops"));

        var rejected = await run.RunAsync("policy", "delete", "-");

        CliAssert.Rejected(run, rejected);

        run.StdinText = CliList.Of("policy", ApiJson.Policy(12, "old vpn"), ApiJson.Policy(13, "web to db"));
        var accepted = await run.RunAsync("policy", "delete", "-");

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds("policyIds")), Is.EqualTo("12,13"));
    }

    // An empty stdin is what a failed list command leaves in a pipe ("Details", "Output").
    [TestCaseSource(nameof(NotAListCases))]
    public async Task Dash_exits_2_and_sends_nothing_for_input_that_is_not_a_list(string stdin)
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/disable");
        run.StubBulk("PUT", path, "systemsUpdated", 2);
        run.StdinText = stdin;

        var rejected = await run.RunAsync("system", "disable", "-");

        CliAssert.Rejected(run, rejected);

        run.StdinText = CliList.WithIds("system", "ABCDE", "FGHIJ");
        var accepted = await run.RunAsync("system", "disable", "-");

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds("systemIds")), Is.EqualTo("ABCDE,FGHIJ"));
    }

    // The item without an ID sits between two with IDs, so a CLI that skipped it and sent the others
    // would show a request.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dash_exits_2_and_sends_nothing_when_a_list_item_has_no_id_field(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 2);
        var ids = command.Ids(2);
        var items = JsonNode.Parse(command.List(ids))!["items"]!.AsArray();
        var withoutId = items[0]!.DeepClone().AsObject();
        withoutId.Remove(CliList.IdField(command.Kind));
        run.StdinText = CliList.Of(command.Kind, items[0]!.ToJsonString(), withoutId.ToJsonString(), items[1]!.ToJsonString());

        var rejected = await run.RunAsync(command.StdinArgs());

        CliAssert.Rejected(run, rejected);
        CliAssert.Problems(rejected.Error, "-");

        run.StdinText = command.List(ids);
        var accepted = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds(command.BodyField)), Is.EqualTo(string.Join(",", ids)));
    }

    // A bare ID per line carries no kind, so the command could not tell key IDs from policy IDs.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dash_exits_2_and_sends_nothing_for_ids_one_per_line(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 2);
        var ids = command.Ids(2);
        run.StdinText = string.Join("\n", ids) + "\n";

        var rejected = await run.RunAsync(command.StdinArgs());

        CliAssert.Rejected(run, rejected);

        run.StdinText = command.List(ids);
        var accepted = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
    }

    // A list built or joined with jq can name an item twice ("Several IDs").
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dash_removes_duplicate_ids_from_the_list_and_keeps_its_order(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 3);
        var ids = command.Ids(3);
        run.StdinText = command.List(ids[1], ids[0], ids[1], ids[2], ids[0]);

        var result = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(result, requested: 3, affected: 3);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds(command.BodyField)), Is.EqualTo(string.Join(",", ids[1], ids[0], ids[2])));
    }

    // A pipeline fed by an empty list, such as system list --pending with nothing waiting, succeeds
    // ("Several IDs").
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dash_with_an_empty_list_prints_zero_counts_makes_no_call_and_exits_0(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 0);
        run.StdinText = command.List();

        var result = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(result, requested: 0, affected: 0);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.ErrorText, Is.Empty);
        });
    }

    // An agent driving a terminal would hang on a read (AGENTS.md "CLI contract"). Any read of stdin
    // fails the run while CliRun.StdinIsTerminal is set, so the rejection shows the command refused
    // without reading.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dash_exits_2_without_reading_stdin_when_stdin_is_a_terminal(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 2);
        run.StdinText = command.List(command.Ids(2));
        run.StdinIsTerminal = true;

        var rejected = await run.RunAsync(command.StdinArgs());

        CliAssert.Rejected(run, rejected);

        run.StdinIsTerminal = false;
        var accepted = await run.RunAsync(command.StdinArgs());

        CliAssert.Bulk(accepted, requested: 2, affected: 2);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(command.Path));
    }

    // Examples 3, 5, 13, 14 and 16. Each list is in the form its list command prints.
    [TestCaseSource(nameof(PipelineExamples))]
    public async Task Example_pipeline_acts_on_every_item_the_list_command_printed(
        string[] args,
        string stdin,
        string method,
        string path,
        string resultField,
        string bodyField,
        string ids,
        int count)
    {
        using var run = CliRun.Start();
        run.StubBulk(method, path, resultField, count);
        run.StdinText = stdin;

        var result = await run.RunAsync(args);

        CliAssert.Bulk(result, requested: count, affected: count);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(string.Join(",", request.BodyIds(bodyField)), Is.EqualTo(ids));
        });
    }

    private static IEnumerable<TestCaseData> NotAListCases()
    {
        yield return NotAList("ids one per line", "ABCDE\nFGHIJ\n");
        yield return NotAList("a JSON array of ids", new JsonArray("ABCDE", "FGHIJ"));
        yield return NotAList("a list without a kind", new JsonObject { ["items"] = new JsonArray(new JsonObject { ["systemId"] = "ABCDE" }), ["total"] = 1 });
        yield return NotAList("a list without items", new JsonObject { ["kind"] = "system", ["total"] = 0 });
        yield return NotAList("items that are not an array", new JsonObject { ["kind"] = "system", ["items"] = new JsonObject(), ["total"] = 0 });
        yield return NotAList("one system", ApiJson.System("ABCDE"));
        yield return NotAList("bulk output", new JsonObject { ["requested"] = 2, ["affected"] = 2 });

        // A list cut short before its closing "]}".
        yield return NotAList("JSON cut short", new JsonObject { ["kind"] = "system", ["items"] = new JsonArray() }.ToJsonString()[..^2]);
        yield return NotAList("nothing", string.Empty);
    }

    private static TestCaseData NotAList(string name, string stdin) => new TestCaseData(stdin).SetArgDisplayNames(name);

    private static TestCaseData NotAList(string name, JsonNode stdin) => NotAList(name, stdin.ToJsonString());

    private static IEnumerable<TestCaseData> PipelineExamples()
    {
        yield return Example(
            "3: system list --pending --key-id 12 | system approve -",
            ["system", "approve", "-"],
            CliList.Of("pending-system", ApiJson.PendingSystem("XYZ12"), ApiJson.PendingSystem("Q7W3E")),
            "PUT",
            "unapproved-systems/approve",
            "systemsApproved",
            "systemIds",
            "XYZ12,Q7W3E");
        yield return Example(
            "5: system list --pending --waiting-for 7d | system decline -",
            ["system", "decline", "-"],
            CliList.Of("pending-system", ApiJson.PendingSystem("M4R8T")),
            "DELETE",
            "unapproved-systems",
            "systemsDeclined",
            "systemIds",
            "M4R8T");
        yield return Example(
            "13: key list --state disabled | key delete -",
            ["key", "delete", "-"],
            CliList.Of("key", ApiJson.Key(12, "build agents 2026-04"), ApiJson.Key(31, "contractor laptops")),
            "DELETE",
            "enrolment-keys",
            "keysDeleted",
            "keyIds",
            "12,31");
        yield return Example(
            "14: system list --not-seen-for 90d | system revoke -",
            ["system", "revoke", "-"],
            CliList.Of("system", ApiJson.System("ABCDE"), ApiJson.System("FGHIJ")),
            "DELETE",
            "systems",
            "systemsRevoked",
            "systemIds",
            "ABCDE,FGHIJ");
        yield return Example(
            "16: dns list-hostnames --filter old-api | dns delete-hostname -",
            ["dns", "delete-hostname", "-"],
            CliList.Of("hostname", ApiJson.Record(7, "old-api"), ApiJson.Record(9, "old-api-v1")),
            "DELETE",
            "dns/records",
            "dnsRecordsDeleted",
            "recordIds",
            "7,9");
    }

    private static TestCaseData Example(string name, string[] args, string stdin, string method, string pathSuffix, string resultField, string bodyField, string ids) =>
        new TestCaseData(args, stdin, method, TestData.OrgPath(pathSuffix), resultField, bodyField, ids, ids.Split(',').Length).SetArgDisplayNames(name);
}
