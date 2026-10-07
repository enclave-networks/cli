namespace Enclave.Cli.Tests.Support;

/// <summary>
/// A clock that always reads one instant, in a given local time zone, for a test whose result depends on the time
/// (<see cref="CliRun.Time"/>).
/// </summary>
// TimeProvider.GetLocalNow converts GetUtcNow into LocalTimeZone, so overriding these two members
// fixes every reading of the clock and of the local time (System.TimeProvider, .NET 8 and later).
// The members a timer needs keep the base behaviour: the CLI starts no timers.
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    private readonly TimeZoneInfo _localTimeZone;

    // GetUtcNow returns an instant with a zero offset (TimeProvider.GetUtcNow documentation), so
    // the instant is kept in that form, whatever offset it was written with.
    public FixedTimeProvider(DateTimeOffset now, TimeZoneInfo localTimeZone)
    {
        ArgumentNullException.ThrowIfNull(localTimeZone);

        _now = now.ToUniversalTime();
        _localTimeZone = localTimeZone;
    }

    public override TimeZoneInfo LocalTimeZone => _localTimeZone;

    public override DateTimeOffset GetUtcNow() => _now;
}
