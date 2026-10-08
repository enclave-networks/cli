using System.Globalization;
using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// The org commands that act on the organisation in use: its properties, users and invites
// (proposed-cli-surface.md "Commands" and "Command options"). org list and org use choose the
// organisation and are tested with the organisation context.
public class OrgCommandTests
{
    private const string SamEmail = "sam@example.com";

    private const string AlexEmail = "alex@example.com";

    private const string SamStoredEmail = "Sam@Example.com";

    private const string AlexStoredEmail = "Alex@Example.com";

    private static readonly Guid SamId = new("5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25");

    private static readonly Guid AlexId = new("0b7e4d19-3c2a-4f6e-8d15-a9b8c7d6e5f4");

    [Test]
    public async Task Org_show_prints_the_properties_of_the_organisation_in_use()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));

        var result = await run.RunAsync("org", "show");

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath()));
            Assert.That(Guid.Parse(JsonAssert.Property(output, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(output, "name").GetString(), Is.EqualTo(TestData.OrgName));
            Assert.That(JsonAssert.Property(output, "maxSystems").GetInt32(), Is.EqualTo(100));
        });
    }

    // Example 63 in proposed-cli-surface.md. The flags set the OrganisationPatchModel fields of the
    // same name (portal Enclave.Api/Modules/OrganisationManagement/Organisation/Models/OrganisationPatchModel.cs).
    [Test]
    public async Task Org_update_patches_the_name_website_and_phone_and_prints_the_updated_organisation()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties("Acme Ltd"));

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
            Assert.That(JsonAssert.Property(result.StdoutJson, "name").GetString(), Is.EqualTo("Acme Ltd"));
        });
    }

    // An update sends only the fields given (proposed-cli-surface.md "Create and update"); a Name the
    // caller did not give would rename the organisation.
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

    // The API returns the users in one body, with no pages
    // (OrganisationScopedClient.GetOrganisationUsersAsync, Enclave.Sdk.Api 1.1.0), and the CLI prints
    // them as a user list (proposed-cli-surface.md "Output").
    [Test]
    public async Task Org_list_users_prints_the_organisation_users_as_a_user_list()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail), (AlexId, AlexEmail)));

        var result = await run.RunAsync("org", "list-users");

        var items = CliAssert.List(result, "user");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("users")));
            Assert.That(JsonRead.StringFieldList(items, "emailAddress"), Is.EqualTo($"{SamEmail},{AlexEmail}"));
            Assert.That(JsonRead.StringFieldList(items, "id"), Is.EqualTo($"{SamId:N},{AlexId:N}").IgnoreCase);
        });
    }

    // Example 19 in proposed-cli-surface.md. org remove-user takes an email address in place of the
    // account ID and looks it up with one call, matching ignoring case ("Names and IDs"). Sam is the
    // second user the lookup reads, so a lookup that took the first user removes the wrong account.
    // The API answers the removal with the removed user's model (portal
    // OrganisationController.RemoveUser), and a single-ID command prints the model ("Several IDs").
    // Each account's removal answers with that account, so the output shows which one was removed.
    [TestCase(SamEmail)]
    [TestCase("SAM@Example.COM")]
    public async Task Org_remove_user_looks_the_email_up_removes_that_account_and_prints_it(string email)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((AlexId, AlexEmail), (SamId, SamEmail)));
        StubRemoval(run, AlexId, AlexEmail);
        StubRemoval(run, SamId, SamEmail);

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
            Assert.That(JsonAssert.Property(result.StdoutJson, "emailAddress").GetString(), Is.EqualTo(SamEmail), result.ToString());
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetString(), Is.EqualTo(SamId.ToString("N")).IgnoreCase);
            Assert.That(result.Stderr, Is.Empty);
        });
    }

    // --id gives the account ID and makes no lookup (proposed-cli-surface.md "Names and IDs"). No
    // command asks for confirmation ("Changes run when given"), so the removal runs with stdin a
    // terminal, where a prompt would wait for an answer.
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

    // An email address no member has matches nothing, which exits 2 invalid_argument with no
    // candidates (proposed-cli-surface.md "Errors and exit codes").
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

    // An email address with --id contradicts itself and exits 2 before any call
    // (proposed-cli-surface.md "Details"). The removal by --id alone run next shows the rejection
    // comes from the pair.
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

    // An account ID is a GUID, checked before any call, so a malformed one exits 2 naming it and
    // sends nothing (proposed-cli-surface.md "ID checks"). An email address after --id is
    // not read as one, since the option decides how a value is read ("Commands"). The removal with a
    // GUID run next shows the rejection comes from the ID.
    [TestCase(SamEmail)]
    [TestCase("12345")]
    [TestCase("../invites")]
    public async Task Org_remove_user_rejects_an_account_id_that_is_not_a_guid_without_a_request(string accountId)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail)));
        run.Stub("DELETE", TestData.OrgPath("invites"));
        StubRemoval(run, SamId, SamEmail);

        CliAssert.Rejected(run, await run.RunAsync("org", "remove-user", "--id", accountId));

        var accepted = await run.RunAsync("org", "remove-user", "--id", SamId.ToString());
        CliAssert.Succeeded(accepted);
        Assert.That(AccountIn(run.SingleRequest().Path), Is.EqualTo(SamId));
    }

    // org remove-user is a single-ID command, which exits 5 for an unknown ID
    // (proposed-cli-surface.md "Several IDs").
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

    // Example 64 in proposed-cli-surface.md, second command. An invite is identified by its email
    // address (OrganisationInviteModel), and the API returns the invites in one body, with no pages.
    [Test]
    public async Task Org_list_invites_prints_the_pending_invites_as_an_invite_list()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("invites"), json: ApiJson.Invites(AlexEmail, SamEmail));

        var result = await run.RunAsync("org", "list-invites");

        var items = CliAssert.List(result, "invite");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("GET"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
            Assert.That(JsonRead.StringFieldList(items, "emailAddress"), Is.EqualTo($"{AlexEmail},{SamEmail}"));
        });
    }

    // Example 64 in proposed-cli-surface.md, first command. The API takes the invite as
    // { emailAddress } (OrganisationScopedClient.InviteUserAsync, Enclave.Sdk.Api 1.1.0). The API
    // answers with the invite's model (portal OrganisationController.CreateInvite), which the
    // command prints. The invite holds the address it was first sent to, which can differ in case
    // from the one given (IOrganisationScopedClient.InviteUserAsync, Enclave.Sdk.Api 1.2.0), so the
    // fake answers with another case, and output copied from the argument does not match.
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

    // Example 19 in proposed-cli-surface.md, second command. The main API cancels an invite by email
    // address, in the body of a DELETE (OrganisationScopedClient.CancelInviteAync, Enclave.Sdk.Api
    // 1.1.0), so the CLI sends the address and makes no lookup ("Partner API"). The API answers with
    // the cancelled invite's model (portal OrganisationController.DeleteInvite), which a single-ID
    // command prints ("Several IDs"). The fake answers with the address in another case, as the
    // invite test above does, so output copied from the argument does not match.
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

    // org cancel-invite is a single-ID command, which exits 5 for an unknown ID
    // (proposed-cli-surface.md "Several IDs"); the API answers 404 for an address with no pending
    // invite (portal OrganisationController.DeleteInvite).
    [Test]
    public async Task Org_cancel_invite_for_an_address_with_no_invite_exits_5()
    {
        using var run = CliRun.Start();
        run.StubProblem("DELETE", TestData.OrgPath("invites"), 404, "Not Found", "No pending invite for that address.");

        var result = await run.RunAsync("org", "cancel-invite", SamEmail);

        CliAssert.Failed(result, "not_found");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("DELETE"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("invites")));
        });
    }

    // Organisation update, user removal and invites need organisation membership and no token scope
    // (portal OrganisationController.cs:49,91,132), and no command asks for confirmation
    // (proposed-cli-surface.md "Changes run when given"). Each command sends its one request and
    // nothing else, with stdin a terminal, where a prompt would wait for an answer.
    [TestCase("org update --name Initech", "PATCH", null)]
    [TestCase("org invite alex@example.com", "POST", "invites")]
    [TestCase("org cancel-invite sam@example.com", "DELETE", "invites")]
    public async Task Org_membership_changes_run_when_given_without_a_prompt(string commandLine, string method, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();
        run.StdinIsTerminal = true;
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties("Initech"));
        run.Stub("POST", TestData.OrgPath("invites"), json: ApiJson.Invite(AlexEmail));
        run.Stub("DELETE", TestData.OrgPath("invites"), json: ApiJson.Invite(SamEmail));

        await CliAssert.AcceptedAsync(run, method, TestData.OrgPath(suffix ?? string.Empty), commandLine.Split(' '));
    }

    // A change the API did not make must never be reported as made. A plain 502 from a proxy is a
    // failure without problem details, which the CLI maps to transient ("Errors and exit codes").
    // These calls report it because Enclave.Sdk.Api 1.1.0 checks their status
    // (proposed-cli-surface.md "`Enclave.Sdk.Api` changes", item 6).
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

    // --dry-run prints the requests the change would send and sends none of them; org.name is null
    // when the organisation was given by ID, as CliRun gives it (proposed-cli-surface.md "Dry run").
    // None of these commands needs a read first, and each change is one call.
    [TestCase("org update --name Initech --dry-run", "PATCH", null, "Name", "Initech")]
    [TestCase("org invite alex@example.com --dry-run", "POST", "invites", "emailAddress", AlexEmail)]
    [TestCase("org cancel-invite sam@example.com --dry-run", "DELETE", "invites", "emailAddress", SamEmail)]
    public async Task Org_changes_with_dry_run_print_the_requests_and_send_nothing(string commandLine, string method, string? suffix, string field, string value)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        using var run = CliRun.Start();

        var result = await run.RunAsync(commandLine.Split(' '));

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        var org = JsonAssert.Property(output, "org");
        var requests = JsonAssert.Property(output, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(JsonAssert.Property(output, "dryRun").GetBoolean(), Is.True);
            Assert.That(Guid.Parse(JsonAssert.Property(org, "id").GetString()!, CultureInfo.InvariantCulture), Is.EqualTo(TestData.OrgId));
            Assert.That(JsonAssert.Property(org, "name").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(JsonAssert.Property(requests[0], "method").GetString(), Is.EqualTo(method));
            Assert.That(JsonAssert.Property(requests[0], "url").GetString(), Does.EndWith(TestData.OrgPath(suffix ?? string.Empty)));
            Assert.That(JsonAssert.Property(JsonAssert.Property(requests[0], "body"), field).GetString(), Is.EqualTo(value));
        });
    }

    // The email lookup is a read the change depends on, so it runs, and only the DELETE it finds the
    // account for is withheld (proposed-cli-surface.md "Dry run").
    [Test]
    public async Task Org_remove_user_with_dry_run_looks_the_email_up_and_prints_the_removal_without_sending_it()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((AlexId, AlexEmail), (SamId, SamEmail)));
        StubRemoval(run, SamId, SamEmail);

        var result = await run.RunAsync("org", "remove-user", SamEmail, "--dry-run");

        CliAssert.Succeeded(result);
        var requests = JsonAssert.Property(result.StdoutJson, "requests");
        Assert.That(requests.GetArrayLength(), Is.EqualTo(1), result.ToString());
        var url = JsonAssert.Property(requests[0], "url").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests.Select(sent => sent.Method), Is.Not.Empty.And.All.EqualTo("GET"));
            Assert.That(JsonAssert.Property(requests[0], "method").GetString(), Is.EqualTo("DELETE"));
            Assert.That(AccountIn(new Uri(url).AbsolutePath), Is.EqualTo(SamId));
        });
    }

    // --dry-run is an option of commands that change something, and a read does not take it
    // (proposed-cli-surface.md "Details"). The read without it run next shows the rejection comes
    // from the option.
    [TestCase("show", null)]
    [TestCase("list-users", "users")]
    [TestCase("list-invites", "invites")]
    public async Task Org_reads_given_dry_run_exit_2_without_a_request(string verb, string? suffix)
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));
        run.Stub("GET", TestData.OrgPath("users"), json: ApiJson.Users((SamId, SamEmail)));
        run.Stub("GET", TestData.OrgPath("invites"), json: ApiJson.Invites(SamEmail));

        await CliAssert.RejectedThenAcceptedAsync(run, ["org", verb, "--dry-run"], ["org", verb], "GET", TestData.OrgPath(suffix ?? string.Empty));
    }

    // The specification names the account a removal acts on and leaves the form of its GUID in the
    // path open: OrganisationUser.Id is written as 32 hex digits (ApiJson.User), and a GUID given to
    // --id is hyphenated. The account is read from the last path segment, so either form passes
    // and any other account fails.
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

    // The fake answers the removal with the removed user's model, as the API does (portal
    // OrganisationController.RemoveUser), at either form of the account's path, for the reason
    // AccountIn gives.
    private static void StubRemoval(CliRun run, Guid accountId, string email)
    {
        foreach (var path in AccountPaths(accountId))
        {
            run.Stub("DELETE", path, json: ApiJson.User(accountId, email));
        }
    }
}
