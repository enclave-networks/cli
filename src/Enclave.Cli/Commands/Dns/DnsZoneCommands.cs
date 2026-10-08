using System.CommandLine;
using Enclave.Api.Modules.SystemManagement.Dns.Models;
using Enclave.Cli.Core;

namespace Enclave.Cli.Commands.Dns;

/// <summary>
/// The zone verbs of `dns`: list-zones, show-zone, create-zone, update-zone and delete-zone. A zone
/// is given by its name, looked up with one list call, or by --id, which makes no lookup
/// (proposed-cli-surface.md "Names and IDs").
/// </summary>
internal static class DnsZoneCommands
{
    public static CliVerb List()
    {
        var verb = new CliVerb("list-zones", "List the DNS zones, reading every page.", CommandScope.Organisation);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var zones = await DnsLookup.ReadZonesAsync(context, org);

            await context.Output.WriteListAsync(ListKind.Zone, zones, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Show()
    {
        var (verb, zone, id) = ForZone("show-zone", "Show a zone.", changes: false);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var zoneId = await DnsLookup.ZoneIdAsync(context, org, context.Get(zone), context.Get(id), zone.Name);
            var model = await SingleItem.CallAsync(() => org.Client.Dns.GetZoneAsync(zoneId));

            await context.Output.WriteAsync(model, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Create()
    {
        var verb = new CliVerb("create-zone", "Create a DNS zone and print it.", CommandScope.Organisation, changes: true);
        var name = verb.Add(CliArguments.Text("zone", "The zone's name, such as internal."));
        var autoDnsTags = verb.Add(CliOptions.TagList("--auto-dns-tags", "Give systems with these tags a name in the zone automatically."));
        var notes = verb.Add(CliOptions.Text("--notes", "The zone's notes."));

        // The API refuses automatic DNS tags that hold a tag twice (portal DnsZoneCreateValidator.cs:32).
        verb.Check(context => ListValues.CheckNoRepeats(autoDnsTags.Name, context.Get(autoDnsTags), StringComparer.Ordinal));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();

            // A create sends an empty list for a list flag left out (proposed-cli-surface.md "Details").
            var zone = await org.Client.Dns.CreateZoneAsync(new DnsZoneCreateModel
            {
                Name = context.Get(name)!,
                AutoDnsTags = context.Get(autoDnsTags)?.ToArray() ?? [],
                Notes = context.Get(notes),
            });

            await context.Output.WriteAsync(zone, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Update()
    {
        var (verb, zone, id) = ForZone("update-zone", "Change a zone and print it.", changes: true);
        var name = verb.Add(CliOptions.Text("--name", "The zone's new name.", "name"));
        var autoDnsTags = verb.Add(CliOptions.TagList("--set-auto-dns-tags", "Replace the tags whose systems get a name in the zone automatically; \"\" removes them all."));
        var notes = verb.Add(CliOptions.Text("--notes", "The zone's notes."));

        verb.AtLeastOne(name, autoDnsTags, notes);

        // The API refuses automatic DNS tags that hold a tag twice (portal DnsZonePatchValidator.cs:28).
        verb.Check(context => ListValues.CheckNoRepeats(autoDnsTags.Name, context.Get(autoDnsTags), StringComparer.Ordinal));

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var zoneId = await DnsLookup.ZoneIdAsync(context, org, context.Get(zone), context.Get(id), zone.Name);

            // An update sends only the fields given, so the zone keeps the rest ("Create and update").
            var patch = org.Client.Dns.UpdateZone(zoneId);

            if (context.Get(name) is { } newName)
            {
                patch.Set(model => model.Name, newName);
            }

            if (context.Get(autoDnsTags) is { } tags)
            {
                patch.Set(model => model.AutoDnsTags, tags.ToArray());
            }

            if (context.Get(notes) is { } text)
            {
                patch.Set(model => model.Notes, text);
            }

            var updated = await SingleItem.CallAsync(() => patch.ApplyAsync());

            await context.Output.WriteAsync(updated, context.CancellationToken);
        });

        return verb;
    }

    public static CliVerb Delete()
    {
        var (verb, zone, id) = ForZone("delete-zone", "Delete a zone and print it. Takes one zone; the API has no bulk zone delete.", changes: true);

        verb.SetHandler(async context =>
        {
            var org = await context.GetOrganisationAsync();
            var zoneId = await DnsLookup.ZoneIdAsync(context, org, context.Get(zone), context.Get(id), zone.Name);

            // The API deletes one zone per call and answers with the zone (portal DnsController.cs:145),
            // so delete-zone is a single-ID command: a 404 exits 5 ("Several IDs").
            var deleted = await SingleItem.CallAsync(() => org.Client.Dns.DeleteZoneAsync(zoneId));

            await context.Output.WriteAsync(deleted, context.CancellationToken);
        });

        return verb;
    }

    private static (CliVerb Verb, Argument<string?> Zone, Option<int?> Id) ForZone(string name, string description, bool changes)
    {
        var verb = new CliVerb(name, description, CommandScope.Organisation, changes);
        var zone = verb.Add(CliArguments.OptionalText("zone", "The zone's name, matched whole and ignoring case."));
        var id = verb.Add(CliOptions.Id("--id", "The zone's ID.", IdFormats.Int32));

        verb.ExactlyOne(zone, id);

        return (verb, zone, id);
    }
}
