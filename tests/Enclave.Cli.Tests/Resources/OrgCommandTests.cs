using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

public class OrgCommandTests
{
    private const string AliceId = "6f1c2a52-8a3e-4d7b-9a51-0c3d2e4f5a6b";

    private const string BobId = "0b7e4d19-3c2a-4f6e-8d15-a9b8c7d6e5f4";

    [Test]
    public async Task Org_show_gets_the_organisation_properties_and_prints_them()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));

        var result = await run.RunAsync("org", "show");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath()));
            Assert.That(JsonAssert.Property(output, "id").GetString(), Is.EqualTo(TestData.OrgId.ToString("D", CultureInfo.InvariantCulture)).IgnoreCase);
            Assert.That(JsonAssert.Property(output, "name").GetString(), Is.EqualTo(TestData.OrgName));
        });
    }

    // The flags set the OrganisationPatchModel fields of the same name: Name, Website and Phone
    // (portal Enclave.Api/Modules/OrganisationManagement/Organisation/Models/OrganisationPatchModel.cs).
    [Test]
    public async Task Org_update_patches_the_name_website_and_phone_and_prints_the_updated_organisation()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties("Acme Ltd"));

        var result = await run.RunAsync("org", "update", "--name", "Acme Ltd", "--website", "https://acme.example", "--phone", "+44 20 7946 0000");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath()));
            Assert.That(JsonAssert.Property(body, "Name").GetString(), Is.EqualTo("Acme Ltd"));
            Assert.That(JsonAssert.Property(body, "Website").GetString(), Is.EqualTo("https://acme.example"));
            Assert.That(JsonAssert.Property(body, "Phone").GetString(), Is.EqualTo("+44 20 7946 0000"));
            Assert.That(JsonAssert.Property(result.StdoutJson, "name").GetString(), Is.EqualTo("Acme Ltd"));
        });
    }

    // A patch sets the fields present and leaves absent fields as they are (proposal, "Create and
    // update"); a Name field the caller did not ask for would rename the organisation.
    [Test]
    public async Task Org_update_sends_only_the_fields_whose_options_are_given()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));

        var result = await run.RunAsync("org", "update", "--website", "https://acme.example");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.That(PropertyNames(request.BodyJson), Is.EqualTo("Website").IgnoreCase);
    }

    // OrganisationPatchModel has no Notes field, so the proposal gives org update --name, --website
    // and --phone only; an update with none of them has nothing to send. The update with a valid
    // field option is then sent.
    [TestCase("org update --notes Internal")]
    [TestCase("org update")]
    public async Task Org_update_without_a_valid_field_option_exits_2_without_sending_a_request(string commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));

        var result = await run.RunAsync(commandLine.Split(' '));

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, "PATCH", TestData.OrgPath(), "org", "update", "--website", "https://acme.example");
    }

    // The API returns the users as a plain list (Enclave.Sdk.Api 1.0.4,
    // OrganisationClient.GetOrganisationUsersAsync); the CLI prints it in the same list envelope as
    // every other list (proposal, "Output").
    [Test]
    public async Task Org_user_list_gets_the_organisation_users_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((new Guid(AliceId), "alice@example.com"), (new Guid(BobId), "bob@example.com")));

        var result = await run.RunAsync("org", "user", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        var items = JsonAssert.Property(output, "items");
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("users")));
            Assert.That(StringField(items, "id"), Is.EqualTo(AliceId + "," + BobId).IgnoreCase);
            Assert.That(StringField(items, "emailAddress"), Is.EqualTo("alice@example.com,bob@example.com"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    // org user remove takes the account ID, so -o id prints account IDs and the output of
    // `org user list -o id` can be passed to it.
    [Test]
    public async Task Org_user_list_with_output_id_prints_one_account_id_per_line()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((new Guid(AliceId), "alice@example.com"), (new Guid(BobId), "bob@example.com")));

        var result = await run.RunAsync("org", "user", "list", "-o", "id");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("users")));
            Assert.That(string.Join(",", result.StdoutLines), Is.EqualTo(AliceId + "," + BobId).IgnoreCase);
        });
    }

    // RemoveUserAsync returns no model (Enclave.Sdk.Api 1.0.4, OrganisationClient.RemoveUserAsync),
    // so success is reported by the exit code with nothing on stderr.
    [Test]
    public async Task Org_user_remove_with_yes_deletes_the_account_from_the_organisation()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("users/" + AliceId));

        var result = await run.RunAsync("org", "user", "remove", AliceId, "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("users/" + AliceId)).IgnoreCase);
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // org user remove takes away a person's access, so it needs --yes (proposal, "Confirmation").
    [Test]
    public async Task Org_user_remove_without_yes_exits_6_naming_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("users/" + AliceId));

        var result = await run.RunAsync("org", "user", "remove", AliceId);

        AssertRejectedWithoutRequest(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    // Account IDs are GUIDs, and Enclave.Sdk.Api 1.0.4 puts the ID into the URL path unescaped, so
    // it is checked before any call (proposal, "ID checks"). The same removal with a GUID is then
    // sent.
    [TestCase("alice@example.com")]
    [TestCase("12345")]
    [TestCase("../invites")]
    public async Task Org_user_remove_rejects_an_account_id_that_is_not_a_guid_without_sending_a_request(string accountId)
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("users/" + AliceId));
        run.Stub("DELETE", TestData.OrgPath("invites"));

        var result = await run.RunAsync("org", "user", "remove", accountId, "--yes");

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, "DELETE", TestData.OrgPath("users/" + AliceId), "org", "user", "remove", AliceId, "--yes");
    }

    // org user remove acts on one account, so it reports an unknown account as not found
    // (proposal, "Several IDs": single-ID commands exit 5 for an unknown ID).
    [Test]
    public async Task Org_user_remove_exits_5_when_the_api_reports_not_found()
    {
        using var run = CliRun.Start();
        run.StubProblem("DELETE", TestData.OrgPath("users/" + AliceId), 404, "Not Found", "The account is not a member of the organisation.");

        var result = await run.RunAsync("org", "user", "remove", AliceId, "--yes");

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(5), result.Stderr);
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("not_found"));
            Assert.That(run.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Org_invite_list_gets_the_pending_invites_and_prints_the_list_envelope()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("invites"), json: ApiJson.Invites("carol@example.com", "dave@example.com"));

        var result = await run.RunAsync("org", "invite", "list");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
            Assert.That(StringField(JsonAssert.Property(output, "items"), "emailAddress"), Is.EqualTo("carol@example.com,dave@example.com"));
            Assert.That(JsonAssert.Property(output, "total").GetInt32(), Is.EqualTo(2));
            Assert.That(JsonAssert.Property(output, "truncated").GetBoolean(), Is.False);
        });
    }

    // An invite is identified by its email address, which is what org invite cancel takes
    // (proposal, "Partner API": CancelInviteAync(email)), so -o id prints email addresses.
    [Test]
    public async Task Org_invite_list_with_output_id_prints_one_email_address_per_line()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("invites"), json: ApiJson.Invites("carol@example.com", "dave@example.com"));

        var result = await run.RunAsync("org", "invite", "list", "-o", "id");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
            Assert.That(string.Join(",", result.StdoutLines), Is.EqualTo("carol@example.com,dave@example.com"));
        });
    }

    // InviteUserAsync returns no model (Enclave.Sdk.Api 1.0.4, OrganisationClient.InviteUserAsync),
    // so success is reported by the exit code with nothing on stderr.
    [Test]
    public async Task Org_invite_send_with_yes_posts_the_email_address()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("invites"));

        var result = await run.RunAsync("org", "invite", "send", "carol@example.com", "--yes");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
            Assert.That(JsonAssert.Property(request.BodyJson, "emailAddress").GetString(), Is.EqualTo("carol@example.com"));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // An invite gives an outside email address access, so it needs --yes (proposal,
    // "Confirmation").
    [Test]
    public async Task Org_invite_send_without_yes_exits_6_naming_yes_and_sends_nothing()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("invites"));

        var result = await run.RunAsync("org", "invite", "send", "carol@example.com");

        AssertRejectedWithoutRequest(run, result, 6, "confirmation_required");
        Assert.That(result.Error.GetRawText(), Does.Contain("--yes"));
    }

    // The API cancels an invite by email address in the body of a DELETE to the invites route
    // (Enclave.Sdk.Api 1.0.4, OrganisationClient.CancelInviteAync). Cancelling an invite removes no
    // existing access, so it is not on the proposal's --yes list.
    [Test]
    public async Task Org_invite_cancel_deletes_the_invite_by_email_address_without_needing_yes()
    {
        using var run = CliRun.Start();
        run.Stub("DELETE", TestData.OrgPath("invites"));

        var result = await run.RunAsync("org", "invite", "cancel", "carol@example.com");

        Assert.That(result.ExitCode, Is.Zero, result.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
            Assert.That(JsonAssert.Property(request.BodyJson, "emailAddress").GetString(), Is.EqualTo("carol@example.com"));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // --dry-run prints the request Enclave.Sdk.Api would send and sends nothing (proposal, "Dry
    // run"); with --dry-run a missing --yes exits 0 (proposal, "Confirmation").
    [TestCase("org update --name Globex --dry-run", "PATCH", "")]
    [TestCase("org user remove " + AliceId + " --dry-run", "DELETE", "users/" + AliceId)]
    [TestCase("org invite send carol@example.com --dry-run", "POST", "invites")]
    [TestCase("org invite cancel carol@example.com --dry-run", "DELETE", "invites")]
    public async Task Org_change_commands_with_dry_run_print_the_request_and_send_nothing(string commandLine, string method, string path)
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
            Assert.That(JsonAssert.Property(request, "url").GetString(), Does.EndWith(TestData.OrgPath(path)).IgnoreCase);
        });
    }

    // --dry-run and --yes exist only on commands that change something; elsewhere they are unknown
    // options, which exit 2. The same command without the option is then sent.
    [TestCase("org show --yes", "org show", "")]
    [TestCase("org show --dry-run", "org show", "")]
    [TestCase("org user list --yes", "org user list", "users")]
    [TestCase("org invite list --dry-run", "org invite list", "invites")]
    public async Task Org_read_commands_reject_change_options_without_sending_a_request(string commandLine, string acceptedCommandLine, string path)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(acceptedCommandLine);
        ArgumentNullException.ThrowIfNull(path);
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((new Guid(AliceId), "alice@example.com")));
        run.Stub("GET", TestData.OrgPath("invites"), json: ApiJson.Invites("carol@example.com"));

        var result = await run.RunAsync(commandLine.Split(' '));

        AssertRejectedWithoutRequest(run, result, 2, "invalid_argument");
        await RunAcceptedAsync(run, "GET", TestData.OrgPath(path), acceptedCommandLine.Split(' '));
    }

    private static void AssertRejectedWithoutRequest(CliRun run, CliResult result, int exitCode, string code)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.Stderr);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo(code));
        });
    }

    // An unknown command or option also exits 2 without a request, so each exit-2 test then runs a
    // corrected command in the same sandbox and checks it reaches the API. That proves the command
    // exists and the rejection came from the input the test changed. Account IDs in paths are
    // compared ignoring case because a GUID can be written in either case.
    private static async Task<RecordedRequest> RunAcceptedAsync(CliRun run, string method, string path, params string[] args)
    {
        var accepted = await run.RunAsync(args);

        Assert.That(accepted.ExitCode, Is.Zero, accepted.Stderr);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(path).IgnoreCase);
        });
        return request;
    }

    // Lists are compared as comma-joined strings, which keeps constant arrays out of the
    // assertions (CA1861) and prints both sides readably on failure.
    private static string PropertyNames(JsonElement obj) =>
        string.Join(",", obj.EnumerateObject().Select(property => property.Name));

    private static string StringField(JsonElement array, string name) =>
        string.Join(",", array.EnumerateArray().Select(item => JsonAssert.Property(item, name).GetString()));
}
