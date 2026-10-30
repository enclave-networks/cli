namespace Enclave.Cli.Core;

// IdField is the item property a command reading the list from "-" takes each ID from.
internal sealed record ListKind(string Name, string? IdField)
{
    // spec/cli-surface.md "Output" names the kinds. Each ID field is the model property that
    // identifies an item to the API; an invite's is its email address, since the API cancels an
    // invite by email address (OrganisationScopedClient.CancelInviteAync). Log entries have no ID.
    public static ListKind System { get; } = new("system", "systemId");

    public static ListKind PendingSystem { get; } = new("pending-system", "systemId");

    public static ListKind Key { get; } = new("key", "id");

    public static ListKind Policy { get; } = new("policy", "id");

    public static ListKind Tag { get; } = new("tag", "tag");

    public static ListKind Zone { get; } = new("zone", "id");

    public static ListKind Hostname { get; } = new("hostname", "id");

    public static ListKind Trust { get; } = new("trust", "id");

    public static ListKind Log { get; } = new("log", null);

    public static ListKind Org { get; } = new("org", "orgId");

    public static ListKind User { get; } = new("user", "id");

    public static ListKind Invite { get; } = new("invite", "emailAddress");

    public static ListKind Customer { get; } = new("customer", "id");

    public static ListKind Admin { get; } = new("admin", "id");
}
