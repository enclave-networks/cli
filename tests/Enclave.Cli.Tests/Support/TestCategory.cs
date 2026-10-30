namespace Enclave.Cli.Tests.Support;

// NUnit categories the CI workflow filters on.
internal static class TestCategory
{
    // Behaviour the CLI does not meet; CI leaves this category out (AGENTS.md "Workflow").
    public const string Pending = "Pending";
}
