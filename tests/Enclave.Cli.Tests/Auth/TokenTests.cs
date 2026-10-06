using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using WireMock;

namespace Enclave.Cli.Tests.Auth;

[Category(TestCategory.Pending)]
public class TokenTests
{
    private const string FileToken = "file-token-2b8e61";

    private const string SystemId = "ABC12";

    private const string Email = "new.user@acme.example";

    private const string InviteId = "inv1";

    private static readonly Guid AccountId = new("5f1e2d3c-4b5a-4968-8776-a5b4c3d2e1f0");

    private static readonly Guid CustomerId = new("0d9c8b7a-6f5e-4d3c-b2a1-9f8e7d6c5b4a");

    // Commands that change something through the API. Each is run with --verbose, and again with
    // --verbose --dry-run. The names are looked up in Arrange.
    private static readonly string[] ApiChanges =
    [
        "org update", "org user remove", "org invite send", "org invite cancel",
        "system update", "system enable", "system disable", "system revoke",
        "pending update", "pending approve", "pending decline",
        "key create", "key update", "key enable", "key disable", "key delete",
        "policy create", "policy update", "policy enable", "policy disable", "policy delete",
        "tag create", "tag update", "tag delete",
        "dns zone create", "dns zone update", "dns zone delete",
        "dns record create", "dns record update", "dns record delete",
        "trust create", "trust update", "trust delete",
        "partner user remove", "partner invite send", "partner invite cancel",
        "partner customer convert", "partner customer admin add", "partner customer admin remove",
        "partner customer invite send", "partner customer invite cancel",
        "partner customer auto-sync enable", "partner customer auto-sync disable",
    ];

    // Commands that change local state only. They have no --dry-run, so each runs with --verbose.
    private static readonly string[] LocalChanges = ["login", "login --token-stdin", "logout", "org use"];

