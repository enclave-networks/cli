using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Enclave.Api.Modules.OrganisationManagement;
using Enclave.Api.Modules.SystemManagement.Dns.Models;
using Enclave.Api.Modules.SystemManagement.EnrolmentKeys.Models;
using Enclave.Api.Modules.SystemManagement.Policies.Models;
using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Api.Modules.SystemManagement.Tags.Models;
using Enclave.Api.Modules.SystemManagement.TrustRequirements.Models;
using Enclave.Api.Modules.SystemManagement.UnapprovedSystems.Models;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// stdout is JSON using the Enclave.Sdk.Api models unchanged, so field names and values match the
// API (proposed-cli-surface.md "Output"; AGENTS.md "CLI contract"). --help and --version print text
// for people, and are the only output that is not JSON ("Errors and exit codes"). Lists and their
// envelope are in ListTests; the output-format options that no longer exist are in
// ParseErrorTests.
public class OutputTests
{
    private const string KeySecret = "KEY-SECRET-5d1e";

    // The IDs are given with --id, or are system IDs and tag names, so no name lookup adds a
    // request.
    public static IEnumerable<TestCaseData> SingleItemCommands()
    {
        yield return SingleItem("system show ABCDE", "systems/ABCDE", typeof(SystemModel), ApiJson.System("ABCDE"));
        yield return SingleItem("system show ABCDE --pending", "unapproved-systems/ABCDE", typeof(UnapprovedSystemModel), ApiJson.PendingSystem("ABCDE"));
        yield return SingleItem("key show --id 12", "enrolment-keys/12", typeof(EnrolmentKeyModel), ApiJson.Key(12));
        yield return SingleItem("policy show --id 7", "policies/7", typeof(PolicyModel), ApiJson.Policy(7));
        yield return SingleItem("tag show web", "tags/web", typeof(TagModel), ApiJson.Tag("web"));
        yield return SingleItem("trust show --id 5", "trust-requirements/5", typeof(TrustRequirementModel), ApiJson.Trust(5));
        yield return SingleItem("dns show-zone --id 4", "dns/zones/4", typeof(DnsZoneModel), ApiJson.Zone(4, "internal"));
        yield return SingleItem("dns show-hostname --id 7", "dns/records/7", typeof(DnsRecordModel), ApiJson.Record(7, "db"));
        yield return SingleItem("dns show", "dns", typeof(DnsSummaryModel), ApiJson.DnsSummary());
        yield return SingleItem("org show", string.Empty, typeof(OrganisationPropertiesModel), ApiJson.OrgProperties(TestData.OrgName));
    }

    // A command that prints one model prints it as Enclave.Sdk.Api writes it, with no envelope, and
    // leaves stderr empty on success: a caller treats anything on stderr as an error ("Errors and
    // exit codes").
    [TestCaseSource(nameof(SingleItemCommands))]
    public async Task A_command_that_prints_one_item_prints_the_api_model_unchanged_and_nothing_on_stderr(string command, string path, Type model, string json)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.Stub("GET", path, json: json);

