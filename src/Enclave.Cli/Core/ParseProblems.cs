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
    // The errors key of an unknown option reported without its name.
    private const string UnnamedOptionKey = "options";

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
    // any part of a value, or anything joined to an option's name (spec/cli-surface.md "Errors
    // and exit codes"). An unknown option is named only when what is printed has the form the
    // CLI's own options have: "--" and lower-case words joined by hyphens, or "-" and one letter or
    // digit. Every option of the CLI is written that way, apart from help's -?, /h and /?
    // (ParseErrorTests checks it), so a mistyped option written as the CLI's options are is named.
    // A word that follows an unknown option is taken as its value and left out unless the option
    // carries a value of its own, and an unknown command or extra argument is described by its
    // position.
    //
    //   token                        named as   the word after it
    //   ---------------------------  ---------  -------------------
    //   --bogus                      --bogus    its value, left out
    //   --bogus=V  --bogus:V         --bogus    reported on its own
    //   "--bogus V" (one argument)   --bogus    reported on its own
    //   -b                           -b         its value, left out
    //   -bV                          -b         reported on its own
    //   --Bogus  --bogusV9           not named  its value, left out
    //   --Bogus=V                    not named  reported on its own
    //
    // A long name ends at "=" or ":", where System.CommandLine splits a known option from its value
    // (Parsing/StringExtensions.cs, TrySplitIntoSubtokens, version 2.0.12), or at white space, which
    // one argument holds when a script quotes an option and its value together. Anything else after
    // the name, such as a token glued to it, leaves the option unnamed, with no suggestion, and the
    // message points to `commands` for the options the command takes.
    //
    // The rule gives up some cases. A value glued to a name with nothing between them reads as part
    // of the name when the value is itself lower-case letters and hyphens, since nothing tells
    // "--notesreviewed" from a mistyped option; a personal access token is 63 characters of
    // Base58, which holds digits and capitals as well as lower-case letters (portal
    // src/Enclave.Accounts.Data/Stores/AccessTokenStore.cs:102-135, StoreReferenceTokenAsync), so
    // one glued to a name is printed only when every character of it is a lower-case letter. A
    // single-dash token shows the character after the dash, which is one character of a value
    // written straight after a dash, too little to give a token away. A word that starts with "-"
    // after an unknown option is read as another option, so such a value is named when it has an
    // option's form; in exchange two unknown options in a row (`--bogus --wrong`) are both
    // reported. An option written with a capital or a digit gets no name and no suggestion, which
    // the pointer to `commands` makes up for, as the suggestion does for the less specific message
    // a mistyped command word gets.
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
                var (name, carriesValue) = ReadOption(token);

                if (name is null)
                {
                    var commands = commandResult.Parent is null ? $"{CliRootCommand.ProgramName} commands" : $"{CliRootCommand.ProgramName} commands {commandName}";
                    problems.Add((UnnamedOptionKey, $"`{commandName}` was given an option it does not have, which is not repeated here because it is not written as an option name: \"--\" and lower-case words joined by hyphens. Run `{commands}` for the options it takes."));
                }
                else
                {
                    var suggestion = Suggestions.Closest(name, OptionNames(commandResult));
                    problems.Add((name, $"`{commandName}` has no option {name}.{DidYouMean(suggestion)}"));
                }

                followsUnknownOption = !carriesValue;
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

    // The name an unknown option is reported by, or null when the token is not written as an option
    // name, and whether the token carries a value of its own; the table above AddUnmatchedTokens
    // gives the cases.
    private static (string? Name, bool CarriesValue) ReadOption(string token)
    {
        if (token[1] != '-')
        {
            return (char.IsAsciiLetterOrDigit(token[1]) ? token[..2] : null, token.Length > 2);
        }

        var end = 2;
        while (end < token.Length && token[end] is not ('=' or ':') && !char.IsWhiteSpace(token[end]))
        {
            end++;
        }

        var name = token[..end];
        var isOptionName = name.Length > 2 && name[2..].Split('-').All(word => word.Length > 0 && word.All(char.IsAsciiLetterLower));

        return (isOptionName ? name : null, end < token.Length);
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
