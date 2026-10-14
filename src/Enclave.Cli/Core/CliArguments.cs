using System.CommandLine;
using System.CommandLine.Parsing;

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
    /// One required name, taken as given, such as a new key's description. <paramref name="takes"/>
    /// says what the name is, for errors: "a key's description". A name that is empty or only white
    /// space is a parse error.
    /// </summary>
    public static Argument<string> Name(string name, string description, string takes) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ExactlyOne,
            CustomParser = result => ReadName(result, name, takes) ?? string.Empty,
        };

    /// <summary>
    /// One name that may be left out, taken as given: the name of the item a command that also takes
    /// --id looks up. Null when not given. A name that is empty or only white space is a parse error.
    /// </summary>
    public static Argument<string?> OptionalName(string name, string description, string takes) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.ZeroOrOne,
            CustomParser = result => ReadName(result, name, takes),
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
    /// Any number of values: names, or IDs in <paramref name="format"/> when it is given, each as
    /// the format reads it, or "-" alone to read a list from stdin (ItemSelection reads the
    /// arguments). An empty list when none is given.
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
    /// Any number of names of items to look up, taken as given, such as key descriptions, or "-"
    /// alone to read a list from stdin (ItemSelection reads the arguments). An empty list when none
    /// is given. <paramref name="takes"/> says what each name is, for errors. A name that is empty or
    /// only white space is a parse error, wherever it comes among the names.
    /// </summary>
    // A blank name among others is refused as one alone is: dropped, the command would act on the
    // other items only, and read, it would cost the lookup and then match nothing.
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

    // No key, policy, trust requirement, zone or hostname has an empty or blank name: the API
    // refuses one when the item is created or renamed (portal Enclave.Api/Modules/SystemManagement,
    // EnrolmentKeyCreateValidator.cs:22, PolicyCreateModelValidator.cs:23,
    // TrustRequirementCreateModelValidator.cs:17, DnsZoneCreateValidator.cs:22-23,
    // DnsRecordCreateValidator.cs:19-21 and the patch validators, each NotEmpty, which fails a
    // string that is empty or white space: FluentValidation 11.9.0, NotEmptyValidator.IsValid), and
    // a hostname's full name holds its zone's name. So a blank name names nothing. Passed on, a
    // lookup would read the list, which needs the token, and then match nothing, and a create would
    // reach the API, which refuses it, so the same command line would exit 3 without a token and 2
    // or 1 with one. Refused as it is parsed, it exits 2 before the token and any call
    // (proposed-cli-surface.md "Details").
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
