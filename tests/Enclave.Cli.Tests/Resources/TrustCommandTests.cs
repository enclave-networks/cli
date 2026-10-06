using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

[Category(TestCategory.Pending)]
public class TrustCommandTests
{
    // A TrustRequirementCreateModel for a user authentication requirement, for the create that
    // follows a rejected create without --from-file. The configuration and condition keys are the
    // ones the API's validator accepts for the azure authority (portal
    // TrustRequirementSettingsUserAuthValidator.cs).
    private const string TrustCreateFile = """
        {
          "description": "Signed in to Microsoft",
          "type": "UserAuthentication",
          "notes": "Created from a file",
          "settings": {
            "configuration": { "authority": "azure", "tenantId": "6d5f1c2a-0b7e-4f39-9d1a-2c3b4e5f6a7b" },
            "conditions": [{ "claim": "groups", "value": "engineering" }]
          }
        }
        """;

    [Test]
    public async Task Trust_list_gets_one_page_of_trust_requirements_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(3), ApiJson.Trust(4)));

        var result = await run.RunAsync("trust", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements")));
            Assert.That(request.QueryValue("per_page"), Is.EqualTo("100"));
            Assert.That(request.QueryValue("search"), Is.Null);
            Assert.That(request.QueryValue("sort"), Is.Null);
            Assert.That(JsonRead.IntFieldList(JsonAssert.Property(output, "items"), "id"), Is.EqualTo("3,4"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task Trust_list_sends_the_search_option_as_the_search_query_parameter()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(3)));

        var result = await run.RunAsync("trust", "list", "--search", "mfa");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements")));
            Assert.That(request.QueryValue("search"), Is.EqualTo("mfa"));
        });
    }

    // Enum option values take the API's names, matched ignoring case, and the API receives its
    // own spelling (proposal, "Options on every command"). TrustRequirementSortOrder has the
    // values Description and RecentlyCreated (Enclave.Configuration.Data,
    // Modules/TrustRequirements/Enums).
    [TestCase("Description", "Description")]
    [TestCase("DESCRIPTION", "Description")]
    [TestCase("RecentlyCreated", "RecentlyCreated")]
    [TestCase("recentlycreated", "RecentlyCreated")]
    public async Task Trust_list_sends_the_sort_order_named_by_the_sort_option_matched_ignoring_case(string value, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(3)));

        var result = await run.RunAsync("trust", "list", "--sort", value);

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements")));
            Assert.That(request.QueryValue("sort"), Is.EqualTo(expected));
        });
    }

    // Alphabetical is a tag sort order; the trust requirement list does not have it.
    [Test]
    public async Task Trust_list_rejects_a_sort_order_the_api_does_not_have_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(3)));

        var result = await run.RunAsync("trust", "list", "--sort", "Alphabetical");

        CliAssert.Rejected(run, result);
        var request = await CliAssert.AcceptedAsync(run, "GET", TestData.OrgPath("trust-requirements"), "trust", "list", "--sort", "Description");
        Assert.That(request.QueryValue("sort"), Is.EqualTo("Description"));
    }

    // The API's trust requirement list takes search and sort only (Enclave.Sdk.Api 1.0.4,
    // TrustRequirementsClient.GetTrustRequirementsAsync), and the proposal gives trust list
    // --search and --sort.
    [Test]
    public async Task Trust_list_rejects_include_disabled_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(3)));

        var result = await run.RunAsync("trust", "list", "--include-disabled");

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "GET", TestData.OrgPath("trust-requirements"), "trust", "list");
    }

    [Test]
    public async Task Trust_list_with_output_id_prints_one_trust_requirement_id_per_line()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(3), ApiJson.Trust(4)));

        var result = await run.RunAsync("trust", "list", "-o", "id");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements")));
            Assert.That(string.Join(",", result.StdoutLines), Is.EqualTo("3,4"));
        });
    }

    [Test]
    public async Task Trust_show_gets_the_trust_requirement_and_prints_its_model()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements/3"), json: ApiJson.Trust(3));

        var result = await run.RunAsync("trust", "show", "3");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements/3")));
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task Trust_update_patches_the_description_and_notes_and_prints_the_updated_requirement()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("trust-requirements/3"), json: ApiJson.Trust(3, "Require MFA"));

        var result = await run.RunAsync("trust", "update", "3", "--description", "Require MFA", "--notes", "Reviewed");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements/3")));
            Assert.That(JsonAssert.Property(body, "Description").GetString(), Is.EqualTo("Require MFA"));
            Assert.That(JsonAssert.Property(body, "Notes").GetString(), Is.EqualTo("Reviewed"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("Require MFA"));
        });
    }

    // A patch sets the fields present and leaves absent fields as they are (proposal, "Create and
    // update").
    [Test]
    public async Task Trust_update_sends_only_the_fields_whose_options_are_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("trust-requirements/3"), json: ApiJson.Trust(3));

        var result = await run.RunAsync("trust", "update", "3", "--description", "Require MFA");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.That(JsonRead.PropertyNameList(request.BodyJson), Is.EqualTo("Description").IgnoreCase);
    }

    // trust update takes --description and --notes (proposal, "Command options"); an update with
    // neither has nothing to send.
    [Test]
    public async Task Trust_update_without_any_field_option_exits_2_without_sending_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("trust-requirements/3"), json: ApiJson.Trust(3));

        var result = await run.RunAsync("trust", "update", "3");

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "PATCH", TestData.OrgPath("trust-requirements/3"), "trust", "update", "3", "--notes", "Reviewed");
    }

    // A command that accepts several IDs always makes the bulk call, also for one ID, and prints
    // { requested, affected } (proposal, "Several IDs").
    [Test]
    public async Task Trust_delete_with_yes_sends_one_id_to_the_bulk_delete()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("trust-requirements"), json: ApiJson.Bulk("requirementsDeleted", 1));

        var result = await run.RunAsync("trust", "delete", "3", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements")));
            Assert.That(JsonRead.IntList(JsonAssert.Property(request.BodyJson, "requirementIds")), Is.EqualTo("3"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(1));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Trust_delete_with_yes_sends_every_id_in_one_bulk_delete()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("trust-requirements"), json: ApiJson.Bulk("requirementsDeleted", 2));

        var result = await run.RunAsync("trust", "delete", "3", "4", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("trust-requirements")));
            Assert.That(JsonRead.IntList(JsonAssert.Property(request.BodyJson, "requirementIds")), Is.EqualTo("3,4"));
            Assert.That(JsonAssert.Property(output, "requested").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "affected").GetInt32(), Is.EqualTo(2));
        });
    }

    // delete cannot be undone, so it needs --yes (proposal, "Confirmation").
    [Test]
    public async Task Trust_delete_without_yes_exits_6_naming_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("trust-requirements"), json: ApiJson.Bulk("requirementsDeleted", 1));

        var result = await run.RunAsync("trust", "delete", "3");

        CliAssert.Rejected(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    // --dry-run prints the request Enclave.Sdk.Api would send and sends nothing (proposal, "Dry
    // run").
    [TestCase("trust update 3 --notes x --dry-run", "PATCH", "trust-requirements/3")]
    [TestCase("trust delete 3 --dry-run", "DELETE", "trust-requirements")]
    public async Task Trust_change_commands_with_dry_run_print_the_request_and_send_nothing(string commandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();

        var result = await run.RunAsync(commandLine.Split(' '));

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var output = result.StdoutJson;
        var request = JsonAssert.Property(output, "request");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo(method));
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(TestData.OrgPath(path)));
        });
    }

    // Trust requirements are created from a file only: their trust conditions are nested, and
    // flags can express only part of them (proposal, "Command options"). The same create with
    // --from-file is then sent.
    [TestCase("trust create")]
    [TestCase("trust create --description MFA")]
    public async Task Trust_create_without_from_file_exits_2_without_sending_a_request(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("trust-requirements"), json: ApiJson.Trust(3));
        var filePath = run.WriteFile("trust.json", TrustCreateFile);

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "POST", TestData.OrgPath("trust-requirements"), "trust", "create", "--from-file", filePath);
    }

    // Trust requirement IDs are integers, and every ID is checked before any call because
    // Enclave.Sdk.Api 1.0.4 puts IDs into URL paths unescaped (proposal, "ID checks"). The same
    // command with integer IDs is then sent.
    [TestCase("trust show abc", "trust show 3", "GET", "trust-requirements/3")]
    [TestCase("trust show ../policies", "trust show 3", "GET", "trust-requirements/3")]
    [TestCase("trust update x3 --notes x", "trust update 3 --notes x", "PATCH", "trust-requirements/3")]
    [TestCase("trust delete 3 three --yes", "trust delete 3 4 --yes", "DELETE", "trust-requirements")]
    public async Task Trust_commands_reject_an_id_that_is_not_an_integer_without_sending_a_request(
        string commandLine, string acceptedCommandLine, string method, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements/3"), json: ApiJson.Trust(3));
        run.Stub("PATCH", TestData.OrgPath("trust-requirements/3"), json: ApiJson.Trust(3));
        run.Stub("DELETE", TestData.OrgPath("trust-requirements"), json: ApiJson.Bulk("requirementsDeleted", 2));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, method, TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }

    // Single-ID commands exit 5 for an unknown ID (proposal, "Several IDs").
    [TestCase("trust show 3", "GET")]
    [TestCase("trust update 3 --notes x", "PATCH")]
    public async Task Trust_single_id_commands_exit_5_when_the_api_reports_not_found(string commandLine, string method)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.StubProblem(method, TestData.OrgPath("trust-requirements/3"), 404, "Not Found", "Trust requirement 3 does not exist.");

        var result = await run.RunAsync(commandLine.Split(' '));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(5), result.Stderr);
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("not_found"));
            Assert.That(run.Requests, Has.Count.EqualTo(1));
        });
    }

    // --dry-run and --yes exist only on commands that change something; elsewhere they are unknown
    // options, which exit 2. The same command without the option is then sent.
    [TestCase("trust list --yes", "trust list", "trust-requirements")]
    [TestCase("trust list --dry-run", "trust list", "trust-requirements")]
    [TestCase("trust show 3 --dry-run", "trust show 3", "trust-requirements/3")]
    public async Task Trust_read_commands_reject_change_options_without_sending_a_request(string commandLine, string acceptedCommandLine, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("trust-requirements"), json: ApiJson.Page(ApiJson.Trust(3)));
        run.Stub("GET", TestData.OrgPath("trust-requirements/3"), json: ApiJson.Trust(3));

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Rejected(run, result);
        await CliAssert.AcceptedAsync(run, "GET", TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }
}
