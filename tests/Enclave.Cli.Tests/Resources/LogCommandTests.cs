using System.Globalization;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Resources;

// The logs API takes only a page and a page size (portal LogsRequestModel), so the CLI applies
// every filter to the entries it reads, and reads only the pages it needs (spec/cli-surface.md
// "Calls per command").
public class LogCommandTests
{
    private const string Sam = "sam@example.com";

    private const string Alex = "alex@example.com";

    // 20:00 UTC is 01:30 the next day in TestData.LocalZone (UTC+05:30), so a time read in the
    // wrong zone gives a different instant and date.
    private static readonly DateTimeOffset Now = new(2030, 3, 14, 20, 0, 0, TimeSpan.Zero);

    private static readonly string LogsPath = TestData.OrgPath("logs");

    // Pages are the smaller of the limit and 200 (spec/cli-surface.md "Details"). Example 68.
    [Test]
    public async Task Log_prints_the_newest_100_entries_newest_first_from_one_page_of_100()
    {
        using var run = CliRun.Start();
        run.StubPages(LogsPath, 100, Entries(250, DateTime.UtcNow));

        var result = await run.RunAsync("log");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, 100)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0"));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.All.EqualTo("100"));
        });
    }

    [Test]
    public async Task Log_prints_every_entry_of_a_log_shorter_than_100_entries()
    {
        using var run = CliRun.Start();
        run.StubPages(LogsPath, 100, Entries(30, DateTime.UtcNow));

        var result = await run.RunAsync("log");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, 30)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0"));
        });
    }

    // The API pages by skipping entries of a list sorted newest first (portal
    // ActivityLogRepository.GetLogsAsync), so an entry written during the read moves older entries
    // onto the next page. Two entries are written after page 0 is read, so page 1 begins with
    // entries 2 and 3 again. The fake serves pages of four whatever per_page asks for.
    [Test]
    public async Task Log_prints_each_entry_once_when_entries_written_while_it_reads_shift_later_pages()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var entries = Entries(10, now.AddMinutes(-5));
        string[] written = [Entry("new 0", now), Entry("new 1", now.AddMinutes(-1))];
        string[] after = [.. written, .. entries];
        StubPage(run, 0, 4, entries.Length, entries[..4]);
        StubPage(run, 1, 4, after.Length, after[4..8]);
        StubPage(run, 2, 4, after.Length, after[8..12]);

        var result = await run.RunAsync("log");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, 10)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // Entries 2 and 3 share a time. An entry written after page 0 is read makes page 1 begin with
    // entry 2 again, then entry 3: a CLI that drops every entry at that time loses entry 3, and one
    // that drops only newer entries prints entry 2 twice.
    [Test]
    public async Task Log_keeps_an_entry_that_shares_its_time_with_the_last_entry_of_the_page_before()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var shared = now.AddMinutes(-7);
        string[] entries =
        [
            Entry("entry 0", now.AddMinutes(-5)),
            Entry("entry 1", now.AddMinutes(-6)),
            Entry("entry 2", shared),
            Entry("entry 3", shared),
            Entry("entry 4", now.AddMinutes(-8)),
            Entry("entry 5", now.AddMinutes(-9)),
        ];
        string[] after = [Entry("new 0", now), .. entries];
        StubPage(run, 0, 3, entries.Length, entries[..3]);
        StubPage(run, 1, 3, after.Length, after[3..6]);
        StubPage(run, 2, 3, after.Length, after[6..7]);

        var result = await run.RunAsync("log");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, 6)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // The API stores entries in batches every 2 seconds, each with its event's time (portal
    // ActivityLogSink, WritePeriod and ConvertEvent), so an entry can be stored after newer ones.
    // After page 0 is read, three entries are written and "late" is stored between entry 1 and
    // entry 2. A CLI that drops every entry newer than the oldest it has read loses "late", and one
    // that remembers only the page before prints entry 2 twice. The fake serves pages of three
    // whatever per_page asks for.
    [Test]
    public async Task Log_prints_an_entry_stored_late_among_entries_already_read_once_in_its_place()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var entries = Entries(5, now.AddMinutes(-5));
        var late = Entry("late", now.AddMinutes(-6.5));
        string[] written = [Entry("new 0", now), Entry("new 1", now.AddMinutes(-1)), Entry("new 2", now.AddMinutes(-2))];
        string[] after = [.. written, entries[0], entries[1], late, .. entries[2..]];
        StubPage(run, 0, 3, entries.Length, entries[..3]);
        StubPage(run, 1, 3, after.Length, after[3..6]);
        StubPage(run, 2, 3, after.Length, after[6..9]);

        var result = await run.RunAsync("log");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("entry 0,entry 1,late,entry 2,entry 3,entry 4"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // 200 is the most the API returns per page (portal PaginationDefaults.MaxPageSize). Example 69 is
    // the first case.
    [TestCase(1000, 200, 1100, "0,1,2,3,4")]
    [TestCase(20, 20, 50, "0")]
    public async Task Log_limit_prints_that_many_of_the_newest_entries_from_pages_of_the_limit_up_to_200(int limit, int perPage, int count, string pages)
    {
        using var run = CliRun.Start();
        run.StubPages(LogsPath, perPage, Entries(count, DateTime.UtcNow));

        var result = await run.RunAsync("log", "--limit", limit.ToString(CultureInfo.InvariantCulture));

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, limit)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo(pages));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.All.EqualTo(perPage.ToString(CultureInfo.InvariantCulture)));
        });
    }

    // The API returns the newest first (portal ActivityLogRepository.GetLogsAsync), so the first
    // older entry ends the read. The last 24 hours hold 250 entries, more than the default 100.
    // --since alone sets no limit, so pages are of 200 (spec/cli-surface.md "Details"). Example 33.
    [Test]
    public async Task Log_since_a_duration_prints_every_entry_since_then_and_stops_reading_at_the_first_older_entry()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var recent = Entries(250, now.AddMinutes(-1));
        var older = Enumerable.Range(0, 250).Select(i => Entry(Invariant($"older {i}"), now.AddHours(-25).AddMinutes(-i)));
        run.StubPages(LogsPath, 200, [.. recent, .. older]);

        var result = await run.RunAsync("log", "--since", "24h");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, 250)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1"));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.Not.Empty.And.All.EqualTo("200"));
        });
    }

    // spec/cli-surface.md "Details".
    [Test]
    public async Task Log_since_and_limit_stop_at_the_limit_when_it_comes_first()
    {
        using var run = CliRun.Start();
        run.StubPages(LogsPath, 3, Entries(10, DateTime.UtcNow));

        var result = await run.RunAsync("log", "--since", "24h", "--limit", "3");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, 3)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0"));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.All.EqualTo("3"));
        });
    }

    // Six entries of the last hour, then older ones, on the first page of 50.
    [Test]
    public async Task Log_since_and_limit_stop_at_the_first_entry_older_than_since_when_it_comes_first()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, 100).Select(i => Entry(Invariant($"entry {i}"), now.AddMinutes(-((i * 10) + 5)))).ToArray();
        run.StubPages(LogsPath, 50, entries);

        var result = await run.RunAsync("log", "--since", "1h", "--limit", "50");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo(Messages(0, 6)));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0"));
        });
    }

    // Entries are an hour apart, and --until falls between entry 3 and entry 4 (spec/cli-surface.md
    // "Details").
    [Test]
    public async Task Log_until_leaves_out_newer_entries_before_the_limit_counts()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, 10).Select(i => Entry(Invariant($"entry {i}"), now.AddHours(-i))).ToArray();
        run.StubPages(LogsPath, 2, entries);
        var until = now.AddHours(-3.5).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var result = await run.RunAsync("log", "--until", until, "--limit", "2");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("entry 4,entry 5"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // Example 70. Page 2 begins with an entry older than 7 days, so reading stops there.
    [Test]
    public async Task Log_since_with_user_prints_only_the_entries_of_that_user_since_then()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        string[] entries =
        [
            Entry("sam changed a policy", now.AddHours(-1), userName: Sam),
            Entry("alex changed a policy", now.AddHours(-2), userName: Alex),
            Entry("sam enrolled a system", now.AddDays(-3), userName: Sam),
            Entry("alex enrolled a system", now.AddDays(-6), userName: Alex),
            Entry("sam created a key", now.AddDays(-8), userName: Sam),
            Entry("sam deleted a key", now.AddDays(-9), userName: Sam),
            Entry("sam renamed a tag", now.AddDays(-10), userName: Sam),
        ];
        run.StubPages(LogsPath, 2, entries);

        var result = await run.RunAsync("log", "--since", "7d", "--user", Sam);

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("sam changed a policy,sam enrolled a system"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // An address that holds the one given, or is held in it, is another user's
    // (spec/cli-surface.md "Details").
    [Test]
    public async Task Log_user_matches_the_whole_user_name_ignoring_case()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        string[] entries =
        [
            Entry("same address", now.AddMinutes(-1), userName: Sam),
            Entry("longer address", now.AddMinutes(-2), userName: "sam@example.com.au"),
            Entry("other user", now.AddMinutes(-3), userName: "pam-sam@example.com"),
            Entry("same address in capitals", now.AddMinutes(-4), userName: "Sam@Example.COM"),
        ];
        run.StubPages(LogsPath, 100, entries);

        var result = await run.RunAsync("log", "--user", "SAM@example.com");

        var items = CliAssert.List(result, "log");
        Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("same address,same address in capitals"));
    }

    // Example 71. Page 2 begins with an entry older than an hour, so reading stops there.
    [Test]
    public async Task Log_since_with_levels_prints_only_the_entries_at_those_levels_since_then()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        string[] entries =
        [
            Entry("disk space low", now.AddMinutes(-5), "Warning"),
            Entry("policy changed", now.AddMinutes(-10), "Information"),
            Entry("enrolment failed", now.AddMinutes(-20), "Error"),
            Entry("system enrolled", now.AddMinutes(-30), "Information"),
            Entry("older warning", now.AddHours(-2), "Warning"),
            Entry("older error", now.AddHours(-3), "Error"),
            Entry("older information", now.AddHours(-4), "Information"),
        ];
        run.StubPages(LogsPath, 2, entries);

        var result = await run.RunAsync("log", "--since", "1h", "--level", "warning,error");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("disk space low,enrolment failed"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // Example 72. Page 2 begins with an entry older than 30 days, so reading stops there.
    [Test]
    public async Task Log_since_with_filter_prints_only_the_entries_whose_message_holds_the_text_ignoring_case()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        string[] entries =
        [
            Entry("System K7P2Q enrolled", now.AddDays(-1)),
            Entry("Policy web to db updated", now.AddDays(-2)),
            Entry("System K7P2Q disabled", now.AddDays(-10)),
            Entry("tag kiosk added to k7p2q", now.AddDays(-12)),
            Entry("System K7P2Q tagged", now.AddDays(-40)),
            Entry("System K7P2Q approved", now.AddDays(-41)),
            Entry("System K7P2Q enrolled before", now.AddDays(-50)),
        ];
        run.StubPages(LogsPath, 2, entries);

        var result = await run.RunAsync("log", "--since", "30d", "--filter", "K7P2Q");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("System K7P2Q enrolled,System K7P2Q disabled,tag kiosk added to k7p2q"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // Example 73. Read as UTC, the window would be five and a half hours later, where none of these
    // entries are.
    [Test]
    public async Task Log_since_and_until_times_without_a_zone_print_the_entries_between_them_in_the_local_time_zone()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(Now, TestData.LocalZone);
        string[] entries =
        [
            Entry("next day", Local(2026, 10, 6, 8, 0)),
            Entry("after the window", Local(2026, 10, 5, 13, 0)),
            Entry("during 11:30", Local(2026, 10, 5, 11, 30)),
            Entry("during 10:00", Local(2026, 10, 5, 10, 0)),
            Entry("during 09:30", Local(2026, 10, 5, 9, 30)),
            Entry("before the window", Local(2026, 10, 5, 8, 30)),
            Entry("day before", Local(2026, 10, 4, 8, 0)),
        ];
        run.StubPages(LogsPath, 2, entries);

        var result = await run.RunAsync("log", "--since", "2026-10-05T09:00", "--until", "2026-10-05T12:00");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("during 11:30,during 10:00,during 09:30"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // In the hour repeated when the clocks go back, 01:30 happens twice. The clock reads the first
    // 01:45 of that night in TestData.DaylightSavingZone (00:45 UTC), so the only 01:30 so far is
    // 00:30 UTC; read as standard time, 01:30 would be 01:30 UTC, after now.
    [Test]
    public async Task Log_since_a_clock_time_in_the_repeated_hour_starts_at_its_latest_occurrence_at_or_before_now()
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(new DateTimeOffset(2026, 10, 25, 0, 45, 0, TimeSpan.Zero), TestData.DaylightSavingZone);
        string[] entries =
        [
            Entry("10 minutes ago", new DateTime(2026, 10, 25, 0, 35, 0, DateTimeKind.Utc)),
            Entry("14 minutes ago", new DateTime(2026, 10, 25, 0, 31, 0, DateTimeKind.Utc)),
            Entry("16 minutes ago", new DateTime(2026, 10, 25, 0, 29, 0, DateTimeKind.Utc)),
            Entry("the day before", new DateTime(2026, 10, 24, 1, 0, 0, DateTimeKind.Utc)),
        ];
        run.StubPages(LogsPath, 200, entries);

        var result = await run.RunAsync("log", "--since", "01:30");

        var items = CliAssert.List(result, "log");
        Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("10 minutes ago,14 minutes ago"));
    }

    // The API writes UTC with Z (portal ActivityLogSink.ConvertEvent;
    // FractionalMillisecondTrimmingDateTimeConverter), so a time with no zone is UTC too.
    // System.Text.Json reads a time with another offset into the machine's local time
    // (dotnet/runtime release/10.0, JsonHelpers.Date.cs, TryParseAsISO). The date is in July so
    // that a machine in a European zone is not at UTC+0, where a CLI that took that local time as
    // UTC would pass too.
    [TestCase("2030-07-14T19:30:00Z", "2030-07-14T18:30:00Z")]
    [TestCase("2030-07-14T19:30:00", "2030-07-14T18:30:00")]
    [TestCase("2030-07-15T01:00:00+05:30", "2030-07-15T00:00:00+05:30")]
    [TestCase("2030-07-14T15:30:00-04:00", "2030-07-14T14:30:00-04:00")]
    public async Task Log_reads_each_entry_time_as_the_instant_the_api_wrote(string newer, string older)
    {
        using var run = CliRun.Start();
        run.Time = new FixedTimeProvider(new DateTimeOffset(2030, 7, 14, 20, 0, 0, TimeSpan.Zero), TestData.LocalZone);
        run.StubPages(LogsPath, 200, EntryAt("within the hour", newer), EntryAt("before the hour", older));

        var result = await run.RunAsync("log", "--since", "1h");

        var items = CliAssert.List(result, "log");
        Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("within the hour"));
    }

    // Pages are of 200 (spec/cli-surface.md "Details"). The third error is on page 2, so page 3 is
    // never read.
    [Test]
    public async Task Log_with_a_filter_and_a_limit_reads_back_until_it_has_that_many_matching_entries()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        int[] errors = [1, 250, 450, 610];
        var entries = Enumerable.Range(0, 700).Select(i => Entry(Invariant($"entry {i}"), now.AddMinutes(-i), errors.Contains(i) ? "Error" : "Information")).ToArray();
        run.StubPages(LogsPath, 200, entries);

        var result = await run.RunAsync("log", "--level", "error", "--limit", "3");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("entry 1,entry 250,entry 450"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.All.EqualTo("200"));
        });
    }

    // Two of 250 entries match, fewer than the default limit of 100; pages are of 200
    // (spec/cli-surface.md "Details").
    [Test]
    public async Task Log_with_a_filter_reads_to_the_start_of_the_log_when_fewer_entries_match_than_the_limit()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, 250).Select(i => Entry(Invariant($"entry {i}"), now.AddMinutes(-i), userName: i is 2 or 207 ? Sam : Alex)).ToArray();
        run.StubPages(LogsPath, 200, entries);

        var result = await run.RunAsync("log", "--user", Sam);

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("entry 2,entry 207"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1"));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.All.EqualTo("200"));
        });
    }

    // Nothing bounds how many entries the CLI reads before enough match, so it asks for the most a
    // page holds (spec/cli-surface.md "Details"; portal PaginationDefaults.MaxPageSize). None of
    // these 450 entries is kept: three calls in pages of 200, where pages of 1 would take 450. Every
    // entry is from the last 450 minutes, so --until 1d leaves out all of them.
    [TestCase("--level", "error")]
    [TestCase("--user", Sam)]
    [TestCase("--filter", "K7P2Q")]
    [TestCase("--until", "1d")]
    public async Task Log_with_an_option_that_leaves_entries_out_asks_for_pages_of_200_whatever_the_limit(string option, string value)
    {
        using var run = CliRun.Start();
        run.StubPages(LogsPath, 200, Entries(450, DateTime.UtcNow));

        var result = await run.RunAsync("log", option, value, "--limit", "1");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(items.GetArrayLength(), Is.Zero);
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.All.EqualTo("200"));
        });
    }

    // spec/cli-surface.md "Command options".
    [TestCase("--level verbose")]
    [TestCase("--level warning,debug")]
    [TestCase("--since yesterday")]
    [TestCase("--until later")]
    [TestCase("--limit many")]
    public async Task Log_with_a_value_its_option_does_not_take_exits_2_without_a_request(string flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        using var run = CliRun.Start();
        run.Stub("GET", LogsPath, json: ApiJson.Page());

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["log", .. flags.Split(' ')],
            ["log"],
            "GET",
            LogsPath);
    }

    private static string[] Entries(int count, DateTime newest) =>
        Enumerable.Range(0, count).Select(i => Entry(Invariant($"entry {i}"), newest.AddMinutes(-i))).ToArray();

    // Joined as JsonRead.StringFieldList joins them.
    private static string Messages(int first, int count) =>
        string.Join(",", Enumerable.Range(first, count).Select(i => Invariant($"entry {i}")));

    // ApiJson.Log fixes the time, level and user, which these tests filter on. TimeStamp is a UTC
    // DateTime in LogEntryModel, which ApiJson writes ending in Z.
    private static string Entry(string message, DateTime timeStamp, string level = "Information", string userName = "admin@acme.example") =>
        ApiJson.Altered(
            ApiJson.Log(message),
            ("timeStamp", timeStamp.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            ("level", level),
            ("userName", userName));

    private static string EntryAt(string message, string timeStamp) =>
        ApiJson.Altered(ApiJson.Log(message), ("timeStamp", timeStamp));

    // Each page is served as the API answers it at that read, with the log's length then, so a test
    // can show the log changing between reads.
    private static void StubPage(CliRun run, int page, int perPage, int total, string[] items) =>
        run.StubWithQuery("GET", LogsPath, "page", page.ToString(CultureInfo.InvariantCulture), json: ApiJson.PageAt(page, perPage, total, items));

    // The UTC time the CLI reads a time without a zone as, with CliHost.Time in TestData.LocalZone.
    private static DateTime Local(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), TestData.LocalZone);
}
