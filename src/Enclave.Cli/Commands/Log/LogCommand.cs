using Enclave.ActivityLog.Data;
using Enclave.Api.Modules.ActivityLogs.Logs.Models;
using Enclave.Cli.Core;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Commands.Log;

/// <summary>
/// `log`: the organisation's activity log, a noun with no verbs (proposed-cli-surface.md
/// "Commands", "Command options").
/// </summary>
internal static class LogCommand
{
    // The entries log prints when neither --limit nor --since is given ("Command options").
    private const int DefaultLimit = 100;

    public static CliVerb Create()
    {
        var verb = new CliVerb(
            "log",
            "Print the organisation's activity log, newest first: the newest 100 entries, --limit entries, or every entry back to --since.",
            CommandScope.Organisation);

        // log is a noun with a plural, and runs as a command itself.
        verb.Aliases.Add("logs");

        var limit = verb.Add(CliOptions.Number("--limit", "How many entries to print; 100 when neither --limit nor --since is given.", minimum: 1));
        var since = verb.Add(CliOptions.Time("--since", "Print every entry back to this time: a duration (24h) or a time.", allowDuration: true));
        var until = verb.Add(CliOptions.Time("--until", "Leave out entries newer than this time: a duration (1h) or a time.", allowDuration: true));
        var user = verb.Add(CliOptions.Text("--user", "Entries by this user, matched whole and ignoring case.", "email"));
        var level = verb.Add(CliOptions.ChoiceList("--level", "Entries at these levels.", ("information", ActivityLogLevel.Information), ("warning", ActivityLogLevel.Warning), ("error", ActivityLogLevel.Error)));
        var filter = verb.Add(CliOptions.Text("--filter", "Entries whose message contains this text, ignoring case."));

        verb.SetHandler(async context =>
        {
            // --until, like --since, names a time in the log's past, so a duration or a clock time
            // counts back from now: --until 1h leaves out the last hour.
            var from = context.Get(since) is { } sinceTime ? context.PastInstant(sinceTime, since) : (DateTimeOffset?)null;
            var to = context.Get(until) is { } untilTime ? context.PastInstant(untilTime, until) : (DateTimeOffset?)null;

            // --since prints every entry back to then, with no limit unless --limit is given
            // ("Command options", "Details").
            var count = context.Get(limit) ?? (from is null ? DefaultLimit : null);
            var matches = Matcher(context.Get(user), context.Get(level), context.Get(filter));
            var leavesOut = to is not null || context.IsGiven(user) || context.IsGiven(level) || context.IsGiven(filter);

            var org = await context.GetOrganisationAsync();
            var entries = await ReadAsync(org.Client.Logs, from, to, count, PageSize(count, leavesOut), matches, context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Log, entries, context.CancellationToken);
        });

        return verb;
    }

    // Pages of 200, the most the API returns per page (portal PaginationDefaults.cs:11), or of the
    // limit when that is smaller, so the newest 100 take one page of 100 ("Details"). --until,
    // --user, --level and --filter leave entries out, so nothing bounds how many entries are read
    // before the limit is met, and pages of a small limit could take one call per entry read: with
    // any of them the pages are 200.
    private static int PageSize(int? limit, bool leavesOut) =>
        limit is { } count && !leavesOut ? Math.Min(count, Paging.PageSize) : Paging.PageSize;

