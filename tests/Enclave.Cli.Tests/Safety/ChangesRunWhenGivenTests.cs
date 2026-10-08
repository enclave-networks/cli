using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// A command that changes something runs when given: there is no confirmation option and no
/// prompt, and stdin is read only after "-" (proposed-cli-surface.md "Changes run when given";
/// AGENTS.md "CLI contract").
/// </summary>
public class ChangesRunWhenGivenTests
{
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
    // does not watch would hang on a question. stdin is a terminal holding "n", the answer that
    // would refuse a prompt, so a CLI that asked would send nothing. stdout holds the command's
    // JSON object alone: the model, the bulk counts, or {} for a response with no body
    // (proposed-cli-surface.md "Several IDs").
    [TestCaseSource(typeof(ChangeCommand), nameof(ChangeCommand.All))]
    public async Task Command_that_changes_something_runs_without_a_prompt_when_stdin_is_a_terminal(ChangeCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);
        run.StdinIsTerminal = true;
        run.StdinText = "n\n";

        var result = await run.RunAsync(command.Args);

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(command.ChangeRequests(run), Has.Length.EqualTo(1));
            Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // IDs come from stdin only after "-" (proposed-cli-surface.md "Several IDs"). stdin holds a list
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
