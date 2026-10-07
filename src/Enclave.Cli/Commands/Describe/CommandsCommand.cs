using System.CommandLine;
using System.Text.Json.Nodes;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Describe;

/// <summary>
/// `commands [&lt;command words&gt;...]`: every command, its arguments and options, the values each
/// option allows, and the error codes with their exit codes, as JSON (proposed-cli-surface.md
/// "Details"). It is generated from the command tree, so it describes every command the CLI has.
/// </summary>
internal static class CommandsCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "commands",
            "Describe every command, its arguments and options, the values each option allows, and the error codes with their exit codes, as JSON. Given a command's words, describe that command, or every command under a noun. Needs no token and makes no call.");

        var words = verb.Add(CliArguments.Many("words", "The words of a command or noun, such as: system list."));

        verb.SetHandler(async context =>
        {
            Command start = context.ParseResult.RootCommandResult.Command;
            var path = new List<string>();

            // A plural noun is accepted here as on the command line, and the description uses the
            // singular names.
            foreach (var word in context.Get(words) ?? [])
            {
                start = start.Subcommands.FirstOrDefault(command => !command.Hidden && (command.Name == word || command.Aliases.Contains(word)))
                    ?? throw CliErrors.InvalidArgument(words.Name, $"`{string.Join(' ', [.. path, word])}` is not a command or noun. Run `{CliRootCommand.ProgramName} commands` for every command.");

                path.Add(start.Name);
            }

            var commands = new JsonArray();
            foreach (var (name, command) in Leaves(start, path))
            {
                commands.Add(Describe(name, command));
            }

            var errors = new JsonArray(ErrorCode.All
                .Select(error => (JsonNode?)new JsonObject { ["code"] = error.Code, ["exitCode"] = error.ExitCode })
                .ToArray());

            await context.Output.WriteAsync(new JsonObject { ["commands"] = commands, ["errors"] = errors }, context.CancellationToken);
        });

        return verb;
    }

    // A command that runs has no subcommands; a noun's commands are its verbs and second-level
    // nouns' verbs.
    private static IEnumerable<(string Name, Command Command)> Leaves(Command command, List<string> path)
    {
        var subcommands = command.Subcommands.Where(subcommand => !subcommand.Hidden).ToArray();

        if (subcommands.Length == 0)
        {
            yield return (string.Join(' ', path), command);
            yield break;
        }

        foreach (var subcommand in subcommands)
        {
            foreach (var leaf in Leaves(subcommand, [.. path, subcommand.Name]))
            {
                yield return leaf;
            }
        }
    }

    private static JsonObject Describe(string name, Command command)
    {
        var arguments = new JsonArray(command.Arguments
            .Where(argument => !argument.Hidden)
            .Select(argument => (JsonNode?)new JsonObject { ["name"] = argument.Name })
            .ToArray());

        var options = new JsonArray();
        foreach (var option in HelpWriter.CommandOptions(command))
        {
            var entry = new JsonObject { ["name"] = option.Name };

            if (option is IChoiceOption choice)
            {
                entry["values"] = new JsonArray(choice.AllowedValues.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
            }

            options.Add(entry);
        }

        return new JsonObject
        {
            ["command"] = name,
            ["description"] = command.Description,
            ["arguments"] = arguments,
            ["options"] = options,
        };
    }
}
