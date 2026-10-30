using System.Text.Json;
using System.Web;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

public class TraceTests
{
    private const string ProductionPartnerApi = "https://partner-api.enclave.io/";

    private const string NetworkFailure = "No connection could be made in this test.";

    private static readonly string[] SourceOnly = ["source"];

    private static readonly string[] OrgFields = ["id", "name", "source"];

    private static readonly string[] PartnerFields = ["id", "source"];

    private static readonly string[] SavedOnly = ["saved"];

    private static readonly string[] ErrorThenTrace = ["error", "trace"];

    private static readonly string[] ErrorOnly = ["error"];

    // OutputTests covers the models the show commands print.
    public static IEnumerable<TestCaseData> Documents()
    {
        yield return Document("kind,items,total", "system", "list");
        yield return Document("requested,affected", "system", "disable", "ABCDE");
        yield return Document("dryRun,org,requests", "system", "disable", "ABCDE", "--dry-run");
        yield return Document("token,org,partner", "status");
        yield return Document("orgId,orgName,role,partnerAccess", "org", "use", TestData.OrgName);
        yield return Document("id", "partner", "use", "--id", TestData.PartnerId.ToString());
    }

    // An empty list after "-" needs no token ("Several IDs").
    public static IEnumerable<TestCaseData> RunsWhereNothingHappened()
    {
        yield return CommandCases.Described("a parse error", "system", "list", "--bogus");
        yield return CommandCases.Described("a bad ID", "system", "show", "../systems/ABCDE");
        yield return CommandCases.Described("no token", "system", "list");
        yield return CommandCases.Described("commands", "commands");
        yield return CommandCases.Described("an empty list after -", "system", "disable", "-");
    }

    public static IEnumerable<TestCaseData> FailuresWithoutProblemDetails()
    {
        yield return new TestCaseData("a network failure", typeof(HttpRequestException)).SetArgDisplayNames("a network failure");
        yield return new TestCaseData("a timeout", typeof(TaskCanceledException)).SetArgDisplayNames("a timeout");
        yield return new TestCaseData("a proxy's 502", typeof(HttpRequestException)).SetArgDisplayNames("a proxy's 502");
    }

    public static IEnumerable<TestCaseData> RunsThatHoldTheToken()
    {
        yield return CommandCases.Described("system list", "system", "list");
        yield return CommandCases.Described("system disable --dry-run", "system", "disable", "ABCDE", "--dry-run");
        yield return CommandCases.Described("a problem response", "system", "show", "ABCDE");
        yield return CommandCases.Described("a network failure", "key", "show", "--id", "12");
    }

