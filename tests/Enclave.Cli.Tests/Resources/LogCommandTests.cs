using System.Globalization;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Resources;

// log prints the organisation's activity log, newest first (proposed-cli-surface.md "Command
// options"). The logs API takes only a page and a page size (portal
// Enclave.Api/Modules/ActivityLogs/Logs/Models/LogsRequestModel.cs), so the CLI applies every filter
// to the entries it reads, and reads only the pages it needs ("Calls per command"). Where a test
// checks the page size, the fake API serves pages of the size the CLI asks for, as the API does.
[Category(TestCategory.Pending)]
public class LogCommandTests
{
    private const string Sam = "sam@example.com";

    private const string Alex = "alex@example.com";

    private static readonly string LogsPath = TestData.OrgPath("logs");

    // log prints the newest 100 entries, newest first, and asks for pages of the smaller of the limit
    // and 200 (proposed-cli-surface.md "Details"). The newest 100 fill the first page of 100, so the
    // second is never read. Example 68.
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

    // A log shorter than the limit is printed whole, read until the API reports no next page.
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

    // --limit changes the number, and the page size is the smaller of the limit and 200, the most the
    // API returns per page (proposed-cli-surface.md "Details"; portal PaginationDefaults.cs:11). The
    // 1,000 newest fill pages 0 to 4 of 200, so page 5 is never read; 20 fill page 0 of 20. Example
    // 69 is the first case.
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

    // --since prints every entry back to then, with no limit, and the CLI stops reading at the first
    // entry older than that, since the API returns the newest first (portal
    // ActivityLogRepository.cs:43). The last 24 hours hold 250 entries, more than the default 100;
    // page 1 holds the first older entry, and page 2 is never read. Example 33.
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
        });
    }

    // With --since and --limit, reading stops at whichever comes first (proposed-cli-surface.md
    // "Details"). Here the limit does: three entries fill the first page of three.
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

    // As above, where --since comes first: six entries of the last hour, then older ones, on the first
    // page of 50, so the second page is never read.
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

    // --until leaves out the entries newer than it before --limit counts (proposed-cli-surface.md
    // "Details"), so the two entries printed are the first two older than --until. Entries are an
    // hour apart and --until falls between the fourth and the fifth.
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

    // --user keeps the entries whose userName is the address given. Page 2 begins with an entry older
    // than 7 days, so reading stops there and page 3 is never read. Example 70.
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

    // --user matches the whole userName, ignoring case (proposed-cli-surface.md "Details"): an
    // address that holds the one given, or is held in it, is another user's.
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

    // --level takes one or more levels, comma separated, and keeps the entries at those levels.
    // Page 2 begins with an entry older than an hour, so page 3 is never read. Example 71.
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

    // --filter matches part of the message, ignoring case (proposed-cli-surface.md "Details"), so the
    // message that names the system in lower case matches too. Page 2 begins with an entry older than
    // 30 days, so page 3 is never read. Example 72.
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

    // Times without a zone are read in the machine's time zone (proposed-cli-surface.md "Command
    // options"). The CLI runs in the test's process, so TimeZoneInfo.Local is the zone it reads, and
    // the expected entries hold on a machine in any zone. --until leaves out the newer entries, and
    // page 2 holds the first entry older than --since, so page 3 is never read. Example 73.
    [Test]
    public async Task Log_since_and_until_times_without_a_zone_print_the_entries_between_them_in_the_local_time_zone()
    {
        using var run = CliRun.Start();
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

    // With a filter and no --since, --limit counts the matching entries, and the CLI reads back until
    // it has that many (proposed-cli-surface.md "Command options"), in pages of the limit ("Details").
    // The third error is on page 3, so page 4 is never read.
    [Test]
    public async Task Log_with_a_filter_and_a_limit_reads_back_until_it_has_that_many_matching_entries()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        int[] errors = [1, 6, 9, 13, 17];
        var entries = Enumerable.Range(0, 20).Select(i => Entry(Invariant($"entry {i}"), now.AddMinutes(-i), errors.Contains(i) ? "Error" : "Information")).ToArray();
        run.StubPages(LogsPath, 3, entries);

        var result = await run.RunAsync("log", "--level", "error", "--limit", "3");

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("entry 1,entry 6,entry 9"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2,3"));
            Assert.That(run.RequestsTo("GET", LogsPath).Select(request => request.QueryValue("per_page")), Is.All.EqualTo("3"));
        });
    }

    // When fewer entries match than the limit, the CLI reads back to the start of the log and prints
    // the matches it found (proposed-cli-surface.md "Command options"): here two of 250, with the
    // default limit of 100, on pages of 100.
    [Test]
    public async Task Log_with_a_filter_reads_to_the_start_of_the_log_when_fewer_entries_match_than_the_limit()
    {
        using var run = CliRun.Start();
        var now = DateTime.UtcNow;
        var entries = Enumerable.Range(0, 250).Select(i => Entry(Invariant($"entry {i}"), now.AddMinutes(-i), userName: i is 2 or 207 ? Sam : Alex)).ToArray();
        run.StubPages(LogsPath, 100, entries);

        var result = await run.RunAsync("log", "--user", Sam);

        var items = CliAssert.List(result, "log");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.StringFieldList(items, "message"), Is.EqualTo("entry 2,entry 207"));
            Assert.That(run.PagesRequested(LogsPath), Is.EqualTo("0,1,2"));
        });
    }

    // --level takes information, warning and error; --since and --until take a duration or a time;
    // --limit takes a number (proposed-cli-surface.md "Command options").
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

    // count entries a minute apart, newest first; "entry 0" is the newest.
    private static string[] Entries(int count, DateTime newest) =>
        Enumerable.Range(0, count).Select(i => Entry(Invariant($"entry {i}"), newest.AddMinutes(-i))).ToArray();

    // The messages of entries first to first + count - 1, as JsonRead.StringFieldList joins them.
    private static string Messages(int first, int count) =>
        string.Join(",", Enumerable.Range(first, count).Select(i => Invariant($"entry {i}")));

    // ApiJson.Log fixes the time, level and user, which these tests filter on. TimeStamp is a UTC
    // DateTime in LogEntryModel, written as ApiJson writes those, ending in Z.
    private static string Entry(string message, DateTime timeStamp, string level = "Information", string userName = "admin@acme.example") =>
        ApiJson.Altered(
            ApiJson.Log(message),
            ("timeStamp", timeStamp.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            ("level", level),
            ("userName", userName));

    // The UTC time of a wall-clock time in the machine's time zone, as the CLI reads a time given
    // without a zone.
    private static DateTime Local(int year, int month, int day, int hour, int minute) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified), TimeZoneInfo.Local);
}
