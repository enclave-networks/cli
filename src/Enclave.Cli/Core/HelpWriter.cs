using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.Text;

namespace Enclave.Cli.Core;

/// <summary>
/// Writes help text for people: usage, arguments, options and the commands below a command.
/// </summary>
// System.CommandLine's own help lists a command's aliases beside its name (HelpBuilder.Default.cs,
// GetIdentifierSymbolUsageLabel, version 2.0.12), which would show each hidden plural noun, and its
// HelpBuilder is internal, so the CLI writes its own.
internal static class HelpWriter
{
    private const int MaxLabelWidth = 32;

    public static string Write(CommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var command = result.Command;
        var name = result.Parent is null ? CliRootCommand.ProgramName : $"{CliRootCommand.ProgramName} {ParseProblems.CommandName(result)}";
        var subcommands = command.Subcommands.Where(subcommand => !subcommand.Hidden).ToArray();
        var text = new StringBuilder();

        if (!string.IsNullOrEmpty(command.Description))
        {
            text.Append(command.Description).Append("\n\n");
        }

        text.Append("Usage:\n  ").Append(name);

        foreach (var argument in command.Arguments.Where(argument => !argument.Hidden))
        {
            text.Append(' ').Append(Usage(argument));
        }

        text.Append(subcommands.Length > 0 ? " <command>" : string.Empty).Append(" [options]\n");

        AppendRows(text, "Arguments", command.Arguments.Where(argument => !argument.Hidden).Select(argument => ($"<{argument.Name}>", argument.Description)));
        AppendRows(text, "Options", Options(result).Select(option => (Label(option), Describe(option))));
        AppendRows(text, "Commands", subcommands.Select(subcommand => (subcommand.Name, subcommand.Description)));

        return text.ToString();
    }

    /// <summary>
    /// The options a command takes, its own first and then the ones every command of its scope takes.
    /// </summary>
    public static IEnumerable<Option> CommandOptions(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var common = command is CliVerb verb ? verb.CommonOptions : [];

        return command.Options
            .Where(option => !option.Hidden && !common.Contains(option))
            .Concat(common.Where(option => !option.Hidden));
    }

    private static IEnumerable<Option> Options(CommandResult result)
    {
        foreach (var option in CommandOptions(result.Command))
        {
            yield return option;
        }

        // Help is recursive from the root, so every command's help lists it.
        if (result.Parent is not null)
        {
            for (var parent = result.Parent as CommandResult; parent is not null; parent = parent.Parent as CommandResult)
            {
                foreach (var option in parent.Command.Options.Where(option => option.Recursive && !option.Hidden))
                {
                    yield return option;
                }
            }
        }
    }

    private static string Usage(Argument argument) =>
        argument.Arity.MinimumNumberOfValues == 0
            ? argument.Arity.MaximumNumberOfValues > 1 ? $"[<{argument.Name}>...]" : $"[<{argument.Name}>]"
            : argument.Arity.MaximumNumberOfValues > 1 ? $"<{argument.Name}>..." : $"<{argument.Name}>";

    private static string Label(Option option)
    {
        var names = option is HelpOption ? $"-h, {option.Name}" : option.Name;

        return option.Arity.MaximumNumberOfValues == 0 || string.IsNullOrEmpty(option.HelpName)
            ? names
            : $"{names} <{option.HelpName}>";
    }

    private static string? Describe(Option option) =>
        option.Arity.MaximumNumberOfValues > 1 && option is not IChoiceOption
            ? $"{option.Description} Repeatable."
            : option.Description;

    private static void AppendRows(StringBuilder text, string heading, IEnumerable<(string Label, string? Description)> rows)
    {
        var list = rows.ToArray();

        if (list.Length == 0)
        {
            return;
        }

        // A label longer than the column, such as an option listing its values, takes a line of its
        // own, so one long label does not push every description to the right.
        var width = Math.Min(list.Max(row => row.Label.Length), MaxLabelWidth) + 2;
        text.Append('\n').Append(heading).Append(":\n");

        foreach (var (label, description) in list)
        {
            text.Append("  ").Append(label);

            if (label.Length >= width)
            {
                text.Append('\n').Append(' ', width + 2);
            }
            else
            {
                text.Append(' ', width - label.Length);
            }

            text.Append(description).Append('\n');
        }
    }
}
