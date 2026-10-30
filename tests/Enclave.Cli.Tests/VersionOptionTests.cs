using System.Reflection;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests;

public class VersionOptionTests
{
    // In-process, the entry assembly is the test host, so a version read from it fails this test,
    // provided the two versions differ, which the first check makes sure of.
    [Test]
    public async Task Version_option_prints_the_enclave_cli_informational_version_and_exits_zero()
    {
        using var run = CliRun.Start();
        var expected = InformationalVersion(typeof(Program).Assembly);

        Assert.That(InformationalVersion(Assembly.GetEntryAssembly()), Is.Not.EqualTo(expected), "The entry assembly carries enclave-cli's version, so this test cannot tell the two apart.");

        var result = await run.RunAsync("--version");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StdoutLines, Is.EqualTo(new[] { expected }));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    private static string? InformationalVersion(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
}
