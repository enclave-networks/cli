using System.Globalization;
using System.Text.Json.Nodes;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Lists in the form enclave-cli list commands print them, { "kind", "items", "total" }, for a test
/// to give a command as stdin after "-" (proposed-cli-surface.md "Output" and "Several IDs").
/// </summary>
internal static class CliList
{
    /// <summary>
    /// Every list kind the CLI prints.
    /// </summary>
    public static IReadOnlyList<string> Kinds { get; } =
    [
        "system",
        "pending-system",
        "key",
        "policy",
        "tag",
        "zone",
        "hostname",
        "trust",
        "log",
        "org",
        "user",
        "invite",
        "customer",
        "admin",
    ];

    // Each kind's ID is the property its model identifies an item by, and the value a command given
    // that list sends: systems by systemId, tags by name, invites by email address (the main API
    // cancels an invite by email, OrganisationClient.CancelInviteAync, and a partner customer's
    // invite is looked up by its email address, proposed-cli-surface.md "Partner API"), and
    // organisations, users, customers and customer admins by their GUID
    // (AccountOrganisationModel.OrgId, OrganisationUser.Id, and Enclave.Sdk.Api 1.1.0's
    // CustomerModel.Id and CustomerUserModel.Id). Log entries have no ID (LogEntryModel).

    /// <summary>
    /// The property that holds the ID of each item in a list of this kind. Throws for log, whose
    /// entries have no ID.
    /// </summary>
    public static string IdField(string kind) => kind switch
    {
        "system" or "pending-system" => "systemId",
        "key" or "policy" or "zone" or "hostname" or "trust" => "id",
        "tag" => "tag",
        "org" => "orgId",
        "user" or "customer" or "admin" => "id",
        "invite" => "emailAddress",
        "log" => throw new ArgumentException("Log entries have no ID.", nameof(kind)),
        _ => throw UnknownKind(kind),
    };

    /// <summary>
    /// A list of this kind holding the given item bodies, with total equal to the number of items.
    /// </summary>
    public static string Of(string kind, params string[] items)
    {
        if (!Kinds.Contains(kind))
        {
            throw UnknownKind(kind);
        }

        var array = new JsonArray();
        foreach (var item in items)
        {
            array.Add(JsonNode.Parse(item));
        }

        return new JsonObject
        {
            ["kind"] = kind,
            ["items"] = array,
            ["total"] = items.Length,
        }.ToJsonString();
    }

    /// <summary>
    /// A list of this kind holding one item for each ID, in the order given. Each item is the
    /// ApiJson body for that kind with the ID in its <see cref="IdField"/>: a number for keys,
    /// policies, zones, hostnames and trust requirements, and a string for the rest. Throws for log,
    /// whose entries have no ID.
    /// </summary>
    public static string WithIds(string kind, params string[] ids) =>
        Of(kind, ids.Select(id => Item(kind, id)).ToArray());

    private static string Item(string kind, string id) => kind switch
    {
        "system" => ApiJson.System(id),
        "pending-system" => ApiJson.PendingSystem(id),
        "key" => ApiJson.Key(Int(id)),
        "policy" => ApiJson.Policy(Int(id)),
        "tag" => ApiJson.Tag(id),
        "zone" => ApiJson.Zone(Int(id), Invariant($"zone{id}")),
        "hostname" => ApiJson.Record(Int(id), Invariant($"host{id}")),
        "trust" => ApiJson.Trust(Int(id)),
        "org" => ApiJson.Org(Guid.Parse(id, CultureInfo.InvariantCulture), Invariant($"Org {id}")),
        "user" => ApiJson.User(Guid.Parse(id, CultureInfo.InvariantCulture), Invariant($"user-{id}@acme.example")),
        "invite" => ApiJson.Invite(id),
        "customer" => ApiJson.Customer(id, Invariant($"Customer {id}")),
        "admin" => ApiJson.CustomerAdmin(id, Invariant($"admin-{id}@example.com")),
        "log" => throw new ArgumentException("Log entries have no ID.", nameof(kind)),
        _ => throw UnknownKind(kind),
    };

    private static int Int(string id) => int.Parse(id, NumberStyles.None, CultureInfo.InvariantCulture);

    private static ArgumentException UnknownKind(string kind) =>
        new($"\"{kind}\" is not a list kind; the kinds are {string.Join(", ", Kinds)}.", nameof(kind));
}
