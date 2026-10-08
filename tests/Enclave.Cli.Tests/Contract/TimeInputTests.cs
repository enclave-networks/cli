using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// --for takes a duration (30m, 8h, 14d) and --until a time: RFC 3339 with its zone, used as given,
// or a time without a zone read in the system's time zone, either a date and time or a clock time
// meaning its next occurrence. Formats do not follow the system locale (proposed-cli-surface.md
// "Command options"). A time that has passed exits 2, and the CLI sends the expiry as a UTC
// instant ("Details"). `policy enable --id 42` carries each value: the API's timed enable takes one
// item by ID (PUT policies/{id}/enable-until), and the expiryDateTime of the body (AutoExpireModel)
// shows how the CLI read the value.
//
// Every run reads a fixed clock in a fixed time zone through CliHost.Time, so each expected
// instant is exact and the same on every machine. The zone is TestData.LocalZone, UTC+05:30 with
// no daylight saving, made by the test: a runner set to UTC is not in it, its offset is not a whole
// number of hours, and its rules are the same on every OS. The clock reads 20:00 UTC, which is
// 01:30 the next day in that zone, so reading a time in UTC and reading it in the zone give
// different dates as well as different hours.
public class TimeInputTests
{
    // A time well after the clock, for a command that needs a valid --until.
    private const string Later = "2030-10-09T17:30:00Z";

