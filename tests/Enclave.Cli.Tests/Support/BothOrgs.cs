namespace Enclave.Cli.Tests.Support;

/// <summary>
/// The GET /account/orgs answer for a token that sees both test organisations: Acme
/// (TestData.OrgId), then Globex (TestData.OtherOrgId).
/// </summary>
// With two organisations, a command that has none chosen exits 2 no_org, and a name chooses
// between them (spec/cli-surface.md "Context"), so a test that uses this answer shows which
// organisation the CLI chose.
internal static class BothOrgs
{
    /// <summary>
    /// The body, a QueryAccountOrgsResponseModel as ApiJson.Orgs writes it.
    /// </summary>
    public static string Body { get; } = ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName));

    /// <summary>
    /// Answers GET /account/orgs on the run's fake API with <see cref="Body"/>.
    /// </summary>
    public static void Stub(CliRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        run.Stub("GET", "/account/orgs", json: Body);
    }
}
