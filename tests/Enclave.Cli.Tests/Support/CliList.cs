using System.Globalization;
using System.Text.Json.Nodes;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// Lists as list commands print them, for a test to give a command as stdin after "-".
/// </summary>
internal static class CliList
{
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

    // An item's ID is the value a command given the list sends. An invite's is its email address:
    // the main API cancels an invite by email (Enclave.Sdk.Api
    // OrganisationScopedClient.CancelInviteAync), and a partner customer's invite is looked up by
    // email (spec/cli-surface.md "Partner API").

    /// <summary>
    /// The property holding each item's ID. Throws for log, whose entries have no ID.
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
    /// A list of this kind holding the item bodies.
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
    /// A list of this kind with one ApiJson body per ID, in order. Throws for log.
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
