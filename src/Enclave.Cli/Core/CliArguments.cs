using System.CommandLine;

namespace Enclave.Cli.Core;

/// <summary>
/// Creates the CLI's positional arguments: names, email addresses, hostnames, tag names and system
/// IDs (proposed-cli-surface.md "Commands"). Other IDs are never positional; they go after --id.
/// </summary>
internal static class CliArguments
{
    /// <summary>
    /// "-" in place of the arguments: read a list printed by an enclave-cli list command from stdin.
    /// </summary>
    public const string Stdin = "-";

    /// <summary>
    /// One required value, taken as given (a name, email address or description).
    /// </summary>
    public static Argument<string> Text(string name, string description) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ExactlyOne,
        };

    /// <summary>
    /// One value that may be left out, taken as given: the name of a command that also takes --id.
    /// Null when not given.
    /// </summary>
    public static Argument<string?> OptionalText(string name, string description) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
        };

    /// <summary>
    /// One required ID in the given form, such as a system ID or a tag name.
    /// </summary>
    public static Argument<string> Id(string name, string description, IdFormat<string> format) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ExactlyOne,
            CustomParser = result =>
            {
                var text = result.Tokens[0].Value;

                if (format.TryParse(text, out var value))
                {
                    return value;
                }

                result.AddError($"<{name}> takes {format.Describe}.");
                return string.Empty;
            },
        };

    /// <summary>
    /// Any number of values: names, or IDs in <paramref name="format"/> when it is given, or "-"
    /// alone to read a list from stdin (ItemSelection reads the arguments). An empty list when none
    /// is given.
    /// </summary>
    public static Argument<IReadOnlyList<string>> Many(string name, string description, IdFormat<string>? format = null) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrMore,
            CustomParser = result =>
            {
                var values = result.Tokens.Select(token => token.Value).ToArray();

                if (values.Length == 1 && values[0] == Stdin)
                {
                    return values;
                }

                if (values.Contains(Stdin))
                {
                    result.AddError($"\"{Stdin}\" reads a list from stdin, so it takes the place of every <{name}>.");
                    return values;
                }

                if (format is not null)
                {
                    foreach (var value in values)
                    {
                        if (!format.TryParse(value, out _))
                        {
                            result.AddError($"Each <{name}> is {format.Describe}.");
                            return values;
                        }
                    }
                }

                return values;
            },
        };
}
