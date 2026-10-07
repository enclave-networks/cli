using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;

namespace Enclave.Cli.Core;

/// <summary>
/// Creates the CLI's options. Each option reads its own value: the option, never the value,
/// decides how a value is read (proposed-cli-surface.md "Commands"). A value an option cannot read
/// is a parse error, so it exits 2 with invalid_argument before any call and before the token is
/// checked.
/// </summary>
// No message repeats the value given, so a token pasted after the wrong option stays out of
// stderr, as it does for unknown options (ParseProblems).
internal static class CliOptions
{
    /// <summary>
    /// Free text, taken as given; null when the option is not given.
    /// </summary>
    public static Option<string?> Text(string name, string description, string valueName = "text") =>
        new(name)
        {
            Description = description,
            HelpName = valueName,
            Arity = ArgumentArity.ExactlyOne,
        };

    /// <summary>
    /// A switch: true when given. It takes no value.
    /// </summary>
    public static Option<bool> Flag(string name, string description) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.Zero,
        };

    /// <summary>
    /// A whole number no smaller than <paramref name="minimum"/>; null when not given.
    /// </summary>
    public static Option<int?> Number(string name, string description, int minimum = 0, string valueName = "n") =>
        Single<int?>(name, description, valueName, (text, result) =>
        {
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= minimum)
            {
                return value;
            }

            result.AddError(string.Create(CultureInfo.InvariantCulture, $"{name} takes a whole number of at least {minimum}."));
            return null;
        });

    /// <summary>
    /// One ID in the given form, such as <see cref="IdFormats.Int32"/>; null when not given.
    /// </summary>
    /// <typeparam name="T">The ID type: int or Guid.</typeparam>
    public static Option<T?> Id<T>(string name, string description, IdFormat<T> format, string valueName = "id")
        where T : struct =>
        Single<T?>(name, description, valueName, (text, result) =>
        {
            if (format.TryParse(text, out var value))
            {
                return value;
            }

            result.AddError($"{name} takes one ID: {format.Describe}.");
            return null;
        });

    /// <summary>
    /// One ID in a text form, such as a tag name (<see cref="IdFormats.Tag"/>) or a system ID; null
    /// when not given.
    /// </summary>
    public static Option<string?> IdText(string name, string description, IdFormat<string> format, string valueName = "id") =>
        Single<string?>(name, description, valueName, (text, result) =>
        {
            if (format.TryParse(text, out var value))
            {
                return value;
            }

            result.AddError($"{name} takes {format.Describe}.");
            return null;
        });

    /// <summary>
    /// IDs in the given form, separated by commas (--id 42,43,57); the option may also be repeated.
    /// Null when not given.
    /// </summary>
    /// <typeparam name="T">The ID type.</typeparam>
    public static Option<IReadOnlyList<T>?> IdList<T>(string name, string description, IdFormat<T> format, string valueName = "ids") =>
        CommaList(name, description, valueName, (piece, result) =>
        {
            if (format.TryParse(piece, out var value))
            {
                return (true, value);
            }

            result.AddError($"{name} takes IDs separated by commas, each {format.Describe}.");
            return (false, value);
        });

    /// <summary>
    /// Values separated by commas (--tags a,b), taken as given; "" gives an empty list, which a
    /// --set- flag uses to clear a list. Null when not given.
    /// </summary>
    public static Option<IReadOnlyList<string>?> List(string name, string description, string valueName = "a,b") =>
        CommaList<string>(name, description, valueName, (piece, _) => (true, piece));

    /// <summary>
    /// Tag names separated by commas, each by the API's tag rule (<see cref="IdFormats.Tag"/>); ""
    /// gives an empty list. Null when not given.
    /// </summary>
    public static Option<IReadOnlyList<string>?> TagList(string name, string description) =>
        IdList(name, description, IdFormats.Tag, "a,b");

    /// <summary>
    /// A value that may be given more than once, each whole: --gateway GW001:10.0.0.0/16 --gateway
    /// GW002:10.1.0.0/16. Commas are not separators, since a value can contain them. Null when not
    /// given.
    /// </summary>
    public static Option<IReadOnlyList<string>?> Repeated(string name, string description, string valueName) =>
        new(name)
        {
            Description = description,
            HelpName = valueName,
            Arity = ArgumentArity.OneOrMore,
            CustomParser = result => result.Tokens.Select(token => token.Value).ToArray(),
        };

    /// <summary>
    /// A value with an optional "=label", which may be given more than once:
    /// --acl "tcp:5432=PostgreSQL" --acl icmp. Null when not given.
    /// </summary>
    public static Option<IReadOnlyList<LabelledValue>?> Labelled(string name, string description, string valueName) =>
        new(name)
        {
            Description = description,
            HelpName = valueName + "[=<label>]",
            Arity = ArgumentArity.OneOrMore,
            CustomParser = result => result.Tokens.Select(token => LabelledValue.Parse(token.Value)).ToArray(),
        };

    /// <summary>
    /// One of an API enum's members, given in lower-case, hyphenated form (RecentlyConnected is
    /// recently-connected) and matched ignoring case. Null when not given.
    /// </summary>
    /// <typeparam name="TEnum">The API enum.</typeparam>
    public static ChoiceOption<TEnum?> Enum<TEnum>(string name, string description, params TEnum[] members)
        where TEnum : struct, Enum =>
        Choice(name, description, members.Select(member => (Hyphenated(member.ToString()), member)).ToArray());

    /// <summary>
    /// One of a fixed set of values, matched ignoring case, each standing for the value the command
    /// uses; null when not given.
    /// </summary>
    /// <typeparam name="T">The value the command uses, such as an API enum member.</typeparam>
    public static ChoiceOption<T?> Choice<T>(string name, string description, params (string Value, T Result)[] choices)
        where T : struct
    {
        var option = new ChoiceOption<T?>(name, choices.Select(choice => choice.Value).ToArray())
        {
            Description = description,
            HelpName = string.Join('|', choices.Select(choice => choice.Value)),
            Arity = ArgumentArity.ExactlyOne,
        };

        option.CustomParser = result =>
        {
            foreach (var (value, choice) in choices)
            {
                if (string.Equals(result.Tokens[0].Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    return choice;
                }
            }

            result.AddError($"{name} takes one of: {string.Join(", ", option.AllowedValues)}.");
            return null;
        };

        return option;
    }

    /// <summary>
    /// One of a fixed set of values, matched ignoring case, each standing for a text the command
    /// uses, such as the search value the API reads (--os mac stands for "Mac"). Null when not given.
    /// </summary>
    public static ChoiceOption<string?> ChoiceText(string name, string description, params (string Value, string Result)[] choices)
    {
        var option = new ChoiceOption<string?>(name, choices.Select(choice => choice.Value).ToArray())
        {
            Description = description,
            HelpName = string.Join('|', choices.Select(choice => choice.Value)),
            Arity = ArgumentArity.ExactlyOne,
        };

        option.CustomParser = result =>
        {
            foreach (var (value, choice) in choices)
            {
                if (string.Equals(result.Tokens[0].Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    return choice;
                }
            }

            result.AddError($"{name} takes one of: {string.Join(", ", option.AllowedValues)}.");
            return null;
        };

        return option;
    }

    /// <summary>
    /// Values from a fixed set, separated by commas (--level warning,error), matched ignoring case.
    /// Null when not given.
    /// </summary>
    /// <typeparam name="T">The value each choice stands for.</typeparam>
    public static ChoiceOption<IReadOnlyList<T>?> ChoiceList<T>(string name, string description, params (string Value, T Result)[] choices)
    {
        var option = new ChoiceOption<IReadOnlyList<T>?>(name, choices.Select(choice => choice.Value).ToArray())
        {
            Description = description,
            HelpName = string.Join('|', choices.Select(choice => choice.Value)) + ",...",
            Arity = ArgumentArity.OneOrMore,
        };

        option.CustomParser = result =>
        {
            var values = new List<T>();

            foreach (var piece in Pieces(result))
            {
                var match = choices.Where(choice => string.Equals(piece, choice.Value, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (match.Length == 0)
                {
                    result.AddError($"{name} takes values separated by commas, each one of: {string.Join(", ", option.AllowedValues)}.");
                    return null;
                }

                values.Add(match[0].Result);
            }

            return values;
        };

        return option;
    }

    /// <summary>
    /// A duration: a whole number and a unit, m, h or d (30m, 8h, 14d). Null when not given.
    /// </summary>
    public static Option<TimeSpan?> Duration(string name, string description) =>
        Single<TimeSpan?>(name, description, "duration", (text, result) =>
        {
            if (TimeInput.TryParseDuration(text, out var duration))
            {
                return duration;
            }

            result.AddError($"{name} takes a duration: a whole number and m, h or d, such as 30m, 8h or 14d.");
            return null;
        });

    /// <summary>
    /// A time: RFC 3339 with its zone (2026-10-09T17:30:00Z), a date and time without a zone
    /// (2026-10-09T17:30) or a clock time (18:00), both read in the local time zone, and, when
    /// <paramref name="allowDuration"/> is set, a duration. CliContext reads it into an instant.
    /// Null when not given.
    /// </summary>
    public static Option<TimeInput?> Time(string name, string description, bool allowDuration = false) =>
        Single<TimeInput?>(name, description, allowDuration ? "duration|time" : "time", (text, result) =>
        {
            if (TimeInput.TryParse(text, allowDuration, out var input))
            {
                return input;
            }

            var forms = "an RFC 3339 time with its zone (2026-10-09T17:30:00Z), a date and time in the local time zone (2026-10-09T17:30), or a clock time (18:00)";
            result.AddError(allowDuration ? $"{name} takes a duration (30m, 8h, 14d), {forms}." : $"{name} takes {forms}.");
            return null;
        });

    /// <summary>
    /// The lower-case, hyphenated form of an API enum member's name: RecentlyConnected is
    /// recently-connected.
    /// </summary>
    public static string Hyphenated(string memberName)
    {
        ArgumentNullException.ThrowIfNull(memberName);

        var builder = new StringBuilder(memberName.Length + 4);

        for (var i = 0; i < memberName.Length; i++)
        {
            if (i > 0 && char.IsUpper(memberName[i]))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(memberName[i]));
        }

        return builder.ToString();
    }

    private static Option<T> Single<T>(string name, string description, string valueName, Func<string, ArgumentResult, T> parse) =>
        new(name)
        {
            Description = description,
            HelpName = valueName,
            Arity = ArgumentArity.ExactlyOne,
            CustomParser = result => parse(result.Tokens[0].Value, result),
        };

    private static Option<IReadOnlyList<T>?> CommaList<T>(string name, string description, string valueName, Func<string, ArgumentResult, (bool Valid, T Value)> parse) =>
        new(name)
        {
            Description = description,
            HelpName = valueName,
            Arity = ArgumentArity.OneOrMore,
            CustomParser = result =>
            {
                var values = new List<T>();

                foreach (var piece in Pieces(result))
                {
                    var (valid, value) = parse(piece, result);

                    if (!valid)
                    {
                        return null;
                    }

                    values.Add(value);
                }

                return values;
            },
        };

    // Each occurrence's value split at commas, with spaces around a piece removed and empty pieces
    // dropped, so "" is an empty list.
    private static IEnumerable<string> Pieces(ArgumentResult result) =>
        result.Tokens.SelectMany(token => token.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
}
