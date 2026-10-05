using System.CommandLine;
using System.CommandLine.Invocation;
using System.Reflection;

namespace Enclave.Cli;

internal static class Program
{
    public static Task<int> Main(string[] args) => CreateRootCommand().Parse(args).InvokeAsync();

    internal static RootCommand CreateRootCommand()
    {
        var root = new RootCommand("Command-line tool for the Enclave Management APIs.");

        // The built-in --version prints the entry assembly's version, which is the host's version
        // when the CLI runs inside another process, as it does in tests (System.CommandLine 2.0,
        // VersionOption.GetExecutableVersion:
        // https://github.com/dotnet/command-line-api/blob/main/src/System.CommandLine/VersionOption.cs).
        // Replacing the action keeps the option's validator, which rejects --version combined with
        // other arguments.
        root.Options.OfType<VersionOption>().Single().Action = new PrintVersionAction();

        root.SetAction(parseResult => parseResult.InvocationConfiguration.Output.WriteLine("Hello, World!"));

        return root;
    }

    private sealed class PrintVersionAction : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            var version = typeof(Program).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;

            parseResult.InvocationConfiguration.Output.WriteLine(version);

            return 0;
        }
    }
}
