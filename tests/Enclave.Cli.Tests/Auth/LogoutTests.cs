using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Login, logout and status".
public class LogoutTests
{
    private static readonly string[] LogoutFields = ["path", "deleted"];

    // Example 67. The path tells the caller which file went: other tools built on Enclave.Sdk.Api
    // read the same file and lose the token too. The default organisation goes with it.
    [Test]
    public async Task Logout_deletes_credentials_json_with_the_default_organisation_prints_its_path_and_makes_no_request()
    {
        using var run = CliRun.Start();
        run.SaveLogin(defaultOrgId: TestData.OrgId.ToString());

        var result = await run.RunAsync("logout");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(run.Files.Paths, Is.Empty);
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(LogoutFields));
            Assert.That(Path.GetFullPath(JsonAssert.Property(output, "path").GetString()!), Is.EqualTo(Path.GetFullPath(run.CredentialsPath)));
            Assert.That(JsonAssert.Property(output, "deleted").ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // An agent can run logout without checking first.
    [Test]
    public async Task Logout_exits_0_and_reports_no_file_when_there_is_no_credentials_file()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_PAT");

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
}
