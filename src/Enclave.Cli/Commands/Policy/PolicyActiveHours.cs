using System.Globalization;
using System.Text.RegularExpressions;
using Enclave.Cli.Core;
using Enclave.Configuration.Data;

namespace Enclave.Cli.Commands.Policy;

// Reads --active-hours and --set-active-hours (spec/cli-surface.md "Details").
internal static partial class PolicyActiveHours
{
    // Monday first, as the days are written: a range runs forwards from Monday to Sunday.
    private static readonly (string Name, DayOfWeek Day)[] Days =
    [
        ("mon", DayOfWeek.Monday),
        ("tue", DayOfWeek.Tuesday),
        ("wed", DayOfWeek.Wednesday),
        ("thu", DayOfWeek.Thursday),
        ("fri", DayOfWeek.Friday),
        ("sat", DayOfWeek.Saturday),
        ("sun", DayOfWeek.Sunday),
    ];

    // Null when the text is not in the documented form ("Details"). The zone is UTC when left out.
    // The zone is sent as given for the API to check (portal ActiveHoursValidator,
    // TimeZoneIdValidator), since the zones the local machine knows can differ from the API's.
    public static ActiveHours? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is < 2 or > 3 || ReadDays(parts[0]) is not { } days)
        {
            return null;
        }

        var times = TimesPattern().Match(parts[1]);

        if (!times.Success)
        {
            return null;
        }

        var start = new HoursMinutes(Number(times.Groups[1]), Number(times.Groups[2]));
        var end = new HoursMinutes(Number(times.Groups[3]), Number(times.Groups[4]));

        return new ActiveHours(parts.Length == 3 ? parts[2] : "UTC", days, start, end);
    }

    // Exits 2 for a value not in the documented form, or hours that start and end at the same time.
    public static void Check(string option, string? text)
    {
        if (text is null)
        {
            return;
        }

        if (Parse(text) is not { } hours)
        {
            throw CliErrors.InvalidArgument(option, $"{option} takes days, a start and end time, and an optional IANA time zone: \"mon-fri 08:00-18:00 Europe/London\". Days are mon to sun, a range or a comma list; times are 24-hour HH:MM.");
        }

        // The check needs a local time at or after the start and before the end, so 00:00-00:00 is
        // never active either (services SystemIsopActiveHoursExtensions.IsWithinActiveHours).
        if (hours.StartTime.Equals(hours.EndTime))
        {
            throw CliErrors.InvalidArgument(option, $"{option} takes a start and an end that differ; hours that start and end at the same time are never active.");
        }
    }

    private static DayOfWeek[]? ReadDays(string text)
    {
        if (text.Contains(',', StringComparison.Ordinal))
        {
            var listed = text.Split(',').Select(DayIndex).ToArray();
            return listed.Any(index => index < 0) ? null : listed.Distinct().Select(index => Days[index].Day).ToArray();
        }

        var range = text.Split('-');

        if (range.Length == 2)
        {
            var first = DayIndex(range[0]);
            var last = DayIndex(range[1]);
            return first < 0 || last < first ? null : Days[first..(last + 1)].Select(day => day.Day).ToArray();
        }

        var single = DayIndex(text);
        return single < 0 ? null : [Days[single].Day];
    }

    private static int DayIndex(string name) => Array.FindIndex(Days, day => string.Equals(day.Name, name, StringComparison.OrdinalIgnoreCase));

    private static int Number(Group group) => int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^([01][0-9]|2[0-3]):([0-5][0-9])-([01][0-9]|2[0-3]):([0-5][0-9])\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimesPattern();
}
