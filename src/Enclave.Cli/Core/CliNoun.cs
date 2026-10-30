using System.CommandLine;

namespace Enclave.Cli.Core;

// The plural is a hidden alias, so a guess such as `systems list` runs (spec/cli-surface.md "Shape
// and naming").
internal sealed class CliNoun : Command
{
    public CliNoun(string name, string? plural, string description)
        : base(name, description)
    {
        if (plural is not null)
        {
            Aliases.Add(plural);
        }

        Action = new CliAction(context =>
        {
            var commandName = ParseProblems.CommandName(context.ParseResult.CommandResult);
            var verbs = string.Join(", ", Subcommands.Where(subcommand => !subcommand.Hidden).Select(subcommand => subcommand.Name));

            throw CliErrors.InvalidArgument("command", $"`{commandName}` needs a command: {verbs}.");
        });
    }

    public TCommand Add<TCommand>(TCommand command)
        where TCommand : Command
    {
        ArgumentNullException.ThrowIfNull(command);

        Subcommands.Add(command);
        return command;
    }
}
