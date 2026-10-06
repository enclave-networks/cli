using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Enclave.Api.Modules.AccountManagement.PublicAccount.Models;
using Enclave.Api.Modules.SystemManagement.Systems.Models;
using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Identifiers;
using Enclave.Sdk.Api;
using Enclave.Sdk.Api.Clients.Interfaces;
using Enclave.Sdk.Api.Exceptions;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Support;

// The tests in this project trust two things this fixture proves: that every ApiJson body is JSON
// Enclave.Sdk.Api 1.0.4 reads into the model it names, field for field, and that a CliRun confines
// the CLI to its own environment, home directory and fake API.
public class HarnessTests
{
    private const string SystemId = "ABCDE";

    private const int ResourceId = 7;

    // The options Enclave.Sdk.Api 1.0.4 reads and writes JSON with (Constants.JsonSerializerOptions,
    // which is internal to the package).
    private static readonly JsonSerializerOptions SdkJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    // The options EnclaveClient.GetSettingsFile reads credentials.json with (Enclave.Sdk.Api 1.0.4).
    private static readonly JsonSerializerOptions CredentialsJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IEnumerable<TestCaseData> ResourceBodies()
    {
        yield return Resource(
            nameof(ApiJson.System),
            ApiJson.System(SystemId, "db"),
            "systems",
            async org => await FirstAsync((await org.EnrolledSystems.GetSystemsAsync()).Items),
            $"systems/{SystemId}",
            async org => await org.EnrolledSystems.GetAsync(SystemId));

        yield return Resource(
            nameof(ApiJson.PendingSystem),
            ApiJson.PendingSystem(SystemId, "db"),
            "unapproved-systems",
            async org => await FirstAsync((await org.UnapprovedSystems.GetSystemsAsync()).Items),
            $"unapproved-systems/{SystemId}",
            async org => await org.UnapprovedSystems.GetAsync(SystemId));

        yield return Resource(
            nameof(ApiJson.Key),
            ApiJson.Key(ResourceId, "laptops", "KEY-123"),
            "enrolment-keys",
            async org => await FirstAsync((await org.EnrolmentKeys.GetEnrolmentKeysAsync()).Items),
            $"enrolment-keys/{ResourceId}",
            async org => await org.EnrolmentKeys.GetAsync(EnrolmentKeyId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Policy),
            ApiJson.Policy(ResourceId, "admins"),
            "policies",
            async org => await FirstAsync((await org.Policies.GetPoliciesAsync()).Items),
            $"policies/{ResourceId}",
            async org => await org.Policies.GetAsync(PolicyId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Tag),
            ApiJson.Tag("servers"),
            "tags",
            async org => await FirstAsync((await org.Tags.GetAsync()).Items),
            "tags/servers",
            async org => await org.Tags.GetAsync("servers"));

        yield return Resource(
            nameof(ApiJson.Zone),
            ApiJson.Zone(ResourceId, "corp"),
            "dns/zones",
            async org => await FirstAsync((await org.Dns.GetZonesAsync()).Items),
            $"dns/zones/{ResourceId}",
            async org => await org.Dns.GetZoneAsync(DnsZoneId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Record),
            ApiJson.Record(ResourceId, "db"),
            "dns/records",
            async org => await FirstAsync((await org.Dns.GetRecordsAsync()).Items),
            $"dns/records/{ResourceId}",
            async org => await org.Dns.GetRecordAsync(DnsRecordId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Trust),
            ApiJson.Trust(ResourceId, "staff"),
            "trust-requirements",
            async org => await FirstAsync((await org.TrustRequirements.GetTrustRequirementsAsync()).Items),
            $"trust-requirements/{ResourceId}",
            async org => await org.TrustRequirements.GetAsync(TrustRequirementId.FromInt(ResourceId)));

        yield return Resource(
            nameof(ApiJson.Log),
            ApiJson.Log("System ABCDE enrolled"),
            "logs",
            async org => await FirstAsync((await org.Logs.GetLogsAsync()).Items),
            getPath: null,
            readOne: null);
    }

