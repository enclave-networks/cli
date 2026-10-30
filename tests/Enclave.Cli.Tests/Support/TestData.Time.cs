namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The local time zone and the fixed clock for a <see cref="FixedTimeProvider"/>.
/// </summary>
internal static partial class TestData
{
    // A --until that has passed exits 2 ("Details"), so a test that needs one in the future gives
    // FutureUntil and runs on Clock, which is fixed before it, whatever the date.
    public const string FutureUntil = "2030-01-01T00:00:00Z";

    private const string LocalZoneName = "Enclave CLI test UTC+05:30";

    // A zone made by the test has the same rules on every OS, and an offset of hours and minutes
    // tells a time read in it from the same time read in a runner's UTC.

    /// <summary>
    /// UTC+05:30 with no daylight saving.
    /// </summary>
    public static TimeZoneInfo LocalZone { get; } =
        TimeZoneInfo.CreateCustomTimeZone(LocalZoneName, TimeSpan.FromMinutes(330), LocalZoneName, LocalZoneName);

    public static FixedTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
}
