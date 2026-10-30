namespace Enclave.Cli.Tests.Support;

// The GET /account/orgs answer for a token that sees both test organisations: Acme
// (TestData.OrgId), then Globex (TestData.OtherOrgId).
// With two organisations, none is chosen by default (spec/cli-surface.md "Context"), so a test
// that uses this answer shows which one the CLI chose.
internal static class BothOrgs
{
    public static string Body { get; } = ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName));

    public static void Stub(CliRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        run.Stub("GET", "/account/orgs", json: Body);
    }
}
