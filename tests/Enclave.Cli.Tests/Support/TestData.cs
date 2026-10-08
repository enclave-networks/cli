using System.Globalization;
using static System.FormattableString;

namespace Enclave.Cli.Tests.Support;

// The class is partial so that tests for one area can add their data in their own file,
// Support/TestData.<Area>.cs, without editing a file other tests share. StyleCop SA1601 requires a
// <summary> on every part.

/// <summary>
/// Fixed identities shared by the tests and the fake API responses.
/// </summary>
internal static partial class TestData
{
    public const string Token = "test-token-7f3a9c";

    public const string OrgName = "Acme";

    public const string OtherOrgName = "Globex";

    public static readonly Guid OrgId = new("6a0f3c2e-58d4-4b7a-9c61-2f8e0d4b1a37");

    public static readonly Guid OtherOrgId = new("c3b9e7d1-0a4f-4e26-8d15-7b2c9f6e3a80");

    public static readonly Guid PartnerId = new("9e2d4f61-3c8b-4a05-b7e9-1d6a0c5f2b48");

    // Enclave.Sdk.Api builds the path from OrganisationGuid.ToString(), which writes the GUID as 32
    // hex digits without hyphens (OrganisationScopedClient constructor, Enclave.Sdk.Api 1.1.0, with
    // Enclave.Sdk.Api.Data 304.48.0).

    /// <summary>
    /// The API path of the test organisation, with an optional suffix.
    /// </summary>
    public static string OrgPath(string suffix = "") => OrgPathOf(OrgId, suffix);

    /// <summary>
    /// The API path of the other test organisation, <see cref="OtherOrgId"/>, with an optional suffix.
    /// </summary>
    public static string OtherOrgPath(string suffix = "") => OrgPathOf(OtherOrgId, suffix);

    /// <summary>
    /// <paramref name="count"/> distinct IDs of one kind of item (a <see cref="CliList.Kinds"/> value
    /// other than log), each valid by the proposal's "ID checks": letters and digits for systems,
    /// integers from 1 for keys, policies, zones, hostnames and trust requirements, tag names for
    /// tags, GUIDs for organisations, users, customers and admins, and email addresses for invites.
    /// </summary>
    public static string[] Ids(string kind, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        return Enumerable.Range(1, count).Select(number => kind switch
        {
            "system" or "pending-system" => Invariant($"S{number:D5}"),
            "key" or "policy" or "zone" or "hostname" or "trust" => number.ToString(CultureInfo.InvariantCulture),
            "tag" => Invariant($"tag-{number}"),
            "org" or "user" or "customer" or "admin" => new Guid(number, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1).ToString(),
            "invite" => Invariant($"invite-{number}@acme.example"),
            _ => throw new ArgumentException($"No IDs for kind \"{kind}\"; the kinds are {string.Join(", ", CliList.Kinds)}, and log entries have no ID.", nameof(kind)),
        }).ToArray();
    }

    private static string OrgPathOf(Guid orgId, string suffix) =>
        suffix.Length == 0 ? $"/org/{orgId:N}" : $"/org/{orgId:N}/{suffix}";
}
