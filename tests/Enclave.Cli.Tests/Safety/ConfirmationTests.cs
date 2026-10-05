using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// Commands on the proposal's --yes list exit 6 without --yes and send nothing; other commands run
/// without it; --dry-run is checked first (proposal, "Confirmation").
/// </summary>
public class ConfirmationTests
{
    private const string Until = "2030-01-01T00:00:00Z";

    private static readonly Guid AccountId = new("5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25");

    private static readonly string[] LookupOnly = ["GET /account/orgs"];

    // The CLI never prompts (AGENTS.md, CLI contract), so a command that cannot be undone, removes
    // a person's access, admits a machine, invites an outside address or schedules a deletion stops
    // with exit 6 and an error naming --yes. The caller learns what to add without a second guess.
    [TestCaseSource(nameof(ConfirmationListCases))]
    public async Task Command_on_the_confirmation_list_exits_6_without_yes_and_sends_nothing(string[] args, string method, string path, string? response)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, 200, response);

        var result = await run.RunAsync(args);

        Assert.That(result.ExitCode, Is.EqualTo(6), result.ToString());
        var error = result.Error;
        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(error, "code").GetString(), Is.EqualTo("confirmation_required"));
            Assert.That(error.GetRawText(), Does.Contain("--yes"));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    [TestCaseSource(nameof(ConfirmationListCases))]
    public async Task Command_on_the_confirmation_list_sends_its_request_with_yes(string[] args, string method, string path, string? response)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, 200, response);

        var result = await run.RunAsync([.. args, "--yes"]);

        Assert.That(result.ExitCode, Is.Zero, result.ToString());
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
        });
    }

    // --dry-run is checked before --yes: a dry run sends no change, so it needs no confirmation, and
    // an agent can preview a destructive command before a human approves the --yes run. The dry run
    // looks up the organisation's name for its output, which is its only request.
    [TestCaseSource(nameof(ConfirmationListCases))]
    public async Task Command_on_the_confirmation_list_with_dry_run_exits_0_without_yes_and_sends_nothing(string[] args, string method, string path, string? response)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, 200, response);
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        var result = await run.RunAsync([.. args, "--dry-run"]);

        Assert.That(result.ExitCode, Is.Zero, result.ToString());
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(JsonAssert.Property(JsonAssert.Property(output, "request"), "method").GetString(), Is.EqualTo(method));
            Assert.That(run.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(LookupOnly));
        });
    }

    // The --yes list stays short so that a permission rule asking a human to approve any command
    // containing --yes stays useful. Commands that can be reversed, or that only remove an invite
    // or schedule a disable, run without it.
    [TestCaseSource(nameof(OffListCases))]
    public async Task Command_off_the_confirmation_list_runs_without_yes(string[] args, string method, string path, string? response)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, 200, response);

        var result = await run.RunAsync(args);

        Assert.That(result.ExitCode, Is.Zero, result.ToString());
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path));
        });
    }

    // --yes exists on every command that changes something, so a script can pass it to each change
    // it makes without knowing which ones need it.
    [Test]
    public async Task Yes_is_accepted_on_a_change_command_off_the_confirmation_list()
    {
        using var run = CliRun.Start();
        run.Stub("PUT", TestData.OrgPath("systems/disable"), 200, ApiJson.Bulk("systemsUpdated", 1));

        var result = await run.RunAsync("system", "disable", "ABCDE", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.ToString());
        Assert.That(run.SingleRequest().Path, Is.EqualTo(TestData.OrgPath("systems/disable")));
    }

    // --yes exists only on commands that change something; on a read-only command it is an unknown
    // option, a parse error. The second run shows the command itself works.
    [Test]
    public async Task Yes_is_an_unknown_option_on_a_read_only_command()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), 200, ApiJson.Page(ApiJson.System("ABCDE")));

        var rejected = await run.RunAsync("system", "list", "--yes");

        Assert.That(rejected.ExitCode, Is.EqualTo(2), rejected.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(rejected.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(run.Requests, Is.Empty);
        });

        var accepted = await run.RunAsync("system", "list");

        Assert.That(accepted.ExitCode, Is.Zero, accepted.ToString());
        Assert.That(run.SingleRequest().Path, Is.EqualTo(TestData.OrgPath("systems")));
    }

    // The organisation-scoped commands on the proposal's --yes list, each without --yes. Enum values
    // match ignoring case (proposal, "Options on every command"), so "delete" schedules a deletion as
    // "Delete" does.
    private static IEnumerable<TestCaseData> ConfirmationListCases()
    {
        yield return Case("key delete", ["key", "delete", "12"], "DELETE", "enrolment-keys", ApiJson.Bulk("keysDeleted", 1));
        yield return Case("policy delete", ["policy", "delete", "3"], "DELETE", "policies", ApiJson.Bulk("policiesDeleted", 1));
        yield return Case("tag delete", ["tag", "delete", "web"], "DELETE", "tags", ApiJson.Bulk("tagsDeleted", 1));
        yield return Case("dns zone delete", ["dns", "zone", "delete", "4"], "DELETE", "dns/zones/4", ApiJson.Zone(4, "example"));
        yield return Case("dns record delete", ["dns", "record", "delete", "7"], "DELETE", "dns/records", ApiJson.Bulk("dnsRecordsDeleted", 1));
        yield return Case("trust delete", ["trust", "delete", "5"], "DELETE", "trust-requirements", ApiJson.Bulk("requirementsDeleted", 1));
        yield return Case("system revoke", ["system", "revoke", "ABCDE"], "DELETE", "systems", ApiJson.Bulk("systemsRevoked", 1));
        yield return Case("pending decline", ["pending", "decline", "ABCDE"], "DELETE", "unapproved-systems", ApiJson.Bulk("systemsDeclined", 1));
        yield return Case("pending approve", ["pending", "approve", "ABCDE"], "PUT", "unapproved-systems/approve", ApiJson.Bulk("systemsApproved", 1));
        yield return Case("org user remove", ["org", "user", "remove", AccountId.ToString()], "DELETE", $"users/{AccountId}", null);
        yield return Case("org invite send", ["org", "invite", "send", "new@example.com"], "POST", "invites", null);
        yield return Case("system enable --until --expiry-action Delete", ["system", "enable", "ABCDE", "--until", Until, "--expiry-action", "Delete"], "PUT", "systems/ABCDE/enable-until", ApiJson.System("ABCDE"));
        yield return Case("key enable --until --expiry-action Delete", ["key", "enable", "12", "--until", Until, "--expiry-action", "Delete"], "PUT", "enrolment-keys/12/enable-until", ApiJson.Key(12));
        yield return Case("policy enable --until --expiry-action Delete", ["policy", "enable", "3", "--until", Until, "--expiry-action", "Delete"], "PUT", "policies/3/enable-until", ApiJson.Policy(3));
        yield return Case("system enable --until --expiry-action delete", ["system", "enable", "ABCDE", "--until", Until, "--expiry-action", "delete"], "PUT", "systems/ABCDE/enable-until", ApiJson.System("ABCDE"));
    }

    private static IEnumerable<TestCaseData> OffListCases()
    {
        yield return Case("system enable", ["system", "enable", "ABCDE"], "PUT", "systems/enable", ApiJson.Bulk("systemsUpdated", 1));
        yield return Case("system disable", ["system", "disable", "ABCDE"], "PUT", "systems/disable", ApiJson.Bulk("systemsUpdated", 1));
        yield return Case("key disable", ["key", "disable", "12"], "PUT", "enrolment-keys/disable", ApiJson.Bulk("keysModified", 1));
        yield return Case("policy disable", ["policy", "disable", "3"], "PUT", "policies/disable", ApiJson.Bulk("policiesUpdated", 1));
        yield return Case("system update", ["system", "update", "ABCDE", "--description", "x"], "PATCH", "systems/ABCDE", ApiJson.System("ABCDE", "x"));
        yield return Case("tag create", ["tag", "create", "web"], "POST", "tags", ApiJson.Tag("web"));
        yield return Case("tag update --name", ["tag", "update", "web", "--name", "www"], "PATCH", "tags/web", ApiJson.Tag("www"));
        yield return Case("org update", ["org", "update", "--name", "Initech"], "PATCH", TestData.OrgPath(), ApiJson.OrgProperties("Initech"));
        yield return Case("org invite cancel", ["org", "invite", "cancel", "old@example.com"], "DELETE", "invites", null);
        yield return Case("system enable --until --expiry-action Disable", ["system", "enable", "ABCDE", "--until", Until, "--expiry-action", "Disable"], "PUT", "systems/ABCDE/enable-until", ApiJson.System("ABCDE"));
        yield return Case("key enable --until --expiry-action Disable", ["key", "enable", "12", "--until", Until, "--expiry-action", "Disable"], "PUT", "enrolment-keys/12/enable-until", ApiJson.Key(12));
        yield return Case("policy enable --until --expiry-action Disable", ["policy", "enable", "3", "--until", Until, "--expiry-action", "Disable"], "PUT", "policies/3/enable-until", ApiJson.Policy(3));
    }

    // pathOrSuffix is a full path when it starts with "/", otherwise a suffix of the test
    // organisation's path.
    private static TestCaseData Case(string name, string[] args, string method, string pathOrSuffix, string? response)
    {
        var path = pathOrSuffix.StartsWith('/') ? pathOrSuffix : TestData.OrgPath(pathOrSuffix);

        return new TestCaseData(args, method, path, response).SetArgDisplayNames(name);
    }
}
