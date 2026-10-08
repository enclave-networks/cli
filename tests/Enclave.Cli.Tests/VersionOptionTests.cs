using System.CommandLine;
using System.Reflection;
using NUnit.Framework;

namespace Enclave.Cli.Tests;

public class VersionOptionTests
{
    // Agents check the version before relying on a command's behaviour, so --version must report
    // the version of enclave-cli itself, whichever process hosts it. The test runs the CLI in-process,
    // where the entry assembly is the test host, so a version read from the entry assembly fails
    // this test.
    [Test]
    public async Task Version_option_prints_the_enclave_cli_informational_version_and_exits_zero()
    {
        using var output = new StringWriter();

        var exitCode = await Program.CreateRootCommand()
            .Parse(["--version"])
            .InvokeAsync(new InvocationConfiguration { Output = output });

        var expected = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(output.ToString().Trim(), Is.EqualTo(expected));
        });
    }
}