    // The logs API takes only a page and a page size (portal LogsRequestModel.cs) and returns the
    // newest entries first (portal ActivityLogRepository.cs:43), so the CLI reads page after page
    // and stops once it has what it prints: the limit, the first entry older than --since, or the
    // start of the log. --until and the filters leave entries out before the limit counts
    // ("Details").
    //
    // A page is the entries after skipping those on the pages before it, so an entry written while
    // the CLI reads moves the older entries down and the next page begins with entries already read.
    // Every entry the next page holds that is newer than the oldest entry read is one of those, and
    // so is an entry with the same time that is already read; both are dropped. The log entry model
    // has no ID (portal LogEntryModel.cs), so an entry is known by all its fields: two entries alike
    // in every field and in time, to the millisecond the API keeps, that a page boundary falls
    // between print once. That takes the same message for the same user from the same address twice
    // in one millisecond, and losing the second copy is acceptable where printing entries twice
    // whenever the log is written to during a read is not.
    private static async Task<List<LogEntryModel>> ReadAsync(
        ILogsClient logs,
        DateTimeOffset? since,
        DateTimeOffset? until,
        int? limit,
        int perPage,
        Func<LogEntryModel, bool> matches,
        CancellationToken cancellationToken)
    {
        var entries = new List<LogEntryModel>();
        var read = new ReadSoFar();
        int? page = 0;

        while (page is { } current)
        {
            var response = await logs.GetLogsAsync(current, perPage);
            var onThisPage = new List<EntryKey>();

            await foreach (var entry in response.Items.WithCancellation(cancellationToken))
            {
                var key = EntryKey.Of(entry);

                if (read.Holds(key))
                {
                    continue;
                }

                onThisPage.Add(key);

                if (since is { } from && key.Time < from)
                {
                    return entries;
                }

                if ((until is { } to && key.Time > to) || !matches(entry))
                {
                    continue;
                }

                entries.Add(entry);

                if (entries.Count == limit)
                {
                    return entries;
                }
            }

            read.Add(onThisPage);
            page = response.Metadata.NextPage;

            // A next page that does not move forwards would read the same pages forever.
            if (page <= current)
            {
                throw new CliException(ErrorCode.ApiError, $"The Enclave API gave page {current} a next page of {page}, which does not move forwards.");
            }
        }

        return entries;
    }

    // The logs API has no filters, so --user, --level and --filter apply to the entries read
    // ("Command options"): --user matches the whole userName and --filter part of the message, each
    // ignoring case ("Details").
    private static Func<LogEntryModel, bool> Matcher(string? user, IReadOnlyList<ActivityLogLevel>? levels, string? text) =>
        entry =>
            (user is null || string.Equals(entry.UserName, user, StringComparison.OrdinalIgnoreCase))
            && (levels is null || levels.Contains(entry.Level))
            && (text is null || (entry.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));

    // LogEntryModel.TimeStamp is a DateTime the API writes in UTC; one read without a zone is taken
    // as UTC too. The API writes it with Z (portal Enclave.ActivityLog/ActivityLogSink.cs:100 stores
    // the UTC time, and Enclave.Api.Scaffolding/FractionalMillisecondTrimmingDateTimeConverter.cs
    // writes a DateTime by its kind), which System.Text.Json reads as DateTimeKind.Utc. A time
    // written with another offset System.Text.Json reads as local time in the machine's zone
    // (dotnet/runtime release/10.0, System.Text.Json JsonHelpers.Date.cs, TryParseAsISO, returns
    // DateTimeOffset.LocalDateTime). ToUniversalTime converts back with that same zone and gives the
    // instant written on any machine, which is why it reads the machine's zone outside CliHost:
    // converting with CliHost.Time's zone, which tests fix, would move the instant by the difference
    // between the two zones.
    private static DateTimeOffset AsUtc(DateTime time) =>
        new(time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc));

    // An entry as the CLI tells entries apart: its time and every field the API gives it.
    private readonly record struct EntryKey(DateTimeOffset Time, ActivityLogLevel Level, string? Message, string? UserName, string? IpAddress)
    {
        public static EntryKey Of(LogEntryModel entry) =>
            new(AsUtc(entry.TimeStamp), entry.Level, entry.Message, entry.UserName, entry.IpAddress);
    }

    // The oldest time read on the pages before this one, and the entries read with that time.
    private sealed class ReadSoFar
    {
        private readonly HashSet<EntryKey> _atOldest = [];

        private DateTimeOffset? _oldest;

        // Whether an entry on a later page is one read already: newer than the oldest entry read, or
        // with that time and read.
        public bool Holds(EntryKey key) =>
            _oldest is { } oldest && (key.Time > oldest || (key.Time == oldest && _atOldest.Contains(key)));

        public void Add(IReadOnlyList<EntryKey> page)
        {
            if (page.Count == 0)
            {
                return;
            }

            var oldest = page.Min(key => key.Time);

            if (oldest != _oldest)
            {
                _atOldest.Clear();
                _oldest = oldest;
            }

            _atOldest.UnionWith(page.Where(key => key.Time == oldest));
        }
    }
}
