using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// "-" in place of the arguments reads a list an enclave-cli list command printed, of the command's
/// own kind, and acts on its items by ID (proposed-cli-surface.md "Several IDs").
/// </summary>
[Category(TestCategory.Pending)]
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

    // Keys, policies, zones, hostnames and trust requirements all have integer IDs, and waiting and
    // enrolled systems share one ID format, so a list of the wrong kind would act on whatever items
    // share its IDs. The refused list holds IDs the command would accept, so only the kind check can
    // refuse it. The second run gives the same IDs in a list of the command's kind and shows the
    // command reads lists.
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

    // proposed-cli-surface.md "Several IDs": the kind is what stops key list | policy delete -
    // deleting the policies that share the keys' numbers.
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

    // "Any other kind, or input that is not a list, exits 2" (proposed-cli-surface.md "Several
    // IDs"), and stdin with no bytes is not a list ("Details"). An empty stdin is what a failed list
    // command leaves in a pipe, since a failed list prints nothing ("Output").
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

    // A list built or joined with jq can name an item twice. Each item is acted on once and counted
    // once in requested, and the rest keep the list's order (proposed-cli-surface.md "Several IDs").
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
    // and changes nothing (proposed-cli-surface.md "Several IDs").
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
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // The CLI never waits for input (AGENTS.md "CLI contract"), and an agent driving a terminal
    // would hang on a read. stdin holds a list of the command's kind, so a CLI that read it anyway
    // would send the bulk call; the second run, with stdin redirected, shows the list is accepted.
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

    // Examples 3, 5, 13, 14 and 16 in proposed-cli-surface.md pipe a list command into a bulk
    // command. Each list here is in the form that list command prints, with the API's models as its
    // items.
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
        yield return NotAList("a JSON array of ids", """["ABCDE","FGHIJ"]""");
        yield return NotAList("a list without a kind", """{"items":[{"systemId":"ABCDE"}],"total":1}""");
        yield return NotAList("a list without items", """{"kind":"system","total":0}""");
        yield return NotAList("items that are not an array", """{"kind":"system","items":{},"total":0}""");
        yield return NotAList("one system", ApiJson.System("ABCDE"));
        yield return NotAList("bulk output", """{"requested":2,"affected":2}""");
        yield return NotAList("JSON cut short", """{"kind":"system","items":[""");
        yield return NotAList("nothing", string.Empty);
    }

    private static TestCaseData NotAList(string name, string stdin) => new TestCaseData(stdin).SetArgDisplayNames(name);

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