    public static IEnumerable<TestCaseData> BulkCalls()
    {
        yield return Bulk("systemsRevoked", "DELETE", "systems", org => org.EnrolledSystems.RevokeSystemsAsync(SystemId));
        yield return Bulk("systemsUpdated", "PUT", "systems/enable", org => org.EnrolledSystems.BulkEnableAsync(SystemId));
        yield return Bulk("systemsUpdated", "PUT", "systems/disable", org => org.EnrolledSystems.BulkDisableAsync(SystemId));
        yield return Bulk("systemsApproved", "PUT", "unapproved-systems/approve", org => org.UnapprovedSystems.ApproveSystemsAsync(SystemId));
        yield return Bulk("systemsDeclined", "DELETE", "unapproved-systems", org => org.UnapprovedSystems.DeclineSystems(SystemId));
        yield return Bulk("keysModified", "PUT", "enrolment-keys/enable", org => org.EnrolmentKeys.BulkEnableAsync(EnrolmentKeyId.FromInt(ResourceId)));
        yield return Bulk("keysModified", "PUT", "enrolment-keys/disable", org => org.EnrolmentKeys.BulkDisableAsync(EnrolmentKeyId.FromInt(ResourceId)));
        yield return Bulk("policiesDeleted", "DELETE", "policies", org => org.Policies.DeletePoliciesAsync(PolicyId.FromInt(ResourceId)));
        yield return Bulk("policiesUpdated", "PUT", "policies/enable", org => org.Policies.EnablePoliciesAsync(PolicyId.FromInt(ResourceId)));
        yield return Bulk("policiesUpdated", "PUT", "policies/disable", org => org.Policies.DisablePoliciesAsync(PolicyId.FromInt(ResourceId)));
        yield return Bulk("tagsDeleted", "DELETE", "tags", org => org.Tags.DeleteTagsAsync("servers"));
        yield return Bulk("dnsRecordsDeleted", "DELETE", "dns/records", org => org.Dns.DeleteRecordsAsync(DnsRecordId.FromInt(ResourceId)));
        yield return Bulk("requirementsDeleted", "DELETE", "trust-requirements", org => org.TrustRequirements.DeleteTrustRequirementsAsync(TrustRequirementId.FromInt(ResourceId)));
    }

    // Each resource body is served as a list item and as the single resource, and read through the
    // SDK calls a command makes. Writing each model back and comparing it with the body proves every
    // property name and value type: a misspelt name leaves the model's property at its default,
    // which the comparison reports, and a wrong value type fails the read.
    [TestCaseSource(nameof(ResourceBodies))]
    public async Task Resource_body_reads_through_the_sdk_list_and_get_calls_and_writes_back_unchanged(
        string body,
        string listPath,
        Func<IOrganisationClient, Task<object>> readItem,
        string? getPath,
        Func<IOrganisationClient, Task<object>>? readOne)
    {
        ArgumentNullException.ThrowIfNull(listPath);
        ArgumentNullException.ThrowIfNull(readItem);

        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath(listPath), json: ApiJson.Page(body));

        if (getPath is not null)
        {
            run.Stub("GET", TestData.OrgPath(getPath), json: body);
        }

        var org = ConnectOrganisation(run);

        var models = new List<object> { await readItem(org) };

        if (readOne is not null)
        {
            models.Add(await readOne(org));
        }

