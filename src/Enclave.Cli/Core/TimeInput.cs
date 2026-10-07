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
    /// The instant this time names, read forwards from now: a duration counts from now, and a clock
    /// time is its next occurrence after now. Null when a date and time without a zone does not
    /// exist in the local time zone (it falls in a daylight saving gap).
    /// </summary>
    public DateTimeOffset? Forwards(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        var now = time.GetUtcNow();

        return _form switch
        {
            Form.Duration => now + _duration,
            Form.Instant => _instant.ToUniversalTime(),
            Form.Local => FromLocal(_local, time.LocalTimeZone),
            _ => NextClock(now, time.LocalTimeZone, forwards: true),
        };
    }

    /// <summary>
    /// The instant this time names, read backwards from now: a duration counts back from now, and a
    /// clock time is its latest occurrence at or before now. Null as for <see cref="Forwards"/>.
    /// </summary>
    public DateTimeOffset? Backwards(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);

        var now = time.GetUtcNow();

        return _form switch
        {
            Form.Duration => now - _duration,
            Form.Instant => _instant.ToUniversalTime(),
            Form.Local => FromLocal(_local, time.LocalTimeZone),
            _ => NextClock(now, time.LocalTimeZone, forwards: false),
        };
    }

    private static DateTimeOffset? FromLocal(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local))
        {
            return null;
        }

        // An ambiguous local time (the hour repeated when daylight saving ends) is read as standard
        // time, which TimeZoneInfo.ConvertTimeToUtc chooses.
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private DateTimeOffset? NextClock(DateTimeOffset now, TimeZoneInfo zone, bool forwards)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var step = forwards ? 1 : -1;

        // Today's occurrence, or the next (or previous) day's when today's is on the wrong side of
        // now. A day whose occurrence falls in a daylight saving gap is skipped.
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
