using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Reflection;

namespace Enclave.Cli.Core;

internal sealed class CliRootCommand : RootCommand
{
    // Used in help and messages, whatever the name of the process that runs the CLI.
    public const string ProgramName = "enclave-cli";

    public CliRootCommand(CliHost host)
        : base("Command-line tool for the Enclave Management APIs. Output is JSON; run `enclave-cli commands` for every command, option, value and error code.")
    {
        ArgumentNullException.ThrowIfNull(host);

        Host = host;

        // [suggest] prints text that is not JSON, so it is refused. It stays registered: with no
        // directives the parser drops a bracketed word before the command without a word, and with one
        // it reports any other as unmatched (System.CommandLine 2.0.12, ParseOperation.ParseDirectives).
        Directives.OfType<SuggestDirective>().Single().Action = new RefuseDirectiveAction();

        Options.OfType<HelpOption>().Single().Action = new HelpAction();

        // The built-in --version prints the entry assembly's version, which in tests is the test
        // host's (System.CommandLine 2.0.12, VersionOption.GetExecutableVersion). Replacing only the
        // action keeps the validator that rejects --version with other arguments.
        Options.OfType<VersionOption>().Single().Action = new VersionAction();

        // No arguments prints the help (spec/cli-surface.md "Errors and exit codes").
        Action = new CliAction(async context => await context.Output.WriteTextAsync(HelpWriter.Write(context.ParseResult.CommandResult)));
    }

    public CliHost Host { get; }

    public static CliHost HostOf(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        return ((CliRootCommand)parseResult.RootCommandResult.Command).Host;
    }

    // Response files are off: System.CommandLine 2.0.12 expands an argument starting with "@" into
    // the lines of the file it names (ParserConfiguration.ResponseFileTokenReplacer), which reads the
    // disk outside CliHost.Files, and "@" can begin an ordinary value. Command.Parse is not virtual;
    // C# prefers this overload over the base Parse(args, configuration = null) (C# specification,
    // "Method invocations"), so callers reach it through the CliRootCommand type.
    public ParseResult Parse(IReadOnlyList<string> args) =>
        Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });

    public TCommand Add<TCommand>(TCommand command)
        where TCommand : Command
    {
        ArgumentNullException.ThrowIfNull(command);

        Subcommands.Add(command);
        return command;
    }

    private sealed class HelpAction : SynchronousCommandLineAction
    {
        // Help is shown whatever else is on the command line, as System.CommandLine's own help is
        // (System.CommandLine 2.0.12, HelpAction.ClearsParseErrors).
        public override bool ClearsParseErrors => true;

        public override int Invoke(ParseResult parseResult)
        {
            parseResult.InvocationConfiguration.Output.Write(HelpWriter.Write(parseResult.CommandResult));
            return 0;
        }
    }

    private sealed class RefuseDirectiveAction : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            var error = CliErrors.InvalidArgument("command", $"{ProgramName} takes no directives such as [suggest].");
            parseResult.InvocationConfiguration.Error.WriteLine(CliOutput.Describe(error).ToJsonString(CliJson.Compact));
            return error.Code.ExitCode;
        }
    }

    private sealed class VersionAction : SynchronousCommandLineAction
    {
        public override int Invoke(ParseResult parseResult)
        {
            if (ParseProblems.Find(parseResult) is { } error)
            {
                parseResult.InvocationConfiguration.Error.WriteLine(CliOutput.Describe(error).ToJsonString(CliJson.Compact));
                return error.Code.ExitCode;
            }

            var version = typeof(CliRootCommand).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;

            parseResult.InvocationConfiguration.Output.WriteLine(version);

            return 0;
        }
    }
}
