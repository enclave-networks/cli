using Enclave.Api.Modules.SystemManagement.Dns.Models;
using Enclave.Cli.Context;
using Enclave.Cli.Core;
using Enclave.Configuration.Data.Identifiers;

namespace Enclave.Cli.Commands.Dns;

/// <summary>
/// Finds DNS zones by name, hostnames by their full name, and the zone a full hostname is in
/// (proposed-cli-surface.md "Names and IDs" and "Details").
/// </summary>
internal static class DnsLookup
{
    /// <summary>
    /// The zone given by ID, or else by name, looked up with one list call. A name that matches no
    /// zone, or several, exits 2 with the candidates.
    /// </summary>
    /// <param name="context">The command's context.</param>
    /// <param name="org">The organisation in use.</param>
    /// <param name="name">The zone's name; read when <paramref name="id"/> is null.</param>
    /// <param name="id">The zone's ID, which needs no lookup.</param>
    /// <param name="key">The option or argument the name came from, for errors.</param>
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

    /// <summary>
    /// Every zone of the organisation, reading every page.
    /// </summary>
    public static Task<List<DnsZoneSummaryModel>> ReadZonesAsync(CliContext context, OrganisationInUse org)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(org);

        return Paging.ReadAllAsync((page, perPage) => org.Client.Dns.GetZonesAsync(page, perPage), context.CancellationToken);
    }

    /// <summary>
    /// The hostnames with these full names, in order, read with one list call. A name that matches
    /// no hostname, or several, exits 2 with the candidates.
    /// </summary>
    // The full name, fqdn, is what identifies a hostname: db.internal and db.lab are both the record
    // "db" (portal DnsRecordModel). The list is read without a search, since the API reads search
    // text in its own search syntax (proposed-cli-surface.md "Filters"), and the full name is matched
    // here, whole and ignoring case, as every name lookup matches.
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

    /// <summary>
    /// The zone a full hostname is in, and the hostname's record name within it, which the API takes
    /// in place of the full name (portal DnsRecordCreateModel, DnsRecordPatchModel). Exits 2 when
    /// the hostname is a zone's name or is in no zone.
    /// </summary>
    /// <param name="hostname">The full hostname, such as db.eu.internal.</param>
    /// <param name="zones">The organisation's zones.</param>
    /// <param name="key">The option or argument the hostname came from, for errors.</param>
    public static (DnsZoneSummaryModel Zone, string RecordName) Place(string hostname, IReadOnlyCollection<DnsZoneSummaryModel> zones, string key)
    {
        ArgumentNullException.ThrowIfNull(hostname);
        ArgumentNullException.ThrowIfNull(zones);

        // A zone's own name would otherwise be read as a record in a shorter zone it ends in:
        // eu.internal as the record "eu" in internal.
        if (zones.Any(zone => string.Equals(zone.Name, hostname, StringComparison.OrdinalIgnoreCase)))
        {
            throw CliErrors.InvalidArgument(key, $"\"{hostname}\" is the name of a zone. A hostname is a name within a zone, such as db.{hostname}.");
        }

        // The zone is the longest zone name the hostname ends in at a label boundary, so with zones
        // internal and eu.internal, db.eu.internal is in eu.internal ("Details"). The record name is
        // the rest, which may contain dots, and is never empty.
        var zone = zones
            .Where(candidate => hostname.Length > candidate.Name.Length + 1
                && hostname.EndsWith("." + candidate.Name, StringComparison.OrdinalIgnoreCase))
            .MaxBy(candidate => candidate.Name.Length)
            ?? throw CliErrors.InvalidArgument(key, $"\"{hostname}\" is in none of the organisation's zones. A hostname is written in full and ends in its zone's name, such as db.internal in the zone internal.");

        return (zone, hostname[..^(zone.Name.Length + 1)]);
    }
}
