using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Test cases built from command lines.
/// </summary>
internal static class CommandCases
{
    /// <summary>
    /// A case whose one argument is the command line <paramref name="args"/>, named by
    /// <paramref name="description"/>.
    /// </summary>
    // The array goes in an object[] so that TestCaseData passes it as the test's one argument; the
    // params object[] constructor would take each string as a separate argument.
    public static TestCaseData Described(string description, params string[] args) =>
        new TestCaseData(new object[] { args }).SetArgDisplayNames(description);

    /// <summary>
    /// A case holding a command line the CLI rejects, the corrected line, the request the corrected
    /// line sends and the body the fake API answers it with, named by the rejected line.
    /// </summary>
    public static TestCaseData RejectedThenCorrected(string rejected, string corrected, string method, string path, string json) =>
        new TestCaseData(rejected.Split(' '), corrected.Split(' '), method, path, json).SetArgDisplayNames(rejected);
}
