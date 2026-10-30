using Enclave.Api.Modules.SystemManagement.Dns.Models;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Dns;

internal static class DnsLookup
{
    // An ID needs no lookup; name is read only when id is null. A name that matches no zone, or
    // several, exits 2 with the candidates, naming key, the option or argument it came from.
    public static async Task<DnsZoneId> ZoneIdAsync(CliContext context, OrganisationInUse org, string? name, int? id, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(org);

        if (id is { } given)
        {
            return DnsZoneId.FromInt(given);
        }

        ArgumentNullException.ThrowIfNull(name);

        var zones = await NameLookup.FindAsync(
            [name],
            (page, perPage) => org.Client.Dns.GetZonesAsync(page, perPage),
            zone => zone.Name,
            zone => zone.Id,
            key,
            "zone",
            context.CancellationToken);

        return zones[0].Id;
    }

    public static Task<List<DnsZoneSummaryModel>> ReadZonesAsync(CliContext context, OrganisationInUse org)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(org);

        return Paging.ReadAllAsync((page, perPage) => org.Client.Dns.GetZonesAsync(page, perPage), context.CancellationToken);
    }

    // The hostnames in the order given. A name that matches no hostname, or several, exits 2 with
    // the candidates.
    // Matched on fqdn, since db.internal and db.lab are both the record "db". The list is read
    // without a search, since the API reads search text in its own syntax (spec/cli-surface.md
    // "Filters").
    public static Task<IReadOnlyList<DnsRecordSummaryModel>> FindHostnamesAsync(CliContext context, OrganisationInUse org, IReadOnlyList<string> hostnames, string key)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(org);

        return NameLookup.FindAsync(
            hostnames,
            (page, perPage) => org.Client.Dns.GetRecordsAsync(pageNumber: page, perPage: perPage),
            record => record.Fqdn,
            record => record.Id,
            key,
            "hostname",
            context.CancellationToken);
    }

    // The record name is what the API takes in place of the full name (portal
    // DnsRecordCreateModel, DnsRecordPatchModel). Exits 2 when the hostname is a zone's name or is
    // in no zone.
    // hostname is the full name, such as db.eu.internal; errors name key, the option or argument
    // it came from.
    public static (DnsZoneSummaryModel Zone, string RecordName) Place(string hostname, IReadOnlyCollection<DnsZoneSummaryModel> zones, string key)
    {
        ArgumentNullException.ThrowIfNull(hostname);
        ArgumentNullException.ThrowIfNull(zones);

        // A zone's own name would otherwise be read as a record in a shorter zone it ends in:
        // eu.internal as the record "eu" in internal. These messages leave the hostname out, as
        // option errors do (CliOptions).
        if (zones.Any(zone => string.Equals(zone.Name, hostname, StringComparison.OrdinalIgnoreCase)))
        {
            throw CliErrors.InvalidArgument(key, "The hostname given is the name of a zone. A hostname is a name within a zone, such as db.internal in the zone internal.");
        }

        // The longest zone name the hostname ends in at a label boundary: with zones internal and
        // eu.internal, db.eu.internal is in eu.internal ("Details"). The record name is the rest,
        // which may contain dots and is never empty.
        var zone = zones
            .Where(candidate => hostname.Length > candidate.Name.Length + 1
                && hostname.EndsWith("." + candidate.Name, StringComparison.OrdinalIgnoreCase))
            .MaxBy(candidate => candidate.Name.Length)
            ?? throw CliErrors.InvalidArgument(key, "The hostname given is in none of the organisation's zones. A hostname is written in full and ends in its zone's name, such as db.internal in the zone internal.");

        return (zone, hostname[..^(zone.Name.Length + 1)]);
    }
}
