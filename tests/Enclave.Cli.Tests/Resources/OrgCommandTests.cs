using System.Globalization;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// org list and org use are tested in Auth/OrgContextTests.cs.
public class OrgCommandTests
{
    private const string SamEmail = "sam@example.com";

    private const string AlexEmail = "alex@example.com";

    private const string SamStoredEmail = "Sam@Example.com";

    private const string AlexStoredEmail = "Alex@Example.com";

    private static readonly Guid SamId = new("5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25");

    private static readonly Guid AlexId = new("0b7e4d19-3c2a-4f6e-8d15-a9b8c7d6e5f4");

    // Example 63. The answer differs from the request, so output copied from the request does not
    // match.
    [Test]
    public async Task Org_update_patches_the_name_website_and_phone_and_prints_the_updated_organisation()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties("Acme Limited"));

        var result = await run.RunAsync("org", "update", "--name", "Acme Ltd", "--website", "https://acme.example", "--phone", "+44 20 7946 0000");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        string[] fields = ["Name", "Website", "Phone"];
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath()));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(fields).IgnoreCase);
            Assert.That(JsonAssert.Property(body, "Name").GetString(), Is.EqualTo("Acme Ltd"));
            Assert.That(JsonAssert.Property(body, "Website").GetString(), Is.EqualTo("https://acme.example"));
            Assert.That(JsonAssert.Property(body, "Phone").GetString(), Is.EqualTo("+44 20 7946 0000"));
            Assert.That(result.StdoutJson.GetProperty("name").GetString(), Is.EqualTo("Acme Limited"));
            Assert.That(JsonRead.GuidOf(result.StdoutJson.GetProperty("id")), Is.EqualTo(TestData.OrgId));
            Assert.That(result.StdoutJson.GetProperty("maxSystems").GetInt32(), Is.EqualTo(100));
        });
    }

    // A Name the caller did not give would rename the organisation.
    [TestCase("--name", "Acme Ltd", "Name")]
    [TestCase("--website", "https://acme.example", "Website")]
    [TestCase("--phone", "+44 20 7946 0000", "Phone")]
    public async Task Org_update_sends_only_the_field_given(string option, string value, string field)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", TestData.OrgPath(), "org", "update", option, value);

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(body), Is.EqualTo(field).IgnoreCase);
            Assert.That(JsonAssert.Property(body, field).GetString(), Is.EqualTo(value));
        });
    }

    // Example 19. Sam is the second user the lookup reads, so a lookup that took the first removes
    // the wrong account. Sam's removal answers with a full name the lookup's item lacks, so output
    // copied from the lookup does not match.
    [TestCase(SamEmail)]
    [TestCase("SAM@Example.COM")]
    public async Task Org_remove_user_looks_the_email_up_removes_that_account_and_prints_it(string email)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((AlexId, AlexEmail), (SamId, SamEmail)));
        StubRemoval(run, AlexId, AlexEmail);
        StubRemoval(run, SamId, SamEmail, "Sam Taylor");

        var result = await run.RunAsync("org", "remove-user", email);

        CliAssert.Succeeded(result);
        var requests = run.Requests;
        Assert.That(requests, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(requests[0].Method, Is.EqualTo("GET"));
            Assert.That(requests[0].Path, Is.EqualTo(TestData.OrgPath("users")));
            Assert.That(requests[1].Method, Is.EqualTo("DELETE"));
            Assert.That(AccountIn(requests[1].Path), Is.EqualTo(SamId));
            Assert.That(result.StdoutJson.GetProperty("emailAddress").GetString(), Is.EqualTo(SamEmail), result.ToString());
            Assert.That(JsonRead.GuidOf(result.StdoutJson.GetProperty("id")), Is.EqualTo(SamId));
            Assert.That(result.StdoutJson.GetProperty("fullName").GetString(), Is.EqualTo("Sam Taylor"));
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // With stdin a terminal, a prompt would wait for an answer.
    [Test]
    public async Task Org_remove_user_with_id_removes_the_account_without_a_lookup_or_a_prompt()
    {
        using var run = CliRun.Start();
        run.StdinIsTerminal = true;
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail)));
        StubRemoval(run, SamId, SamEmail);

        var result = await run.RunAsync("org", "remove-user", "--id", SamId.ToString());

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(AccountIn(request.Path), Is.EqualTo(SamId));
            Assert.That(JsonAssert.Property(result.StdoutJson, "emailAddress").GetString(), Is.EqualTo(SamEmail), result.ToString());
        });
    }

    [Test]
    public async Task Org_remove_user_given_an_email_no_member_has_exits_2_with_no_candidates()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail), (AlexId, AlexEmail)));
        StubRemoval(run, SamId, SamEmail);
        StubRemoval(run, AlexId, AlexEmail);

        var result = await run.RunAsync("org", "remove-user", "nobody@example.com");

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").EnumerateArray(), Is.Empty);
            Assert.That(run.Requests.Select(request => request.Method), Is.Not.Empty.And.All.EqualTo("GET"));
        });
    }

    [Test]
    public async Task Org_remove_user_given_an_email_and_an_id_exits_2_without_a_request()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail)));
        StubRemoval(run, SamId, SamEmail);

        CliAssert.Rejected(run, await run.RunAsync("org", "remove-user", SamEmail, "--id", SamId.ToString()));

        var accepted = await run.RunAsync("org", "remove-user", "--id", SamId.ToString());
        CliAssert.Succeeded(accepted);
        Assert.That(AccountIn(run.SingleRequest().Path), Is.EqualTo(SamId));
    }

    // An email address after --id is not read as one, since the option decides how a value is read.
    [TestCase(SamEmail)]
    [TestCase("12345")]
    public async Task Org_remove_user_rejects_an_account_id_that_is_not_a_guid_without_a_request(string accountId)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail)));
        StubRemoval(run, SamId, SamEmail);

        CliAssert.Rejected(run, await run.RunAsync("org", "remove-user", "--id", accountId));

        var accepted = await run.RunAsync("org", "remove-user", "--id", SamId.ToString());
        CliAssert.Succeeded(accepted);
        Assert.That(AccountIn(run.SingleRequest().Path), Is.EqualTo(SamId));
    }

    [Test]
    public async Task Org_remove_user_with_an_account_id_the_api_does_not_know_exits_5()
    {
        using var run = CliRun.Start();

        foreach (var path in AccountPaths(SamId))
        {
            run.StubProblem("DELETE", path, 404, "Not Found", "The account is not a member of the organisation.");
        }

        var result = await run.RunAsync("org", "remove-user", "--id", SamId.ToString());

        CliAssert.Failed(result, "not_found");
        Assert.That(AccountIn(run.SingleRequest().Path), Is.EqualTo(SamId));
    }

    // Example 64. The invite holds the address it was first sent to, which can differ in case from
    // the one given (Enclave.Sdk.Api IOrganisationScopedClient.InviteUserAsync), so the fake answers
    // in another case.
    [Test]
    public async Task Org_invite_posts_the_email_address_and_prints_the_invite_the_api_returns()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("invites"), json: ApiJson.Invite(AlexStoredEmail));

        var result = await run.RunAsync("org", "invite", AlexEmail);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
            Assert.That(JsonAssert.Property(request.BodyJson, "emailAddress").GetString(), Is.EqualTo(AlexEmail));
            Assert.That(JsonAssert.Property(result.StdoutJson, "emailAddress").GetString(), Is.EqualTo(AlexStoredEmail), result.ToString());
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // Example 19. The API cancels an invite by the address in the DELETE body (Enclave.Sdk.Api
    // OrganisationScopedClient.CancelInviteAync), so no lookup is needed. The fake answers in another
    // case, so output copied from the argument does not match.
    [Test]
    public async Task Org_cancel_invite_deletes_the_invite_by_email_address_without_a_lookup_and_prints_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("invites"), json: ApiJson.Invites(SamEmail));
        run.Stub("DELETE", TestData.OrgPath("invites"), json: ApiJson.Invite(SamStoredEmail));

        var result = await run.RunAsync("org", "cancel-invite", SamEmail);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
            Assert.That(JsonAssert.Property(request.BodyJson, "emailAddress").GetString(), Is.EqualTo(SamEmail));
            Assert.That(JsonAssert.Property(result.StdoutJson, "emailAddress").GetString(), Is.EqualTo(SamStoredEmail), result.ToString());
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // The API refuses such an address (portal CreateOrganisationInviteModelValidator,
    // DeleteOrganisationInviteModelValidator: EmailAddress), and remove-user's lookup matches no
    // member. The run without a token gives the same error, so a CLI that checked the token first
    // exits 3.
    [TestCase("invite", "")]
    [TestCase("invite", " ")]
    [TestCase("cancel-invite", "")]
    [TestCase("cancel-invite", " \t ")]
    [TestCase("remove-user", "")]
    [TestCase("remove-user", "  ")]
    public async Task Org_commands_given_a_blank_email_address_exit_2_before_the_token_and_any_call(string verb, string email)
    {
        using var withToken = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");
        withToken.Stub("POST", TestData.OrgPath("invites"), json: ApiJson.Invite(AlexEmail));
        withToken.Stub("DELETE", TestData.OrgPath("invites"), json: ApiJson.Invite(SamEmail));
        withToken.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail)));
        StubRemoval(withToken, SamId, SamEmail);

        var first = await withToken.RunAsync("org", verb, email);
        var second = await withoutToken.RunAsync("org", verb, email);

        CliAssert.Rejected(withToken, first);
        CliAssert.Rejected(withoutToken, second);
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNameList(first.Error.GetProperty("errors")), Is.EqualTo("email"), first.ToString());
            Assert.That(second.Error.GetRawText(), Is.EqualTo(first.Error.GetRawText()));
        });
    }

    // A change the API did not make must never be reported as made. Enclave.Sdk.Api checks the
    // status before it reads the body (OrganisationScopedClient.ReadModelAsync; spec/cli-surface.md
    // "`Enclave.Sdk.Api` changes", item 6), so an HTML 502 is never read as a model.
    [TestCase("org invite alex@example.com", "POST", "invites")]
    [TestCase("org cancel-invite sam@example.com", "DELETE", "invites")]
    public async Task Org_invite_changes_report_a_proxy_error_as_transient(string commandLine, string method, string suffix)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(suffix);
        using var run = CliRun.Start();
        run.StubRaw(method, TestData.OrgPath(suffix), 502, "text/html", "<html><body>Bad Gateway</body></html>");

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Failed(result, "transient");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(method));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath(suffix)));
        });
    }

    // As above, for RemoveUserAsync.
    [Test]
    public async Task Org_remove_user_reports_a_proxy_error_as_transient()
    {
        using var run = CliRun.Start();

        foreach (var path in AccountPaths(SamId))
        {
            run.StubRaw("DELETE", path, 502, "text/html", "<html><body>Bad Gateway</body></html>");
        }

        var result = await run.RunAsync("org", "remove-user", "--id", SamId.ToString());

        CliAssert.Failed(result, "transient");
        Assert.That(AccountIn(run.SingleRequest().Path), Is.EqualTo(SamId));
    }

    // None of these commands needs a read, so the fake API receives no request at all.
    [TestCase("org update --name Initech --dry-run", "PATCH", null, "Name", "Initech")]
    [TestCase("org invite alex@example.com --dry-run", "POST", "invites", "emailAddress", AlexEmail)]
    [TestCase("org cancel-invite sam@example.com --dry-run", "DELETE", "invites", "emailAddress", SamEmail)]
    public async Task Org_changes_with_dry_run_print_the_requests_and_send_nothing(string commandLine, string method, string? suffix, string field, string value)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();

        var result = await run.RunAsync(commandLine.Split(' '));

        var request = DryRunOutput.RequestInTestOrg(result, method, TestData.OrgPath(suffix ?? string.Empty));
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(JsonAssert.Property(request, "body"), field).GetString(), Is.EqualTo(value));
        });
    }

    // The email lookup is a read the change depends on, so it runs ("Dry run").
    [Test]
    public async Task Org_remove_user_with_dry_run_looks_the_email_up_and_prints_the_removal_without_sending_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((AlexId, AlexEmail), (SamId, SamEmail)));
        StubRemoval(run, SamId, SamEmail);

        var result = await run.RunAsync("org", "remove-user", SamEmail, "--dry-run");

        var (_, request) = DryRunOutput.ReadOne(result);
        var url = JsonAssert.Property(request, "url").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests.Select(sent => sent.Method), Is.Not.Empty.And.All.EqualTo("GET"));
            Assert.That(JsonAssert.Property(request, "method").GetString(), Is.EqualTo("DELETE"));
            Assert.That(AccountIn(new Uri(url).AbsolutePath), Is.EqualTo(SamId));
        });
    }

    // The specification leaves the form of the GUID in the path open, so either form passes and any
    // other account fails.
    private static Guid? AccountIn(string path)
    {
        var prefix = TestData.OrgPath("users/");

        return path.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(path[prefix.Length..], CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    private static string[] AccountPaths(Guid accountId) =>
        [TestData.OrgPath("users/" + accountId.ToString("N")), TestData.OrgPath("users/" + accountId.ToString("D"))];

    // The API answers with the removed user's model (portal OrganisationController.RemoveUser). Both
    // forms of the path are answered, for the reason AccountIn gives.
    private static void StubRemoval(CliRun run, Guid accountId, string email, string? fullName = null)
    {
        var user = JsonNode.Parse(ApiJson.User(accountId, email))!.AsObject();

        if (fullName is not null)
        {
            user["fullName"] = fullName;
        }

        foreach (var path in AccountPaths(accountId))
        {
            run.Stub("DELETE", path, json: user.ToJsonString());
        }
    }
}
