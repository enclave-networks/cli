using System.Globalization;
using System.Text.RegularExpressions;

namespace Enclave.Cli.Core;

/// <summary>
/// A time as a command line gives it, read into an instant against the clock and local time zone
/// in <see cref="CliHost.Time"/>.
/// </summary>
// The forms are those of proposed-cli-surface.md "Command options": a duration (30m, 8h, 14d), an
// RFC 3339 time with its zone, a date and time without a zone, or a clock time (18:00), the last
// two read in the machine's time zone. Each is parsed with the invariant culture and an exact
// pattern, so a command means the same on every machine whatever its locale and calendar; forms a
// locale would also read, such as "10/09/2030" or "5:30 PM", are refused.
internal sealed partial class TimeInput
{
    private static readonly string[] ZonedFormats =
    [
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
    ];

    private static readonly string[] LocalFormats =
    [
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH:mm:ss",
    ];

    private readonly Form _form;

    private readonly TimeSpan _duration;

    private readonly DateTimeOffset _instant;

    private readonly DateTime _local;

    private readonly TimeOnly _clock;

    private TimeInput(Form form, TimeSpan duration = default, DateTimeOffset instant = default, DateTime local = default, TimeOnly clock = default)
    {
        _form = form;
        _duration = duration;
        _instant = instant;
        _local = local;
        _clock = clock;
    }

    private enum Form
    {
        Duration,
        Instant,
        Local,
        Clock,
    }

    /// <summary>
    /// Reads a duration: a whole number above zero followed by m (minutes), h (hours) or d (days).
    /// </summary>
    public static bool TryParseDuration(string text, out TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(text);

        duration = default;
        var match = DurationPattern().Match(text);

        if (!match.Success || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count == 0)
        {
            return false;
        }

        var minutesPerUnit = match.Groups[2].Value switch
        {
            "m" => 1L,
            "h" => 60L,
            _ => 24L * 60L,
        };

        // TimeSpan holds about 29,000 years; a longer duration is refused, not wrapped.
        if (count > (long)TimeSpan.MaxValue.TotalMinutes / minutesPerUnit)
        {
            return false;
        }

        duration = TimeSpan.FromMinutes(count * minutesPerUnit);
        return true;
    }

