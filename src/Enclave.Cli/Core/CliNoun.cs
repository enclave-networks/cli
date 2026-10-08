using System.CommandLine;

namespace Enclave.Cli.Core;

/// <summary>
/// A noun, such as `system`, which holds verbs. Its plural is accepted as a hidden alias, so a guess
/// such as `systems list` runs `system list`; help and `commands` show the singular name only
/// (proposed-cli-surface.md "Shape and naming"). Given without a verb it exits 2.
/// </summary>
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

    /// <summary>
    /// Adds a verb, or a second-level noun, and returns it.
    /// </summary>
    /// <typeparam name="TCommand">The command's type, kept for the caller.</typeparam>
    public TCommand Add<TCommand>(TCommand command)
        where TCommand : Command
    {
        ArgumentNullException.ThrowIfNull(command);

        Subcommands.Add(command);
        return command;
    }
}
