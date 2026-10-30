using System.CommandLine;
using System.Globalization;
using Enclave.Api.Modules.SystemManagement.Common.Models;
using Enclave.Configuration.Data.Enums;

namespace Enclave.Cli.Core;

// --for, --until and --then, which make a change temporary ("Options on every command").
internal sealed class Timing
{
    private Timing(Option<TimeSpan?> forOption, Option<TimeInput?> untilOption, Option<ExpiryAction?> thenOption)
    {
        For = forOption;
        Until = untilOption;
        Then = thenOption;
    }

    public Option<TimeSpan?> For { get; }

    public Option<TimeInput?> Until { get; }

    public Option<ExpiryAction?> Then { get; }

    // The descriptions read "{change} for this long" and "disable the {item} (the default), or
    // {deleteName} it"; deleteName is the --then value for ExpiryAction.Delete.
    public static Timing AddTo(CliVerb verb, string change, string item, string deleteName = "delete")
    {
        ArgumentNullException.ThrowIfNull(verb);

        var timing = new Timing(
            verb.Add(CliOptions.Duration("--for", $"{change} for this long.")),
            verb.Add(CliOptions.Time("--until", $"{change} until this time.")),
            verb.Add(CliOptions.Choice("--then", $"What happens when the time is up: disable the {item} (the default), or {deleteName} it.", ("disable", ExpiryAction.Disable), (deleteName, ExpiryAction.Delete))));

        verb.Exclusive(timing.For, timing.Until);
        verb.Requires(timing.Then, timing.For, timing.Until);

        return timing;
    }

    public bool IsGiven(CliContext context) => context.IsGiven(For) || context.IsGiven(Until);

    // The API's timed enable has no bulk form, so it takes one item. A name given twice is one item,
    // since names match ignoring case; IDs arrive with duplicates removed ("Several IDs").
    public void RequireOne<TId>(CliContext context, ItemSelection<TId> selection, string item)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(selection);

        if (selection.Ids.Count + selection.Names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            Option option = context.IsGiven(For) ? For : Until;
            throw CliErrors.InvalidArgument(option.Name, $"{option.Name} takes one {item}: the API's timed enable has no bulk form.");
        }
    }

    // Null when neither --for nor --until is given. A --until time that has passed exits 2.
    public DateTimeOffset? Expiry(CliContext context) => context.ExpiryFrom(For, Until);

    public ExpiryAction Action(CliContext context) => context.Get(Then) ?? ExpiryAction.Disable;

    // "o" writes the instant with its +00:00 offset, as Enclave.Sdk.Api
    // EnrolmentKeysClient.EnableUntilAsync does. No time zone ID is sent: with one, the API moves the
    // expiry when that zone's rules change (AutoExpireModel.TimeZoneId; "Details").
    public AutoExpireModel? AutoExpire(CliContext context) =>
        Expiry(context) is { } instant
            ? new AutoExpireModel(null, instant.ToString("o", CultureInfo.InvariantCulture), Action(context))
            : null;
}
