using System.Text.Json.Nodes;

namespace Enclave.Cli.Tests.Support;

// The API keeps a hostname as a record name within a zone and returns the full name as fqdn
// (portal DnsRecordModel), so db.internal is the record "db" in the zone "internal".

// DNS record bodies for hostnames in zones of any name.
internal static partial class ApiJson
{
    // A DnsRecordModel for the hostname name.zoneName, which also reads as a DnsRecordSummaryModel
    // list item.
    public static string HostnameRecord(int id, string name, int zoneId, string zoneName, params string[] tags)
    {
        var record = JsonNode.Parse(WithUsedTags(Record(id, name), tags))!.AsObject();
        record["zoneId"] = zoneId;
        record["zoneName"] = zoneName;
        record["fqdn"] = $"{name}.{zoneName}";
        return record.ToJsonString();
    }
}