    /// <summary>
    /// Reads a time in one of the forms the CLI takes; a duration only when
    /// <paramref name="allowDuration"/> is set.
    /// </summary>
    public static bool TryParse(string text, bool allowDuration, out TimeInput? input)
    {
        ArgumentNullException.ThrowIfNull(text);

        input = null;

        if (allowDuration && TryParseDuration(text, out var duration))
        {
            input = new TimeInput(Form.Duration, duration: duration);
            return true;
        }

        if (ZonedPattern().IsMatch(text))
        {
            // zzz reads "+05:30" but not "Z", which RFC 3339 also allows for UTC.
            var normalised = text.EndsWith('Z') ? string.Concat(text.AsSpan(0, text.Length - 1), "+00:00") : text;

            if (DateTimeOffset.TryParseExact(normalised, ZonedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            {
                input = new TimeInput(Form.Instant, instant: instant);
                return true;
            }

            return false;
        }

        if (DateTime.TryParseExact(text, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            input = new TimeInput(Form.Local, local: DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
            return true;
        }

        if (ClockPattern().IsMatch(text) && TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock))
        {
            input = new TimeInput(Form.Clock, clock: clock);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The instant this time names, read forwards from now, as UTC: a duration counts from now, and
    /// a clock time is its next occurrence after now. Exits 2, naming the option
    /// <paramref name="name"/>, when a time without a zone does not exist in the local time zone (it
    /// falls in a daylight saving gap), or a duration reaches past the latest time the CLI holds.
    /// </summary>
    public DateTimeOffset Forwards(TimeProvider time, string name)
    {
        ArgumentNullException.ThrowIfNull(time);

        var now = time.GetUtcNow();

        return _form switch
        {
            Form.Duration => After(now, _duration, name),
            Form.Instant => _instant.ToUniversalTime(),
            Form.Local => FromLocal(_local, time.LocalTimeZone) ?? throw NotInLocalZone(name),
            _ => NextClock(now, time.LocalTimeZone, forwards: true) ?? throw NotInLocalZone(name),
        };
    }

    /// <summary>
    /// The instant this time names, read backwards from now, as UTC: a duration counts back from
    /// now, and a clock time is its latest occurrence at or before now. Exits 2 as
    /// <see cref="Forwards"/> does, and when a duration reaches back before the earliest time the
    /// CLI holds.
    /// </summary>
    public DateTimeOffset Backwards(TimeProvider time, string name)
    {
        ArgumentNullException.ThrowIfNull(time);

        var now = time.GetUtcNow();

        return _form switch
        {
            Form.Duration => Before(now, _duration, name),
            Form.Instant => _instant.ToUniversalTime(),
            Form.Local => FromLocal(_local, time.LocalTimeZone) ?? throw NotInLocalZone(name),
            _ => NextClock(now, time.LocalTimeZone, forwards: false) ?? throw NotInLocalZone(name),
        };
    }

    /// <summary>
    /// The instant <paramref name="duration"/> after <paramref name="now"/>, as UTC. Exits 2,
    /// naming the option <paramref name="name"/>, when it is past the end of year 9999.
    /// </summary>
    // DateTimeOffset holds the years 1 to 9999 (DateTimeOffset.MinValue and MaxValue,
    // https://learn.microsoft.com/dotnet/api/system.datetimeoffset.maxvalue), and arithmetic beyond
    // them throws ArgumentOutOfRangeException, which no option check would name. A duration of up to
    // about 29,000 years parses (TimeSpan.MaxValue), so the instant is checked against the range
    // here. The difference of two DateTimeOffset values is at most 10,000 years, so the check itself
    // cannot overflow.
    public static DateTimeOffset After(DateTimeOffset now, TimeSpan duration, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        return duration <= DateTimeOffset.MaxValue - now
            ? now.ToUniversalTime() + duration
            : throw CliErrors.InvalidArgument(name, $"The duration given to {name} reaches past the end of year 9999, the latest time the CLI holds.");
    }

    /// <summary>
    /// The instant <paramref name="duration"/> before <paramref name="now"/>, as UTC. Exits 2,
    /// naming the option <paramref name="name"/>, when it is before the start of year 1.
    /// </summary>
    // After gives the reason for the range check.
    public static DateTimeOffset Before(DateTimeOffset now, TimeSpan duration, string name)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);

        return duration <= now - DateTimeOffset.MinValue
            ? now.ToUniversalTime() - duration
            : throw CliErrors.InvalidArgument(name, $"The duration given to {name} reaches back before the start of year 1, the earliest time the CLI holds.");
    }

    private static CliException NotInLocalZone(string name) =>
        CliErrors.InvalidArgument(name, $"The time given to {name} does not exist in the local time zone.");

    private static DateTimeOffset? FromLocal(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        // An ambiguous local time (the hour repeated when daylight saving ends) is read as standard
        // time, which TimeZoneInfo.ConvertTimeToUtc chooses: "If dateTime is ambiguous, this method
        // assumes that it is the standard time of the source time zone"
        // (https://learn.microsoft.com/dotnet/api/system.timezoneinfo.converttimetoutc, .NET 10).
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private DateTimeOffset? NextClock(DateTimeOffset now, TimeZoneInfo zone, bool forwards)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var step = forwards ? 1 : -1;

        // Two days away is far enough: today's occurrence can be on the wrong side of now, and the
        // next day's can fall in a daylight saving gap, which leaves that day without one. A clock
        // time with no occurrence that close is reported as not existing in the local time zone.
        for (var days = 0; Math.Abs(days) <= 2; days += step)
        {
            var candidate = FromLocal(today.AddDays(days).Add(_clock.ToTimeSpan()), zone);

            if (candidate is { } instant && (forwards ? instant > now : instant <= now))
            {
                return instant;
            }
        }

        return null;
    }

    [GeneratedRegex(@"^([0-9]+)([mhd])\z", RegexOptions.CultureInvariant)]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2}(\.[0-9]{1,7})?)?(Z|[+-][0-9]{2}:[0-9]{2})\z", RegexOptions.CultureInvariant)]
    private static partial Regex ZonedPattern();

    [GeneratedRegex(@"^[0-9]{2}:[0-9]{2}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();
}
