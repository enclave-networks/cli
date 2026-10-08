using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// --dry-run prints { "dryRun": true, "org": { "id", "name" }, "requests": [ { "method", "url", "body" } ] }
/// with the requests Enclave.Sdk.Api builds for the change, sends none of them and exits 0
/// (proposed-cli-surface.md "Dry run").
/// </summary>
public class DryRunTests
{
    // Each request's fields, sorted and joined with commas.
    private const string RequestFields = "body,method,url";

    private static readonly string[] OutputFields = ["dryRun", "org", "requests"];

    private static readonly string[] OrgFields = ["id", "name"];

    private static readonly Dictionary<string, string> NoQuery = new(StringComparer.Ordinal);

    // The reads a change depends on run under --dry-run, such as the zone a hostname belongs to;
    // only the change is withheld. The change itself is answered by the fake API, so a CLI that
    // sent it would show it in the requests. The organisation is given by ID (ENCLAVE_ORG_ID), so
    // no lookup is made and its name is null (proposed-cli-surface.md "Dry run").
    [TestCaseSource(typeof(ChangeCommand), nameof(ChangeCommand.All))]
    public async Task Dry_run_prints_the_request_a_change_would_send_and_sends_no_change(ChangeCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);

        var result = await run.RunAsync([.. command.Args, "--dry-run"]);

