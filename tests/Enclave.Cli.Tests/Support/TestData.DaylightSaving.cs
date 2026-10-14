namespace Enclave.Cli.Tests.Support;

// London's rules, made by the test so that they are the same on every OS and need no time zone
// database: UTC+00:00, and UTC+01:00 from the last Sunday of March to the last Sunday of October.
// A TransitionTime gives the change into daylight saving time in standard time, and the change out
// of it in daylight saving time
// (https://learn.microsoft.com/dotnet/api/system.timezoneinfo.transitiontime.timeofday, .NET 10), so
// the clocks go forward at 01:00 and back at 02:00, as London's do. On 25 October 2026 they go back
// at 02:00, so the local times 01:00 to 01:59 happen twice: first at UTC+01:00, 00:00 to 00:59 UTC,
// then at UTC+00:00, 01:00 to 01:59 UTC.

/// <summary>
/// A local time zone with daylight saving, for a test whose result depends on the hour repeated
/// when the clocks go back.
/// </summary>
internal static partial class TestData
{
    private const string DaylightSavingZoneName = "Enclave CLI test UTC+00:00 with daylight saving";

    /// <summary>
    /// UTC+00:00, with an hour of daylight saving from 01:00 on the last Sunday of March to 02:00 on
    /// the last Sunday of October.
    /// </summary>
    public static TimeZoneInfo DaylightSavingZone { get; } = TimeZoneInfo.CreateCustomTimeZone(
        DaylightSavingZoneName,
        TimeSpan.Zero,
        DaylightSavingZoneName,
        DaylightSavingZoneName + " (standard)",
        DaylightSavingZoneName + " (daylight saving)",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0, DateTimeKind.Unspecified), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0, DateTimeKind.Unspecified), 10, 5, DayOfWeek.Sunday)),
        ]);
}
