using Enclave.ActivityLog.Data;
using Enclave.Api.Modules.ActivityLogs.Logs.Models;
using Enclave.Cli.Core;
using Enclave.Sdk.Api.Clients.Interfaces;

namespace Enclave.Cli.Commands.Log;

// log is a noun with no verbs (spec/cli-surface.md "Commands", "Command options").
internal static class LogCommand
{
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
        var user = verb.Add(CliOptions.Filter("--user", "Entries by this user, matched whole and ignoring case.", "email"));
        var level = verb.Add(CliOptions.ChoiceList("--level", "Entries at these levels.", ("information", ActivityLogLevel.Information), ("warning", ActivityLogLevel.Warning), ("error", ActivityLogLevel.Error)));
        var filter = verb.Add(CliOptions.Filter("--filter", "Entries whose message contains this text, ignoring case."));

        verb.SetHandler(async context =>
        {
            // --until, like --since, names a time in the log's past: --until 1h leaves out the last
            // hour.
            var from = context.Get(since) is { } sinceTime ? context.PastInstant(sinceTime, since) : (DateTimeOffset?)null;
            var to = context.Get(until) is { } untilTime ? context.PastInstant(untilTime, until) : (DateTimeOffset?)null;

            // --since has no limit unless --limit is given ("Command options").
            var count = context.Get(limit) ?? (from is null ? DefaultLimit : null);
            var matches = Matcher(context.Get(user), context.Get(level), context.Get(filter));
            var leavesOut = to is not null || context.IsGiven(user) || context.IsGiven(level) || context.IsGiven(filter);

            var org = await context.GetOrganisationAsync();
            var entries = await ReadAsync(org.Client.Logs, from, to, count, PageSize(count, leavesOut), matches, context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Log, entries, context.CancellationToken);
        });

        return verb;
    }

    // A limit under 200, the API's largest page (portal PaginationDefaults.MaxPageSize), is the page
    // size, so the newest 100 take one call ("Details"). --until and the filters leave out an
    // unbounded number of entries, and pages of a small limit could then take one call per entry
    // read, so with any of them the pages are 200.
    private static int PageSize(int? limit, bool leavesOut) =>
        limit is { } count && !leavesOut ? Math.Min(count, Paging.PageSize) : Paging.PageSize;

    // The logs API takes only a page and a page size (portal LogsRequestModel) and returns the
    // newest entries first (ActivityLogRepository.GetLogsAsync), so the CLI reads pages until it
    // has the limit, an entry older than --since, or the start of the log. --until and the filters
    // leave entries out before the limit counts ("Details").
    //
    // Pages are offsets, so entries written during a read push older ones onto later pages, and the
    // API stores entries in batches every 2 seconds with their event's time
    // (ActivityLogSink.WritePeriod and ConvertEvent), so one stored late can land among those
    // already read. An entry read on any earlier page is dropped, and the rest sorted newest first.
    // The model has no ID (LogEntryModel), so an entry is keyed on all its fields: two alike to the
    // millisecond on different pages print once, which is acceptable where printing entries twice
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
        var read = new HashSet<EntryKey>();
        int? page = 0;

        while (page is { } current)
        {
            var response = await logs.GetLogsAsync(current, perPage);
            var onThisPage = new List<EntryKey>();

            await foreach (var entry in response.Items.WithCancellation(cancellationToken))
            {
                var key = EntryKey.Of(entry);

                if (read.Contains(key))
                {
                    continue;
                }

                onThisPage.Add(key);

                if (since is { } from && key.Time < from)
                {
                    return NewestFirst(entries);
                }

                if ((until is { } to && key.Time > to) || !matches(entry))
                {
                    continue;
                }

                entries.Add(entry);

                if (entries.Count == limit)
                {
                    return NewestFirst(entries);
                }
            }

            read.UnionWith(onThisPage);
            page = response.Metadata.NextPage;

            // A next page that does not move forwards would read the same pages forever.
            if (page <= current)
            {
                throw new CliException(ErrorCode.ApiError, $"The Enclave API gave page {current} a next page of {page}, which does not move forwards.");
            }
        }

        return NewestFirst(entries);
    }

    // A stable sort, so entries with one time keep the order the API gave them.
    private static List<LogEntryModel> NewestFirst(List<LogEntryModel> entries) =>
        [.. entries.OrderByDescending(entry => CliJson.ToUtc(entry.TimeStamp))];

    // The logs API has no filters, so these apply to the entries read ("Command options").
    private static Func<LogEntryModel, bool> Matcher(string? user, IReadOnlyList<ActivityLogLevel>? levels, string? text) =>
        entry =>
            (user is null || string.Equals(entry.UserName, user, StringComparison.OrdinalIgnoreCase))
            && (levels is null || levels.Contains(entry.Level))
            && (text is null || (entry.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false));

    private readonly record struct EntryKey(DateTimeOffset Time, ActivityLogLevel Level, string? Message, string? UserName, string? IpAddress)
    {
        public static EntryKey Of(LogEntryModel entry) =>
            new(CliJson.ToUtc(entry.TimeStamp), entry.Level, entry.Message, entry.UserName, entry.IpAddress);
    }
}