        await AssertBodyMatchesModelsAsync(body, models);
    }

    [Test]
    public async Task Page_body_reads_as_a_paginated_response_whose_metadata_follows_the_api()
    {
        using var run = CliRun.Start();
        var body = ApiJson.PageOf(5, ApiJson.System("A"), ApiJson.System("B"));
        run.Stub("GET", TestData.OrgPath("systems"), json: body);

        var page = await ConnectOrganisation(run).EnrolledSystems.GetSystemsAsync();

        var ids = new List<string>();
        await foreach (var item in page.Items)
        {
            ids.Add(item.SystemId);
        }

        var json = Parse(body);

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", ids), Is.EqualTo("A,B"));
            Assert.That(page.Metadata.Total, Is.EqualTo(5));
            Assert.That(page.Metadata.FirstPage, Is.Zero);
            Assert.That(page.Metadata.LastPage, Is.EqualTo(2));
            Assert.That(page.Metadata.NextPage, Is.EqualTo(1));
            Assert.That(page.Metadata.PrevPage, Is.Null);
            AssertSameJson(Write(page.Metadata), json.GetProperty("metadata"));
            AssertSameJson(Write(page.Links), json.GetProperty("links"));
        });
    }

    [Test]
    public async Task Single_page_body_has_a_total_equal_to_its_item_count_and_no_next_page()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page(ApiJson.System("A"), ApiJson.System("B")));

        var page = await ConnectOrganisation(run).EnrolledSystems.GetSystemsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.Metadata.Total, Is.EqualTo(2));
            Assert.That(page.Metadata.LastPage, Is.Zero);
            Assert.That(page.Metadata.NextPage, Is.Null);
            Assert.That(page.Links.Next, Is.Null);
        });
    }

    [Test]
    public async Task Orgs_body_reads_through_GetOrganisationsAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName));
        run.Stub("GET", "/account/orgs", json: body);

        var orgs = await Connect(run).GetOrganisationsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(orgs.Select(org => org.OrgId), Is.EqualTo(new[] { OrganisationGuid.FromGuid(TestData.OrgId), OrganisationGuid.FromGuid(TestData.OtherOrgId) }));
            Assert.That(orgs.Select(org => org.OrgName), Is.EqualTo(new[] { TestData.OrgName, TestData.OtherOrgName }));
            AssertSameJson(Write(orgs), Parse(body).GetProperty("orgs"));
        });
    }

    [Test]
    public async Task OrgProperties_body_reads_through_the_organisation_GetAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.OrgProperties(TestData.OrgName);
        run.Stub("GET", TestData.OrgPath(), json: body);

        var properties = await ConnectOrganisation(run).GetAsync();

        Assert.That(properties, Is.Not.Null);
        await AssertBodyMatchesModelsAsync(body, [properties!]);
    }

    [Test]
    public async Task Users_body_reads_through_GetOrganisationUsersAsync()
    {
        using var run = CliRun.Start();
        var userId = Guid.NewGuid();
        var body = ApiJson.Users((userId, "ops@acme.example"));
        run.Stub("GET", TestData.OrgPath("users"), json: body);

        var users = await ConnectOrganisation(run).GetOrganisationUsersAsync();

        Assert.Multiple(() =>
        {
            Assert.That(users.Single().Id, Is.EqualTo(AccountGuid.FromGuid(userId)));
            Assert.That(users.Single().EmailAddress, Is.EqualTo("ops@acme.example"));
            AssertSameJson(Write(users), Parse(body).GetProperty("users"));
        });
    }

    [Test]
    public async Task Invites_body_reads_through_GetPendingInvitesAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.Invites("new@acme.example", "other@acme.example");
        run.Stub("GET", TestData.OrgPath("invites"), json: body);

        var invites = await ConnectOrganisation(run).GetPendingInvitesAsync();

        Assert.Multiple(() =>
        {
            Assert.That(string.Join(",", invites.Select(invite => invite.EmailAddress)), Is.EqualTo("new@acme.example,other@acme.example"));
            AssertSameJson(Write(invites), Parse(body).GetProperty("invites"));
        });
    }

    [Test]
    public async Task DnsSummary_body_reads_through_GetPropertiesSummaryAsync()
    {
        using var run = CliRun.Start();
        var body = ApiJson.DnsSummary();
        run.Stub("GET", TestData.OrgPath("dns"), json: body);

        var summary = await ConnectOrganisation(run).Dns.GetPropertiesSummaryAsync();

        await AssertBodyMatchesModelsAsync(body, [summary]);
    }

    [TestCaseSource(nameof(BulkCalls))]
    public async Task Bulk_body_reads_through_the_sdk_bulk_call(string field, string method, string path, Func<IOrganisationClient, Task<int>> call)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(call);

        using var run = CliRun.Start();
        run.Stub(method, TestData.OrgPath(path), json: ApiJson.Bulk(field, 3));

        var count = await call(ConnectOrganisation(run));

        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public void StubProblem_reaches_the_sdk_as_problem_details()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", TestData.OrgPath($"systems/{SystemId}"), 404, "Not Found", "No such system.");

        var ex = Assert.ThrowsAsync<EnclaveApiException>(() => ConnectOrganisation(run).EnrolledSystems.GetAsync(SystemId));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.ProblemDetails.Status, Is.EqualTo(404));
            Assert.That(ex.ProblemDetails.Title, Is.EqualTo("Not Found"));
            Assert.That(ex.ProblemDetails.Detail, Is.EqualTo("No such system."));
        });
    }

    [Test]
    public async Task Requests_record_method_path_query_body_and_authorization_in_order()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.Page());
        run.Stub("DELETE", TestData.OrgPath("systems"), json: ApiJson.Bulk("systemsRevoked", 2));
        var org = ConnectOrganisation(run);

        await org.EnrolledSystems.GetSystemsAsync(searchTerm: "a,b c", pageNumber: 2);
        await org.EnrolledSystems.RevokeSystemsAsync("A", "B");

        var requests = run.Requests;

        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].Method, Is.EqualTo("GET"));
            Assert.That(requests[0].Path, Is.EqualTo(TestData.OrgPath("systems")));
            Assert.That(requests[0].Query["search"], Is.EqualTo("a,b c"));
            Assert.That(requests[0].Query["page"], Is.EqualTo("2"));
            Assert.That(requests[0].Authorization, Is.EqualTo($"Bearer {TestData.Token}"));
            Assert.That(requests[1].Method, Is.EqualTo("DELETE"));
            Assert.That(string.Join(",", JsonAssert.Strings(JsonAssert.Property(requests[1].BodyJson, "systemIds"))), Is.EqualTo("A,B"));
        });
    }

    [Test]
    public void SingleRequest_fails_the_test_unless_exactly_one_request_arrived()
    {
        using var run = CliRun.Start();

        Assert.Throws<AssertionException>(() => run.SingleRequest());
    }

    [Test]
    public async Task StubWithQuery_takes_precedence_over_Stub_for_the_same_path()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("systems"), json: ApiJson.PageOf(3, ApiJson.System("A")));
        run.StubWithQuery("GET", TestData.OrgPath("systems"), "page", "1", json: ApiJson.PageOf(3, ApiJson.System("B")));
        var systems = ConnectOrganisation(run).EnrolledSystems;

        var first = await FirstAsync((await systems.GetSystemsAsync(pageNumber: 0)).Items);
        var second = await FirstAsync((await systems.GetSystemsAsync(pageNumber: 1)).Items);

        Assert.Multiple(() =>
        {
            Assert.That(((SystemSummaryModel)first).SystemId, Is.EqualTo("A"));
            Assert.That(((SystemSummaryModel)second).SystemId, Is.EqualTo("B"));
        });
    }

    [Test]
    public void SaveCredentials_writes_the_file_Enclave_Sdk_Api_reads()
    {
        using var run = CliRun.Start();

        run.SaveCredentials("saved-token");

        var options = JsonSerializer.Deserialize<EnclaveClientOptions>(run.Files.ReadText(run.CredentialsPath)!, CredentialsJsonOptions);

        Assert.Multiple(() =>
        {
            Assert.That(run.CredentialsPath, Is.EqualTo(Path.Combine(run.Home, ".enclave", "credentials.json")));
            Assert.That(options!.PersonalAccessToken, Is.EqualTo("saved-token"));
            Assert.That(options.BaseUrl, Is.EqualTo(run.ApiBaseUrl));
            Assert.That(run.Files.IsPrivate(run.CredentialsPath), Is.True);
        });
    }

    // The CLI must never see the environment of the process running the tests, which on a developer
    // machine can hold a real token. A variable set in the process environment is invisible to the
    // host CliRun builds, and the host reads values from CliRun.Environment alone.
    [Test]
    public void Host_sees_only_the_run_environment_home_directory_stdin_and_fake_api()
    {
        const string ProbeName = "ENCLAVE_CLI_HARNESS_PROBE";
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_EXTRA"] = "extra";
        run.Environment["ENCLAVE_ORG"] = null;
        run.StdinText = "line one\nline two\n";
        run.StdinIsTerminal = true;

        Environment.SetEnvironmentVariable(ProbeName, "from-process");
        try
        {
            var host = run.CreateHost();

            Assert.Multiple(() =>
            {
                Assert.That(host.GetEnvironmentVariable(ProbeName), Is.Null);
                Assert.That(host.GetEnvironmentVariable("PATH"), Is.Null);
                Assert.That(host.GetEnvironmentVariable("ENCLAVE_TOKEN"), Is.EqualTo(TestData.Token));
                Assert.That(host.GetEnvironmentVariable("ENCLAVE_EXTRA"), Is.EqualTo("extra"));
                Assert.That(host.GetEnvironmentVariable("ENCLAVE_ORG"), Is.Null);
                Assert.That(host.HomeDirectory, Is.EqualTo(run.Home));
                Assert.That(host.HomeDirectory, Does.StartWith(Path.GetTempPath()));
                Assert.That(Directory.Exists(host.HomeDirectory), Is.False);
                Assert.That(host.Files, Is.SameAs(run.Files));
                Assert.That(run.Files.Paths, Is.Empty);
                Assert.That(host.Stdin.ReadToEnd(), Is.EqualTo("line one\nline two\n"));
                Assert.That(host.StdinIsTerminal, Is.True);
                Assert.That(host.DefaultApiUrl, Is.EqualTo(run.ApiUrl));
                Assert.That(host.DefaultApiUrl.Host, Is.EqualTo("127.0.0.1"));
            });

            // The fake API must accept connections from this machine only. WireMock.Net reports its
            // URL as http://localhost:{port} when started on 127.0.0.1 (version 2.18.0), so the
            // reported URL does not show where it listens; the open sockets on the port do.
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                .Where(endpoint => endpoint.Port == run.ApiUrl.Port)
                .ToArray();

            Assert.That(listeners, Is.Not.Empty);
            Assert.That(listeners.Where(endpoint => !IPAddress.IsLoopback(endpoint.Address)), Is.Empty);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProbeName, null);
        }
    }

    [Test]
    public void Run_environment_defaults_to_the_test_token_and_organisation()
    {
        using var run = CliRun.Start();

        Assert.Multiple(() =>
        {
            Assert.That(run.Environment["ENCLAVE_TOKEN"], Is.EqualTo(TestData.Token));
            Assert.That(run.Environment["ENCLAVE_ORG"], Is.EqualTo(TestData.OrgId.ToString()));
            Assert.That(run.StdinText, Is.Empty);
            Assert.That(run.StdinIsTerminal, Is.False);
        });
    }

    // Tests keep their files in memory, so the harness writes nothing to the disk: the files a test
    // sets up are in run.Files and no directory appears at the home or work path.
    [Test]
    public void Harness_files_live_in_memory_and_never_on_disk()
    {
        using var run = CliRun.Start();

        var input = run.WriteFile("policy.json", "{}");
        run.SaveCredentials(TestData.Token);

        Assert.Multiple(() =>
        {
            Assert.That(run.Files.ReadText(input), Is.EqualTo("{}"));
            Assert.That(run.Files.IsPrivate(input), Is.False);
            Assert.That(run.Files.Exists(run.CredentialsPath), Is.True);
            Assert.That(Directory.Exists(run.Home), Is.False);
            Assert.That(Directory.Exists(run.WorkDirectory), Is.False);
        });
    }

    // RunAsync fails a run that wrote to the disk under the run's paths, so a CLI that goes around
    // CliHost.Files cannot pass a test. The directory is made here to stand in for such a write.
    [Test]
    public void RunAsync_fails_when_a_directory_appears_on_disk_under_the_run()
    {
        using var run = CliRun.Start();
        Directory.CreateDirectory(run.Home);

        Assert.That(async () => await run.RunAsync("--version"), Throws.TypeOf<AssertionException>());
    }

    [Test]
    public async Task RunAsync_captures_stdout_and_the_exit_code()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("--version");

        var expected = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
            .InformationalVersion;

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero);
            Assert.That(result.StdoutLines, Is.EqualTo(new[] { expected }));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task RunAsync_captures_stderr_and_a_nonzero_exit_code()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("--no-such-option");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.Zero);
            Assert.That(result.Stderr, Does.Contain("--no-such-option"));
        });
    }

    [Test]
    public void Result_json_accessors_fail_the_test_when_the_output_is_not_json()
    {
        var result = new CliResult(1, "not json", "first\nsecond\n");

        Assert.Multiple(() =>
        {
            Assert.Throws<AssertionException>(() => _ = result.StdoutJson);
            Assert.Throws<AssertionException>(() => _ = result.StderrJson);
            Assert.Throws<AssertionException>(() => _ = result.Error);
        });
    }

    [Test]
    public void Result_error_returns_the_error_object_of_the_single_stderr_line()
    {
        var result = new CliResult(5, string.Empty, "{\"error\":{\"code\":\"not_found\",\"status\":404}}\r\n");

        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("not_found"));
            Assert.That(JsonAssert.Property(result.Error, "Status").GetInt32(), Is.EqualTo(404));
            Assert.That(result.StderrJson, Has.Count.EqualTo(1));
        });
    }

    private static TestCaseData Resource(
        string name,
        string body,
        string listPath,
        Func<IOrganisationClient, Task<object>> readItem,
        string? getPath,
        Func<IOrganisationClient, Task<object>>? readOne) =>
        new TestCaseData(body, listPath, readItem, getPath, readOne).SetArgDisplayNames(name);

    private static TestCaseData Bulk(string field, string method, string path, Func<IOrganisationClient, Task<int>> call) =>
        new TestCaseData(field, method, path, call).SetArgDisplayNames(field, method, path);

    private static EnclaveClient Connect(CliRun run) => new(new EnclaveClientOptions
    {
        BaseUrl = run.ApiBaseUrl,
        PersonalAccessToken = TestData.Token,
    });

    private static IOrganisationClient ConnectOrganisation(CliRun run) =>
        Connect(run).CreateOrganisationClient(new AccountOrganisationModel(
            OrganisationGuid.FromGuid(TestData.OrgId),
            TestData.OrgName,
            UserOrganisationRole.Owner,
            partnerAccess: false));

    private static async Task<object> FirstAsync<T>(IAsyncEnumerable<T> items)
        where T : notnull
    {
        await foreach (var item in items)
        {
            return item;
        }

        throw new AssertionException("Expected at least one item in the page.");
    }

    // Every property each model writes must be in the body with the same value, and every property
    // in the body must be one that at least one of the models writes. A body serves both the list
    // item and the single resource, so a property only one of them has is allowed.
    private static async Task AssertBodyMatchesModelsAsync(string body, IReadOnlyList<object> models)
    {
        var expected = Parse(body);
        var written = new HashSet<string>(StringComparer.Ordinal);
        var mismatches = new List<string>();

        foreach (var model in models)
        {
            var actual = await WriteAsync(model);

            foreach (var property in actual.EnumerateObject())
            {
                written.Add(property.Name);

                if (!expected.TryGetProperty(property.Name, out var value))
                {
                    mismatches.Add($"The body lacks \"{property.Name}\", which {model.GetType().Name} writes as {property.Value.GetRawText()}.");
                }
                else if (!SameJson(value, property.Value))
                {
                    mismatches.Add($"{model.GetType().Name}.{property.Name} reads {value.GetRawText()} from the body and writes {property.Value.GetRawText()}.");
                }
            }
        }

        foreach (var property in expected.EnumerateObject().Where(property => !written.Contains(property.Name)))
        {
            mismatches.Add($"No model reads the body's \"{property.Name}\".");
        }

        Assert.That(mismatches, Is.Empty);
    }

    private static void AssertSameJson(JsonElement actual, JsonElement expected) =>
        Assert.That(SameJson(actual, expected), Is.True, $"Expected {expected.GetRawText()}{Environment.NewLine}but the model writes {actual.GetRawText()}");

    // Objects compare by property name whatever the order, since a model writes its properties in
    // declaration order and the bodies list them in their own order. Strings compare by value, so an
    // escape sequence and the character it stands for are equal.
    private static bool SameJson(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        switch (left.ValueKind)
        {
            case JsonValueKind.Object:
                var leftProperties = left.EnumerateObject().ToList();

                return leftProperties.Count == right.EnumerateObject().Count()
                    && leftProperties.All(property => right.TryGetProperty(property.Name, out var other) && SameJson(property.Value, other));

            case JsonValueKind.Array:
                var leftItems = left.EnumerateArray().ToList();
                var rightItems = right.EnumerateArray().ToList();

                return leftItems.Count == rightItems.Count
                    && leftItems.Zip(rightItems).All(pair => SameJson(pair.First, pair.Second));

            case JsonValueKind.String:
                return string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                return left.GetDecimal() == right.GetDecimal();

            default:
                return true;
        }
    }

    private static async Task<JsonElement> WriteAsync(object model)
    {
        // Some models hold IAsyncEnumerable properties, which only the asynchronous serializer writes.
        using var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, model, model.GetType(), SdkJsonOptions);
        stream.Position = 0;
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static JsonElement Write<T>(T value) => JsonSerializer.SerializeToElement(value, SdkJsonOptions);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
