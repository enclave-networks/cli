using System.CommandLine;
using System.Globalization;
using Enclave.Api.Modules.SystemManagement.Common.Models;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Enums;

namespace Enclave.Cli.Commands.Key;

/// <summary>
/// --for, --until and --then, which make a key create or a key enable temporary
/// (proposed-cli-surface.md "Command options").
/// </summary>
internal sealed class KeyTiming
{
    private KeyTiming(Option<TimeSpan?> forOption, Option<TimeInput?> untilOption, Option<ExpiryAction?> thenOption)
    {
        For = forOption;
        Until = untilOption;
        Then = thenOption;
    }

    public Option<TimeSpan?> For { get; }

    public Option<TimeInput?> Until { get; }

    public Option<ExpiryAction?> Then { get; }

    /// <summary>
    /// Adds the three options to the verb, with the checks between them: --for with --until, or
    /// --then without either, exits 2 ("Details").
    /// </summary>
    public static KeyTiming AddTo(CliVerb verb)
    {
        var timing = new KeyTiming(
            verb.Add(CliOptions.Duration("--for", "Make the change for this long.")),
            verb.Add(CliOptions.Time("--until", "Make the change until this time.")),
            verb.Add(CliOptions.Choice("--then", "What happens when the time is up: disable the key (the default), or delete it.", ("disable", ExpiryAction.Disable), ("delete", ExpiryAction.Delete))));

        verb.Exclusive(timing.For, timing.Until);
        verb.Requires(timing.Then, timing.For, timing.Until);

        return timing;
    }

    /// <summary>
    /// The UTC instant --for or --until gives, or null when neither is given. A --until time that
    /// has passed exits 2.
    /// </summary>
    public DateTimeOffset? Expiry(CliContext context) => context.ExpiryFrom(For, Until);

    /// <summary>
    /// What happens at the expiry: --then, or disable when it is left out.
    /// </summary>
    public ExpiryAction Action(CliContext context) => context.Get(Then) ?? ExpiryAction.Disable;

    /// <summary>
    /// The expiry as a create sends it, or null when neither --for nor --until is given.
    /// </summary>
    // The API takes the expiry as ISO 8601 text (AutoExpireModel.ExpiryDateTime), which "o" writes
    // with the instant's +00:00 offset, as Enclave.Sdk.Api 1.0.5 EnrolmentKeysClient.EnableUntilAsync
    // writes it. No time zone ID is sent: with one, the API moves the expiry when that zone's rules
    // change (AutoExpireModel.TimeZoneId), and the CLI sends an instant (proposed-cli-surface.md
    // "Details").
    public AutoExpireModel? AutoExpire(CliContext context) =>
        Expiry(context) is { } instant
            ? new AutoExpireModel(null, instant.ToString("o", CultureInfo.InvariantCulture), Action(context))
            : null;
}
