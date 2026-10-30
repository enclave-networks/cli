using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Login, logout and status".
public class LogoutTests
{
    private static readonly string[] LogoutFields = ["path", "deleted"];

    // Example 67. The path tells the caller which file went: other tools built on Enclave.Sdk.Api
    // read the same file and lose the token too.
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

    // An agent can run logout without checking first, with or without ENCLAVE_TOKEN set.
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

    // cli.json holds CLI settings, which say nothing about who is signed in and stay for the next
    // login.
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
}
