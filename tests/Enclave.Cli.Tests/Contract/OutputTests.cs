using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Enclave.Api.Modules.ActivityLogs.Logs.Models;
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
using static System.FormattableString;

namespace Enclave.Cli.Tests.Contract;

// The output rules in proposed-cli-surface.md ("Output", "Options on every command") hold for every
// command, so an agent learns them once. Each list rule is checked against the list command of every
// top-level noun.
[Category(TestCategory.Pending)]
public class OutputTests
{
    private const string KeySecret = "SECRET-KEY-VALUE";

    // The options Enclave.Sdk.Api 1.0.4 reads and writes models with (Constants.JsonSerializerOptions,
    // internal to the package): camelCase names and enums as their member names. Output equal to a
    // model written with these options carries the API's field names and values unchanged.
    private static readonly JsonSerializerOptions SdkJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Dictionary<string, ListCommand> ListCommands = new ListCommand[]
    {
        new("system list", "systems", typeof(SystemSummaryModel), i => ApiJson.System(SystemId(i)), SystemId, "systemId", HasId: true),
        new("pending list", "unapproved-systems", typeof(UnapprovedSystemSummaryModel), i => ApiJson.PendingSystem(SystemId(i)), SystemId, "systemId", HasId: true),
        new("key list", "enrolment-keys", typeof(EnrolmentKeySummaryModel), i => ApiJson.Key(NumericId(i)), NumericIdText, "id", HasId: true),
        new("policy list", "policies", typeof(PolicyModel), i => ApiJson.Policy(NumericId(i)), NumericIdText, "id", HasId: true),
        new("tag list", "tags", typeof(TagSummaryModel), i => ApiJson.Tag(TagName(i)), TagName, "tag", HasId: true),
        new("trust list", "trust-requirements", typeof(TrustRequirementSummaryModel), i => ApiJson.Trust(NumericId(i)), NumericIdText, "id", HasId: true),
        new("dns zone list", "dns/zones", typeof(DnsZoneSummaryModel), i => ApiJson.Zone(NumericId(i), ZoneName(i)), NumericIdText, "id", HasId: true),
        new("dns record list", "dns/records", typeof(DnsRecordSummaryModel), i => ApiJson.Record(NumericId(i), RecordName(i)), NumericIdText, "id", HasId: true),

        // Log entries have no ID (LogEntryModel), so log list takes part in every list rule except
        // -o id, and its entries are recognised by their message.
        new("log list", "logs", typeof(LogEntryModel), i => ApiJson.Log(LogMessage(i)), LogMessage, "message", HasId: false),
    }.ToDictionary(list => list.Command, StringComparer.Ordinal);

    private static readonly string[] EnvelopeFields = ["items", "total", "truncated"];

    private static readonly string[] WarningFields = ["warning"];

    private static readonly string[] FirstThreePages = ["0", "1", "2"];

    private static readonly string[] FirstTwoPages = ["0", "1"];

    private static readonly string[] TwoKeyIds = ["7001", "7002"];

    private static readonly string[] OneKeyId = ["7001"];

    public static IEnumerable<string> AllListCommands() => ListCommands.Keys;

    public static IEnumerable<string> ListCommandsWithIds() => ListCommands.Values.Where(list => list.HasId).Select(list => list.Command);

    public static IEnumerable<TestCaseData> ListCommandsByEnvelopeFreeFormat()
    {
        foreach (var list in ListCommands.Values)
        {
            yield return new TestCaseData(list.Command, "table");

            if (list.HasId)
            {
                yield return new TestCaseData(list.Command, "id");
            }
        }
    }

