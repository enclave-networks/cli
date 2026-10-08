using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// The CLI writes a hostname in full, db.internal, and its zone is the zone its name ends in
// (proposed-cli-surface.md "Commands"). The API keeps a hostname as a record name within a zone and
// returns the full name as fqdn (portal Enclave.Api/Modules/SystemManagement/Dns/Models/DnsRecordModel.cs),
// so db.internal is the record "db" in the zone "internal". The body is ApiJson.Record's with the
// zone, full name and tags changed, so it carries the properties HarnessTests proves Enclave.Sdk.Api reads.

/// <summary>
/// DNS record bodies for hostnames in zones of any name.
/// </summary>
internal static partial class ApiJson
{
    /// <summary>
    /// A DnsRecordModel for the hostname <paramref name="name"/>.<paramref name="zoneName"/>: the
    /// record <paramref name="name"/> in zone <paramref name="zoneId"/>, with the given tags. It also
    /// reads as a DnsRecordSummaryModel list item.
    /// </summary>
    public static string HostnameRecord(int id, string name, int zoneId, string zoneName, params string[] tags)
    {
        var record = JsonNode.Parse(WithUsedTags(Record(id, name), tags))!.AsObject();
        record["zoneId"] = zoneId;
        record["zoneName"] = zoneName;
        record["fqdn"] = $"{name}.{zoneName}";
        return record.ToJsonString();
    }
}
