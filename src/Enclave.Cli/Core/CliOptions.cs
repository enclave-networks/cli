using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text;

namespace Enclave.Cli.Core;

// A value an option cannot read is a parse error, which exits 2 before the token is read and before
// any call. No error here repeats the value, so a token pasted after the wrong option stays out of
// stderr, as for unknown options (ParseProblems). NameLookup and OrganisationResolver do repeat a
// name that matches nothing, since the user needs it to see which name failed.
internal static class CliOptions
{
    public static Option<string?> Text(string name, string description, string valueName = "text") =>
        new(name)
        {
            Description = description,
            HelpName = valueName,
            Arity = ArgumentArity.ExactlyOne,
        };

    // A blank filter is refused: it usually comes from an unset shell variable, and read as the
    // filter left out, `system list --filter "$NAME" | enclave-cli system revoke -` would revoke
    // every system (spec/cli-surface.md "Details").
    public static Option<string?> Filter(string name, string description, string valueName = "text") =>
        Single<string?>(name, description, valueName, (text, result) =>
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            result.AddError($"{name} filters the list, and the value given is blank. Give a value, or leave {name} out.");
            return null;
        });

    /// <summary>
    /// For text with no blank form, such as a new name or a tag's colour; a blank value is a parse
    /// error. <paramref name="takes"/> says what it is, for errors: "a full hostname".
    /// </summary>
    // Sent, a blank value would reach the API after the token is read, so the same command line would
    // exit 3 without a token and 1 with one. Each command cites the validator that refuses it.
    // Optional text the API takes empty, such as notes, is a Text option, where "" clears it
    // (spec/cli-surface.md "Details").
    public static Option<string?> NonBlank(string name, string description, string takes, string valueName = "text") =>
        Single<string?>(name, description, valueName, (text, result) =>
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            result.AddError($"{name} takes {takes}, and the value given is blank.");
            return null;
        });

    public static Option<bool> Flag(string name, string description) =>
        new(name)
        {
            Description = description,
            Arity = ArgumentArity.Zero,
        };

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
    /// "" gives an empty list, or a parse error with <paramref name="needsEntries"/>; an empty ID
    /// among others ("42,", "42,,43") is a parse error.
    /// </summary>
    /// <typeparam name="T">The ID type.</typeparam>
    public static Option<IReadOnlyList<T>?> IdList<T>(string name, string description, IdFormat<T> format, string valueName = "ids", bool needsEntries = false) =>
        CommaList(name, description, valueName, IdPiece(name, format), needsEntries ? NeedsEntries(name) : null);

    /// <summary>
    /// Reads "" and empty entries as <see cref="IdList{T}"/> does.
    /// </summary>
    public static Option<IReadOnlyList<string>?> List(string name, string description, string valueName = "a,b", bool needsEntries = false) =>
        CommaList<string>(name, description, valueName, (piece, _) => (true, piece), needsEntries ? NeedsEntries(name) : null);

    public static Option<IReadOnlyList<string>?> TagList(string name, string description, bool needsEntries = false) =>
        IdList(name, description, IdFormats.Tag, "a,b", needsEntries);

    /// <summary>
    /// A value that holds no tag ("", ",") is a parse error, as a blank <see cref="Filter"/> is.
    /// </summary>
    public static Option<IReadOnlyList<string>?> TagFilter(string name, string description) =>
        CommaList(name, description, "a,b", IdPiece(name, IdFormats.Tag), HoldsNothing(name));

    // Commas are not separators, since a value can contain them.
    public static Option<IReadOnlyList<string>?> Repeated(string name, string description, string valueName) =>
        new(name)
        {
            Description = description,
            HelpName = valueName,
            Arity = ArgumentArity.OneOrMore,
            CustomParser = result => result.Tokens.Select(token => token.Value).ToArray(),
        };

    /// <summary>
    /// A blank value before its label is a parse error. With <paramref name="clears"/>, for a --set-
    /// flag, a blank value given alone clears the list
    /// (<see cref="ListValues.Clears(IReadOnlyList{LabelledValue})"/>).
    /// </summary>
    // A value with nothing before its label names nothing: dropped, it would shorten the list; sent,
    // the API would refuse it after the token is read (portal EnrolmentKeyIpConstraintInputValidator,
    // NotEmpty; spec/cli-surface.md "Details").
    public static Option<IReadOnlyList<LabelledValue>?> Labelled(string name, string description, string valueName, bool clears = false) =>
        new(name)
        {
            Description = description,
            HelpName = valueName + "[=<label>]",
            Arity = ArgumentArity.OneOrMore,
            CustomParser = result =>
            {
                var values = result.Tokens.Select(token => LabelledValue.Parse(token.Value)).ToArray();

                if (clears && ListValues.Clears(values))
                {
                    return values;
                }

                if (values.Any(value => string.IsNullOrWhiteSpace(value.Value)))
                {
                    result.AddError($"Each {name} takes a {valueName}, and one given is blank.");
                    return null;
                }

                return values;
            },
        };

    /// <summary>
    /// Each member is given hyphenated: RecentlyConnected is recently-connected.
    /// </summary>
    /// <typeparam name="TEnum">The API enum.</typeparam>
    public static ChoiceOption<TEnum?> Enum<TEnum>(string name, string description, params TEnum[] members)
        where TEnum : struct, Enum =>
        Choice(name, description, members.Select(member => (Hyphenated(member.ToString()), member)).ToArray());

    public static ChoiceOption<T?> Choice<T>(string name, string description, params (string Value, T Result)[] choices)
        where T : struct =>
        OneOf(name, description, choices.Select(choice => (choice.Value, (T?)choice.Result)).ToArray());

    public static ChoiceOption<string?> ChoiceText(string name, string description, params (string Value, string Result)[] choices) =>
        OneOf(name, description, choices.Select(choice => (choice.Value, (string?)choice.Result)).ToArray());

    // The list names what a command keeps, as log --level keeps the entries at those levels, so one
    // with no value keeps nothing, and is refused as a blank Filter is.
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
            if (Pieces(result, name) is not { } pieces)
            {
                return null;
            }

            var values = new List<T>();

            foreach (var piece in pieces)
            {
                var match = choices.Where(choice => string.Equals(piece, choice.Value, StringComparison.OrdinalIgnoreCase)).ToArray();

                if (match.Length == 0)
                {
                    result.AddError($"{name} takes values separated by commas, each one of: {string.Join(", ", option.AllowedValues)}.");
                    return null;
                }

                values.Add(match[0].Result);
            }

            if (values.Count == 0)
            {
                result.AddError(HoldsNothing(name));
                return null;
            }

            return values;
        };

        return option;
    }

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
    /// CliContext reads the value into an instant.
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

    // TResult is the nullable result, so default is the null a failed parse returns.
    private static ChoiceOption<TResult> OneOf<TResult>(string name, string description, (string Value, TResult Result)[] choices)
    {
        var option = new ChoiceOption<TResult>(name, choices.Select(choice => choice.Value).ToArray())
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
            return default!;
        };

        return option;
    }

    // noEntries refuses an empty list where it means nothing: a filter (HoldsNothing) or a list that
    // needs entries (NeedsEntries).
    private static Option<IReadOnlyList<T>?> CommaList<T>(string name, string description, string valueName, Func<string, ArgumentResult, (bool Valid, T Value)> parse, string? noEntries = null) =>
        new(name)
        {
            Description = description,
            HelpName = valueName,
            Arity = ArgumentArity.OneOrMore,
            CustomParser = result =>
            {
                if (Pieces(result, name) is not { } pieces)
                {
                    return null;
                }

                var values = new List<T>();

                foreach (var piece in pieces)
                {
                    var (valid, value) = parse(piece, result);

                    if (!valid)
                    {
                        return null;
                    }

                    values.Add(value);
                }

                if (noEntries is not null && values.Count == 0)
                {
                    result.AddError(noEntries);
                    return null;
                }

                return values;
            },
        };

    private static Func<string, ArgumentResult, (bool Valid, T Value)> IdPiece<T>(string name, IdFormat<T> format) =>
        (piece, result) =>
        {
            if (format.TryParse(piece, out var value))
            {
                return (true, value);
            }

            result.AddError($"{name} takes IDs separated by commas, each {format.Describe}.");
            return (false, value);
        };

    private static string HoldsNothing(string name) =>
        $"{name} filters the list, and the value given names nothing. Give at least one value, or leave {name} out.";

    // An empty list means something only where it clears a list, as a --set- flag's does. On a
    // create, or on --add-tags and --remove-tags, it would act as the flag left out, which an empty
    // value is never read as (spec/cli-surface.md "Details").
    private static string NeedsEntries(string name) =>
        $"{name} takes at least one value, and the value given holds none. Give a value, or leave {name} out.";

    // A blank value holds no pieces, so --set-tags "" gives the empty list that clears a list. Any
    // other empty piece ("web,", ",", or "" beside another value) is a parse error: dropped, it would
    // shorten the list without a word, and it usually comes from an unset shell variable, as in
    // `--trust "uk only,$EXTRA"` (spec/cli-surface.md "Details"). Null after an error.
    private static string[]? Pieces(ArgumentResult result, string name)
    {
        if (result.Tokens.All(token => string.IsNullOrWhiteSpace(token.Value)))
        {
            return [];
        }

        var pieces = result.Tokens.SelectMany(token => token.Value.Split(',', StringSplitOptions.TrimEntries)).ToArray();

        if (pieces.Any(piece => piece.Length == 0))
        {
            result.AddError($"{name} takes values separated by commas, and the value given holds an empty one.");
            return null;
        }

        return pieces;
    }
}