    public static IEnumerable<TestCaseData> SingleItemCommands()
    {
        yield return SingleItem("system show SYS7001", "systems/SYS7001", typeof(SystemModel), ApiJson.System("SYS7001"));
        yield return SingleItem("pending show SYS7001", "unapproved-systems/SYS7001", typeof(UnapprovedSystemModel), ApiJson.PendingSystem("SYS7001"));
        yield return SingleItem("key show 7001", "enrolment-keys/7001", typeof(EnrolmentKeyModel), ApiJson.Key(7001));
        yield return SingleItem("policy show 7001", "policies/7001", typeof(PolicyModel), ApiJson.Policy(7001));
        yield return SingleItem("tag show web", "tags/web", typeof(TagModel), ApiJson.Tag("web"));
        yield return SingleItem("trust show 7001", "trust-requirements/7001", typeof(TrustRequirementModel), ApiJson.Trust(7001));
        yield return SingleItem("dns zone show 7001", "dns/zones/7001", typeof(DnsZoneModel), ApiJson.Zone(7001, "internal"));
        yield return SingleItem("dns record show 7001", "dns/records/7001", typeof(DnsRecordModel), ApiJson.Record(7001, "host"));
        yield return SingleItem("dns show", "dns", typeof(DnsSummaryModel), ApiJson.DnsSummary());
        yield return SingleItem("org show", string.Empty, typeof(OrganisationPropertiesModel), ApiJson.OrgProperties(TestData.OrgName));
    }

    public static IEnumerable<TestCaseData> SingleItemCommandsWithoutModel() =>
        SingleItemCommands().Select(item =>
            new TestCaseData(item.Arguments[0], item.Arguments[1], item.Arguments[3]).SetArgDisplayNames((string)item.Arguments[0]!));