    [TestCaseSource(nameof(Documents))]
    public async Task Trace_comes_last_after_the_documents_own_fields(string[] args, string fields)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var run = CliRun.Start();
        BothOrgs.Stub(run);
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));
        run.StubBulk("PUT", TestData.OrgPath("systems/disable"), "systemsUpdated", 1);

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(result.StdoutTrace.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(string.Join(",", JsonRead.PropertyNamesBeforeTrace(result.StdoutJson)), Is.EqualTo(fields), result.ToString());
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    [Test]
    public async Task A_document_and_its_trace_print_indented()
    {
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        var indented = JsonRead.Indented(result.StdoutJson) + Environment.NewLine;
        Assert.Multiple(() =>
        {
            Assert.That(result.StdoutTrace.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(result.Stdout.ReplaceLineEndings(), Is.EqualTo(indented.ReplaceLineEndings()), result.ToString());
        });
    }

    [TestCase("ENCLAVE_TOKEN")]
    [TestCase("credentials.json")]
    [TestCase("stdin")]
    public async Task The_trace_names_where_the_token_came_from(string source)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        run.StubPages(TestData.OrgPath("systems"), 200);
        string[] args = ["system", "list"];
        var expected = source;

        if (source == "credentials.json")
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
            run.SaveCredentials(TestData.Token);
            expected = run.CredentialsPath;
        }
        else if (source == "stdin")
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
            run.StdinText = TestData.Token;
            args = ["login", "--token-stdin"];
        }

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        var token = JsonAssert.Property(result.StdoutTrace, "token");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(token), Is.EqualTo(SourceOnly), result.ToString());
            Assert.That(JsonAssert.Property(token, "source").GetString(), Is.EqualTo(expected), result.ToString());
        });
    }

    [Test]
    public async Task The_trace_of_an_organisation_command_names_the_api_it_called()
    {
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 200);

        var result = await run.RunAsync("system", "list");

        CliAssert.Succeeded(result);
        var trace = result.StdoutTrace;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(trace, "api").GetString(), Is.EqualTo(run.ApiUrl.AbsoluteUri), result.ToString());
            Assert.That(trace.TryGetProperty("partnerApi", out _), Is.False, result.ToString());
            Assert.That(trace.TryGetProperty("partner", out _), Is.False, result.ToString());
        });
    }

    // The default is saved as ID and name, so its name comes with it; an ID given any other way
    // has no name, since no lookup ran ("Context").
    [TestCase("ENCLAVE_ORG_ID")]
    [TestCase("--org-id")]
    [TestCase("--org")]
    [TestCase("ENCLAVE_ORG")]
    [TestCase("cli.json")]
    [TestCase("only-organisation")]
    public async Task The_trace_names_the_organisation_and_what_chose_it(string source)
    {
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 200);
        run.StubPages(TestData.OtherOrgPath("systems"), 200);
        string[] args = ["system", "list"];
        (Guid Id, string? Name, string Source) expected = (TestData.OtherOrgId, TestData.OtherOrgName, source);

        if (source == "only-organisation")
        {
            run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        }
        else
        {
            BothOrgs.Stub(run);
        }

        if (source != "ENCLAVE_ORG_ID")
        {
            run.Environment.Remove("ENCLAVE_ORG_ID");
        }

        switch (source)
        {
            case "ENCLAVE_ORG_ID":
                expected = (TestData.OrgId, null, source);
                break;
            case "--org-id":
                args = [.. args, source, TestData.OtherOrgId.ToString()];
                expected = (TestData.OtherOrgId, null, source);
                break;
            case "--org":
                args = [.. args, source, TestData.OtherOrgName];
                break;
            case "ENCLAVE_ORG":
                run.Environment["ENCLAVE_ORG"] = TestData.OtherOrgName;
                break;
            case "cli.json":
                run.SaveDefaults(TestData.OtherOrgId.ToString(), TestData.OtherOrgName);
                expected = (TestData.OtherOrgId, TestData.OtherOrgName, run.CliConfigPath);
                break;
            default:
                expected = (TestData.OrgId, TestData.OrgName, source);
                break;
        }

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        var org = JsonAssert.Property(result.StdoutTrace, "org");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(org), Is.EqualTo(OrgFields), result.ToString());
            Assert.That(JsonRead.IdOf(org), Is.EqualTo(expected.Id), result.ToString());
            Assert.That(JsonAssert.Property(org, "name").GetString(), Is.EqualTo(expected.Name), result.ToString());
            Assert.That(JsonAssert.Property(org, "source").GetString(), Is.EqualTo(expected.Source), result.ToString());
        });
    }

    // A partner command calls the partner API only ("Partner API").
    [TestCase("--partner-id")]
    [TestCase("ENCLAVE_PARTNER_ID")]
    [TestCase("cli.json")]
    public async Task The_trace_of_a_partner_command_names_the_partner_api_and_the_partner(string source)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        string[] args = ["partner", "customer", "list"];
        var expected = source;

        switch (source)
        {
            case "--partner-id":
                args = [.. args, source, TestData.PartnerId.ToString()];
                break;
            case "ENCLAVE_PARTNER_ID":
                run.Environment[source] = TestData.PartnerId.ToString();
                break;
            default:
                run.SaveDefaults(partnerId: TestData.PartnerId.ToString());
                expected = run.CliConfigPath;
                break;
        }

        var result = await run.RunAsync(args);

        PartnerApiFake.AssertSentToThePartnerApi(run, result);
        var trace = result.StdoutTrace;
        var partner = JsonAssert.Property(trace, "partner");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(trace, "partnerApi").GetString(), Is.EqualTo(run.PartnerApiUrl.AbsoluteUri), result.ToString());
            Assert.That(trace.TryGetProperty("api", out _), Is.False, result.ToString());
            Assert.That(trace.TryGetProperty("org", out _), Is.False, result.ToString());
            Assert.That(JsonRead.PropertyNames(partner), Is.EqualTo(PartnerFields), result.ToString());
            Assert.That(JsonRead.IdOf(partner), Is.EqualTo(TestData.PartnerId), result.ToString());
            Assert.That(JsonAssert.Property(partner, "source").GetString(), Is.EqualTo(expected), result.ToString());
        });
    }

    // --dry-run sends nothing, and the network beneath the CLI's handler fails every request, so
    // nothing reaches the live API.
    [Test]
    public async Task The_trace_of_a_partner_command_names_the_production_partner_api_when_no_address_is_given()
    {
        using var run = PartnerApiFake.StartWithPartnerOnly();
        var network = new FailingNetwork(() => new HttpRequestException(NetworkFailure));
        run.DefaultPartnerApiUrl = null;
        run.NetworkHandler = network.CreateHandler;

        var result = await run.RunAsync("partner", "customer", "create", TestData.NewCustomerName, "--dry-run");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutTrace, "partnerApi").GetString(), Is.EqualTo(ProductionPartnerApi), result.ToString());
            Assert.That(network.Calls, Is.Empty);
        });
    }

    // One item to a page makes two pages, so a trace with one request per command fails.
    [TestCase("system list --org Acme", "GET")]
    [TestCase("system disable ABCDE", "PUT")]
    public async Task The_trace_lists_each_request_sent_in_the_order_sent(string command, string method)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        BothOrgs.Stub(run);
        run.StubPages(TestData.OrgPath("systems"), 1, ApiJson.System("ABCDE"), ApiJson.System("FGHIJ"));
        run.StubBulk("PUT", TestData.OrgPath("systems/disable"), "systemsUpdated", 1);

        var result = await run.RunAsync(command.Split(' '));

        CliAssert.Succeeded(result);
        var received = run.Requests.Select(request => Describe(request.Method, new Uri(run.ApiUrl, request.Path), request.Query)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(received, Is.Not.Empty.And.All.StartWith(method + " "));
            Assert.That(TracedRequests(result.StdoutTrace), Is.EqualTo(received), result.ToString());
        });
    }

    // tag set reads the tag before the change ("Dry run").
    [Test]
    public async Task Under_dry_run_the_trace_lists_the_reads_sent_and_the_report_lists_the_change()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("tags/web");
        run.Stub("GET", path, json: ApiJson.Tag("web"));

        var result = await run.RunAsync("tag", "set", "web", "--notes", "front end", "--dry-run");

        var (_, change) = DryRunOutput.ReadOne(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {path}" }));
            Assert.That(JsonRead.StringList(JsonAssert.Property(result.StdoutTrace, "requests")), Is.EqualTo($"GET {run.ApiBaseUrl}{path}"), result.ToString());
            Assert.That(JsonAssert.Property(change, "method").GetString(), Is.EqualTo("PATCH"), result.ToString());
        });
    }

    // With the organisation given by ID, the dry run of a bulk change sends nothing.
    [Test]
    public async Task Under_dry_run_a_command_that_sends_nothing_traces_no_requests()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("system", "disable", "ABCDE", "--dry-run");

        DryRunOutput.RequestInTestOrg(result, "PUT", "/systems/disable");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.StdoutTrace.TryGetProperty("requests", out _), Is.False, result.ToString());
        });
    }

    [TestCase("login with one organisation")]
    [TestCase("login with two organisations")]
    [TestCase("org use")]
    public async Task The_trace_lists_the_files_the_command_saved(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        using var run = CliRun.Start();
        string[] args = name == "org use" ? ["org", "use", TestData.OrgName] : ["login"];
        var expected = name switch
        {
            "login with one organisation" => $"{run.CredentialsPath},{run.CliConfigPath}",
            "login with two organisations" => run.CredentialsPath,
            _ => run.CliConfigPath,
        };

        if (name == "login with one organisation")
        {
            run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        }
        else
        {
            BothOrgs.Stub(run);
        }

        var result = await run.RunAsync(args);

        CliAssert.Succeeded(result);
        Assert.That(JsonRead.StringList(JsonAssert.Property(result.StdoutTrace, "saved")), Is.EqualTo(expected), result.ToString());
    }

    // partner use makes no call and reads no token ("Login, logout and status"), so saving is all
    // that happened.
    [Test]
    public async Task The_trace_of_partner_use_holds_only_the_file_saved()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("partner", "use", "--id", TestData.PartnerId.ToString());

        CliAssert.Succeeded(result);
        var trace = result.StdoutTrace;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(trace), Is.EqualTo(SavedOnly), result.ToString());
            Assert.That(JsonRead.StringList(JsonAssert.Property(trace, "saved")), Is.EqualTo(run.CliConfigPath), result.ToString());
        });
    }

    [Test]
    public async Task An_error_prints_the_error_then_the_trace_of_the_run()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/ABCDE");
        run.StubProblem("GET", path, 403, "Forbidden");

        var result = await run.RunAsync("system", "show", "ABCDE");

        CliAssert.Failed(result, "forbidden");
        var trace = result.StderrTrace;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(result.StderrJson), Is.EqualTo(ErrorThenTrace), result.ToString());
            Assert.That(JsonAssert.Property(JsonAssert.Property(trace, "token"), "source").GetString(), Is.EqualTo("ENCLAVE_TOKEN"), result.ToString());
            Assert.That(JsonAssert.Property(trace, "api").GetString(), Is.EqualTo(run.ApiUrl.AbsoluteUri), result.ToString());
            Assert.That(JsonRead.IdOf(JsonAssert.Property(trace, "org")), Is.EqualTo(TestData.OrgId), result.ToString());
            Assert.That(JsonRead.StringList(JsonAssert.Property(trace, "requests")), Is.EqualTo($"GET {run.ApiBaseUrl}{path}"), result.ToString());
        });
    }

    [Test]
    public async Task A_problem_response_traces_no_cause()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", TestData.OrgPath("systems/ABCDE"), 404, "Not Found", "No such system.");

        var result = await run.RunAsync("system", "show", "ABCDE");

        CliAssert.Failed(result, "not_found");
        Assert.That(result.StderrTrace.TryGetProperty("cause", out _), Is.False, result.ToString());
    }

    [TestCaseSource(nameof(FailuresWithoutProblemDetails))]
    public async Task A_failure_without_problem_details_traces_the_exception_behind_it(string failure, Type exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/ABCDE");

        switch (failure)
        {
            case "a network failure":
                run.NetworkHandler = new FailingNetwork(() => new HttpRequestException(NetworkFailure)).CreateHandler;
                break;
            case "a timeout":
                run.NetworkHandler = new FailingNetwork(() => new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.",
                    new TimeoutException("The operation was canceled."))).CreateHandler;
                break;
            default:
                run.StubRaw("GET", path, 502, "text/html", "<html><body><h1>502 Bad Gateway</h1></body></html>");
                break;
        }

        var result = await run.RunAsync("system", "show", "ABCDE");

        CliAssert.Failed(result, "transient");
        var trace = result.StderrTrace;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(trace, "cause").GetString(), Does.StartWith(exception.FullName + ": "), result.ToString());
            Assert.That(JsonRead.StringList(JsonAssert.Property(trace, "requests")), Is.EqualTo($"GET {run.ApiBaseUrl}{path}"), result.ToString());
        });
    }

    [Test]
    public async Task The_cause_of_a_network_failure_carries_its_message()
    {
        using var run = CliRun.Start();
        run.NetworkHandler = new FailingNetwork(() => new HttpRequestException(NetworkFailure)).CreateHandler;

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "transient");
        Assert.That(JsonAssert.Property(result.StderrTrace, "cause").GetString(), Is.EqualTo($"{typeof(HttpRequestException).FullName}: {NetworkFailure}"), result.ToString());
    }

    [TestCaseSource(nameof(RunsWhereNothingHappened))]
    public async Task A_run_where_nothing_happened_prints_no_trace(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var run = CliRun.Start();
        run.StdinText = CliList.Of("system");

        if (args is ["system", "list"])
        {
            run.Environment.Remove("ENCLAVE_TOKEN");
        }

        var result = await run.RunAsync(args);

        var document = result.ExitCode == 0 ? result.StdoutJson : result.StderrJson;
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonRead.PropertyNames(document), Does.Not.Contain("trace"), result.ToString());

            if (result.ExitCode != 0)
            {
                Assert.That(JsonRead.PropertyNames(document), Is.EqualTo(ErrorOnly), result.ToString());
            }
        });
    }

    [TestCaseSource(nameof(RunsThatHoldTheToken))]
    public async Task The_trace_holds_neither_the_token_nor_the_authorization_header(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));
        run.StubProblem("GET", TestData.OrgPath("systems/ABCDE"), 404, "Not Found");

        if (args.Contains("key"))
        {
            run.NetworkHandler = new FailingNetwork(() => new HttpRequestException(NetworkFailure)).CreateHandler;
        }

        var result = await run.RunAsync(args);

        var trace = result.ExitCode == 0 ? result.StdoutTrace : result.StderrTrace;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(trace, "token").GetRawText(), Does.Not.Contain(TestData.Token), result.ToString());
            Assert.That(result.Stdout + result.Stderr, Does.Not.Contain("Authorization").IgnoreCase, result.ToString());
            Assert.That(result.Stdout + result.Stderr, Does.Not.Contain("Bearer").IgnoreCase, result.ToString());
            TokenAssert.Absent(result);
        });
    }

    private static TestCaseData Document(string fields, params string[] args) =>
        new TestCaseData(args, fields).SetArgDisplayNames(string.Join(' ', args));

    // A request as "METHOD scheme://host:port/path?query", with the query parameters in name order,
    // so a URL with its parameters in another order compares equal.
    private static string Describe(string method, Uri url, IReadOnlyDictionary<string, string> query)
    {
        var parameters = string.Join("&", query.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
        return $"{method} {url.GetLeftPart(UriPartial.Path)}?{parameters}";
    }

    private static string[] TracedRequests(JsonElement trace) =>
        JsonAssert.Strings(JsonAssert.Property(trace, "requests"))
            .Select(request =>
            {
                var words = request.Split(' ');
                Assert.That(words, Has.Length.EqualTo(2), request);
                var url = new Uri(words[1]);
                var query = HttpUtility.ParseQueryString(url.Query);
                return Describe(words[0], url, query.AllKeys.OfType<string>().ToDictionary(key => key, key => query[key] ?? string.Empty, StringComparer.Ordinal));
            })
            .ToArray();
}
