using Enclave.Cli.Tests.Support;

namespace Enclave.Cli.Tests.Safety;

// A value type, so the public test methods taking one need no null check (CA1062).

/// <summary>
/// A command that takes several items: the bulk call it makes and the kind of list it reads after "-".
/// </summary>
/// <param name="Name">The command words, such as "system disable".</param>
/// <param name="Method">The HTTP method of the bulk call.</param>
/// <param name="PathSuffix">The bulk call's path below /org/{orgId}/.</param>
/// <param name="BodyField">The request body property holding the IDs.</param>
/// <param name="ResultField">The response body property holding the count of items affected.</param>
/// <param name="Kind">The list kind the command reads after "-", whose IDs are the command's IDs.</param>
/// <param name="OtherKind">A list kind the command refuses, whose IDs are also valid IDs for the command.</param>
/// <param name="MalformedId">An ID the command's ID check refuses.</param>
public readonly record struct BulkCommand(
    string Name,
    string Method,
    string PathSuffix,
    string BodyField,
    string ResultField,
    string Kind,
    string OtherKind,
    string MalformedId)
{
    // Routes, body fields and result fields are those of Enclave.Sdk.Api 1.1.0 (SystemsClient,
    // UnapprovedSystemsClient, EnrolmentKeysClient, PoliciesClient, TagsClient, DnsClient,
    // TrustRequirementsClient) and of the portal's bulk result models. Key delete is
    // EnrolmentKeysClient.BulkDeleteAsync, which reads keysDeleted, where the bulk enable and
    // disable read keysModified (portal EnrolmentKeysController.cs:295, DeleteBulkEnrolmentKeys,
    // BulkKeyActionModel and BulkEnrolmentKeyDeleteResult).
    //
    // approve and decline act on systems waiting for approval, so they read the output of
    // system list --pending and refuse that of system list; the other system verbs take the reverse
    // (proposed-cli-surface.md "Several IDs"). Every other kind here has IDs the command would also
    // accept: system IDs share one format, the integer IDs are all integers, and a key ID is a valid
    // tag name. Only the kind check can then refuse the list.
    //
    // Each malformed ID breaks the format the "ID checks" table gives for its kind; some would
    // rewrite a URL path if they reached one. Integer IDs are 32-bit ("Details"; portal
    // Enclave.Configuration.Data/Identifiers, IdBackingType.Int), so 99999999999 is refused.
    public static IReadOnlyList<BulkCommand> All { get; } =
    [
        new("system approve", "PUT", "unapproved-systems/approve", "systemIds", "systemsApproved", "pending-system", "system", "AB CD"),
        new("system decline", "DELETE", "unapproved-systems", "systemIds", "systemsDeclined", "pending-system", "system", "../systems/ABCDE"),
        new("system enable", "PUT", "systems/enable", "systemIds", "systemsUpdated", "system", "pending-system", "ABC-DE"),
        new("system disable", "PUT", "systems/disable", "systemIds", "systemsUpdated", "system", "pending-system", "AB/CD"),
        new("system revoke", "DELETE", "systems", "systemIds", "systemsRevoked", "system", "pending-system", "../systems/ABCDE"),
        new("key enable", "PUT", "enrolment-keys/enable", "keyIds", "keysModified", "key", "policy", "12a"),
        new("key disable", "PUT", "enrolment-keys/disable", "keyIds", "keysModified", "key", "policy", "1.5"),
        new("key delete", "DELETE", "enrolment-keys", "keyIds", "keysDeleted", "key", "policy", "../12"),
        new("policy enable", "PUT", "policies/enable", "policyIds", "policiesUpdated", "policy", "key", "0x1F"),
        new("policy disable", "PUT", "policies/disable", "policyIds", "policiesUpdated", "policy", "key", "three"),
        new("policy delete", "DELETE", "policies", "policyIds", "policiesDeleted", "policy", "key", "99999999999"),
        new("tag delete", "DELETE", "tags", "tags", "tagsDeleted", "tag", "key", "../systems/ABCDE"),
        new("dns delete-hostname", "DELETE", "dns/records", "recordIds", "dnsRecordsDeleted", "hostname", "zone", "7/../8"),
        new("trust delete", "DELETE", "trust-requirements", "requirementIds", "requirementsDeleted", "trust", "policy", "five"),
    ];

    /// <summary>
    /// The bulk call's full URL path for the test organisation.
    /// </summary>
    public string Path => TestData.OrgPath(PathSuffix);

    // Systems take IDs as arguments, since they have no names, and tags are given by name only. The
    // arguments of key, policy, hostname and trust requirement commands are names, so their IDs go
    // after --id (proposed-cli-surface.md "Shape and naming").

    /// <summary>
    /// Whether the command takes its IDs as --id a,b,c.
    /// </summary>
    public bool TakesIdOption => Kind is "key" or "policy" or "hostname" or "trust";

    private string[] Words => Name.Split(' ');

    /// <summary>
    /// The command line acting on these IDs.
    /// </summary>
    public string[] Args(params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (TakesIdOption)
        {
            return [.. Words, "--id", string.Join(",", ids)];
        }

        return [.. Words, .. ids];
    }

    /// <summary>
    /// The command line that reads a list from stdin.
    /// </summary>
    public string[] StdinArgs() => [.. Words, "-"];

    /// <summary>
    /// Valid, distinct IDs of the command's kind.
    /// </summary>
    public string[] Ids(int count) => TestData.Ids(Kind, count);

    /// <summary>
    /// A list of the command's kind holding one item for each ID, as a list command prints it.
    /// </summary>
    public string List(params string[] ids) => CliList.WithIds(Kind, ids);

    // NUnit names each test case after its arguments' ToString.
    public override string ToString() => Name;

    /// <summary>
    /// Answers successive bulk calls with these counts; the last count answers every call after it.
    /// </summary>
    internal void StubBulk(CliRun run, params int[] affected) => run.StubBulk(Method, Path, ResultField, affected);

    /// <summary>
    /// The bulk calls the fake API received, in order.
    /// </summary>
    internal IReadOnlyList<RecordedRequest> BulkCalls(CliRun run) => run.RequestsTo(Method, Path);

    /// <summary>
    /// The IDs of each bulk call, joined with commas, and the calls joined with "|", in the order
    /// the fake API received them.
    /// </summary>
    internal string IdsSent(CliRun run)
    {
        // A lambda in a struct cannot use the struct's own members, so the field is copied first.
        var field = BodyField;

        return string.Join("|", BulkCalls(run).Select(request => string.Join(",", request.BodyIds(field))));
    }
}