        var (org, request) = ReadDryRunOfOneRequest(result);
        var urls = command.Paths.Select(path => ExpectedUrl(run, path)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IdOf(org), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Text(request, "method"), Is.EqualTo(command.Method));
            Assert.That(Url(request), Is.AnyOf(urls));
            Assert.That(Changes(run), Is.Empty);
            Assert.That(run.RequestsTo("GET", "/account/orgs"), Is.Empty);
        });
    }

    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dry_run_of_a_bulk_command_prints_the_ids_it_would_send(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 2);
        var ids = command.Ids(2);

        var result = await run.RunAsync([.. command.Args(ids), "--dry-run"]);

        var (_, request) = ReadDryRunOfOneRequest(result);
        Assert.Multiple(() =>
        {
            Assert.That(Text(request, "method"), Is.EqualTo(command.Method));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, command.Path)));
            Assert.That(BodyIds(request, command.BodyField), Is.EqualTo(string.Join(",", ids)));
            Assert.That(command.BulkCalls(run), Is.Empty);
        });
    }

    // The API takes at most 200 IDs per bulk call (portal Enclave.Utilities/HardLimits.cs:22), so a
    // command over 200 IDs makes several calls, and the dry run shows each of them, in order.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dry_run_of_a_bulk_command_over_200_items_shows_each_call_it_would_make(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 200, 200, 50);
        var ids = command.Ids(450);

        var result = await run.RunAsync([.. command.Args(ids), "--dry-run"]);

        var (_, requests) = ReadDryRun(result);
        var expectedUrl = ExpectedUrl(run, command.Path);
        Assert.That(requests, Has.Length.EqualTo(3), result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(requests.Select(request => Text(request, "method")), Is.All.EqualTo(command.Method));
            Assert.That(requests.Select(Url), Is.All.EqualTo(expectedUrl));
            Assert.That(string.Join("|", requests.Select(request => BodyIds(request, command.BodyField))), Is.EqualTo(Batches(ids)));
            Assert.That(command.BulkCalls(run), Is.Empty);
        });
    }

    // An empty list makes no call (proposed-cli-surface.md "Several IDs"), so its dry run shows no
    // request.
    [TestCaseSource(typeof(BulkCommand), nameof(BulkCommand.All))]
    public async Task Dry_run_given_an_empty_list_on_stdin_shows_no_request(BulkCommand command)
    {
        using var run = CliRun.Start();
        command.StubBulk(run, 0);
        run.StdinText = command.List();

        var result = await run.RunAsync([.. command.StdinArgs(), "--dry-run"]);

        var (_, requests) = ReadDryRun(result);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // Example 4 in proposed-cli-surface.md: a reviewer sees which systems an approval would admit
    // before it is sent.
    [Test]
    public async Task Dry_run_of_system_approve_prints_the_ids_of_the_list_on_stdin()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("unapproved-systems/approve");
        run.StubBulk("PUT", path, "systemsApproved", 2);
        run.StdinText = CliList.Of("pending-system", ApiJson.PendingSystem("XYZ12"), ApiJson.PendingSystem("Q7W3E"));

        var result = await run.RunAsync("system", "approve", "-", "--dry-run");

        var (_, request) = ReadDryRunOfOneRequest(result);
        Assert.Multiple(() =>
        {
            Assert.That(Text(request, "method"), Is.EqualTo("PUT"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, path)));
            Assert.That(BodyIds(request, "systemIds"), Is.EqualTo("XYZ12,Q7W3E"));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // Example 54 in proposed-cli-surface.md. The dry run and the delete run in one sandbox, so the
    // request the dry run prints is compared with the request the delete then sends. A name costs
    // one lookup in each run, the dry run included, and --id none ("Calls per command", "Dry run").
    [TestCase(false)]
    [TestCase(true)]
    public async Task Dry_run_of_policy_delete_prints_the_request_policy_delete_then_sends(bool byName)
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies");
        run.Stub("GET", path, 200, ApiJson.Page(ApiJson.Policy(17, "old vpn"), ApiJson.Policy(42, "web to db")));
        run.StubBulk("DELETE", path, "policiesDeleted", 1);
        string[] args = ["policy", "delete", "--id", "17"];

        if (byName)
        {
            args = ["policy", "delete", "old vpn"];
        }

        var preview = await run.RunAsync([.. args, "--dry-run"]);

        var (_, printed) = ReadDryRunOfOneRequest(preview);
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("DELETE", path), Is.Empty);
            Assert.That(run.RequestsTo("GET", path), Has.Count.EqualTo(byName ? 1 : 0));
        });

        var deleted = await run.RunAsync(args);

        CliAssert.Bulk(deleted, requested: 1, affected: 1);
        var sent = run.RequestsTo("DELETE", path);
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Text(printed, "method"), Is.EqualTo(sent[0].Method));
            Assert.That(Url(printed), Is.EqualTo(ExpectedUrl(run, sent[0].Path)));
            Assert.That(BodyIds(printed, "policyIds"), Is.EqualTo(string.Join(",", sent[0].BodyIds("policyIds"))));
            Assert.That(BodyIds(printed, "policyIds"), Is.EqualTo("17"));
        });
    }

    // A named organisation is looked up, one call (proposed-cli-surface.md "Calls per command"), so
    // the CLI has both its ID and its name to print. ENCLAVE_ORG_ID names the other organisation
    // when the name comes from --org, so the output shows the named one was used.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Dry_run_prints_the_organisation_given_by_name_and_builds_the_url_from_its_id(bool fromEnvironment)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        string[] args = ["system", "disable", "ABCDE", "--dry-run", "--org", TestData.OtherOrgName];

        if (fromEnvironment)
        {
            run.Environment.Remove("ENCLAVE_ORG_ID");
            run.Environment["ENCLAVE_ORG"] = TestData.OtherOrgName;
            args = ["system", "disable", "ABCDE", "--dry-run"];
        }

        var result = await run.RunAsync(args);

        var (org, request) = ReadDryRunOfOneRequest(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IdOf(org), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(Text(org, "name"), Is.EqualTo(TestData.OtherOrgName));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, TestData.OtherOrgPath("systems/disable"))));
            Assert.That(string.Join(",", run.Calls()), Is.EqualTo("GET /account/orgs"));
        });
    }

    // An organisation given by ID needs no lookup, so the dry run makes none and has no name to
    // print (proposed-cli-surface.md "Dry run").
    [Test]
    public async Task Dry_run_builds_the_url_from_the_organisation_given_by_org_id_and_prints_a_null_name()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));

        var result = await run.RunAsync("system", "disable", "ABCDE", "--org-id", TestData.OtherOrgId.ToString(), "--dry-run");

        var (org, request) = ReadDryRunOfOneRequest(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.IdOf(org), Is.EqualTo(TestData.OtherOrgId));
            Assert.That(JsonAssert.Property(org, "name").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, TestData.OtherOrgPath("systems/disable"))));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // Personal access tokens do not expire (portal Enclave.Accounts/Controllers/Api/
    // TokensApiController.cs:105), and dry-run output ends up in agent transcripts and CI logs, so
    // it leaves the Authorization header out, under --verbose too. The organisation is named, so
    // the lookup carrying the token shows the CLI held the token it left out.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Dry_run_output_leaves_out_the_authorization_header_and_the_token(bool tokenFromCredentialsFile)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));

        if (tokenFromCredentialsFile)
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
            run.SaveCredentials(TestData.Token);
        }

        var result = await run.RunAsync("system", "revoke", "ABCDE", "--org", TestData.OrgName, "--dry-run", "--verbose");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutJson, "dryRun").GetBoolean(), Is.True);
            Assert.That(run.SingleRequest().Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(result.Stdout, Does.Not.Contain("authorization").IgnoreCase);
            Assert.That(result.Stdout, Does.Not.Contain("bearer").IgnoreCase);
            TokenAssert.Absent(result);
        });
    }

    // An update sends only the fields given (proposed-cli-surface.md "Create and update"), and the
    // printed body is the one Enclave.Sdk.Api would send, so it holds the description alone.
    // PatchClient keys the body by the model's property name (Enclave.Sdk.Api 1.1.0,
    // PatchClient.cs, Set), which JsonAssert.Property matches ignoring case.
    [Test]
    public async Task Dry_run_of_an_update_prints_a_body_holding_only_the_fields_given()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/ABCDE");
        run.Stub("PATCH", path, 200, ApiJson.System("ABCDE", "web server"));

        var result = await run.RunAsync("system", "update", "ABCDE", "--description", "web server", "--dry-run");

        var (_, request) = ReadDryRunOfOneRequest(result);
        var body = JsonAssert.Property(request, "body");
        Assert.Multiple(() =>
        {
            Assert.That(Text(request, "method"), Is.EqualTo("PATCH"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, path)));
            Assert.That(JsonRead.PropertyNames(body), Has.Length.EqualTo(1));
            Assert.That(JsonAssert.Property(body, "description").GetString(), Is.EqualTo("web server"));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // DnsClient.DeleteZoneAsync sends DELETE with no body (Enclave.Sdk.Api 1.1.0), which the dry run
    // shows as "body": null (proposed-cli-surface.md "Dry run").
    [Test]
    public async Task Dry_run_of_a_request_without_a_body_prints_a_null_body()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("dns/zones/4");
        run.Stub("DELETE", path, 200, ApiJson.Zone(4, "internal"));

        var result = await run.RunAsync("dns", "delete-zone", "--id", "4", "--dry-run");

        var (_, request) = ReadDryRunOfOneRequest(result);
        Assert.Multiple(() =>
        {
            Assert.That(Text(request, "method"), Is.EqualTo("DELETE"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, path)));
            Assert.That(JsonAssert.Property(request, "body").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // tag set reads the tag to choose between the API's update and create calls, and that read
    // runs under --dry-run too (proposed-cli-surface.md "Dry run"), so the dry run shows the call
    // the command would make: an update when the tag exists, a create when it does not.
    [TestCase(true, "PATCH", "tags/web")]
    [TestCase(false, "POST", "tags")]
    public async Task Dry_run_of_tag_set_reads_the_tag_and_shows_the_update_or_create_it_chooses(bool tagExists, string method, string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        var tag = TestData.OrgPath("tags/web");
        var path = TestData.OrgPath(pathSuffix);

        if (tagExists)
        {
            run.Stub("GET", tag, 200, ApiJson.Tag("web"));
        }
        else
        {
            run.StubProblem("GET", tag, 404, "Not Found", "No tag is named web.");
        }

        run.Stub(method, path, 200, ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--notes", "front end", "--dry-run");

        var (_, request) = ReadDryRunOfOneRequest(result);
        Assert.Multiple(() =>
        {
            Assert.That(Text(request, "method"), Is.EqualTo(method));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, path)));
            Assert.That(run.RequestsTo("GET", tag), Has.Count.EqualTo(1));
            Assert.That(Changes(run), Is.Empty);
        });
    }

    // A flag that replaces a list keeps the label of every entry that stays. A label given with the
    // flag replaces it, an empty one removes it, and a new entry without a label has none
    // (proposed-cli-surface.md "Command options"). The CLI reads the key to find the labels, a read
    // the dry run makes too ("Dry run"), and the dry run shows the list it would send.
    // 172.16.0.0/12 is left out, so it goes. A label can hold a comma, since the flag is repeated.
    [Test]
    public async Task Dry_run_of_an_update_that_replaces_a_list_shows_the_labels_it_keeps()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("enrolment-keys/12");
        run.Stub("GET", path, 200, KeyWithAllowedRanges(12, ("203.0.113.0/24", "London office"), ("198.51.100.0/24", "CI runners"), ("192.0.2.0/24", "Lab"), ("172.16.0.0/12", "Old VPN")));
        run.Stub("PATCH", path, 200, ApiJson.Key(12, "build agents"));

        var result = await run.RunAsync(
            "key",
            "update",
            "--id",
            "12",
            "--set-allow-ip",
            "203.0.113.0/24",
            "--set-allow-ip",
            "198.51.100.0/24=Build farm",
            "--set-allow-ip",
            "192.0.2.0/24=",
            "--set-allow-ip",
            "10.20.0.0/16",
            "--set-allow-ip",
            "198.18.0.0/15=Office, ground floor",
            "--dry-run");

        var (_, request) = ReadDryRunOfOneRequest(result);
        var ranges = JsonAssert.Property(JsonAssert.Property(request, "body"), "ipConstraints");
        Assert.Multiple(() =>
        {
            Assert.That(Text(request, "method"), Is.EqualTo("PATCH"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, path)));
            Assert.That(
                string.Join("|", ranges.EnumerateArray().Select(range => $"{Text(range, "range")}={Text(range, "description")}")),
                Is.EqualTo("203.0.113.0/24=London office|198.51.100.0/24=Build farm|192.0.2.0/24=|10.20.0.0/16=|198.18.0.0/15=Office, ground floor"));
            Assert.That(run.RequestsTo("GET", path), Has.Count.EqualTo(1));
            Assert.That(Changes(run), Is.Empty);
        });
    }

    // --then revoke is the API's Delete on a system (proposed-cli-surface.md "Options on every
    // command"), --for counts from when the command runs, and the CLI sends the expiry as a UTC
    // instant ("Details").
    [Test]
    public async Task Dry_run_of_a_timed_enable_prints_the_expiry_and_the_action_taken_then()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/K7P2Q/enable-until");
        run.Stub("PUT", path, 200, ApiJson.System("K7P2Q"));
        var before = DateTimeOffset.UtcNow;

        var result = await run.RunAsync("system", "enable", "K7P2Q", "--for", "24h", "--then", "revoke", "--dry-run");

        var after = DateTimeOffset.UtcNow;
        var (_, request) = ReadDryRunOfOneRequest(result);
        var body = JsonAssert.Property(request, "body");
        var expiry = JsonRead.ExpiryDateTime(body);
        Assert.Multiple(() =>
        {
            Assert.That(Text(request, "method"), Is.EqualTo("PUT"));
            Assert.That(Url(request), Is.EqualTo(ExpectedUrl(run, path)));
            Assert.That(Text(body, "expiryAction"), Is.EqualTo("Delete"));
            Assert.That(expiry, Is.InRange(before.AddHours(24).AddMinutes(-1), after.AddHours(24).AddMinutes(1)));
            Assert.That(expiry.Offset, Is.EqualTo(TimeSpan.Zero));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // --dry-run exists only on commands that change something through the API, so on a read it is
    // an unknown option and exits 2 (proposed-cli-surface.md "Details"). The second run shows the
    // command itself works.
    [TestCaseSource(nameof(ReadOnlyCases))]
    public async Task Dry_run_is_an_unknown_option_on_a_command_that_only_reads(string[] args, string path, string response)
    {
        ArgumentNullException.ThrowIfNull(args);

        using var run = CliRun.Start();
        run.Stub("GET", path, 200, response);

        var rejected = await run.RunAsync([.. args, "--dry-run"]);

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync(args);

        CliAssert.Succeeded(accepted);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(path));
    }

    // login, logout, org use and partner use change only local files and do not take --dry-run
    // (proposed-cli-surface.md "Dry run"), so it is an unknown option: exit 2, with the file left
    // as it was. The second run, without it, shows the command changes the file.
    [TestCaseSource(typeof(LocalCommand), nameof(LocalCommand.All))]
    public async Task Dry_run_is_an_unknown_option_on_a_command_that_changes_only_local_files(LocalCommand command)
    {
        using var run = CliRun.Start();
        command.Arrange(run);
        var file = command.FilePath(run);

        var rejected = await run.RunAsync([.. command.Args, "--dry-run"]);

        CliAssert.Rejected(run, rejected);
        Assert.That(run.Files.Exists(file), Is.EqualTo(!command.FileExistsAfter));

        var accepted = await run.RunAsync(command.Args);

        CliAssert.Succeeded(accepted);
        Assert.That(run.Files.Exists(file), Is.EqualTo(command.FileExistsAfter));
    }

    private static IEnumerable<TestCaseData> ReadOnlyCases()
    {
        yield return ReadOnly(["system", "list"], TestData.OrgPath("systems"), ApiJson.Page(ApiJson.System("ABCDE")));
        yield return ReadOnly(["system", "list", "--pending"], TestData.OrgPath("unapproved-systems"), ApiJson.Page(ApiJson.PendingSystem("XYZ12")));
        yield return ReadOnly(["system", "show", "ABCDE"], TestData.OrgPath("systems/ABCDE"), ApiJson.System("ABCDE"));
        yield return ReadOnly(["key", "show", "--id", "12"], TestData.OrgPath("enrolment-keys/12"), ApiJson.Key(12));
        yield return ReadOnly(["policy", "list"], TestData.OrgPath("policies"), ApiJson.Page(ApiJson.Policy(42)));
        yield return ReadOnly(["tag", "show", "web"], TestData.OrgPath("tags/web"), ApiJson.Tag("web"));
        yield return ReadOnly(["dns", "show"], TestData.OrgPath("dns"), ApiJson.DnsSummary());
        yield return ReadOnly(["dns", "list-hostnames"], TestData.OrgPath("dns/records"), ApiJson.Page(ApiJson.Record(7, "db")));
        yield return ReadOnly(["trust", "show", "--id", "5"], TestData.OrgPath("trust-requirements/5"), ApiJson.Trust(5));
        yield return ReadOnly(["log"], TestData.OrgPath("logs"), ApiJson.Page(ApiJson.Log("System enrolled")));
        yield return ReadOnly(["org", "show"], TestData.OrgPath(), ApiJson.OrgProperties(TestData.OrgName));
        yield return ReadOnly(["org", "list-users"], TestData.OrgPath("users"), ApiJson.Users());
        yield return ReadOnly(["org", "list-invites"], TestData.OrgPath("invites"), ApiJson.Invites());
        yield return ReadOnly(["org", "list"], "/account/orgs", ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        yield return ReadOnly(["status"], "/account/orgs", ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
    }

    private static TestCaseData ReadOnly(string[] args, string path, string response) =>
        new TestCaseData(args, path, response).SetArgDisplayNames(string.Join(' ', args));

    // Checks the output's shape and returns its "org" object and the items of "requests". Each
    // request object holds method, url and body only, so no header can appear in it.
    private static (JsonElement Org, JsonElement[] Requests) ReadDryRun(CliResult result)
    {
        CliAssert.Succeeded(result);

        var output = result.StdoutJson;
        Assert.That(output.ValueKind, Is.EqualTo(JsonValueKind.Object), result.ToString());

        var org = JsonAssert.Property(output, "org");
        var requests = JsonAssert.Property(output, "requests");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(OutputFields), result.ToString());
            Assert.That(JsonAssert.Property(output, "dryRun").ValueKind, Is.EqualTo(JsonValueKind.True));
            Assert.That(org.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(requests.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(result.Stderr, Is.Empty);
        });

        var items = requests.EnumerateArray().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(org), Is.EquivalentTo(OrgFields), result.ToString());
            Assert.That(items.Select(item => item.ValueKind), Is.All.EqualTo(JsonValueKind.Object), result.ToString());
        });
        Assert.That(
            items.Select(item => string.Join(",", JsonRead.PropertyNames(item).Order(StringComparer.Ordinal))),
            Is.All.EqualTo(RequestFields),
            result.ToString());

        return (org, items);
    }

    // The output of a command that makes one call: exactly one request.
    private static (JsonElement Org, JsonElement Request) ReadDryRunOfOneRequest(CliResult result)
    {
        var (org, requests) = ReadDryRun(result);
        Assert.That(requests, Has.Length.EqualTo(1), result.ToString());

        return (org, requests[0]);
    }

    // A string property's value, or an empty string when it is missing or null.
    private static string Text(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString()!;
            }
        }

        return string.Empty;
    }

    private static string Url(JsonElement request) => new Uri(Text(request, "url")).AbsoluteUri;

    // The URL Enclave.Sdk.Api builds: the API base URL, the fake API's here, with the call's path.
    private static string ExpectedUrl(CliRun run, string path) => new Uri(run.ApiUrl, path).AbsoluteUri;

    // The IDs in a printed body, read as RecordedRequest reads the IDs of a sent body.
    private static string BodyIds(JsonElement request, string field) =>
        string.Join(",", new RecordedRequest("DRY-RUN", string.Empty, NoQuery, JsonAssert.Property(request, "body").GetRawText(), null).BodyIds(field));

    // The IDs of each call joined with commas, and the calls joined with "|", for IDs sent in calls
    // of 200 in the order given.
    private static string Batches(string[] ids) =>
        string.Join("|", ids.Chunk(200).Select(batch => string.Join(",", batch)));

    // The requests that would change something; a dry run makes none.
    private static string Changes(CliRun run) =>
        string.Join(", ", run.Requests.Where(request => request.Method != "GET").Select(request => $"{request.Method} {request.Path}"));

    private static string KeyWithAllowedRanges(int id, params (string Range, string Label)[] ranges)
    {
        var key = JsonNode.Parse(ApiJson.Key(id, "build agents"))!;
        var constraints = new JsonArray();

        foreach (var (range, label) in ranges)
        {
            constraints.Add(new JsonObject { ["range"] = range, ["description"] = label });
        }

        key["ipConstraints"] = constraints;
        return key.ToJsonString();
    }
}
