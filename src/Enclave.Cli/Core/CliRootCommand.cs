using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.Reflection;

namespace Enclave.Cli.Core;

/// <summary>
/// The enclave-cli command, which holds the nouns and the top-level commands and carries the
/// <see cref="CliHost"/> every command action reads.
/// </summary>
internal sealed class CliRootCommand : RootCommand
{
    /// <summary>
    /// The CLI's name in help and messages, whatever the name of the process that runs it.
    /// </summary>
    public const string ProgramName = "enclave-cli";

    public CliRootCommand(CliHost host)
        : base("Command-line tool for the Enclave Management APIs. Output is JSON; run `enclave-cli commands` for every command, option, value and error code.")
    {
        ArgumentNullException.ThrowIfNull(host);

        Host = host;

        // A directive such as [suggest] prints text that is not JSON, and the CLI has no use for one,
        // so [suggest] is refused as an invalid argument. The directive stays registered: with no
        // directives the parser drops a word in brackets before the command without a word, and with
        // one it reports any other as unmatched (ParseOperation.ParseDirectives, System.CommandLine
        // 2.0.12), which the CLI then reports as a parse error.
        Directives.OfType<SuggestDirective>().Single().Action = new RefuseDirectiveAction();

        Options.OfType<HelpOption>().Single().Action = new HelpAction();

        // The built-in --version prints the entry assembly's version, which is the host's version
        // when the CLI runs inside another process, as it does in tests (System.CommandLine 2.0,
        // VersionOption.GetExecutableVersion:
        // https://github.com/dotnet/command-line-api/blob/main/src/System.CommandLine/VersionOption.cs).
        // Replacing the action keeps the option's validator, which rejects --version combined with
        // other arguments.
        Options.OfType<VersionOption>().Single().Action = new VersionAction();

        // enclave-cli with no arguments prints the help (proposed-cli-surface.md "Errors and exit
        // codes"); anything it cannot parse is reported by the action before this runs.
        Action = new CliAction(async context => await context.Output.WriteTextAsync(HelpWriter.Write(context.ParseResult.CommandResult)));
    }

    public CliHost Host { get; }

    /// <summary>
    /// The host of the command line's root command.
    /// </summary>
    public static CliHost HostOf(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        return ((CliRootCommand)parseResult.RootCommandResult.Command).Host;
    }

    /// <summary>
    /// Parses a command line with the CLI's parser settings.
    /// </summary>
    // System.CommandLine expands an argument starting with "@" into the lines of the file it names
    // (ParserConfiguration.ResponseFileTokenReplacer, on by default in version 2.0.12). That would
    // read the disk outside CliHost.Files, and "@" can begin an ordinary value, so it is turned off.
    // Command.Parse is not virtual. A call with the arguments alone binds to this overload, which C#
    // prefers over the base class's Parse(args, configuration = null) (C# specification, "Method
    // invocations": methods of a base type are removed when a derived type has an applicable one),
    // so callers reach it through the CliRootCommand type that Program.CreateRootCommand returns.
    public ParseResult Parse(IReadOnlyList<string> args) =>
        Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });

    /// <summary>
    /// Adds a noun or a top-level command and returns it.
    /// </summary>
    /// <typeparam name="TCommand">The command's type, kept for the caller.</typeparam>
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
        // (HelpAction.ClearsParseErrors, version 2.0.12).
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
