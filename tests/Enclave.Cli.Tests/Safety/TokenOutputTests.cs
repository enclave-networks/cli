using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

// spec/cli-surface.md "Login, logout and status". Each test also checks what the command did, so a
// command that printed nothing because it did nothing does not pass. Partner/ covers the partner
// commands.
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

    // Whether the file exists afterwards shows the command ran.
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
