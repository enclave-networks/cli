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

            var org = await context.GetOrganisationAsync();
            var entries = await ReadAsync(org.Client.Logs, from, to, count, matches, context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Log, entries, context.CancellationToken);
        });

        return verb;
    }

    // The logs API takes only a page and a page size (portal LogsRequestModel.cs) and returns the
    // newest entries first (portal ActivityLogRepository.cs:43), so the CLI reads page after page
    // and stops once it has what it prints: the limit, the first entry older than --since, or the
    // start of the log. --until and the filters leave entries out before the limit counts
    // ("Details").
    private static async Task<List<LogEntryModel>> ReadAsync(
        ILogsClient logs,
        DateTimeOffset? since,
        DateTimeOffset? until,
        int? limit,
        Func<LogEntryModel, bool> matches,
        CancellationToken cancellationToken)
    {
        // Pages of 200, the most the API returns per page (portal PaginationDefaults.cs:11), or of
        // the limit when that is smaller, so the newest 100 take one page of 100 ("Details").
        var perPage = Math.Min(limit ?? Paging.PageSize, Paging.PageSize);
        var entries = new List<LogEntryModel>();
        int? page = 0;

        while (page is { } current)
        {
            var response = await logs.GetLogsAsync(current, perPage);

            await foreach (var entry in response.Items.WithCancellation(cancellationToken))
            {
                var time = AsUtc(entry.TimeStamp);

                if (since is { } from && time < from)
                {
                    return entries;
                }

                if ((until is { } to && time > to) || !matches(entry))
                {
                    continue;
                }

                entries.Add(entry);

                if (entries.Count == limit)
                {
                    return entries;
                }
            }

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
    // as UTC too.
    private static DateTimeOffset AsUtc(DateTime time) =>
        new(time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