    [Test]
    public async Task ENCLAVE_TOKEN_takes_precedence_over_credentials_json()
    {
        using var run = CliRun.Start();
        run.SaveCredentials(FileToken);
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
        });
    }

    // A CI job whose secret is missing often gets ENCLAVE_TOKEN set to an empty string. An empty
    // value names no token, so the saved file supplies it, the same as when the variable is unset.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public async Task Credentials_json_supplies_the_token_when_ENCLAVE_TOKEN_is_unset_or_empty(string? environmentToken)
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_TOKEN"] = environmentToken;
        run.SaveCredentials(FileToken);
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());

        var result = await run.RunAsync("system", "list");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {FileToken}"));
        });
    }

    // credentials.json's baseUrl is the API address (proposal "Login, logout and status": the CLI
    // reads the file and passes EnclaveClientOptions to Enclave.Sdk.Api), and ENCLAVE_TOKEN keeps it.
    // A second fake API at another address shows where the request went; the default API receives
    // nothing.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Credentials_json_base_url_is_the_api_address_whichever_source_supplies_the_token(bool tokenFromEnvironment)
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", TestData.OrgPath("systems"), 200, ApiJson.Page());
        run.SaveCredentials(FileToken, otherUrl);

        if (!tokenFromEnvironment)
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
        }

        var result = await run.RunAsync("system", "list");

        var received = otherApi.LogEntries.Select(entry => entry.RequestMessage).OfType<IRequestMessage>().ToArray();
        var expectedToken = tokenFromEnvironment ? TestData.Token : FileToken;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, result.ToString());
            Assert.That(run.Requests, Is.Empty, "The request went to the default API address.");
            Assert.That(received.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[] { $"GET {TestData.OrgPath("systems")}" }));
            Assert.That(received.Select(request => Authorization(request)), Is.EqualTo(new[] { $"Bearer {expectedToken}" }));
        });
    }

    // With no token there is nothing to authenticate with, so the CLI reports it before any call.
    // The commands cover one that acts within an organisation and two that do not.
    [TestCase("system", "list")]
    [TestCase("org", "list")]
    [TestCase("status")]
    public async Task A_command_exits_3_with_token_missing_and_sends_nothing_when_there_is_no_token(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());

        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(3), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("token_missing"));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // There is no --token option (proposal "Options on every command"): a token given as an
    // argument ends up in shell history, process listings, CI logs and agent transcripts, and
    // personal access tokens never expire. A parse error exits 2 before anything is sent or saved.
    [TestCase("login", "--token", TestData.Token)]
    [TestCase("status", "--token", TestData.Token)]
    [TestCase("system", "list", "--token", TestData.Token)]
    public async Task Token_is_not_an_option(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());

        var result = await run.RunAsync(args);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), result.ToString());
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.False);
        });
    }

    // No command prints the token, under --verbose or --dry-run either (proposal "Login, logout and
    // status"); a printed token lands in CI logs and agent transcripts and never expires. Each
    // command gets valid arguments and --yes where it needs one, so it runs to the point where a
    // token could be printed: the API commands send their request with the token, a dry run
    // prints the request it would send, and a partner command reports not_implemented after
    // resolving its context.
    [TestCaseSource(nameof(ChangingCommandRuns))]
    public async Task The_token_appears_in_neither_stdout_nor_stderr_of_a_command_that_changes_something(string command, bool dryRun)
    {
        using var run = CliRun.Start();
        var change = Arrange(run, command);
        string[] args = dryRun ? [.. change.Args, "--verbose", "--dry-run"] : [.. change.Args, "--verbose"];

        var result = await run.RunAsync(args);

        var requests = run.Requests;

        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Does.Not.Contain(TestData.Token));
            Assert.That(result.Stderr, Does.Not.Contain(TestData.Token));

            if (change.Partner)
            {
                Assert.That(result.ExitCode, Is.EqualTo(1), result.ToString());
                Assert.That(result.Stderr, Does.Contain("not_implemented"));
                Assert.That(requests, Is.Empty);
            }
            else if (dryRun || !change.SendsRequest)
            {
                Assert.That(result.ExitCode, Is.Zero, result.ToString());
                Assert.That(requests, Is.Empty);
            }
            else
            {
                Assert.That(result.ExitCode, Is.Zero, result.ToString());
                Assert.That(requests, Is.Not.Empty);
                Assert.That(requests.Select(request => request.Authorization), Is.All.EqualTo($"Bearer {TestData.Token}"));
            }
        });
    }

    private static IEnumerable<TestCaseData> ChangingCommandRuns()
    {
        foreach (var command in ApiChanges)
        {
            yield return new TestCaseData(command, false).SetArgDisplayNames(command, "--verbose");
            yield return new TestCaseData(command, true).SetArgDisplayNames(command, "--verbose --dry-run");
        }

        foreach (var command in LocalChanges)
        {
            yield return new TestCaseData(command, false).SetArgDisplayNames(command, "--verbose");
        }
    }

    // Stubs the responses a command needs to succeed and returns its arguments. ENCLAVE_TOKEN and
    // ENCLAVE_ORG keep the harness defaults unless a case says otherwise.
    private static Change Arrange(CliRun run, string command)
    {
        var account = AccountId.ToString();
        var customer = CustomerId.ToString();

        switch (command)
        {
            case "org update":
                run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));
                return Api("org", "update", "--name", TestData.OrgName);
            case "org user remove":
                // RemoveUserAsync takes the account ID as a string (Enclave.Sdk.Api 1.0.4), so the
                // path carries whichever GUID form the CLI passes; this test is about the token, so
                // both forms are answered.
                run.Stub("DELETE", TestData.OrgPath($"users/{AccountId:D}"));
                run.Stub("DELETE", TestData.OrgPath($"users/{AccountId:N}"));
                return Api("org", "user", "remove", account, "--yes");
            case "org invite send":
                run.Stub("POST", TestData.OrgPath("invites"));
                return Api("org", "invite", "send", Email, "--yes");
            case "org invite cancel":
                run.Stub("DELETE", TestData.OrgPath("invites"));
                return Api("org", "invite", "cancel", Email);
            case "system update":
                run.Stub("PATCH", TestData.OrgPath($"systems/{SystemId}"), json: ApiJson.System(SystemId));
                return Api("system", "update", SystemId, "--description", "web");
            case "system enable":
                run.Stub("PUT", TestData.OrgPath("systems/enable"), json: ApiJson.Bulk("systemsUpdated", 1));
                return Api("system", "enable", SystemId);
            case "system disable":
                run.Stub("PUT", TestData.OrgPath("systems/disable"), json: ApiJson.Bulk("systemsUpdated", 1));
                return Api("system", "disable", SystemId);
            case "system revoke":
                run.Stub("DELETE", TestData.OrgPath("systems"), json: ApiJson.Bulk("systemsRevoked", 1));
                return Api("system", "revoke", SystemId, "--yes");
            case "pending update":
                run.Stub("PATCH", TestData.OrgPath($"unapproved-systems/{SystemId}"), json: ApiJson.PendingSystem(SystemId));
                return Api("pending", "update", SystemId, "--description", "web");
            case "pending approve":
                run.Stub("PUT", TestData.OrgPath("unapproved-systems/approve"), json: ApiJson.Bulk("systemsApproved", 1));
                return Api("pending", "approve", SystemId, "--yes");
            case "pending decline":
                run.Stub("DELETE", TestData.OrgPath("unapproved-systems"), json: ApiJson.Bulk("systemsDeclined", 1));
                return Api("pending", "decline", SystemId, "--yes");
            case "key create":
                run.Stub("POST", TestData.OrgPath("enrolment-keys"), json: ApiJson.Key(1, "laptops"));
                return Api("key", "create", "--description", "laptops");
            case "key update":
                run.Stub("PATCH", TestData.OrgPath("enrolment-keys/1"), json: ApiJson.Key(1, "laptops"));
                return Api("key", "update", "1", "--description", "laptops");
            case "key enable":
                run.Stub("PUT", TestData.OrgPath("enrolment-keys/enable"), json: ApiJson.Bulk("keysModified", 1));
                return Api("key", "enable", "1");
            case "key disable":
                run.Stub("PUT", TestData.OrgPath("enrolment-keys/disable"), json: ApiJson.Bulk("keysModified", 1));
                return Api("key", "disable", "1");
            case "key delete":
                run.Stub("DELETE", TestData.OrgPath("enrolment-keys"), json: ApiJson.Bulk("keysDeleted", 1));
                return Api("key", "delete", "1", "--yes");
            case "policy create":
                run.Stub("POST", TestData.OrgPath("policies"), json: ApiJson.Policy(1, "web"));
                return Api("policy", "create", "--from-file", run.WriteFile("policy.json", """{"description":"web"}"""));
            case "policy update":
                run.Stub("PATCH", TestData.OrgPath("policies/1"), json: ApiJson.Policy(1, "web"));
                return Api("policy", "update", "1", "--description", "web");
            case "policy enable":
                run.Stub("PUT", TestData.OrgPath("policies/enable"), json: ApiJson.Bulk("policiesUpdated", 1));
                return Api("policy", "enable", "1");
            case "policy disable":
                run.Stub("PUT", TestData.OrgPath("policies/disable"), json: ApiJson.Bulk("policiesUpdated", 1));
                return Api("policy", "disable", "1");
            case "policy delete":
                run.Stub("DELETE", TestData.OrgPath("policies"), json: ApiJson.Bulk("policiesDeleted", 1));
                return Api("policy", "delete", "1", "--yes");
            case "tag create":
                run.Stub("POST", TestData.OrgPath("tags"), json: ApiJson.Tag("web"));
                return Api("tag", "create", "web");
            case "tag update":
                run.Stub("PATCH", TestData.OrgPath("tags/web"), json: ApiJson.Tag("web"));
                return Api("tag", "update", "web", "--notes", "front end");
            case "tag delete":
                run.Stub("DELETE", TestData.OrgPath("tags"), json: ApiJson.Bulk("tagsDeleted", 1));
                return Api("tag", "delete", "web", "--yes");
            case "dns zone create":
                run.Stub("POST", TestData.OrgPath("dns/zones"), json: ApiJson.Zone(1, "internal.example"));
                return Api("dns", "zone", "create", "internal.example");
            case "dns zone update":
                run.Stub("PATCH", TestData.OrgPath("dns/zones/1"), json: ApiJson.Zone(1, "internal.example"));
                return Api("dns", "zone", "update", "1", "--notes", "office");
            case "dns zone delete":
                run.Stub("DELETE", TestData.OrgPath("dns/zones/1"), json: ApiJson.Zone(1, "internal.example"));
                return Api("dns", "zone", "delete", "1", "--yes");
            case "dns record create":
                run.Stub("POST", TestData.OrgPath("dns/records"), json: ApiJson.Record(1, "www"));
                return Api("dns", "record", "create", "www", "--zone", "1");
            case "dns record update":
                run.Stub("PATCH", TestData.OrgPath("dns/records/1"), json: ApiJson.Record(1, "www"));
                return Api("dns", "record", "update", "1", "--notes", "office");
            case "dns record delete":
                run.Stub("DELETE", TestData.OrgPath("dns/records"), json: ApiJson.Bulk("dnsRecordsDeleted", 1));
                return Api("dns", "record", "delete", "1", "--yes");
            case "trust create":
                run.Stub("POST", TestData.OrgPath("trust-requirements"), json: ApiJson.Trust(1, "vpn"));
                var trust = run.WriteFile("trust.json", """{"description":"vpn","type":"PublicIp","settings":{"configuration":{},"conditions":[]}}""");
                return Api("trust", "create", "--from-file", trust);
            case "trust update":
                run.Stub("PATCH", TestData.OrgPath("trust-requirements/1"), json: ApiJson.Trust(1, "vpn"));
                return Api("trust", "update", "1", "--description", "vpn");
            case "trust delete":
                run.Stub("DELETE", TestData.OrgPath("trust-requirements"), json: ApiJson.Bulk("requirementsDeleted", 1));
                return Api("trust", "delete", "1", "--yes");
            case "partner user remove":
                return Partner("partner", "user", "remove", account, "--yes");
            case "partner invite send":
                return Partner("partner", "invite", "send", Email, "--yes");
            case "partner invite cancel":
                return Partner("partner", "invite", "cancel", InviteId);
            case "partner customer convert":
                return Partner("partner", "customer", "convert", customer, "--yes");
            case "partner customer admin add":
                return Partner("partner", "customer", "admin", "add", customer, account, "--yes");
            case "partner customer admin remove":
                return Partner("partner", "customer", "admin", "remove", customer, account, "--yes");
            case "partner customer invite send":
                return Partner("partner", "customer", "invite", "send", customer, Email, "--yes");
            case "partner customer invite cancel":
                return Partner("partner", "customer", "invite", "cancel", customer, InviteId);
            case "partner customer auto-sync enable":
                return Partner("partner", "customer", "auto-sync", "enable", customer);
            case "partner customer auto-sync disable":
                return Partner("partner", "customer", "auto-sync", "disable", customer);
            case "login":
                run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
                return Api("login");
            case "login --token-stdin":
                run.Environment.Remove("ENCLAVE_TOKEN");
                run.StdinText = TestData.Token + "\n";
                run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
                return Api("login", "--token-stdin");
            case "logout":
                run.SaveCredentials(TestData.Token);
                return new Change(["logout"], Partner: false, SendsRequest: false);
            case "org use":
                run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
                return Api("org", "use", TestData.OtherOrgName);
            default:
                throw new ArgumentOutOfRangeException(nameof(command), command, "No arrangement for this command.");
        }
    }

    private static Change Api(params string[] args) => new(args, Partner: false, SendsRequest: true);

    // A partner must be chosen, or the command exits 2 with no_partner before it reaches the
    // missing partner client.
    private static Change Partner(params string[] args) =>
        new([.. args, "--partner", TestData.PartnerId.ToString()], Partner: true, SendsRequest: false);

    private static string? Authorization(IRequestMessage request) =>
        request.Headers?
            .Where(header => string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            .Select(header => string.Join(", ", header.Value))
            .FirstOrDefault();

    private sealed record Change(string[] Args, bool Partner, bool SendsRequest);
}