    private static readonly DateTimeOffset Now = new(2030, 3, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly string EnableUntilPath = TestData.OrgPath("policies/42/enable-until");

    [TestCase("30m", 30)]
    [TestCase("8h", 8 * 60)]
    [TestCase("14d", 14 * 24 * 60)]
    public async Task For_a_duration_expires_that_long_after_now(string duration, int minutes)
    {
        using var run = StartWithPolicy(Now);

        var result = await run.RunAsync("policy", "enable", "--id", "42", "--for", duration);

        Assert.That(Expiry(EnableUntilBody(run, result)), Is.EqualTo(Now.AddMinutes(minutes)));
    }

    // Afterwards the item is disabled unless --then says otherwise ("Command options"), so the
    // request states the API's Disable (portal Enclave.Configuration.Data/Enums/ExpiryAction.cs).
    [Test]
    public async Task For_without_then_disables_the_item_when_it_expires()
    {
        using var run = StartWithPolicy(Now);

        var result = await run.RunAsync("policy", "enable", "--id", "42", "--for", "8h");

        Assert.That(EnableUntilBody(run, result).GetProperty("expiryAction").GetString(), Is.EqualTo("Disable"));
    }

    // The zone written in the time decides the instant, whatever the system's time zone is.
    [TestCase("2030-10-09T17:30:00Z", "2030-10-09T17:30:00Z")]
    [TestCase("2030-10-09T17:30:00+00:00", "2030-10-09T17:30:00Z")]
    [TestCase("2030-10-09T17:30:00-04:00", "2030-10-09T21:30:00Z")]
    [TestCase("2030-10-09T17:30:00+05:30", "2030-10-09T12:00:00Z")]
    [TestCase("2030-10-09T17:30:00+01:00", "2030-10-09T16:30:00Z")]
    public async Task Until_an_rfc_3339_time_with_its_zone_expires_at_that_instant(string until, string expected)
    {
        using var run = StartWithPolicy(Now);

        var result = await run.RunAsync("policy", "enable", "--id", "42", "--until", until);

        Assert.That(Expiry(EnableUntilBody(run, result)), Is.EqualTo(Instant(expected)));
    }

    // 09:00 in the system's zone is 03:30 UTC; read as UTC it would be 09:00 UTC.
    [Test]
    public async Task Until_a_date_and_time_without_a_zone_is_read_in_the_system_time_zone()
    {
        using var run = StartWithPolicy(Now);

        var result = await run.RunAsync("policy", "enable", "--id", "42", "--until", "2030-03-15T09:00");

        Assert.That(Expiry(EnableUntilBody(run, result)), Is.EqualTo(Instant("2030-03-15T03:30:00Z")));
    }

    // Example 22's form. The clock reads, in the system's zone: 01:30 on the 15th, so 18:00 is later
    // that day; 17:59, one minute before it; 18:01, so that day's 18:00 has passed and the next is on
    // the 16th. 18:00 on the 15th in the zone is 12:30 UTC.
    [TestCase("2030-03-14T20:00:00Z", "2030-03-15T12:30:00Z")]
    [TestCase("2030-03-15T12:29:00Z", "2030-03-15T12:30:00Z")]
    [TestCase("2030-03-15T12:31:00Z", "2030-03-16T12:30:00Z")]
    public async Task Until_a_clock_time_is_its_next_occurrence_in_the_system_time_zone(string now, string expected)
    {
        using var run = StartWithPolicy(Instant(now));

        var result = await run.RunAsync("policy", "enable", "--id", "42", "--until", "18:00");

        Assert.That(Expiry(EnableUntilBody(run, result)), Is.EqualTo(Instant(expected)));
    }

    // An expiry that has passed would end the change before it starts ("Details": --until in the
    // past exits 2). 01:00 on the 15th in the system's zone is 19:30 UTC on the 14th, half an hour
    // before the clock, while 01:00 UTC on the 15th is after it, so the second case also shows the
    // zoneless time is read in the system's zone.
    [TestCase("2030-03-14T19:59:00Z")]
    [TestCase("2030-03-15T01:00")]
    [TestCase("2029-12-31T23:59:59-04:00")]
    public async Task Until_a_time_that_has_passed_exits_2_without_a_request(string until)
    {
        using var run = StartWithPolicy(Now);

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "enable", "--id", "42", "--until", until],
            ["policy", "enable", "--id", "42", "--until", Later],
            "PUT",
            EnableUntilPath);
    }

    // A parser that follows the current culture reads the year in that culture's calendar, and the
    // Thai Buddhist calendar of th-TH puts 543 years between its years and the Gregorian ones
    // (System.Globalization.ThaiBuddhistCalendar). The time must mean the same as on any other
    // machine.
    [TestCase("2030-10-09T17:30:00Z", "2030-10-09T17:30:00Z")]
    [TestCase("2030-10-09T17:30", "2030-10-09T12:00:00Z")]
    [SetCulture("th-TH")]
    public async Task Until_a_time_means_the_same_under_a_locale_with_another_calendar(string until, string expected)
    {
        using var run = StartWithPolicy(Now);

        var result = await run.RunAsync("policy", "enable", "--id", "42", "--until", until);

        Assert.That(Expiry(EnableUntilBody(run, result)), Is.EqualTo(Instant(expected)));
    }

    // Each value is a form the specification does not list, and en-US reads every one of them: a
    // date in the locale's order, a date with dots, a 12-hour clock and a month name. A CLI that
    // parsed with the current culture would take them here and read them differently elsewhere, so
    // each exits 2 and sends nothing. "8x" has no unit the duration form knows.
    [TestCase("--until", "10/09/2030 17:30")]
    [TestCase("--until", "09.10.2030 17:30")]
    [TestCase("--until", "5:30 PM")]
    [TestCase("--until", "Oct 9, 2030 17:30")]
    [TestCase("--for", "8x")]
    [SetCulture("en-US")]
    public async Task A_time_in_a_form_the_cli_does_not_take_exits_2_without_a_request(string option, string value)
    {
        using var run = StartWithPolicy(Now);

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "enable", "--id", "42", option, value],
            ["policy", "enable", "--id", "42", "--until", Later],
            "PUT",
            EnableUntilPath);
    }

    // A time holds instants from year 1 to year 9999 (DateTimeOffset.MinValue and MaxValue), so a
    // duration that reaches beyond them names no instant the CLI can send or compare, and the
    // argument is refused, naming its option, before any call. From the clock in 2030, 5000000d
    // forwards is about the year 15700 and 1000000d back is about 700 years before year 1; both are
    // durations the duration form reads (TimeSpan holds about 29,000 years). log's --since and
    // --until count back from now. The corrected run proves the command and option exist.
    [TestCase("policy enable --id 42", "--for", "5000000d", "8h", "PUT", "policies/42/enable-until")]
    [TestCase("log", "--since", "1000000d", "24h", "GET", "logs")]
    [TestCase("log", "--until", "1000000d", "1h", "GET", "logs")]
    public async Task A_duration_reaching_beyond_the_times_the_cli_can_hold_exits_2_naming_its_option(
        string command,
        string option,
        string duration,
        string corrected,
        string method,
        string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = StartWithPolicy(Now);
        var path = TestData.OrgPath(pathSuffix);
        run.StubPages(TestData.OrgPath("logs"), 200);

        var rejected = await run.RunAsync([.. command.Split(' '), option, duration]);

        CliAssert.Rejected(run, rejected);
        Assert.That(JsonRead.PropertyNames(rejected.Error.GetProperty("errors")), Is.EqualTo(new[] { option }));

        await CliAssert.AcceptedAsync(run, method, path, [.. command.Split(' '), option, corrected]);
    }

    // --for and --until each set the expiry, so together they cannot mean one expiry; --then says
    // what happens at an expiry, so without either it has nothing to apply to ("Details").
    [TestCase("--for 8h --until 2030-10-09T17:30:00Z", "--until 2030-10-09T17:30:00Z")]
    [TestCase("--then delete", "--for 8h --then delete")]
    public async Task Options_that_contradict_each_other_exit_2_without_a_request(string rejected, string corrected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(corrected);
        using var run = StartWithPolicy(Now);

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["policy", "enable", "--id", "42", .. rejected.Split(' ')],
            ["policy", "enable", "--id", "42", .. corrected.Split(' ')],
            "PUT",
            EnableUntilPath);
    }

    private static CliRun StartWithPolicy(DateTimeOffset now)
    {
        var run = CliRun.Start();
        run.Time = new FixedTimeProvider(now, TestData.LocalZone);
        run.Stub("PUT", EnableUntilPath, json: ApiJson.Policy(42, "contractors"));
        return run;
    }

    // The CLI sends the expiry as a UTC instant ("Details"), so the time carries a zero offset,
    // written as Z or +00:00; a time with no offset would leave the API to guess its zone. The API
    // rejects a body whose timeZoneId gives a different offset at the expiry than the one written
    // in expiryDateTime (portal AutoExpireModelValidator.cs, AreOffsetAndTimeZoneConsistent), so a
    // zone sent with the time must be one the API knows, with a zero offset at that instant.
    private static JsonElement EnableUntilBody(CliRun run, CliResult result)
    {
        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PUT"));
            Assert.That(request.Path, Is.EqualTo(EnableUntilPath));
        });

        var body = request.BodyJson;
        Assert.That(JsonAssert.Property(body, "expiryDateTime").GetString(), Does.EndWith("Z").Or.EndWith("+00:00"), body.ToString());

        if (body.TryGetProperty("timeZoneId", out var zone) && zone.ValueKind == JsonValueKind.String)
        {
            Assert.That(TimeZoneInfo.TryFindSystemTimeZoneById(zone.GetString()!, out var timeZone), Is.True, $"The API does not know the timeZoneId in {body}");
            Assert.That(timeZone!.GetUtcOffset(Expiry(body)), Is.EqualTo(TimeSpan.Zero), $"The timeZoneId disagrees with the UTC instant in {body}");
        }

        return body;
    }

    private static DateTimeOffset Expiry(JsonElement body) => JsonRead.ExpiryDateTime(body);

    private static DateTimeOffset Instant(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.None);
}
