using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// logout deletes credentials.json and prints { "path", "deleted" }, deleted being false when there
// was no file (proposed-cli-surface.md "Login, logout and status").
[Category(TestCategory.Pending)]
public class LogoutTests
{
    private static readonly string[] LogoutFields = ["path", "deleted"];

    // Example 67. logout is local: it removes the saved token and makes no API call. Printing the
    // path tells the caller which file went, which matters because other tools built on
    // Enclave.Sdk.Api read the same file and lose the token too.
    [Test]
    public async Task Logout_deletes_credentials_json_prints_its_path_and_makes_no_request()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(TestData.Token);

        var result = await run.RunAsync("logout");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(LogoutFields));
            Assert.That(Path.GetFullPath(JsonAssert.Property(output, "path").GetString()!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            Assert.That(JsonAssert.Property(output, "deleted").ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // After logout the machine holds no saved token whether or not one was saved before, so a
    // repeated logout succeeds and reports that there was no file. An agent can run logout without
    // checking first, with or without ENCLAVE_TOKEN set, since logout needs no token.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Logout_exits_0_and_reports_no_file_when_there_is_no_credentials_file(bool environmentToken)
    {
        using var run = CliRun.Start();

        if (!environmentToken)
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
        }

        var result = await run.RunAsync("logout");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(LogoutFields));
            Assert.That(Path.GetFullPath(JsonAssert.Property(output, "path").GetString()!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            Assert.That(JsonAssert.Property(output, "deleted").ValueKind, Is.EqualTo(JsonValueKind.False));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // logout deletes the token file only. cli.json holds CLI settings (the default organisation and
    // partner, AGENTS.md "CLI contract"), which say nothing about who is signed in and stay for the
    // next login.
    [Test]
    public async Task Logout_leaves_cli_json_in_place()
    {
        using var run = CliRun.Start();
        const string Settings = "{}";
        run.SaveCredentials(TestData.Token);
        run.Files.WriteText(run.CliConfigPath, Settings, privateToUser: false);

        var result = await run.RunAsync("logout");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
            Assert.That(run.Files.ReadText(run.CliConfigPath), Is.EqualTo(Settings));
        });
    }

    // logout changes only a local file, so it does not take --dry-run ("Dry run"), and an option a
    // command does not take exits 2 ("Details") with the file left in place. The run without the
    // option proves the rejection withheld the deletion.
    [Test]
    public async Task Logout_does_not_take_dry_run_and_keeps_the_file()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(TestData.Token);

        CliAssert.Rejected(run, await run.RunAsync("logout", "--dry-run"));
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.True);

        CliAssert.Succeeded(await run.RunAsync("logout"));
        Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
    }
}
