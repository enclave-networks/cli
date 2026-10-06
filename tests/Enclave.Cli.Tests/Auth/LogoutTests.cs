using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

[Category(TestCategory.Pending)]
public class LogoutTests
{
    // logout is local: it removes the saved token and makes no API call. Printing the path tells the
    // caller which file went, which matters because other tools built on Enclave.Sdk.Api read the
    // same file (proposal "Login, logout and status").
    [Test]
    public async Task Logout_deletes_credentials_json_prints_its_path_and_makes_no_request()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(TestData.Token);

        var result = await run.RunAsync("logout");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var output = result.StdoutJson;

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(run.CredentialsPath), Is.False);
            Assert.That(Path.GetFullPath(JsonAssert.Property(output, "path").GetString()!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            Assert.That(JsonAssert.Property(output, "deleted").ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // Deleting the file does not revoke the token, and personal access tokens never expire (portal
    // TokensApiController.cs:105), so the caller is told where the token must be revoked.
    [Test]
    public async Task Logout_says_the_token_stays_valid_until_it_is_revoked_in_the_portal()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(TestData.Token);

        var result = await run.RunAsync("logout");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(result.StdoutJson.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(result.Stdout, Does.Contain("revoke").IgnoreCase.And.Contain("portal").IgnoreCase);
        });
    }

    // Logging out leaves the machine with no saved token whether or not one was saved before, so a
    // repeated logout succeeds and reports that there was nothing to delete. An agent can then run
    // logout without checking first.
    [Test]
    public async Task Logout_exits_0_and_reports_nothing_deleted_when_there_is_no_credentials_file()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("logout");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var output = result.StdoutJson;

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFullPath(JsonAssert.Property(output, "path").GetString()!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            Assert.That(JsonAssert.Property(output, "deleted").ValueKind, Is.EqualTo(JsonValueKind.False));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // logout removes the token only. cli.json holds CLI settings (the default organisation and
    // partner), which say nothing about who is signed in and stay for the next login.
    [Test]
    public async Task Logout_leaves_cli_json_in_place()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(TestData.Token);
        const string Settings = "{}";
        await File.WriteAllTextAsync(run.CliConfigPath, Settings);

        var result = await run.RunAsync("logout");

        var settingsAfter = File.Exists(run.CliConfigPath) ? await File.ReadAllTextAsync(run.CliConfigPath) : null;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(File.Exists(run.CredentialsPath), Is.False);
            Assert.That(settingsAfter, Is.EqualTo(Settings));
        });
    }
}
