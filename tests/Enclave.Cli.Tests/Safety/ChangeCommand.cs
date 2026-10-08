using Enclave.Cli.Tests.Support;

namespace Enclave.Cli.Tests.Safety;

// A value type, so the public test methods taking one need no null check (CA1062).

/// <summary>
/// A command that changes something through the API in the organisation in use: a command line
/// that runs it, the request that makes the change, and the answers the fake API gives.
/// </summary>
/// <param name="Name">The command words, with any option that selects a different request.</param>
/// <param name="Args">The command line.</param>
/// <param name="Method">The HTTP method of the change.</param>
/// <param name="PathSuffix">The change's path below /org/{orgId}/, empty for the organisation itself.</param>
/// <param name="Response">The body the API answers the change with, or null for none.</param>
/// <param name="ReadPathSuffix">A path below /org/{orgId}/ the command may read first, or null.</param>
/// <param name="ReadResponse">The body of that read.</param>
/// <param name="OtherPathSuffix">A second spelling of the change's path, or null.</param>
public readonly record struct ChangeCommand(
    string Name,
    string[] Args,
    string Method,
    string PathSuffix,
    string? Response,
    string? ReadPathSuffix = null,
    string? ReadResponse = null,
    string? OtherPathSuffix = null)
{
    // Declared before All, whose initialiser reads it: static initialisers run in textual order.
    private static readonly Guid AccountId = new("5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25");

    // Every command in proposed-cli-surface.md "Commands" that changes something through the main
    // API. login, logout and the use commands change local files only, and the partner customer
    // commands use the partner API; they are tested in Partner/. Items are given by ID where the
    // command allows it, so the change is the only request; dns create-hostname finds its zone from
    // the hostname, and tag set reads the tag to choose between the API's update and create calls
    // ("Dry run"), so those reads are answered. Routes are those of Enclave.Sdk.Api 1.1.0.
    //
    // RemoveUserAsync takes the account ID as a string (OrganisationScopedClient.cs:91), so the path
    // carries whichever GUID form the CLI passes, and both forms are answered.
    public static IReadOnlyList<ChangeCommand> All { get; } =
    [
        new("org update", ["org", "update", "--name", "Acme Ltd"], "PATCH", string.Empty, ApiJson.OrgProperties("Acme Ltd")),
        new("org remove-user", ["org", "remove-user", "--id", AccountId.ToString()], "DELETE", $"users/{AccountId:N}", ApiJson.User(AccountId, "sam@example.com"), OtherPathSuffix: $"users/{AccountId:D}"),
        new("org invite", ["org", "invite", "alex@example.com"], "POST", "invites", ApiJson.Invite("alex@example.com")),
        new("org cancel-invite", ["org", "cancel-invite", "sam@example.com"], "DELETE", "invites", ApiJson.Invite("sam@example.com")),
        new("system update", ["system", "update", "ABCDE", "--description", "web server"], "PATCH", "systems/ABCDE", ApiJson.System("ABCDE", "web server")),
        new("system update --pending", ["system", "update", "XYZ12", "--pending", "--set-tags", "kiosk,lobby"], "PATCH", "unapproved-systems/XYZ12", ApiJson.PendingSystem("XYZ12")),
        new("system approve", ["system", "approve", "XYZ12"], "PUT", "unapproved-systems/approve", ApiJson.Bulk("systemsApproved", 1)),
        new("system decline", ["system", "decline", "XYZ12"], "DELETE", "unapproved-systems", ApiJson.Bulk("systemsDeclined", 1)),
        new("system enable", ["system", "enable", "ABCDE"], "PUT", "systems/enable", ApiJson.Bulk("systemsUpdated", 1)),
        new("system enable --for", ["system", "enable", "K7P2Q", "--for", "24h", "--then", "revoke"], "PUT", "systems/K7P2Q/enable-until", ApiJson.System("K7P2Q")),
        new("system disable", ["system", "disable", "ABCDE"], "PUT", "systems/disable", ApiJson.Bulk("systemsUpdated", 1)),
        new("system revoke", ["system", "revoke", "ABCDE"], "DELETE", "systems", ApiJson.Bulk("systemsRevoked", 1)),
        new("key create", ["key", "create", "ci runners", "--ephemeral", "--tags", "ci,runner"], "POST", "enrolment-keys", ApiJson.Key(31, "ci runners")),
        new("key update", ["key", "update", "--id", "12", "--require-approval"], "PATCH", "enrolment-keys/12", ApiJson.Key(12, "build agents")),
        new("key enable", ["key", "enable", "--id", "12"], "PUT", "enrolment-keys/enable", ApiJson.Bulk("keysModified", 1)),
        new("key enable --for", ["key", "enable", "--id", "31", "--for", "14d", "--then", "delete"], "PUT", "enrolment-keys/31/enable-until", ApiJson.Key(31, "contractor laptops")),
        new("key disable", ["key", "disable", "--id", "12"], "PUT", "enrolment-keys/disable", ApiJson.Bulk("keysModified", 1)),
        new("key delete", ["key", "delete", "--id", "12"], "DELETE", "enrolment-keys", ApiJson.Bulk("keysDeleted", 1)),
        new("policy create", ["policy", "create", "web to db", "--senders", "web", "--receivers", "db", "--acl", "tcp:5432"], "POST", "policies", ApiJson.Policy(42, "web to db")),
        new("policy update", ["policy", "update", "--id", "42", "--set-senders", "web,api"], "PATCH", "policies/42", ApiJson.Policy(42, "web to db")),
        new("policy enable", ["policy", "enable", "--id", "42"], "PUT", "policies/enable", ApiJson.Bulk("policiesUpdated", 1)),
        new("policy enable --for", ["policy", "enable", "--id", "23", "--for", "8h"], "PUT", "policies/23/enable-until", ApiJson.Policy(23, "contractors")),
        new("policy disable", ["policy", "disable", "--id", "42"], "PUT", "policies/disable", ApiJson.Bulk("policiesUpdated", 1)),
        new("policy delete", ["policy", "delete", "--id", "17"], "DELETE", "policies", ApiJson.Bulk("policiesDeleted", 1)),
        new("tag set", ["tag", "set", "web", "--notes", "front end"], "PATCH", "tags/web", ApiJson.Tag("web"), ReadPathSuffix: "tags/web", ReadResponse: ApiJson.Tag("web")),
        new("tag delete", ["tag", "delete", "web"], "DELETE", "tags", ApiJson.Bulk("tagsDeleted", 1)),
        new("dns create-zone", ["dns", "create-zone", "internal", "--auto-dns-tags", "web"], "POST", "dns/zones", ApiJson.Zone(4, "internal")),
        new("dns update-zone", ["dns", "update-zone", "--id", "4", "--set-auto-dns-tags", "web,api"], "PATCH", "dns/zones/4", ApiJson.Zone(4, "internal")),
        new("dns delete-zone", ["dns", "delete-zone", "--id", "4"], "DELETE", "dns/zones/4", ApiJson.Zone(4, "internal")),
        new("dns create-hostname", ["dns", "create-hostname", "db.internal", "--systems", "ABCDE,FGHIJ"], "POST", "dns/records", ApiJson.Record(7, "db"), ReadPathSuffix: "dns/zones", ReadResponse: ApiJson.Page(ApiJson.Zone(4, "internal"))),
        new("dns update-hostname", ["dns", "update-hostname", "--id", "7", "--set-systems", "ABCDE,FGHIJ,KLMNO"], "PATCH", "dns/records/7", ApiJson.Record(7, "db")),
        new("dns delete-hostname", ["dns", "delete-hostname", "--id", "7"], "DELETE", "dns/records", ApiJson.Bulk("dnsRecordsDeleted", 1)),
        new("trust create", ["trust", "create", "portal login", "--authority", "portal"], "POST", "trust-requirements", ApiJson.Trust(5, "portal login")),
        new("trust update", ["trust", "update", "--id", "5", "--description", "uk only"], "PATCH", "trust-requirements/5", ApiJson.Trust(5, "uk only")),
        new("trust delete", ["trust", "delete", "--id", "5"], "DELETE", "trust-requirements", ApiJson.Bulk("requirementsDeleted", 1)),
    ];

    /// <summary>
    /// The change's full URL path for the test organisation.
    /// </summary>
    public string Path => TestData.OrgPath(PathSuffix);

    /// <summary>
    /// The full URL paths the change may be sent to: Path, and the other spelling where there is one.
    /// </summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            if (OtherPathSuffix is null)
            {
                return [Path];
            }

            return [Path, TestData.OrgPath(OtherPathSuffix)];
        }
    }

    // NUnit names each test case after its arguments' ToString.
    public override string ToString() => Name;

    /// <summary>
    /// Answers the change, and any read the command makes first, so the command can succeed.
    /// </summary>
    internal void Arrange(CliRun run)
    {
        foreach (var path in Paths)
        {
            run.Stub(Method, path, 200, Response);
        }

        if (ReadPathSuffix is not null)
        {
            run.Stub("GET", TestData.OrgPath(ReadPathSuffix), 200, ReadResponse);
        }
    }

    /// <summary>
    /// The requests the fake API received that make the change, in order.
    /// </summary>
    internal RecordedRequest[] ChangeRequests(CliRun run)
    {
        // A lambda in a struct cannot use the struct's own members, so they are copied first.
        var method = Method;
        var paths = Paths;

        return run.Requests.Where(request => request.Method == method && paths.Contains(request.Path)).ToArray();
    }
}
