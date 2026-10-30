using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

internal static class CommandCases
{
    // A case whose one argument is the command line args, named by description.
    // The array goes in an object[] so that TestCaseData passes it as the test's one argument; the
    // params object[] constructor would take each string as a separate argument.
    public static TestCaseData Described(string description, params string[] args) =>
        new TestCaseData(new object[] { args }).SetArgDisplayNames(description);

    // A case holding a rejected command line, the corrected line, the request the corrected line
    // sends and the body the fake API answers it with, named by the rejected line.
    public static TestCaseData RejectedThenCorrected(string rejected, string corrected, string method, string path, string json) =>
        new TestCaseData(rejected.Split(' '), corrected.Split(' '), method, path, json).SetArgDisplayNames(rejected);
}