    // Agents read list items by the API's field names, so each item is the Enclave.Sdk.Api model
    // with every field under its API name and value, nulls included, and the envelope adds only the
    // three fields the proposal names ("Output").
    [TestCaseSource(nameof(AllListCommands))]
    public async Task List_commands_print_the_api_items_unchanged_inside_an_items_total_truncated_envelope(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        var items = Items(list, 0, 3);
        run.Stub("GET", list.Path, json: ApiJson.PageOf(3, items));

        var result = await run.RunAsync(list.Args);

        AssertSucceededWithEmptyStderr(result);
        Assert.That(run.SingleRequest().Path, Is.EqualTo(list.Path));

        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(output), Is.EquivalentTo(EnvelopeFields));
            Assert.That(output.GetProperty("total").GetInt32(), Is.EqualTo(3));
            Assert.That(output.GetProperty("truncated").GetBoolean(), Is.False);
            Assert.That(output.GetProperty("items").GetArrayLength(), Is.EqualTo(items.Length));
        });

        Assert.Multiple(() =>
        {
            for (var i = 0; i < items.Length; i++)
            {
                AssertModelUnchanged(items[i], list.ItemModel, output.GetProperty("items")[i]);
            }
        });
    }

    // The API pages at 30 items when per_page is absent (portal Enclave.Api.Scaffolding,
    // PaginationDefaults.DefaultPageSize), and the proposal's default limit is 100, so the CLI sends
    // the limit as the page size and asks for the first page, page 0 (PaginatedRequestModel.Page).
    [TestCaseSource(nameof(AllListCommands))]
    public async Task List_commands_request_a_page_of_100_when_no_limit_is_given(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        run.Stub("GET", list.Path, json: ApiJson.PageOf(3, Items(list, 0, 3)));

        var result = await run.RunAsync(list.Args);

        AssertSucceededWithEmptyStderr(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(list.Path));
            Assert.That(request.Query.GetValueOrDefault("per_page"), Is.EqualTo("100"));
            Assert.That(PageNumber(request), Is.EqualTo("0"));
        });
    }

    // The total (12) is larger than the limit, so the envelope reports truncation and the agent
    // knows there is more to fetch.
    [TestCaseSource(nameof(AllListCommands))]
    public async Task List_commands_send_the_limit_as_the_page_size_and_report_truncated_when_the_total_is_larger(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        run.Stub("GET", list.Path, json: ApiJson.PageOf(12, Items(list, 0, 5)));

        var result = await run.RunAsync([.. list.Args, "--limit", "5"]);

        AssertSucceededWithEmptyStderr(result);
        Assert.That(run.SingleRequest().Query.GetValueOrDefault("per_page"), Is.EqualTo("5"));

        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(Markers(output, list), Is.EqualTo(Enumerable.Range(0, 5).Select(list.Marker)));
            Assert.That(output.GetProperty("total").GetInt32(), Is.EqualTo(12));
            Assert.That(output.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    // --all fetches every page (proposal "Calls per command": one call per page, at most 200 items
    // per page). 450 items take three pages of 200, numbered from 0 as the API numbers them
    // (portal PaginatedRequestModel.Page). Pages after the first are served only to a request for
    // that page number, so a CLI that asks for the wrong page receives the wrong items.
    [TestCaseSource(nameof(AllListCommands))]
    public async Task List_commands_with_all_fetch_every_page_and_print_every_item_untruncated(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        StubPages(run, list, total: 450);

        var result = await run.RunAsync([.. list.Args, "--all"]);

        AssertSucceededWithEmptyStderr(result);
        var requests = run.Requests;
        Assert.Multiple(() =>
        {
            Assert.That(requests.Select(request => request.Path), Is.All.EqualTo(list.Path));
            Assert.That(requests.Select(request => request.Query.GetValueOrDefault("per_page")), Is.All.EqualTo("200"));
            Assert.That(requests.Select(PageNumber), Is.EqualTo(FirstThreePages));
        });

        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(Markers(output, list), Is.EqualTo(Enumerable.Range(0, 450).Select(list.Marker)));
            Assert.That(output.GetProperty("total").GetInt32(), Is.EqualTo(450));
            Assert.That(output.GetProperty("truncated").GetBoolean(), Is.False);
        });
    }

    // The API returns at most 200 items per page (portal PaginationDefaults.MaxPageSize), so a
    // limit of 250 takes two pages of 200 and prints the first 250 items.
    [TestCaseSource(nameof(AllListCommands))]
    public async Task List_commands_with_a_limit_above_200_fetch_pages_of_200_and_print_the_limit(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        StubPages(run, list, total: 1000);

        var result = await run.RunAsync([.. list.Args, "--limit", "250"]);

        AssertSucceededWithEmptyStderr(result);
        var requests = run.Requests;
        Assert.Multiple(() =>
        {
            Assert.That(requests.Select(request => request.Path), Is.All.EqualTo(list.Path));
            Assert.That(requests.Select(request => request.Query.GetValueOrDefault("per_page")), Is.All.EqualTo("200"));
            Assert.That(requests.Select(PageNumber), Is.EqualTo(FirstTwoPages));
        });

        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(Markers(output, list), Is.EqualTo(Enumerable.Range(0, 250).Select(list.Marker)));
            Assert.That(output.GetProperty("total").GetInt32(), Is.EqualTo(1000));
            Assert.That(output.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    // -o id feeds pipelines such as `pending list -o id | pending approve -` (proposal "Several IDs"),
    // so stdout holds the IDs and nothing else.
    [TestCaseSource(nameof(ListCommandsWithIds))]
    public async Task List_commands_with_output_id_print_one_id_per_line_and_nothing_else(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        run.Stub("GET", list.Path, json: ApiJson.PageOf(3, Items(list, 0, 3)));

        var result = await run.RunAsync([.. list.Args, "-o", "id"]);

        AssertSucceededWithEmptyStderr(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(list.Path));
            Assert.That(OutputLines(result.Stdout), Is.EqualTo(Enumerable.Range(0, 3).Select(list.Marker)));
        });
    }

    [TestCaseSource(nameof(AllListCommands))]
    public async Task List_commands_with_output_table_print_a_table_naming_every_item(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        run.Stub("GET", list.Path, json: ApiJson.PageOf(3, Items(list, 0, 3)));

        var result = await run.RunAsync([.. list.Args, "-o", "table"]);

        AssertSucceededWithEmptyStderr(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(list.Path));
            Assert.That(IsJson(result.Stdout), Is.False, "a table is not JSON");

            for (var i = 0; i < 3; i++)
            {
                Assert.That(result.Stdout, Does.Contain(list.Marker(i)));
            }
        });
    }

    // The proposal allows table on list commands only ("Options on every command"). The format is
    // checked with the other arguments, before any call; the API is stubbed so that a CLI that calls
    // it anyway is caught by the request log.
    [TestCaseSource(nameof(SingleItemCommandsWithoutModel))]
    public async Task Commands_other_than_list_reject_output_table(string command, string path, string json)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.Stub("GET", path, json: json);

        var result = await run.RunAsync([.. command.Split(' '), "-o", "table"]);

        CliAssert.Rejected(run, result);
    }

    // -o id and -o table drop the envelope, so stderr carries the truncation (proposal "Output"):
    // one line, which a caller tells from an error by its "warning" key.
    [TestCaseSource(nameof(ListCommandsByEnvelopeFreeFormat))]
    public async Task Truncated_id_and_table_output_writes_one_truncation_warning_to_stderr(string command, string format)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        run.Stub("GET", list.Path, json: ApiJson.PageOf(5, Items(list, 0, 2)));

        var result = await run.RunAsync([.. list.Args, "--limit", "2", "-o", format]);

        CliAssert.Succeeded(result);
        var stderr = result.StderrJson;
        Assert.That(stderr, Has.Count.EqualTo(1), result.ToString());

        var warning = JsonAssert.Property(stderr[0], "warning");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(stderr[0]), Is.EqualTo(WarningFields));
            Assert.That(warning.GetProperty("code").GetString(), Is.EqualTo("truncated"));
            Assert.That(warning.GetProperty("shown").GetInt32(), Is.EqualTo(2));
            Assert.That(warning.GetProperty("total").GetInt32(), Is.EqualTo(5));
            Assert.That(result.Stdout, Does.Contain(list.Marker(0)).And.Contain(list.Marker(1)));
        });
    }

    [TestCaseSource(nameof(ListCommandsByEnvelopeFreeFormat))]
    public async Task Untruncated_id_and_table_output_writes_nothing_to_stderr(string command, string format)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        run.Stub("GET", list.Path, json: ApiJson.PageOf(2, Items(list, 0, 2)));

        var result = await run.RunAsync([.. list.Args, "--limit", "2", "-o", format]);

        AssertSucceededWithEmptyStderr(result);
        Assert.That(result.Stdout, Does.Contain(list.Marker(0)).And.Contain(list.Marker(1)));
    }

    // JSON output reports truncation in the envelope, so stderr stays empty and a caller that treats
    // any stderr output as a failure does not fail a successful list.
    [TestCaseSource(nameof(AllListCommands))]
    public async Task Truncated_json_output_reports_truncation_in_the_envelope_and_writes_nothing_to_stderr(string command)
    {
        var list = ListCommands[command];
        using var run = CliRun.Start();
        run.Stub("GET", list.Path, json: ApiJson.PageOf(5, Items(list, 0, 2)));

        var result = await run.RunAsync([.. list.Args, "--limit", "2"]);

        AssertSucceededWithEmptyStderr(result);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(output.GetProperty("truncated").GetBoolean(), Is.True);
            Assert.That(output.GetProperty("total").GetInt32(), Is.EqualTo(5));
        });
    }

    // An enrolment key's secret lets a machine join the network. -o id output goes into pipelines
    // and logs, so it leaves the secret out (proposal "Output"); the API's key list items carry it
    // (EnrolmentKeySummaryModel.Key).
    [Test]
    public async Task Key_list_with_output_id_prints_the_key_ids_without_the_key_secret()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("enrolment-keys"), json: ApiJson.Page(ApiJson.Key(7001, "web", KeySecret), ApiJson.Key(7002, "db", KeySecret)));

        var result = await run.RunAsync("key", "list", "-o", "id");

        AssertSucceededWithEmptyStderr(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(TestData.OrgPath("enrolment-keys")));
            Assert.That(OutputLines(result.Stdout), Is.EqualTo(TwoKeyIds));
            Assert.That(result.Stdout + result.Stderr, Does.Not.Contain(KeySecret));
        });
    }

    // -o id on a command that prints one model prints that model's ID.
    [Test]
    public async Task Key_show_with_output_id_prints_the_key_id_without_the_key_secret()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("enrolment-keys/7001"), json: ApiJson.Key(7001, "web", KeySecret));

        var result = await run.RunAsync("key", "show", "7001", "-o", "id");

        AssertSucceededWithEmptyStderr(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(TestData.OrgPath("enrolment-keys/7001")));
            Assert.That(OutputLines(result.Stdout), Is.EqualTo(OneKeyId));
            Assert.That(result.Stdout + result.Stderr, Does.Not.Contain(KeySecret));
        });
    }

    // JSON output is the model unchanged, and EnrolmentKeyModel.Key is the secret an agent needs to
    // enrol a system (proposal "Output").
    [Test]
    public async Task Key_show_prints_the_key_secret_in_json_output()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("enrolment-keys/7001"), json: ApiJson.Key(7001, "web", KeySecret));

        var result = await run.RunAsync("key", "show", "7001");

        AssertSucceededWithEmptyStderr(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(TestData.OrgPath("enrolment-keys/7001")));
            Assert.That(result.StdoutJson.GetProperty("key").GetString(), Is.EqualTo(KeySecret));
        });
    }

    // A command that prints one model prints it unchanged, with no envelope, and leaves stderr empty
    // on success: callers treat stderr output as an error or a warning (proposal "Errors and exit
    // codes", "Output").
    [TestCaseSource(nameof(SingleItemCommands))]
    public async Task Single_item_commands_print_the_api_model_unchanged_and_nothing_on_stderr(string command, string path, Type model, string json)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.Stub("GET", path, json: json);

        var result = await run.RunAsync(command.Split(' '));

        AssertSucceededWithEmptyStderr(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(path));
            AssertModelUnchanged(json, model, result.StdoutJson);
        });
    }

    // --verbose adds diagnostics on stderr only, so a caller can turn it on without changing what it
    // parses from stdout. The diagnostics never include the token (proposal "Login, logout and
    // status").
    [TestCase("system list", "systems", false)]
    [TestCase("system show SYS7001", "systems/SYS7001", true)]
    public async Task Verbose_writes_diagnostics_to_stderr_and_leaves_stdout_unchanged(string command, string pathSuffix, bool single)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var quiet = CliRun.Start();
        using var verbose = CliRun.Start();
        var json = single ? ApiJson.System("SYS7001") : ApiJson.Page(ApiJson.System("SYS7001"));
        quiet.Stub("GET", TestData.OrgPath(pathSuffix), json: json);
        verbose.Stub("GET", TestData.OrgPath(pathSuffix), json: json);

        var quietResult = await quiet.RunAsync(command.Split(' '));
        var verboseResult = await verbose.RunAsync([.. command.Split(' '), "--verbose"]);

        AssertSucceededWithEmptyStderr(quietResult);
        Assert.Multiple(() =>
        {
            CliAssert.Succeeded(verboseResult);
            Assert.That(verbose.SingleRequest().Path, Is.EqualTo(TestData.OrgPath(pathSuffix)));
            Assert.That(verboseResult.Stdout, Is.EqualTo(quietResult.Stdout));
            Assert.That(verboseResult.Stderr, Is.Not.Empty);
            Assert.That(verboseResult.Stderr, Does.Not.Contain(TestData.Token));
        });
    }

    private static TestCaseData SingleItem(string command, string pathSuffix, Type model, string json) =>
        new TestCaseData(command, TestData.OrgPath(pathSuffix), model, json).SetArgDisplayNames(command);

    private static string[] Items(ListCommand list, int start, int count) =>
        Enumerable.Range(start, count).Select(list.Item).ToArray();

    // Serves `total` items in pages of 200: the first page to any request for the path (a CLI may
    // leave out page=0, the API's default), later pages only to a request for that page number.
    private static void StubPages(CliRun run, ListCommand list, int total)
    {
        const int PageSize = 200;
        var pages = (total + PageSize - 1) / PageSize;

        for (var page = 0; page < pages; page++)
        {
            var items = Items(list, page * PageSize, Math.Min(PageSize, total - (page * PageSize)));
            var json = PageJson(total, page, pages, items);

            if (page == 0)
            {
                run.Stub("GET", list.Path, json: json);
            }
            else
            {
                run.StubWithQuery("GET", list.Path, "page", page.ToString(CultureInfo.InvariantCulture), json: json);
            }
        }
    }

    // ApiJson.PageOf describes a first page. A later page carries its own previous and next page
    // numbers, as the portal's PaginatedQueryHandler.GetResponseAsync writes them.
    private static string PageJson(int total, int page, int pages, string[] items)
    {
        var json = JsonNode.Parse(ApiJson.PageOf(total, items))!;
        var lastPage = pages - 1;
        int? previousPage = page > 0 ? page - 1 : null;
        int? nextPage = page < lastPage ? page + 1 : null;

        json["metadata"]!["prevPage"] = previousPage;
        json["metadata"]!["lastPage"] = lastPage;
        json["metadata"]!["nextPage"] = nextPage;
        json["links"]!["prev"] = previousPage is null ? null : Invariant($"?page={previousPage}");
        json["links"]!["next"] = nextPage is null ? null : Invariant($"?page={nextPage}");
        json["links"]!["last"] = Invariant($"?page={lastPage}");

        return json.ToJsonString();
    }

    private static string PageNumber(RecordedRequest request) =>
        request.Query.TryGetValue("page", out var page) ? page : "0";

    private static IEnumerable<string> Markers(JsonElement envelope, ListCommand list) =>
        envelope.GetProperty("items").EnumerateArray().Select(item => ValueText(item.GetProperty(list.MarkerProperty)));

    private static string ValueText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();

    // The lines of stdout, keeping blank lines (they would be output other than IDs) and dropping
    // the newline that ends the last line.
    private static string[] OutputLines(string stdout)
    {
        var text = stdout.Replace("\r\n", "\n", StringComparison.Ordinal);
        return text.EndsWith('\n') ? text[..^1].Split('\n') : text.Split('\n');
    }

    private static bool IsJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The expected output is the model as Enclave.Sdk.Api reads it from the API's JSON, written back
    // with the SDK's options. Reading through the list item's model drops the fields its summary
    // model lacks, which ApiJson's single-item bodies carry.
    private static void AssertModelUnchanged(string apiJson, Type model, JsonElement output)
    {
        var expected = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize(apiJson, model, SdkJson), model, SdkJson);

        Assert.That(
            JsonElement.DeepEquals(output, expected),
            Is.True,
            $"Expected the {model.Name} unchanged:{Environment.NewLine}{expected}{Environment.NewLine}Found:{Environment.NewLine}{output}");
    }

    private static void AssertSucceededWithEmptyStderr(CliResult result) =>
        Assert.Multiple(() =>
        {
            CliAssert.Succeeded(result);
            Assert.That(result.Stderr, Is.Empty);
        });

    private static string SystemId(int index) => Invariant($"SYS{7001 + index}");

    private static int NumericId(int index) => 7001 + index;

    private static string NumericIdText(int index) => NumericId(index).ToString(CultureInfo.InvariantCulture);

    private static string TagName(int index) => Invariant($"tag-{index}");

    private static string ZoneName(int index) => Invariant($"zone{index}.internal");

    private static string RecordName(int index) => Invariant($"host{index}");

    private static string LogMessage(int index) => Invariant($"event-{index}");

    // One list command: its name, the API path it reads, the Enclave.Sdk.Api model of its items,
    // how to build item i, the text that identifies item i in output, and the property holding it.
    private sealed record ListCommand(string Command, string PathSuffix, Type ItemModel, Func<int, string> Item, Func<int, string> Marker, string MarkerProperty, bool HasId)
    {
        public string Path => TestData.OrgPath(PathSuffix);

        public string[] Args => Command.Split(' ');
    }
}
