namespace Enclave.Cli.Tests.Support;

// A value type, so the public test methods taking one need no null check (CA1062).

// A command that takes several items: the bulk call it makes and the kind of list it reads after "-".
// Name: The command words, such as "system disable".
// Method: The HTTP method of the bulk call.
// PathSuffix: The bulk call's path below /org/{orgId}/.
// BodyField: The request body property holding the IDs.
// ResultField: The response body property holding the count of items affected.
// Kind: The list kind the command reads after "-", whose IDs are the command's IDs.
// OtherKind: A list kind the command refuses, whose IDs are also valid IDs for the command.
// MalformedId: An ID the command's ID check refuses.
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
    // Routes and fields are Enclave.Sdk.Api's clients' and the portal's bulk result models'. Key
    // delete reads keysDeleted where key enable and disable read keysModified (portal
    // EnrolmentKeysController.DeleteBulkEnrolmentKeys, BulkEnrolmentKeyDeleteResult).
    //
    // OtherKind's IDs are ones the command would also accept, so only the kind check can refuse the
    // list (spec/cli-surface.md "Several IDs").
    //
    // Each malformed ID breaks the format of the "ID checks" table; some would rewrite a URL path.
    // Integer IDs are 32-bit (portal IdBackingType.Int), so 99999999999 is refused.
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

    // The bulk call's full URL path for the test organisation.
    public string Path => TestData.OrgPath(PathSuffix);

    // These commands take names as arguments, so their IDs go after --id (spec/cli-surface.md
    // "Shape and naming").
    public bool TakesIdOption => Kind is "key" or "policy" or "hostname" or "trust";

    private string[] Words => Name.Split(' ');

    public string[] Args(params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (TakesIdOption)
        {
            return [.. Words, "--id", string.Join(",", ids)];
        }

        return [.. Words, .. ids];
    }

    public string[] StdinArgs() => [.. Words, "-"];

    // Valid, distinct IDs of the command's kind.
    public string[] Ids(int count) => TestData.Ids(Kind, count);

    // A list of the command's kind, as a list command prints it, with one item per ID.
    public string List(params string[] ids) => CliList.WithIds(Kind, ids);

    // NUnit names each test case after its arguments' ToString.
    public override string ToString() => Name;

    // Answers successive bulk calls with these counts; the last count answers every call after it.
    internal void StubBulk(CliRun run, params int[] affected) => run.StubBulk(Method, Path, ResultField, affected);

    internal IReadOnlyList<RecordedRequest> BulkCalls(CliRun run) => run.RequestsTo(Method, Path);

    // The IDs of each bulk call joined with commas, and the calls joined with "|".
    internal string IdsSent(CliRun run)
    {
        // A lambda in a struct cannot use the struct's own members, so the field is copied first.
        var field = BodyField;

        return string.Join("|", BulkCalls(run).Select(request => string.Join(",", request.BodyIds(field))));
    }

    // The IDs in calls of 200, in the form IdsSent gives.
    // spec/cli-surface.md "Several IDs"; portal HardLimits.MaxBulkIds.
    internal static string Batches(string[] ids) =>
        string.Join("|", ids.Chunk(200).Select(batch => string.Join(",", batch)));
}
