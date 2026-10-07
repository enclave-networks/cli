namespace Enclave.Cli.Core;

/// <summary>
/// The kind a list prints in its envelope, and the item property that holds each item's ID, which
/// a command reading the list after "-" acts on.
/// </summary>
internal sealed record ListKind(string Name, string? IdField)
{
    // proposed-cli-surface.md "Output" names the kinds. The ID fields are the properties of the
    // Enclave.Sdk.Api models that identify an item to the API: SystemSummaryModel.SystemId, the
    // integer Id of keys, policies, zones, records and trust requirements, TagSummaryModel.Tag,
    // AccountOrganisationModel.OrgId, OrganisationUser.Id, and OrganisationInviteModel.EmailAddress,
    // since the API cancels an invite by email address (OrganisationClient.CancelInviteAync).
    // Log entries have no ID.
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
