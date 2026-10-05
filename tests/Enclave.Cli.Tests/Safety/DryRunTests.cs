using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// --dry-run prints { "dryRun": true, "org": { "id", "name" }, "request": { "method", "url", "body" } }
/// with the request Enclave.Sdk.Api would send, sends no change and exits 0 (proposal, "Dry run").
/// </summary>
public class DryRunTests
{
    private const string Until = "2030-01-01T00:00:00Z";

    private static readonly string[] TopLevelFields = ["dryRun", "org", "request"];

    private static readonly string[] RequestFields = ["method", "url", "body"];

    private static readonly string[] LookupOnly = ["GET /account/orgs"];

    private static readonly string[] DescriptionOnly = ["Description"];

    private static readonly string[] TwoSystems = ["ABCDE", "FGHIJ"];

    // The org name in the output comes from GET /account/orgs: with the organisation given by ID
    // (ENCLAVE_ORG here) the dry run makes that one lookup so a person reading the preview sees which
    // organisation the change would hit. The lookup is a read, and it is the only request.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dry_run_of_a_bulk_command_prints_the_bulk_request_and_sends_nothing(BulkCommand command)
    {
        using var run = StartWithOrgs();
        command.StubBulk(run, affected: 2);
        var ids = command.Ids(2);

        var result = await run.RunAsync([.. command.ArgsWithoutYes(ids), "--dry-run"]);

        var (org, request) = ReadDryRun(result);
        Assert.Multiple(() =>
        {
            Assert.That(OrgId(org), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OrgName));
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo(command.Method));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, command.Path)));
            Assert.That(command.BodyIds(JsonAssert.Property(request, "body")), Is.EquivalentTo(ids));
            Assert.That(RequestLines(run), Is.EqualTo(LookupOnly));
        });
    }

    [Test]
    public async Task Dry_run_of_a_bulk_command_prints_the_ids_read_from_stdin()
    {
        using var run = StartWithOrgs();
        run.StdinText = "ABCDE\r\nFGHIJ\n\nABCDE\n";

        var result = await run.RunAsync("system", "disable", "-", "--dry-run");

        var (_, request) = ReadDryRun(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Strings(JsonAssert.Property(JsonAssert.Property(request, "body"), "systemIds")), Is.EquivalentTo(TwoSystems));
            Assert.That(RequestLines(run), Is.EqualTo(LookupOnly));
        });
    }

    // TagsClient.CreateAsync posts TagCreateModel with camelCase names (Enclave.Sdk.Api 1.0.4,
    // Constants.JsonSerializerOptions).
    [Test]
    public async Task Dry_run_of_tag_create_prints_the_create_request()
    {
        using var run = StartWithOrgs();
        run.Stub("POST", TestData.OrgPath("tags"), 200, ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "create", "web", "--notes", "front end", "--dry-run");

        var (_, request) = ReadDryRun(result);
        var body = JsonAssert.Property(request, "body");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo("POST"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, TestData.OrgPath("tags"))));
            Assert.That(JsonAssert.Property(body, "tag").GetString(), Is.EqualTo("web"));
            Assert.That(JsonAssert.Property(body, "notes").GetString(), Is.EqualTo("front end"));
            Assert.That(RequestLines(run), Is.EqualTo(LookupOnly));
        });
    }

    // PatchClient sends only the fields that were set, keyed by the C# property name (Enclave.Sdk.Api
    // 1.0.4, PatchClient.cs, Set and ApplyAsync). The printed body is that body, so it holds
    // Description alone: a field missing from a patch is left as it is, and a preview that showed
    // other fields would misstate the change.
    [Test]
    public async Task Dry_run_of_system_update_prints_a_patch_body_holding_only_the_fields_given()
    {
        using var run = StartWithOrgs();
        run.Stub("PATCH", TestData.OrgPath("systems/ABCDE"), 200, ApiJson.System("ABCDE", "x"));

        var result = await run.RunAsync("system", "update", "ABCDE", "--description", "x", "--dry-run");

        var (_, request) = ReadDryRun(result);
        var body = JsonAssert.Property(request, "body");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo("PATCH"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, TestData.OrgPath("systems/ABCDE"))));
            Assert.That(body.EnumerateObject().Select(property => property.Name), Is.EqualTo(DescriptionOnly));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("x"));
            Assert.That(RequestLines(run), Is.EqualTo(LookupOnly));
        });
    }

    // SystemsClient.EnableUntilAsync sends AutoExpireModel: timeZoneId, expiryDateTime as ISO 8601
    // and expiryAction by name (Enclave.Sdk.Api 1.0.4).
    [Test]
    public async Task Dry_run_of_a_timed_enable_prints_the_enable_until_request()
    {
        using var run = StartWithOrgs();
        run.Stub("PUT", TestData.OrgPath("systems/ABCDE/enable-until"), 200, ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "enable", "ABCDE", "--until", Until, "--expiry-action", "Delete", "--dry-run");

        var (_, request) = ReadDryRun(result);
        var body = JsonAssert.Property(request, "body");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo("PUT"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, TestData.OrgPath("systems/ABCDE/enable-until"))));
            Assert.That(JsonAssert.Property(body, "expiryAction").GetString(), Is.EqualTo("Delete"));
            Assert.That(
                DateTimeOffset.Parse(JsonAssert.Property(body, "expiryDateTime").GetString()!, CultureInfo.InvariantCulture),
                Is.EqualTo(DateTimeOffset.Parse(Until, CultureInfo.InvariantCulture)));
            Assert.That(RequestLines(run), Is.EqualTo(LookupOnly));
        });
    }

    // DnsClient.DeleteZoneAsync sends DELETE with no body (Enclave.Sdk.Api 1.0.4). The output keeps
    // one shape for every request, so "body" is present and null.
    [Test]
    public async Task Dry_run_of_a_request_without_a_body_prints_a_null_body()
    {
        using var run = StartWithOrgs();
        run.Stub("DELETE", TestData.OrgPath("dns/zones/4"), 200, ApiJson.Zone(4, "example"));

        var result = await run.RunAsync("dns", "zone", "delete", "4", "--dry-run");

        var (_, request) = ReadDryRun(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo("DELETE"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, TestData.OrgPath("dns/zones/4"))));
            Assert.That(JsonAssert.Property(request, "body").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(RequestLines(run), Is.EqualTo(LookupOnly));
        });
    }

    // Personal access tokens do not expire (portal Enclave.Accounts/Controllers/Api/
    // TokensApiController.cs:105), and dry-run output ends up in agent transcripts and CI logs, so
    // it leaves the Authorization header out, also under --verbose. The lookup request carrying the
    // token shows the CLI held it, so its absence from the output is not for want of a token.
    [TestCaseSource(nameof(TokenCases))]
    public async Task Dry_run_output_contains_neither_the_token_nor_the_authorization_header(string[] args, string tokenSource)
    {
        using var run = StartWithOrgs();

        if (tokenSource == "credentials file")
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
            run.SaveCredentials(TestData.Token);
        }

        var result = await run.RunAsync([.. args, "--dry-run", "--verbose"]);

        Assert.That(result.ExitCode, Is.Zero, result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutJson, "dryRun").GetBoolean(), Is.True);
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(result.Stdout, Does.Not.Contain(TestData.Token));
            Assert.That(result.Stderr, Does.Not.Contain(TestData.Token));
            Assert.That(result.Stdout, Does.Not.Contain("authorization").IgnoreCase);
            Assert.That(result.Stdout, Does.Not.Contain("bearer").IgnoreCase);
        });
    }

    // The organisation can be given by ID or by name (proposal, "Context"). Either way the dry run
    // reports that organisation's ID and name and builds the URL from its ID, with the one lookup.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Dry_run_prints_the_organisation_given_by_org_and_builds_the_url_from_its_id(bool byName)
    {
        using var run = StartWithOrgs();
        var orgArgument = byName ? TestData.OtherOrgName : TestData.OtherOrgId.ToString();

        var result = await run.RunAsync("system", "disable", "ABCDE", "--org", orgArgument, "--dry-run");

        var (org, request) = ReadDryRun(result);
        Assert.Multiple(() =>
        {
            Assert.That(OrgId(org), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(TestData.OtherOrgName));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, $"/org/{TestData.OtherOrgId:N}/systems/disable")));
            Assert.That(RequestLines(run), Is.EqualTo(LookupOnly));
        });
    }

    // --dry-run exists only on commands that change something, so on a read-only command it is an
    // unknown option, a parse error: exit 2 with nothing sent. The second run shows the command
    // itself works.
    [TestCaseSource(nameof(ReadOnlyCases))]
    public async Task Dry_run_is_an_unknown_option_on_a_read_only_command(string[] args, string path, string response)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, 200, response);

        var rejected = await run.RunAsync([.. args, "--dry-run"]);

        Assert.That(rejected.ExitCode, Is.EqualTo(2), rejected.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(rejected.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(rejected.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
            Assert.That(run.Requests, Is.Empty);
        });

        var accepted = await run.RunAsync(args);

        Assert.That(accepted.ExitCode, Is.Zero, accepted.ToString());
        Assert.That(run.SingleRequest().Path, Is.EqualTo(path));
    }

    private static IEnumerable<TestCaseData> TokenCases()
    {
        string[][] commands =
        [
            ["system", "disable", "ABCDE"],
            ["system", "revoke", "ABCDE"],
            ["tag", "create", "web"],
            ["system", "update", "ABCDE", "--description", "x"],
            ["org", "update", "--name", "Initech"],
            ["org", "invite", "send", "new@example.com"],
        ];

        foreach (var args in commands)
        {
            foreach (var tokenSource in new[] { "ENCLAVE_TOKEN", "credentials file" })
            {
                yield return new TestCaseData(args, tokenSource).SetArgDisplayNames(string.Join(' ', args), tokenSource);
            }
        }
    }

    private static IEnumerable<TestCaseData> ReadOnlyCases()
    {
        yield return ReadOnly(["system", "list"], TestData.OrgPath("systems"), ApiJson.Page(ApiJson.System("ABCDE")));
        yield return ReadOnly(["system", "show", "ABCDE"], TestData.OrgPath("systems/ABCDE"), ApiJson.System("ABCDE"));
        yield return ReadOnly(["pending", "list"], TestData.OrgPath("unapproved-systems"), ApiJson.Page());
        yield return ReadOnly(["key", "list"], TestData.OrgPath("enrolment-keys"), ApiJson.Page(ApiJson.Key(12)));
        yield return ReadOnly(["tag", "show", "web"], TestData.OrgPath("tags/web"), ApiJson.Tag("web"));
        yield return ReadOnly(["dns", "show"], TestData.OrgPath("dns"), ApiJson.DnsSummary());
        yield return ReadOnly(["log", "list"], TestData.OrgPath("logs"), ApiJson.Page(ApiJson.Log("System enrolled")));
        yield return ReadOnly(["org", "show"], TestData.OrgPath(), ApiJson.OrgProperties(TestData.OrgName));
        yield return ReadOnly(["org", "user", "list"], TestData.OrgPath("users"), ApiJson.Users());
        yield return ReadOnly(["org", "list"], "/account/orgs", ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
    }

    private static TestCaseData ReadOnly(string[] args, string path, string response) =>
        new TestCaseData(args, path, response).SetArgDisplayNames(string.Join(' ', args));

    // Answers the organisation lookup with the test organisation and one other, so the dry run's
    // name lookup has to pick the right one.
    private static CliRun StartWithOrgs()
    {
        var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        return run;
    }

    // Checks the output's shape and returns its "org" and "request" objects. The request object
    // holds method, url and body only, so no header can appear in it.
    private static (JsonElement Org, JsonElement Request) ReadDryRun(CliResult result)
    {
        Assert.That(result.ExitCode, Is.Zero, result.ToString());

        var output = result.StdoutJson;
        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());

        var request = JsonAssert.Property(output, "request");
        Assert.Multiple(() =>
        {
            Assert.That(output.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(TopLevelFields));
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(request.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(result.Stderr, Is.Empty);
        });
        Assert.That(request.EnumerateObject().Select(property => property.Name), Is.EquivalentTo(RequestFields));

        return (JsonAssert.Property(output, "org"), request);
    }

    private static Guid? OrgId(JsonElement org) =>
        Guid.TryParse(JsonAssert.Property(org, "id").GetString(), out var id) ? id : null;

    private static string Url(JsonElement request) =>
        new Uri(JsonAssert.Property(request, "url").GetString()!).AbsoluteUri;

    // The URL Enclave.Sdk.Api builds: the API base URL (the fake API's here) with the call's path.
    private static string ExpectedUrl(CliRun run, string path) => new Uri(run.ApiUrl, path).AbsoluteUri;

    private static string[] RequestLines(CliRun run) =>
        run.Requests.Select(request => $"{request.Method} {request.Path}").ToArray();
}
