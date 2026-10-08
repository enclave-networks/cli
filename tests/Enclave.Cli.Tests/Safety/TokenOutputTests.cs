using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// No command prints the token, on stdout or stderr, under --verbose and --dry-run too
/// (proposed-cli-surface.md "Login, logout and status").
/// </summary>
// Each command runs with a known token to the point where it could print it, and each test also
// checks what the command did, so a command that printed nothing because it did nothing does not
// pass. Partner commands are covered with their own outcomes in Partner/.
public class TokenOutputTests
{
    // The change request carrying the token shows the CLI held the token it left out.
    [TestCaseSource(typeof(ChangeCommand), nameof(ChangeCommand.All))]
    public async Task Command_that_changes_something_prints_the_token_on_neither_stdout_nor_stderr_under_verbose(ChangeCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);

        var result = await run.RunAsync([.. command.Args, "--verbose"]);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", command.ChangeRequests(run).Select(request => request.Authorization)), Is.EqualTo($"Bearer {TestData.Token}"));
            TokenAssert.Absent(result);
        });
    }

    [TestCaseSource(typeof(ChangeCommand), nameof(ChangeCommand.All))]
    public async Task Dry_run_of_a_command_that_changes_something_prints_the_token_on_neither_stdout_nor_stderr(ChangeCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);

        var result = await run.RunAsync([.. command.Args, "--verbose", "--dry-run"]);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutJson, "dryRun").GetBoolean(), Is.True);
            Assert.That(command.ChangeRequests(run), Is.Empty);
            TokenAssert.Absent(result);
        });
    }

    // The token comes from ENCLAVE_TOKEN or from credentials.json (proposed-cli-surface.md "Options
    // on every command"), and either way stays out of the output.
    [TestCaseSource(typeof(ChangeCommand), nameof(ChangeCommand.All))]
    public async Task Token_from_credentials_json_is_printed_on_neither_stdout_nor_stderr(ChangeCommand command)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.SaveCredentials(TestData.Token);
        command.Arrange(run);

        var result = await run.RunAsync([.. command.Args, "--verbose"]);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", command.ChangeRequests(run).Select(request => request.Authorization)), Is.EqualTo($"Bearer {TestData.Token}"));
            TokenAssert.Absent(result);
        });
    }

    // login saves the token, logout deletes it, and org use saves a default organisation. Whether
    // the file exists afterwards shows the command ran.
    [TestCaseSource(nameof(LocalCommands))]
    public async Task Command_that_changes_local_files_prints_the_token_on_neither_stdout_nor_stderr(LocalCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);

        var result = await run.RunAsync([.. command.Args, "--verbose"]);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Files.Exists(command.FilePath(run)), Is.EqualTo(command.FileExistsAfter));
            TokenAssert.Absent(result);
        });
    }

    // partner use is covered with the other partner commands in Partner/.
    private static IEnumerable<LocalCommand> LocalCommands() =>
        LocalCommand.All.Where(command => command.Name != "partner use");
}