        var result = await run.RunAsync(command.Split(' '));

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var expected = await ApiJson.AsSdkModelAsync(json, model);
        Assert.Multiple(() =>
        {
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(
                JsonElement.DeepEquals(result.StdoutJson, expected),
                Is.True,
                $"Expected the {model.Name} as Enclave.Sdk.Api writes it:{Environment.NewLine}{expected}{Environment.NewLine}Found:{Environment.NewLine}{result.Stdout}");
        });
    }

    // The secret is what enrols a system, and a script reads it from the output (example 8:
    // `key create ... | jq -r .key`), so key output carries EnrolmentKeyModel.Key ("Output").
    [Test]
    public async Task Key_show_prints_the_key_secret()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("enrolment-keys/12"), json: ApiJson.Key(12, "build agents", KeySecret));

        var result = await run.RunAsync("key", "show", "--id", "12");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(TestData.OrgPath("enrolment-keys/12")));
            Assert.That(result.StdoutJson.GetProperty("key").GetString(), Is.EqualTo(KeySecret));
        });
    }

    [Test]
    public async Task Key_list_prints_each_key_secret()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("enrolment-keys");
        run.StubPages(path, 200, ApiJson.Key(12, "build agents", KeySecret), ApiJson.Key(13, "laptops", KeySecret + "-2"));

        var result = await run.RunAsync("key", "list");

        var items = CliAssert.List(result, "key");
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(path), Is.EqualTo("0"));
            Assert.That(JsonRead.StringFieldList(items, "key"), Is.EqualTo($"{KeySecret},{KeySecret}-2"));
        });
    }

    // --verbose adds diagnostics on stderr only, so a caller can turn it on without changing what it
    // parses from stdout ("Options on every command"). The diagnostics never include the token
    // ("Login, logout and status").
    [TestCase("system list", "systems")]
    [TestCase("system show ABCDE", "systems/ABCDE")]
    public async Task Verbose_writes_diagnostics_to_stderr_and_leaves_stdout_and_the_requests_unchanged(string command, string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var quiet = CliRun.Start();
        using var verbose = CliRun.Start();
        var path = TestData.OrgPath(pathSuffix);
        StubSystems(quiet, path);
        StubSystems(verbose, path);

        var quietResult = await quiet.RunAsync(command.Split(' '));
        var verboseResult = await verbose.RunAsync([.. command.Split(' '), "--verbose"]);

        CliAssert.Succeeded(quietResult);
        CliAssert.Succeeded(verboseResult);
        Assert.Multiple(() =>
        {
            Assert.That(quietResult.Stderr, Is.Empty);
            Assert.That(quiet.Calls(), Is.Not.Empty.And.All.EqualTo($"GET {path}"));
            Assert.That(verbose.Calls(), Is.EqualTo(quiet.Calls()));
            Assert.That(verboseResult.Stdout, Is.EqualTo(quietResult.Stdout));
            Assert.That(verboseResult.Stderr, Is.Not.Empty);
            Assert.That(verboseResult.Stdout + verboseResult.Stderr, Does.Not.Contain(TestData.Token));
        });
    }

    // --verbose writes diagnostics as JSON lines, { "verbose": "…" } ("Details"), and among them the
    // method and URL of each request, so a caller can see what was sent and where without a proxy.
    // The list reads two pages, so a CLI that logged one request per command, or none, fails, and
    // the bulk disable is a change. Each logged request is compared with what the fake API received,
    // in order, query included. The token is in no line ("Login, logout and status").
    [TestCase("system list", "GET")]
    [TestCase("system disable ABCDE", "PUT")]
    public async Task Verbose_logs_the_method_and_url_of_each_request_in_the_order_sent(string command, string method)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 1, ApiJson.System("ABCDE"), ApiJson.System("FGHIJ"));
        run.StubBulk("PUT", TestData.OrgPath("systems/disable"), "systemsUpdated", 1);

        var result = await run.RunAsync([.. command.Split(' '), "--verbose"]);

        CliAssert.Succeeded(result);
        var received = run.Requests.Select(request => Describe(request.Method, new Uri(run.ApiUrl, request.Path), request.Query)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(received, Is.Not.Empty.And.All.StartWith(method + " "));
            Assert.That(LoggedRequests(result), Is.EqualTo(received));
            TokenAssert.Absent(result);
        });
    }

    // The API names the gateway priority that follows the order of the gateways Ordered, and
    // Enclave.Sdk.Api.Data 304.48.0 names that enum member Prioritised (proposed-cli-surface.md
    // "`Enclave.Sdk.Api` changes", item 9). Output keeps the API's names ("Output"), so a policy the
    // API sends as Ordered prints as Ordered, in a single item and in a list.
    [TestCase("policy show --id 7")]
    [TestCase("policy list")]
    public async Task A_policy_whose_gateway_priority_is_ordered_prints_it_as_Ordered(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        var policy = JsonNode.Parse(ApiJson.Policy(7, "office LAN"))!;
        policy["type"] = "Gateway";
        policy["gatewayTrafficDirection"] = "Exit";
        policy["gatewayPriority"] = "Ordered";
        run.Stub("GET", TestData.OrgPath("policies/7"), json: policy.ToJsonString());
        run.StubPages(TestData.OrgPath("policies"), 200, policy.ToJsonString());

        var result = await run.RunAsync(command.Split(' '));

        CliAssert.Succeeded(result);
        var printed = command.EndsWith("list", StringComparison.Ordinal) ? CliAssert.List(result, "policy")[0] : result.StdoutJson;
        Assert.That(JsonAssert.Property(printed, "gatewayPriority").GetString(), Is.EqualTo("Ordered"), result.ToString());
    }

    // `enclave-cli` with no arguments prints the help and exits 0 ("Errors and exit codes"), the
    // same text --help prints. Help is for reading before anything is set up, so neither run has a
    // token or an organisation, and neither calls the API.
    [Test]
    public async Task No_arguments_print_the_help_that_help_prints_and_exit_0()
    {
        using var bare = CliRun.Start();
        using var help = CliRun.Start();
        foreach (var run in new[] { bare, help })
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
            run.Environment.Remove("ENCLAVE_ORG_ID");
        }

        var bareResult = await bare.RunAsync();
        var helpResult = await help.RunAsync("--help");

        CliAssert.Succeeded(bareResult);
        CliAssert.Succeeded(helpResult);
        Assert.Multiple(() =>
        {
            Assert.That(helpResult.Stdout, Is.Not.Empty);
            Assert.That(bareResult.Stdout, Is.EqualTo(helpResult.Stdout));
            Assert.That(bareResult.Stderr, Is.Empty);
            Assert.That(helpResult.Stderr, Is.Empty);
            Assert.That(bare.Requests, Is.Empty);
            Assert.That(help.Requests, Is.Empty);
        });
    }

    // A command's help names its options, so a person can read what the command takes, and is text
    // on stdout with an exit code of 0, needing no token or organisation and making no call.
    [TestCase("system list", "--filter")]
    [TestCase("policy create", "--acl")]
    [TestCase("commands", "--verbose")]
    public async Task Help_on_a_command_prints_its_options_and_exits_0_without_a_request(string command, string option)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG_ID");

        var result = await run.RunAsync([.. command.Split(' '), "--help"]);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.Stdout, Does.Contain(option));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    private static TestCaseData SingleItem(string command, string pathSuffix, Type model, string json) =>
        new TestCaseData(command, TestData.OrgPath(pathSuffix), model, json).SetArgDisplayNames(command);

    // A request as "METHOD scheme://host:port/path?query", with the query parameters in name order,
    // so a URL logged with its parameters in another order compares equal.
    private static string Describe(string method, Uri url, IReadOnlyDictionary<string, string> query)
    {
        var parameters = string.Join("&", query.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
        return $"{method} {url.GetLeftPart(UriPartial.Path)}?{parameters}";
    }

    // The requests --verbose logged: each stderr line { "verbose": "<METHOD> <URL> …" } whose text
    // starts with an HTTP method and an absolute URL.
    private static string[] LoggedRequests(CliResult result)
    {
        var logged = new List<string>();

        foreach (var line in result.StderrJson)
        {
            if (!line.TryGetProperty("verbose", out var text) || text.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var words = text.GetString()!.Split(' ');

            if (words.Length >= 2
                && words[0] is "GET" or "POST" or "PUT" or "PATCH" or "DELETE"
                && Uri.TryCreate(words[1], UriKind.Absolute, out var url))
            {
                var query = HttpUtility.ParseQueryString(url.Query);
                var parameters = query.AllKeys.OfType<string>().ToDictionary(key => key, key => query[key] ?? string.Empty, StringComparer.Ordinal);
                logged.Add(Describe(words[0], url, parameters));
            }
        }

        return [.. logged];
    }

    private static void StubSystems(CliRun run, string path)
    {
        if (path.EndsWith("/systems", StringComparison.Ordinal))
        {
            run.StubPages(path, 200, ApiJson.System("ABCDE"), ApiJson.System("FGHIJ"));
        }
        else
        {
            run.Stub("GET", path, json: ApiJson.System("ABCDE"));
        }
    }
}
