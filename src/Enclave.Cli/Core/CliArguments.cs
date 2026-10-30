using System.CommandLine;
using System.CommandLine.Parsing;

namespace Enclave.Cli.Core;

// Only names, email addresses, hostnames, tag names and system IDs are positional; other IDs go
// after --id (spec/cli-surface.md "Commands").
internal static class CliArguments
{
    public const string Stdin = "-";

    public static Argument<string> Text(string name, string description) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ExactlyOne,
        };

    public static Argument<string?> OptionalText(string name, string description) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
        };

    /// <summary>
    /// A blank name is a parse error. <paramref name="takes"/> says what the name is, for errors:
    /// "a key's description".
    /// </summary>
    public static Argument<string> Name(string name, string description, string takes) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ExactlyOne,
            CustomParser = result => ReadName(result, name, takes) ?? string.Empty,
        };

    /// <summary>
    /// A blank name is a parse error.
    /// </summary>
    public static Argument<string?> OptionalName(string name, string description, string takes) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
            CustomParser = result => ReadName(result, name, takes),
        };

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
    /// Names, or IDs in <paramref name="format"/> when it is given, or "-" alone for a list on stdin
    /// (ItemSelection). Empty when none is given.
    /// </summary>
    public static Argument<IReadOnlyList<string>> Many(string name, string description, IdFormat<string>? format = null) =>
        AnyNumber(name, description, (values, result) =>
        {
            if (format is null)
            {
                return;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!format.TryParse(values[i], out var value))
                {
                    result.AddError($"Each <{name}> is {format.Describe}.");
                    return;
                }

                values[i] = value;
            }
        });

    /// <summary>
    /// Names of items to look up, or "-" alone for a list on stdin (ItemSelection). Empty when none
    /// is given. <paramref name="takes"/> says what each name is, for errors.
    /// </summary>
    // A blank name among others is refused as one alone is: dropped, the command would act on the
    // others only; sent, it would cost a lookup and match nothing.
    public static Argument<IReadOnlyList<string>> Names(string name, string description, string takes) =>
        AnyNumber(name, description, (values, result) =>
        {
            if (values.Any(string.IsNullOrWhiteSpace))
            {
                result.AddError($"Each <{name}> takes {takes}, and one given is blank.");
            }
        });

    private static Argument<IReadOnlyList<string>> AnyNumber(string name, string description, Action<string[], ArgumentResult> read) =>
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

                read(values, result);
                return values;
            },
        };

    // A blank name names nothing: the API refuses one on create and rename (portal
    // EnrolmentKeyCreateValidator, PolicyCreateModelValidator, TrustRequirementCreateModelValidator,
    // DnsZoneCreateValidator, DnsRecordCreateValidator and the patch validators, each NotEmpty).
    // Refused while parsing, it exits 2 before the token is read and before any call
    // (spec/cli-surface.md "Details").
    private static string? ReadName(ArgumentResult result, string name, string takes)
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        var text = result.Tokens[0].Value;

        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        result.AddError($"<{name}> takes {takes}, and the value given is blank.");
        return null;
    }
}
