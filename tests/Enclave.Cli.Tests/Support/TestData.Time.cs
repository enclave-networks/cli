namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The local time zone a test whose result depends on the time zone gives the CLI, through a
/// <see cref="FixedTimeProvider"/>.
/// </summary>
internal static partial class TestData
{
    private const string LocalZoneName = "Enclave CLI test UTC+05:30";

    // UTC+05:30 with no daylight saving, made by the test: a runner set to UTC is not in it, its
    // offset is not a whole number of hours, and its rules are the same on every OS, so reading a
    // time in it and reading the same time in UTC give instants that differ in hours and minutes.

    /// <summary>
    /// UTC+05:30 with no daylight saving.
    /// </summary>
    public static TimeZoneInfo LocalZone { get; } =
        TimeZoneInfo.CreateCustomTimeZone(LocalZoneName, TimeSpan.FromMinutes(330), LocalZoneName, LocalZoneName);
}
