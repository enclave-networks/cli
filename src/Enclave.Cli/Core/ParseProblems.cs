using System.CommandLine;
using System.CommandLine.Parsing;

namespace Enclave.Cli.Core;

/// <summary>
/// Reports what System.CommandLine could not parse as one invalid_argument error.
/// </summary>
// Every command action of the CLI clears System.CommandLine's parse errors (ClearsParseErrors), so
// the parser never runs its own ParseErrorAction, which writes text and help and exits 1
// (ParseErrorAction.cs, System.CommandLine 2.0.12). Clearing removes the errors the parser
// attached to the command itself, which are the unmatched tokens, and keeps those of options and
// arguments (ParseOperation.ValidateAndAddDefaultResults). The unmatched tokens stay in
// ParseResult.UnmatchedTokens, so this class rebuilds those errors from them and adds the errors
// the parser kept.
internal static class ParseProblems
{
    /// <summary>
    /// The error for everything that did not parse, with detail naming the first problem and
    /// errors listing each, or null when the command line parsed.
    /// </summary>
    public static CliException? Find(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        var problems = new List<(string Key, string Message)>();
        AddUnmatchedTokens(parseResult, problems);

        // An error the parser attached to a command is about an unmatched token, which the tokens
        // above already report, and its message repeats the token. It reaches here when the action
        // that runs does not clear parse errors (--version), or when the token came before a
        // subcommand (`system --bogus list`), since clearing keeps the errors of commands other
        // than the innermost.
        foreach (var error in parseResult.Errors.Where(error => error.SymbolResult is not CommandResult))
        {
            problems.Add((KeyOf(error.SymbolResult), MessageOf(error)));
        }

        if (problems.Count == 0)
        {
            return null;
        }

        var errors = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var group in problems.GroupBy(problem => problem.Key, StringComparer.Ordinal))
        {
            errors[group.Key] = group.Select(problem => problem.Message).ToArray();
        }

        return new CliException(ErrorCode.InvalidArgument, problems[0].Message, errors: errors);
    }

    /// <summary>
    /// The command's full name without the executable, such as "partner customer list", or
    /// "enclave-cli" for the root.
    /// </summary>
    public static string CommandName(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var names = new List<string>();

        for (var current = result; current.Parent is CommandResult parent; current = parent)
        {
            names.Insert(0, current.Command.Name);
        }

        return names.Count == 0 ? CliRootCommand.ProgramName : string.Join(' ', names);
    }

    // A value given to an unknown option could be a token passed by mistake, so no message repeats
    // a value: an unknown option is named by the part before any "=", a word that follows an
    // unknown option is taken as its value and left out, and an unknown command or extra argument
    // is described by its position (proposed-cli-surface.md "Errors and exit codes"). The
    // trade-off is a less specific message for a mistyped command word, which the suggestion makes
    // up for.
    private static void AddUnmatchedTokens(ParseResult parseResult, List<(string Key, string Message)> problems)
    {
        var commandResult = parseResult.CommandResult;
        var command = commandResult.Command;
        var commandName = CommandName(commandResult);
        var followsUnknownOption = false;
        var reportedUnknownCommand = false;

        foreach (var token in parseResult.UnmatchedTokens)
        {
            if (token.Length > 1 && token[0] == '-')
            {
                var name = token.Split('=', 2)[0];
                var suggestion = Suggestions.Closest(name, OptionNames(commandResult));
                var message = $"`{commandName}` has no option {name}.{DidYouMean(suggestion)}";

                problems.Add((name, message));
                followsUnknownOption = !token.Contains('=', StringComparison.Ordinal);
                continue;
            }

            if (followsUnknownOption)
            {
                followsUnknownOption = false;
                continue;
            }

            if (VisibleSubcommands(command).Any())
            {
                // The words after an unknown command belong to it, so only the first is reported.
                if (!reportedUnknownCommand)
                {
                    var suggestion = Suggestions.Closest(token, VisibleSubcommands(command).Select(subcommand => subcommand.Name));
                    problems.Add(("command", $"`{commandName}` has no command by the name given.{DidYouMean(suggestion)} Its commands are: {string.Join(", ", VisibleSubcommands(command).Select(subcommand => subcommand.Name))}."));
                    reportedUnknownCommand = true;
                }

                continue;
            }

            problems.Add(("arguments", $"`{commandName}` was given more arguments than it takes."));
        }
    }

    private static string DidYouMean(string? suggestion) => suggestion is null ? string.Empty : $" Did you mean {suggestion}?";

    private static IEnumerable<Command> VisibleSubcommands(Command command) =>
        command.Subcommands.Where(subcommand => !subcommand.Hidden);

    private static IEnumerable<string> OptionNames(CommandResult commandResult)
    {
        foreach (var option in commandResult.Command.Options.Where(option => !option.Hidden))
        {
            yield return option.Name;
        }

        for (var parent = commandResult.Parent as CommandResult; parent is not null; parent = parent.Parent as CommandResult)
        {
            foreach (var option in parent.Command.Options.Where(option => option.Recursive && !option.Hidden))
            {
                yield return option.Name;
            }
        }
    }

    // A missing value gets a message that names the command or option in the CLI's terms; any
    // other parser message, such as a value given twice to an option that takes one, is passed on.
    private static string MessageOf(ParseError error) => error.SymbolResult switch
    {
        OptionResult { Tokens.Count: 0 } option when option.Option.Arity.MinimumNumberOfValues > 0 =>
            $"{option.Option.Name} needs a value.",
        ArgumentResult { Tokens.Count: 0, Parent: OptionResult option } =>
            $"{option.Option.Name} needs a value.",
        ArgumentResult { Tokens.Count: 0, Parent: CommandResult command } argument =>
            $"`{CommandName(command)}` needs <{argument.Argument.Name}>.",
        _ => error.Message,
    };

    private static string KeyOf(SymbolResult? result) => result switch
    {
        OptionResult option => option.Option.Name,
        ArgumentResult { Parent: OptionResult option } => option.Option.Name,
        ArgumentResult argument => argument.Argument.Name,
        _ => "command",
    };
}
