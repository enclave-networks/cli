namespace Enclave.Cli.Tests.Support;

// A clock that always reads one instant, in a given local time zone.
// TimeProvider.GetLocalNow converts GetUtcNow into LocalTimeZone, so these two members fix every
// reading of the clock and the local time (System.TimeProvider, .NET 8 and later). The CLI starts
// no timers, so the timer members keep the base behaviour.
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    private readonly TimeZoneInfo _localTimeZone;

    // GetUtcNow returns a zero offset (TimeProvider.GetUtcNow documentation).
    public FixedTimeProvider(DateTimeOffset now, TimeZoneInfo localTimeZone)
    {
        ArgumentNullException.ThrowIfNull(localTimeZone);

        _now = now.ToUniversalTime();
        _localTimeZone = localTimeZone;
    }

    public override TimeZoneInfo LocalTimeZone => _localTimeZone;

    public override DateTimeOffset GetUtcNow() => _now;
}
