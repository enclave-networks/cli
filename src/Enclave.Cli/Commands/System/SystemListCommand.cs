using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Api.Modules.SystemManagement.UnapprovedSystems.Models;
using Enclave.Cli.Commands.Key;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Modules.Systems.Enums;

namespace Enclave.Cli.Commands.Systems;

internal static class SystemListCommand
{
    public static CliVerb Create()
    {
        var verb = new CliVerb("list", "List the enrolled systems, or with --pending the systems waiting for approval, reading every page.", CommandScope.Organisation);

        var filter = verb.Add(CliOptions.Filter("--filter", "Search text, sent as typed; takes the API's search syntax as well as plain words."));
        var tags = verb.Add(CliOptions.TagFilter("--tag", "Systems with every tag given."));

        // The API matches os exactly and case-sensitively, and rewrites Mac to Darwin, the name it
        // stores for macOS (portal SystemSearchKeyService, the "os" key and BuildFilterAsync).
        var state = verb.Add(CliOptions.ChoiceText("--state", "Connected or disconnected systems.", ("connected", "connected"), ("disconnected", "disconnected")));
        var os = verb.Add(CliOptions.ChoiceText("--os", "Systems on this operating system.", ("windows", "Windows"), ("linux", "Linux"), ("mac", "Mac")));
        var type = verb.Add(CliOptions.ChoiceText("--type", "General-purpose or ephemeral systems.", ("general", "general"), ("ephemeral", "ephemeral")));
        var gateway = verb.Add(CliOptions.Flag("--gateway", "Systems that act as gateways."));
        var key = verb.Add(CliOptions.Filter("--key", "Systems enrolled with the key of this description.", "name"));
        var keyId = verb.Add(CliOptions.Id("--key-id", "Systems enrolled with the key of this ID.", IdFormats.Int32));
        var notSeenFor = verb.Add(CliOptions.Duration("--not-seen-for", "Systems not connected and not seen for at least this long; a system never seen counts from when it enrolled."));
        var includeDisabled = verb.Add(CliOptions.Flag("--include-disabled", "Include disabled systems."));
        var sort = verb.Add(CliOptions.Enum(
            "--sort",
            "The order to list systems in. With --pending: recently-enrolled, description, enrolment-key-used or description-or-hostname.",
            SystemQuerySortMode.RecentlyEnrolled,
            SystemQuerySortMode.RecentlyConnected,
            SystemQuerySortMode.Description,
            SystemQuerySortMode.DescriptionOrHostname,
            SystemQuerySortMode.EnrolmentKeyUsed,
            SystemQuerySortMode.LastConnected,
            SystemQuerySortMode.State,
            SystemQuerySortMode.Version));
        var pending = verb.Add(CliOptions.Flag("--pending", "List the systems waiting for approval."));
        var waitingFor = verb.Add(CliOptions.Duration("--waiting-for", "With --pending: systems that have waited at least this long."));

        verb.Exclusive(key, keyId);
        verb.Requires(waitingFor, pending);

        // The spec gives these flags to enrolled systems only ("Command options", "Details").
        // --filter takes the same keys with --pending: the API parses both lists' searches the same
        // way (portal PaginatedUnapprovedSystemsHandler, SystemRepository.BuildFilterAsync).
        Option[] enrolledOnly = [state, os, type, gateway, notSeenFor, includeDisabled];
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
                throw CliErrors.InvalidArgument(sort.Name, "With --pending, --sort takes one of: recently-enrolled, description, enrolment-key-used, description-or-hostname.");
            }
        });

        verb.Check(context =>
        {
            if (context.IsGiven(notSeenFor) && context.Get(state) == "connected")
            {
                throw CliErrors.InvalidArgument(state.Name, "--not-seen-for lists disconnected systems only, so --state connected cannot be given with it.");
            }
        });

        verb.SetHandler(async context =>
        {
            // A duration that reaches back too far exits 2, so the cut-offs are worked out before
            // the token is read ("Errors and exit codes").
            var unseenSince = context.Get(notSeenFor) is { } unseen ? context.PastInstant(unseen, notSeenFor) : (DateTimeOffset?)null;
            var waitingSince = context.Get(waitingFor) is { } wait ? context.PastInstant(wait, waitingFor) : (DateTimeOffset?)null;

            var org = await context.GetOrganisationAsync();
            var enrolmentKey = context.Get(keyId) ?? (context.Get(key) is { } description ? (await KeyLookup.FindAsync(context, org, [description], key.Name))[0].ToInt() : null);

            var search = new SearchText(context.Get(filter))
                .Add("tags", context.Get(tags))
                .Add("state", unseenSince is null ? context.Get(state) : null)
                .Add("os", context.Get(os))
                .Add("type", context.Get(type))
                .AddFlag("gateway", context.Get(gateway))
                .ToSearchTerm();

            // state:disconnected matches on the session whether or not the system is enabled, so it
            // keeps out a disabled system that is online, which the API reports as Disabled (portal
            // SystemSearchKeyService, the "state" key; SystemExtensions). The API reads only the first
            // state: term (BaseSearchKeyService.AddOrUpdateQueryDetails), so this one goes ahead of the
            // --filter text, where a later one cannot replace it.
            if (unseenSince is not null)
            {
                search = search is null ? "state:disconnected" : $"state:disconnected {search}";
            }

            if (context.Get(pending))
            {
                var pendingSort = context.Get(sort) is { } mode ? PendingSort(mode) : null;
                var waiting = await Paging.ReadAllAsync(
                    (page, perPage) => org.Client.UnapprovedSystems.GetSystemsAsync(enrolmentKey, search, pendingSort, page, perPage),
                    context.CancellationToken);

                // --waiting-for has no API search key ("Filters").
                if (waitingSince is { } enrolledBy)
                {
                    waiting = waiting.Where(system => new DateTimeOffset(CliJson.ToUtc(system.EnrolledAt)) <= enrolledBy).ToList();
                }

                await context.Output.WriteListAsync(ListKind.PendingSystem, waiting, context.CancellationToken);
                return;
            }

            // The list handler never reads the dns parameter (portal PaginatedSystemsHandler), so the
            // CLI sends none ("Left out").
            var systems = await Paging.ReadAllAsync(
                (page, perPage) => org.Client.EnrolledSystems.GetSystemsAsync(
                    enrolmentKey,
                    search,
                    context.Get(includeDisabled) ? true : null,
                    context.Get(sort),
                    dnsName: null,
                    pageNumber: page,
                    perPage: perPage),
                context.CancellationToken);

            // --not-seen-for has no API search key either. A system never seen counts from when it
            // enrolled, so one that has just enrolled is not listed for removal. A connected system
            // can go long without a new lastSeen, and this list feeds `system revoke` in example 14,
            // so a connected system is never listed ("Filters"); the check on state backs up the
            // search.
            if (unseenSince is { } lastSeenBy)
            {
                systems = systems
                    .Where(system => system.State != SystemState.Connected && (system.LastSeen ?? system.EnrolledAt) <= lastSeenBy)
                    .ToList();
            }

            await context.Output.WriteListAsync(ListKind.System, systems, context.CancellationToken);
        });

        return verb;
    }

    // UnapprovedSystemQuerySortMode's members are a subset of SystemQuerySortMode's (portal
    // UnapprovedSystemQuerySortMode).
    private static UnapprovedSystemQuerySortMode? PendingSort(SystemQuerySortMode mode) => mode switch
    {
        SystemQuerySortMode.RecentlyEnrolled => UnapprovedSystemQuerySortMode.RecentlyEnrolled,
        SystemQuerySortMode.Description => UnapprovedSystemQuerySortMode.Description,
        SystemQuerySortMode.EnrolmentKeyUsed => UnapprovedSystemQuerySortMode.EnrolmentKeyUsed,
        SystemQuerySortMode.DescriptionOrHostname => UnapprovedSystemQuerySortMode.DescriptionOrHostname,
        _ => null,
    };
}
