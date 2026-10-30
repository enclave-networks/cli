using System.CommandLine;
using System.CommandLine.Parsing;

namespace Enclave.Cli.Core;

// Every command action clears System.CommandLine's parse errors (CliAction.ClearsParseErrors). That
// removes the errors attached to the command itself, which are the unmatched tokens, and keeps
// those of options and arguments (System.CommandLine 2.0.12,
// ParseOperation.ValidateAndAddDefaultResults), so this class rebuilds the removed ones from
// ParseResult.UnmatchedTokens.
internal static class ParseProblems
{
    // The errors key of an unknown option reported without its name.
    private const string UnnamedOptionKey = "options";

    /// <summary>
    /// Null when the command line parsed.
    /// </summary>
    public static CliException? Find(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);

        var problems = new List<(string Key, string Message)>();
        AddUnmatchedTokens(parseResult, problems);

        // A command's errors are about unmatched tokens, reported above, and repeat the token. They
        // reach here under --version, whose action does not clear them, and for a token before a
        // subcommand (`system --bogus list`), since clearing keeps the errors of outer commands.
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
    /// Such as "partner customer list"; "enclave-cli" for the root.
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
    // any part of a value, or anything joined to an option's name (spec/cli-surface.md "Errors and
    // exit codes"). An unknown option is named only when it has the form every CLI option but help's
    // -?, /h and /? has (ParseErrorTests): "--" and lower-case words joined by hyphens, or "-" and
    // one letter or digit. A word after an unknown option is taken as its value and left out unless
    // the option carries a value of its own. An unknown command or extra argument is described by
    // its position.
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
    // (System.CommandLine 2.0.12, StringExtensions.TrySplitIntoSubtokens), or at white space, as
    // when a script quotes an option and its value together. Anything else glued to the name leaves
    // the option unnamed, and the message points to `commands`.
    //
    // Trade-offs: a value glued to a name reads as part of it when the value is lower-case letters
    // and hyphens, since nothing tells "--notesreviewed" from a mistyped option; a personal access
    // token is Base58, with digits and capitals (portal AccessTokenStore.StoreReferenceTokenAsync),
    // so one is printed only when every character is a lower-case letter. A single-dash token shows
    // one character of a value, too little to give a token away. A value starting with "-" after an
    // unknown option is read as another option, and named when it has an option's form; in exchange
    // `--bogus --wrong` reports both. An option with a capital or digit gets no name or suggestion;
    // the pointer to `commands` makes up for it.
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
                    var suggestion = Suggestions.Closest(name, HelpWriter.VisibleOptions(commandResult).Select(option => option.Name));
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

    // The table above AddUnmatchedTokens gives the cases.
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
