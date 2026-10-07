using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.UnapprovedSystems.Models;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.Systems.Enums;

namespace Enclave.Cli.Commands.Systems;

/// <summary>
/// `system list`: the enrolled systems, or with --pending the systems waiting for approval
/// (proposed-cli-surface.md "Command options", "Filters").
/// </summary>
internal static class SystemListCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("list", "List the enrolled systems, or with --pending the systems waiting for approval, reading every page.", CommandScope.Organisation);

        var filter = verb.Add(CliOptions.Text("--filter", "Search text, sent as typed; takes the API's search syntax as well as plain words."));
        var tags = verb.Add(CliOptions.TagList("--tag", "Systems with every tag given."));

        // The values each flag stands for are the API's search values ("Filters"): os matches the
        // platform name the API stores exactly, so mac is Mac.
        var state = verb.Add(CliOptions.ChoiceText("--state", "Connected or disconnected systems.", ("connected", "connected"), ("disconnected", "disconnected")));
        var os = verb.Add(CliOptions.ChoiceText("--os", "Systems on this operating system.", ("windows", "Windows"), ("linux", "Linux"), ("mac", "Mac")));
        var type = verb.Add(CliOptions.ChoiceText("--type", "General-purpose or ephemeral systems.", ("general", "general"), ("ephemeral", "ephemeral")));
        var gateway = verb.Add(CliOptions.Flag("--gateway", "Systems that act as gateways."));
        var key = verb.Add(CliOptions.Text("--key", "Systems enrolled with the key of this description.", "name"));
        var keyId = verb.Add(CliOptions.Id("--key-id", "Systems enrolled with the key of this ID.", IdFormats.Int32));
        var dnsName = verb.Add(CliOptions.Text("--dns-name", "Systems that answer to this DNS name.", "name"));
        var notSeenFor = verb.Add(CliOptions.Duration("--not-seen-for", "Systems not seen for at least this long; a system never seen counts from when it enrolled."));
        var includeDisabled = verb.Add(CliOptions.Flag("--include-disabled", "Include disabled systems."));
        var sort = verb.Add(CliOptions.Enum(
            "--sort",
            "The order to list systems in. With --pending: recently-enrolled, description or enrolment-key-used.",
            SystemQuerySortMode.RecentlyEnrolled,
            SystemQuerySortMode.RecentlyConnected,
            SystemQuerySortMode.Description,
            SystemQuerySortMode.DescriptionOrHostname,
            SystemQuerySortMode.EnrolmentKeyUsed));
        var pending = verb.Add(CliOptions.Flag("--pending", "List the systems waiting for approval."));
        var waitingFor = verb.Add(CliOptions.Duration("--waiting-for", "With --pending: systems that have waited at least this long."));

        verb.Exclusive(key, keyId);
        verb.Requires(waitingFor, pending);

        // The API keeps waiting systems apart, and their list takes no state, platform, type,
        // gateway, DNS or disabled filter (UnapprovedSystemsClient.GetSystemsAsync, Enclave.Sdk.Api
        // 1.1.0), so those options exit 2 with --pending ("Details").
        Option[] enrolledOnly = [state, os, type, gateway, dnsName, notSeenFor, includeDisabled];
        verb.Check(context =>
        {
            if (!context.Get(pending))
            {
                return;
            }

            if (enrolledOnly.FirstOrDefault(context.IsGiven) is { } option)
            {
                throw CliErrors.InvalidArgument(option.Name, $"{option.Name} applies to enrolled systems only, so it cannot be given with --pending.");
            }

            if (context.Get(sort) is { } mode && PendingSort(mode) is null)
            {
                throw CliErrors.InvalidArgument(sort.Name, "With --pending, --sort takes one of: recently-enrolled, description, enrolment-key-used.");
            }
        });

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var enrolmentKey = context.Get(keyId) ?? await KeyIdAsync(context, org, context.Get(key));

            var search = new SearchText(context.Get(filter))
                .Add("tags", context.Get(tags))
                .Add("state", context.Get(state))
                .Add("os", context.Get(os))
                .Add("type", context.Get(type))
                .AddFlag("gateway", context.Get(gateway))
                .ToSearchTerm();

            var now = context.Host.Time.GetUtcNow();

            if (context.Get(pending))
            {
                var pendingSort = context.Get(sort) is { } mode ? PendingSort(mode) : null;
                var waiting = await Paging.ReadAllAsync(
                    (page, perPage) => org.Client.UnapprovedSystems.GetSystemsAsync(enrolmentKey, search, pendingSort, page, perPage),
                    context.CancellationToken);

                // --waiting-for has no API search key, so the CLI keeps the matches from every page
                // it read, and total counts them ("Filters").
                if (context.Get(waitingFor) is { } wait)
                {
                    waiting = waiting.Where(system => AsUtc(system.EnrolledAt) <= now - wait).ToList();
                }

                await context.Output.WriteListAsync(ListKind.PendingSystem, waiting, context.CancellationToken);
                return;
            }

            var systems = await Paging.ReadAllAsync(
                (page, perPage) => org.Client.EnrolledSystems.GetSystemsAsync(
                    enrolmentKey,
                    search,
                    context.Get(includeDisabled) ? true : null,
                    context.Get(sort),
                    context.Get(dnsName),
                    page,
                    perPage),
                context.CancellationToken);

            // --not-seen-for has no API search key either. A system never seen counts from when it
            // enrolled, so one that has just enrolled is not listed for removal ("Filters").
            if (context.Get(notSeenFor) is { } unseen)
            {
                systems = systems.Where(system => (system.LastSeen ?? system.EnrolledAt) <= now - unseen).ToList();
            }

            await context.Output.WriteListAsync(ListKind.System, systems, context.CancellationToken);
        });

        return verb;
    }

    // --key names the key by description, looked up with one list call that includes disabled keys
    // ("Names and IDs", "Filters"), and its ID goes in the enrolment_key parameter as --key-id's does.
    private static async Task<int?> KeyIdAsync(CliContext context, OrganisationInUse org, string? description)
    {
        if (description is null)
        {
            return null;
        }

        var keys = await NameLookup.FindAsync(
            [description],
            (page, perPage) => org.Client.EnrolmentKeys.GetEnrolmentKeysAsync(includeDisabled: true, pageNumber: page, perPage: perPage),
            enrolmentKey => enrolmentKey.Description,
            enrolmentKey => enrolmentKey.Id,
            "--key",
            "key",
            context.CancellationToken);

        return keys[0].Id.ToInt();
    }

    // UnapprovedSystemQuerySortMode's members are a subset of SystemQuerySortMode's (portal
    // UnapprovedSystems/Models/UnapprovedSystemQuerySortMode.cs).
    private static UnapprovedSystemQuerySortMode? PendingSort(SystemQuerySortMode mode) => mode switch
    {
        SystemQuerySortMode.RecentlyEnrolled => UnapprovedSystemQuerySortMode.RecentlyEnrolled,
        SystemQuerySortMode.Description => UnapprovedSystemQuerySortMode.Description,
        SystemQuerySortMode.EnrolmentKeyUsed => UnapprovedSystemQuerySortMode.EnrolmentKeyUsed,
        _ => null,
    };

    // UnapprovedSystemSummaryModel.EnrolledAt is a DateTime, which the API writes in UTC; one read
    // without a zone is taken as UTC too.
    private static DateTimeOffset AsUtc(DateTime time) =>
        new(time.Kind == DateTimeKind.Local ? time.ToUniversalTime() : DateTime.SpecifyKind(time, DateTimeKind.Utc));
}
