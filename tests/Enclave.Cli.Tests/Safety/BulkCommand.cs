using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

// A value type, so the public test methods taking one need no null check (CA1062).
/// <summary>
/// An organisation-scoped command that takes several IDs, and the bulk call it makes.
/// </summary>
/// <param name="Name">The command words, such as "system disable".</param>
/// <param name="Method">The HTTP method of the bulk call.</param>
/// <param name="PathSuffix">The bulk call's path below /org/{orgId}/.</param>
/// <param name="BodyField">The request body property holding the IDs.</param>
/// <param name="ResultField">The response body property holding the count of IDs affected.</param>
/// <param name="Kind">The kind of ID the command takes.</param>
/// <param name="NeedsYes">Whether the command is on the proposal's --yes list.</param>
/// <param name="MalformedId">An ID the command's ID check rejects.</param>
public readonly record struct BulkCommand(
    string Name,
    string Method,
    string PathSuffix,
    string BodyField,
    string ResultField,
    IdKind Kind,
    bool NeedsYes,
    string MalformedId)
{
    // Routes, body fields and result fields are those of Enclave.Sdk.Api 1.0.4 (SystemsClient,
    // UnapprovedSystemsClient, EnrolmentKeysClient, PoliciesClient, TagsClient, DnsClient,
    // TrustRequirementsClient) and of the portal's bulk result models. Enclave.Sdk.Api lacks key
    // delete; its route and fields are the API's (portal EnrolmentKeysController.cs:295,
    // DeleteBulkEnrolmentKeys, BulkKeyActionModel and BulkEnrolmentKeyDeleteResult).
    //
    // Each malformed ID breaks the format the proposal's "ID checks" table gives for its kind. Some
    // would rewrite a URL path if they reached one (../), the others are plain format errors.
    // 99999999999 is outside the range of the integer IDs, whose backing type is int (portal
    // Enclave.Configuration.Data/Identifiers, IdBackingType.Int).
    public static IReadOnlyList<BulkCommand> All { get; } =
    [
        new("system enable", "PUT", "systems/enable", "systemIds", "systemsUpdated", IdKind.System, false, "ABC-DE"),
        new("system disable", "PUT", "systems/disable", "systemIds", "systemsUpdated", IdKind.System, false, "ABC/DE"),
        new("system revoke", "DELETE", "systems", "systemIds", "systemsRevoked", IdKind.System, true, "../systems/ABCDE"),
        new("pending approve", "PUT", "unapproved-systems/approve", "systemIds", "systemsApproved", IdKind.System, true, "AB CD"),
        new("pending decline", "DELETE", "unapproved-systems", "systemIds", "systemsDeclined", IdKind.System, true, "../systems/ABCDE"),
        new("key enable", "PUT", "enrolment-keys/enable", "keyIds", "keysModified", IdKind.Integer, false, "12a"),
        new("key disable", "PUT", "enrolment-keys/disable", "keyIds", "keysModified", IdKind.Integer, false, "1.5"),
        new("key delete", "DELETE", "enrolment-keys", "keyIds", "keysDeleted", IdKind.Integer, true, "../12"),
        new("policy enable", "PUT", "policies/enable", "policyIds", "policiesUpdated", IdKind.Integer, false, "0x1F"),
        new("policy disable", "PUT", "policies/disable", "policyIds", "policiesUpdated", IdKind.Integer, false, "three"),
        new("policy delete", "DELETE", "policies", "policyIds", "policiesDeleted", IdKind.Integer, true, "99999999999"),
        new("tag delete", "DELETE", "tags", "tags", "tagsDeleted", IdKind.Tag, true, "Web"),
        new("dns record delete", "DELETE", "dns/records", "recordIds", "dnsRecordsDeleted", IdKind.Integer, true, "7/../8"),
        new("trust delete", "DELETE", "trust-requirements", "requirementIds", "requirementsDeleted", IdKind.Integer, true, "five"),
    ];

    /// <summary>
    /// The bulk call's full URL path for the test organisation.
    /// </summary>
    public string Path => TestData.OrgPath(PathSuffix);

    /// <summary>
    /// The command line for these IDs, with --yes when the command needs it.
    /// </summary>
    public string[] Args(params string[] ids) => NeedsYes ? [.. ArgsWithoutYes(ids), "--yes"] : ArgsWithoutYes(ids);

    /// <summary>
    /// The command line for these IDs, without --yes.
    /// </summary>
    public string[] ArgsWithoutYes(params string[] ids) => [.. Name.Split(' '), .. ids];

    /// <summary>
    /// A valid ID of this command's kind; different numbers give different IDs.
    /// </summary>
    public string Id(int number) => Kind switch
    {
        IdKind.System => "S" + number.ToString("D4", CultureInfo.InvariantCulture),
        IdKind.Tag => "tag-" + number.ToString(CultureInfo.InvariantCulture),
        _ => number.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Valid, distinct IDs 1 to <paramref name="count"/> of this command's kind.
    /// </summary>
    public string[] Ids(int count) => Enumerable.Range(1, count).Select(Id).ToArray();

    /// <summary>
    /// The IDs as stdin lines, one per line.
    /// </summary>
    public static string Lines(IEnumerable<string> ids) => string.Join("\n", ids) + "\n";

    /// <summary>
    /// The IDs in a request body, as strings. System IDs and tags are JSON strings; the other IDs
    /// are typed integer IDs, which Enclave.Sdk.Api.Data 304.48.0 writes as JSON numbers.
    /// </summary>
    public string[] BodyIds(JsonElement body)
    {
        var field = BodyField;
        var array = JsonAssert.Property(body, field);

        if (Kind != IdKind.Integer)
        {
            return JsonAssert.Strings(array);
        }

        if (array.ValueKind != JsonValueKind.Array)
        {
            throw new AssertionException($"Expected \"{field}\" to be a JSON array of numbers, found {array.ValueKind}: {body}");
        }

        return array.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.Number
                ? item.GetInt64().ToString(CultureInfo.InvariantCulture)
                : throw new AssertionException($"Expected \"{field}\" to hold JSON numbers, found an item of kind {item.ValueKind}: {body}"))
            .ToArray();
    }

    // NUnit names each test case after its arguments' ToString.
    public override string ToString() => Name;

    /// <summary>
    /// Answers the bulk call with a result counting <paramref name="affected"/> IDs.
    /// </summary>
    internal void StubBulk(CliRun run, int affected) => run.Stub(Method, Path, 200, ApiJson.Bulk(ResultField, affected));
}

/// <summary>
/// The kinds of ID a bulk command takes.
/// </summary>
public enum IdKind
{
    /// <summary>
    /// A system ID: letters and digits.
    /// </summary>
    System,

    /// <summary>
    /// A tag name.
    /// </summary>
    Tag,

    /// <summary>
    /// An integer ID: enrolment key, policy, DNS record or trust requirement.
    /// </summary>
    Integer,
}
