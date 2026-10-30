using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

internal static class TokenAssert
{
    // Personal access tokens do not expire (portal TokensApiController.Create), and command output
    // ends up in CI logs and agent transcripts (spec/cli-surface.md "Login, logout and status").
    public static void Absent(CliResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Does.Not.Contain(TestData.Token), "The token is on stdout.");
            Assert.That(result.Stderr, Does.Not.Contain(TestData.Token), "The token is on stderr.");
        });
    }
}
