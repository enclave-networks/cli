using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// A command that changes something runs when given: there is no confirmation option and no
/// prompt, and stdin is read only after "-" (spec/cli-surface.md "Changes run when given";
/// AGENTS.md "CLI contract").
/// </summary>
public class ChangesRunWhenGivenTests
{
    // The field that identifies the model each ChangeCommand that takes one item answers with, in
    // the ApiJson body its change is answered with. A command missing here fails its case, so a
    // command added to ChangeCommand.All needs its field named.
    private static readonly Dictionary<string, string> ModelIdFields = new(StringComparer.Ordinal)
    {
        ["org update"] = "id",
        ["org remove-user"] = "id",
        ["org invite"] = "emailAddress",
        ["org cancel-invite"] = "emailAddress",
        ["system update"] = "systemId",
        ["system update --pending"] = "systemId",
        ["system enable --for"] = "systemId",
        ["key create"] = "id",
        ["key update"] = "id",
        ["key enable --for"] = "id",
        ["policy create"] = "id",
        ["policy update"] = "id",
        ["policy enable --for"] = "id",
        ["tag set"] = "tag",
        ["dns create-zone"] = "id",
        ["dns update-zone"] = "id",
        ["dns delete-zone"] = "id",
        ["dns create-hostname"] = "id",
        ["dns update-hostname"] = "id",
        ["trust create"] = "id",
        ["trust update"] = "id",
    };

    // There is no confirmation option, so --yes is an unknown option, a parse error that exits 2
    // and sends nothing. A parse error alone does not show the command exists, so the second run,
    // without --yes, shows it sends its change.
    [TestCaseSource(typeof(ChangeCommand), nameof(ChangeCommand.All))]
    public async Task Yes_is_an_unknown_option_on_a_command_that_changes_something(ChangeCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);

        var rejected = await run.RunAsync([.. command.Args, "--yes"]);

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(command.Args);

        CliAssert.Succeeded(accepted);
        Assert.That(command.ChangeRequests(run), Has.Length.EqualTo(1));
    }

    // The CLI never prompts (AGENTS.md "CLI contract"): an agent running a command in a terminal it
    // does not watch would hang on a question. stdin is a terminal, and any read of it fails the run
    // (CliRun.StdinIsTerminal), so a CLI that asked fails the test. stdout holds the command's
    // JSON object alone: the bulk counts for a command that takes several items, and otherwise the
    // model the API answered the change with (spec/cli-surface.md "Several IDs": single-ID
    // commands print the model, and org invite prints the invite the API returns). The model is
    // checked by the field that identifies it, so an output that leaves the model out, such as {},
    // fails.
    [TestCaseSource(typeof(ChangeCommand), nameof(ChangeCommand.All))]
    public async Task Command_that_changes_something_runs_without_a_prompt_when_stdin_is_a_terminal(ChangeCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);
        run.StdinIsTerminal = true;

        var result = await run.RunAsync(command.Args);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(command.ChangeRequests(run), Has.Length.EqualTo(1));
            Assert.That(result.Stderr, Is.Empty);
        });

        if (BulkCommand.All.Any(bulk => bulk.Name == command.Name))
        {
            CliAssert.Bulk(result, requested: 1, affected: 1);
            return;
        }

        var field = ModelIdFields[command.Name];
        Assert.That(
            JsonAssert.Property(result.StdoutJson, field).GetRawText(),
            Is.EqualTo(JsonAssert.Property(JsonRead.Parse(command.Response!), field).GetRawText()),
            result.ToString());
    }

    // IDs come from stdin only after "-" (spec/cli-surface.md "Several IDs"). stdin holds a list
    // of other items of the command's own kind, so a CLI that read it would add them to the call.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Command_given_ids_as_arguments_leaves_a_list_on_stdin_unread(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 1);
        var ids = command.Ids(3);
        run.StdinText = command.List(ids[1], ids[2]);

        var result = await run.RunAsync(command.Args(ids[0]));

        CliAssert.Bulk(result, requested: 1, affected: 1);
        Assert.That(string.Join(",", run.SingleRequest().BodyIds(command.BodyField)), Is.EqualTo(ids[0]));
    }
}
