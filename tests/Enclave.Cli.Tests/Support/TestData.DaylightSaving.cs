namespace Enclave.Cli.Tests.Support;

// London's rules, made by the test so they are the same on every OS. A TransitionTime gives the
// change into daylight saving in standard time and the change out in daylight saving time
// (https://learn.microsoft.com/dotnet/api/system.timezoneinfo.transitiontime.timeofday, .NET 10).
// On 25 October 2026 the local times 01:00 to 01:59 happen twice: 00:00 to 00:59 UTC, then 01:00
// to 01:59 UTC.

// A local time zone with daylight saving, for a test whose result depends on the hour repeated
// when the clocks go back.
internal static partial class TestData
{
    private const string DaylightSavingZoneName = "Enclave CLI test UTC+00:00 with daylight saving";

    // UTC+00:00, with an hour of daylight saving from 01:00 on the last Sunday of March to 02:00 on
    // the last Sunday of October.
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
