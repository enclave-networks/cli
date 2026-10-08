using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.Dns.Models;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api.Exceptions;

namespace Enclave.Cli.Commands.Dns;

/// <summary>
/// The hostname verbs of `dns`: list-hostnames, show-hostname, create-hostname, update-hostname and
/// delete-hostname. A hostname is written in full, db.internal, which the API keeps as the record
/// "db" in the zone "internal" (portal DnsRecordModel: name, zoneId, fqdn); it is given by that full
/// name, looked up with one list call, or by --id, which makes no lookup (proposed-cli-surface.md
/// "Names and IDs").
/// </summary>
internal static class DnsHostnameCommands
{
    public static CliVerb List()
    {
        var verb = new CliVerb("list-hostnames", "List the hostnames, reading every page.", CommandScope.Organisation);
        var zone = verb.Add(CliOptions.Text("--zone", "Hostnames in the zone of this name.", "name"));
        var zoneId = verb.Add(CliOptions.Id("--zone-id", "Hostnames in the zone of this ID.", IdFormats.Int32));
        var filter = verb.Add(CliOptions.Text("--filter", "Hostnames whose name contains this text."));

        verb.Exclusive(zone, zoneId);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            DnsZoneId? inZone = context.IsGiven(zone) || context.IsGiven(zoneId)
                ? await DnsLookup.ZoneIdAsync(context, org, context.Get(zone), context.Get(zoneId), zone.Name)
                : null;
            var search = new SearchText(context.Get(filter)).ToSearchTerm();

            var records = await Paging.ReadAllAsync(
                (page, perPage) => org.Client.Dns.GetRecordsAsync(inZone, search, page, perPage),
                context.CancellationToken);

            await context.Output.WriteListAsync(ListKind.Hostname, records, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Show()
    {
        var (verb, hostname, id) = ForHostname("show-hostname", "Show a hostname.", changes: false);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var recordId = context.Get(id) is { } given
                ? DnsRecordId.FromInt(given)
                : (await DnsLookup.FindHostnamesAsync(context, org, [context.Get(hostname)!], hostname.Name))[0].Id;

            var record = await SingleItem.CallAsync(() => org.Client.Dns.GetRecordAsync(recordId));

            await context.Output.WriteAsync(record, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Create()
    {
        var verb = new CliVerb("create-hostname", "Create a hostname in the zone its name ends in, and print it.", CommandScope.Organisation, changes: true);
        var hostname = verb.Add(CliArguments.Text("hostname", "The full hostname, such as db.internal."));
        var tags = verb.Add(CliOptions.TagList("--tags", "The hostname answers with the systems that have these tags."));
        var systems = verb.Add(CliOptions.IdList("--systems", "The hostname answers with these systems.", IdFormats.System, "id,id"));
        var notes = verb.Add(CliOptions.Text("--notes", "The hostname's notes."));

        // The API refuses tags that hold a tag twice (portal DnsRecordCreateValidator.cs:32).
        verb.Check(context => ListValues.CheckNoRepeats(tags.Name, context.Get(tags), StringComparer.Ordinal));
        verb.Check(context => CheckSystems(systems.Name, context.Get(systems)));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var zones = await DnsLookup.ReadZonesAsync(context, org);
            var (zone, recordName) = DnsLookup.Place(context.Get(hostname)!, zones, hostname.Name);

            // ENCLAVE is the only record type the API has (portal DnsRecordTypeFormatConverter.cs:12),
            // and a create sends an empty list for a list flag left out (proposed-cli-surface.md
            // "Details"). The hostname does not exist yet, so a 404 here is about another item, a
            // zone or a system (portal DnsRecordCreateHandler.cs:65-68, DnsRecordHandlerBase.cs:117-123),
            // and exits 1 as any API error does ("Errors and exit codes").
            var record = await org.Client.Dns.CreateRecordAsync(new DnsRecordCreateModel
            {
                Name = recordName,
                ZoneId = zone.Id,
                Type = "ENCLAVE",
                Tags = context.Get(tags) ?? [],
                Systems = context.Get(systems) ?? [],
                Notes = context.Get(notes),
            });

            await context.Output.WriteAsync(record, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Update()
    {
        var (verb, hostname, id) = ForHostname("update-hostname", "Change a hostname and print it.", changes: true);
        var name = verb.Add(CliOptions.Text("--name", "The hostname's new full name, in the same zone.", "hostname"));
        var setTags = verb.Add(CliOptions.TagList("--set-tags", "Replace the hostname's tags; \"\" removes them all."));
        var addTags = verb.Add(CliOptions.TagList("--add-tags", "Add tags, keeping the hostname's others."));
        var removeTags = verb.Add(CliOptions.TagList("--remove-tags", "Remove tags, keeping the hostname's others."));
        var setSystems = verb.Add(CliOptions.IdList("--set-systems", "Replace the hostname's systems; \"\" removes them all.", IdFormats.System, "id,id"));
        var notes = verb.Add(CliOptions.Text("--notes", "The hostname's notes."));

        verb.AtLeastOne(name, setTags, addTags, removeTags, setSystems, notes);
        verb.Check(context => CheckSystems(setSystems.Name, context.Get(setSystems)));

        verb.SetHandler(async context =>
        {
            var newName = context.Get(name);
            var (set, add, remove) = (context.Get(setTags), context.Get(addTags), context.Get(removeTags));

            var org = await context.GetOrganisationAsync();
            var current = await CurrentAsync(context, org, context.Get(hostname), hostname.Name, context.Get(id), newName is not null || TagEdits.NeedsCurrentTags(set, add, remove));

            // An update sends only the fields given, so the hostname keeps the rest ("Create and
            // update").
            var patch = org.Client.Dns.UpdateRecord(current.Id);

            if (newName is not null)
            {
                var zones = await DnsLookup.ReadZonesAsync(context, org);
                var (zone, recordName) = DnsLookup.Place(newName, zones, name.Name);

                // The API cannot move a record between zones: the patch takes a record name within
                // the record's own zone (portal DnsRecordPatchModel.Name; "Details").
                if (zone.Id != current.ZoneId)
                {
                    throw CliErrors.InvalidArgument(name.Name, $"{name.Name} takes a full hostname in the hostname's own zone; \"{newName}\" is in the zone \"{zone.Name}\", and the API cannot move a hostname to another zone.");
                }

                patch.Set(model => model.Name, recordName);
            }

            if (TagEdits.Apply(set, add, remove, current.Tags) is { } tagList)
            {
                patch.Set(model => model.Tags, tagList);
            }

            if (context.Get(setSystems) is { } systemList)
            {
                patch.Set(model => model.Systems, systemList);
            }

            if (context.Get(notes) is { } text)
            {
                patch.Set(model => model.Notes, text);
            }

            var updated = await PatchAsync(() => patch.ApplyAsync());

            await context.Output.WriteAsync(updated, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Delete()
    {
        var verb = new CliVerb("delete-hostname", "Delete hostnames. Takes full hostnames, IDs after --id, or \"-\" to read a list from stdin, and prints { requested, affected }.", CommandScope.Organisation, changes: true);
        var hostnames = verb.Add(CliArguments.Many("hostname", "A full hostname, such as db.internal."));
        var ids = verb.Add(CliOptions.IdList("--id", "Hostname IDs, separated by commas.", IdFormats.Int32));

        verb.Exclusive(hostnames, ids);

        verb.SetHandler(async context =>
        {
            var selection = await Items.ReadAsync(context, hostnames, ids, ListKind.Hostname, IdFormats.Int32);

            // An empty list makes no call and exits 0, so a pipeline fed by an empty list succeeds
            // (proposed-cli-surface.md "Several IDs"). Its dry run prints the dry run output, which
            // names the organisation, with no request ("Dry run"), so it goes on to resolve the
            // organisation.
            if (selection.Ids.Count == 0 && selection.Names.Count == 0 && !context.IsDryRun)
            {
                await context.Output.WriteBulkAsync(new BulkResult(0, 0), context.CancellationToken);
                return;
            }

            var org = await context.GetOrganisationAsync();
            var recordIds = selection.Names.Count == 0
                ? selection.Ids
                : (await DnsLookup.FindHostnamesAsync(context, org, selection.Names, hostnames.Name)).Select(record => record.Id.ToInt()).Distinct().ToArray();

            var result = await Bulk.RunAsync(recordIds, batch => org.Client.Dns.DeleteRecordsAsync(batch.Select(DnsRecordId.FromInt)));

            await context.Output.WriteBulkAsync(result, context.CancellationToken);
        });

        return verb;
    }

    // The hostname an update acts on: its ID, and, when the update needs them, its zone and tags.
    // A hostname given by name comes with both from the lookup. One given by --id is read first only
    // when --name, --add-tags or --remove-tags needs it, since --name must stay in the hostname's zone
    // and the tag flags patch the whole list ("Calls per command").
    private static async Task<CurrentHostname> CurrentAsync(CliContext context, OrganisationInUse org, string? hostname, string key, int? id, bool needsRead)
    {
        if (hostname is not null)
        {
            var found = (await DnsLookup.FindHostnamesAsync(context, org, [hostname], key))[0];
            return new CurrentHostname(found.Id, found.ZoneId, found.Tags.Select(tag => tag.Tag));
        }

        var recordId = DnsRecordId.FromInt(id!.Value);

        if (!needsRead)
        {
            return new CurrentHostname(recordId, null, null);
        }

        // One hostname named by ID: a 404 means it does not exist, exit 5 ("Several IDs").
        var record = await SingleItem.CallAsync(() => org.Client.Dns.GetRecordAsync(recordId));
        return new CurrentHostname(record.Id, record.ZoneId, record.Tags.Select(tag => tag.Tag));
    }

    // The API upper-cases system IDs and answers 404 no-such-system when it finds fewer systems than
    // it was given (portal DnsRecordCreateHandler.cs:104, DnsRecordPatchHandler.cs:128,
    // DnsRecordHandlerBase.cs:93-124), so it refuses a system given twice, in either case.
    private static void CheckSystems(string option, IReadOnlyList<string>? systems) =>
        ListValues.CheckNoRepeats(option, systems, StringComparer.OrdinalIgnoreCase);

    // The API answers update-hostname's patch 404 for the hostname itself (dns-record-not-found,
    // portal DnsController.cs:361-368) and for a --set-systems system that is not an approved system
    // of the organisation (no-such-system, portal
    // DnsRecordHandlerBase.cs:117-123), whose detail names the system. Only the first means the
    // hostname the command names was not found, exit 5 ("Errors and exit codes"); any other 404,
    // one without that problem type included, exits 1 and carries the API's detail.
    private static async Task<T> PatchAsync<T>(Func<Task<T>> patch)
    {
        try
        {
            return await patch();
        }
        catch (Exception exception) when (exception is not CliException)
        {
            throw ApiErrors.ToCliException(exception, notFoundIsUnknownItem: IsHostnameNotFound(exception));
        }
    }

    // The API writes a problem type as a URL that ends in the type's name (portal
    // Enclave.Api.Scaffolding/ProblemResponseFactory.cs:41-51, Enclave.Api/WebStartup.cs:166).
    private static bool IsHostnameNotFound(Exception exception) =>
        exception is EnclaveApiException { ProblemDetails.Type: { } type }
        && string.Equals(type[(type.LastIndexOf('/') + 1)..], "dns-record-not-found", StringComparison.Ordinal);

    private static (CliVerb Verb, Argument<string?> Hostname, Option<int?> Id) ForHostname(string name, string description, bool changes)
    {
        var verb = new CliVerb(name, description, CommandScope.Organisation, changes);
        var hostname = verb.Add(CliArguments.OptionalText("hostname", "The full hostname, such as db.internal, matched ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The hostname's ID.", IdFormats.Int32));

        verb.ExactlyOne(hostname, id);

        return (verb, hostname, id);
    }

    private sealed record CurrentHostname(DnsRecordId Id, DnsZoneId? ZoneId, IEnumerable<string>? Tags);
}
