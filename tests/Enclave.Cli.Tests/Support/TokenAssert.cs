using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Checks that a run's output does not hold the token.
/// </summary>
internal static class TokenAssert
{
    /// <summary>
    /// Asserts TestData.Token appears in neither stdout nor stderr.
    /// </summary>
    // No command prints the token, under --verbose and --dry-run too (proposed-cli-surface.md
    // "Login, logout and status"). Personal access tokens do not expire (portal
    // Enclave.Accounts/Controllers/Api/TokensApiController.cs:105), and command output ends up in CI
    // logs and agent transcripts.
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
