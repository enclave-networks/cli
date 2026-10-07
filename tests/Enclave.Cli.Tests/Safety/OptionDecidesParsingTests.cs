using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Safety;

/// <summary>
/// The option a value is given with decides how it is read: --id and the --&lt;item&gt;-id options take
/// IDs, arguments and the --&lt;item&gt; options take names, and the CLI never inspects a value to guess
/// its type (proposed-cli-surface.md "Shape and naming").
/// </summary>
public class OptionDecidesParsingTests
{
    private static readonly Guid AccountId = new("5b8e1c47-2d93-4f60-a7b1-c04e9d3f6a25");

    // 12 is the ID of one key and the description of another, so the systems request shows which
    // reading the CLI took. --key-id uses the API's enrolment_key parameter (proposed-cli-surface.md
    // "Filters"; SystemsClient.BuildQueryString, Enclave.Sdk.Api 1.0.4).
    [Test]
    public async Task Key_id_option_reads_its_value_as_a_key_id_and_makes_no_lookup()
    {
        using var run = CliRun.Start();
        var systems = TestData.OrgPath("systems");
        run.Stub("GET", TestData.OrgPath("enrolment-keys"), 200, ApiJson.Page(ApiJson.Key(12, "build agents"), ApiJson.Key(31, "12")));
        run.Stub("GET", systems, 200, ApiJson.Page(ApiJson.System("ABCDE")));

        var result = await run.RunAsync("system", "list", "--key-id", "12");

        CliAssert.List(result, "system");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Path, Is.EqualTo(systems));
            Assert.That(request.QueryValue("enrolment_key"), Is.EqualTo("12"));
        });
    }

    // --key takes a description, so "12" names the key described "12", whose ID is 31.
    [Test]
    public async Task Key_option_reads_its_value_as_a_key_description_even_when_it_is_a_number()
    {
        using var run = CliRun.Start();
        var keys = TestData.OrgPath("enrolment-keys");
        var systems = TestData.OrgPath("systems");
        run.Stub("GET", keys, 200, ApiJson.Page(ApiJson.Key(12, "build agents"), ApiJson.Key(31, "12")));
        run.Stub("GET", systems, 200, ApiJson.Page(ApiJson.System("ABCDE")));

        var result = await run.RunAsync("system", "list", "--key", "12");

        CliAssert.List(result, "system");
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("GET", keys), Has.Count.EqualTo(1));
            Assert.That(string.Join(",", run.RequestsTo("GET", systems).Select(request => request.QueryValue("enrolment_key"))), Is.EqualTo("31"));
        });
    }

    // The argument of a policy command is a description, so "17" names the policy described "17",
    // whose ID is 5; policy 17 is another policy.
    [Test]
    public async Task Policy_argument_is_read_as_a_description_even_when_it_is_a_number()
    {
        using var run = CliRun.Start();
        var policies = TestData.OrgPath("policies");
        run.Stub("GET", policies, 200, ApiJson.Page(ApiJson.Policy(17, "old vpn"), ApiJson.Policy(5, "17")));
        run.StubBulk("DELETE", policies, "policiesDeleted", 1);

        var result = await run.RunAsync("policy", "delete", "17");

        CliAssert.Bulk(result, requested: 1, affected: 1);
        Assert.That(
            string.Join("|", run.RequestsTo("DELETE", policies).Select(request => string.Join(",", request.BodyIds("policyIds")))),
            Is.EqualTo("5"));
    }

    // --id takes IDs only, so a description after it is refused and never looked up. A policy
    // described "old vpn" exists, so a lookup would find it; the second run, by ID, makes no lookup.
    [Test]
    public async Task Id_option_refuses_a_description_and_never_looks_it_up()
    {
        using var run = CliRun.Start();
        var policies = TestData.OrgPath("policies");
        run.Stub("GET", policies, 200, ApiJson.Page(ApiJson.Policy(17, "old vpn")));
        run.StubBulk("DELETE", policies, "policiesDeleted", 1);

        var request = await CliAssert.RejectedThenAcceptedAsync(run, ["policy", "delete", "--id", "old vpn"], ["policy", "delete", "--id", "17"], "DELETE", policies);

        Assert.That(string.Join(",", request.BodyIds("policyIds")), Is.EqualTo("17"));
    }

    // --org-id takes a GUID, so an organisation name after it is refused and never looked up.
    [Test]
    public async Task Org_id_option_refuses_a_name_and_never_looks_it_up()
    {
        using var run = CliRun.Start();
        var otherOrgDisable = $"/org/{TestData.OtherOrgId:N}/systems/disable";
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.StubBulk("PUT", otherOrgDisable, "systemsUpdated", 1);

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["system", "disable", "ABCDE", "--org-id", TestData.OtherOrgName],
            ["system", "disable", "ABCDE", "--org-id", TestData.OtherOrgId.ToString()],
            "PUT",
            otherOrgDisable);
    }

    // --org takes a name. The value is the ID of an organisation the token sees, and no organisation
    // has it as its name, so it matches nothing: the CLI exits 2 invalid_argument after the lookup,
    // with no candidates, and changes nothing (proposed-cli-surface.md "Errors and exit codes").
    // Either organisation's change is answered, so a CLI that read the value as an ID, or fell back
    // to ENCLAVE_ORG_ID, would show a request.
    [Test]
    public async Task Org_option_reads_its_value_as_a_name_even_when_it_is_a_guid()
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.StubBulk("PUT", TestData.OrgPath("systems/disable"), "systemsUpdated", 1);
        run.StubBulk("PUT", $"/org/{TestData.OtherOrgId:N}/systems/disable", "systemsUpdated", 1);

        var result = await run.RunAsync("system", "disable", "ABCDE", "--org", TestData.OtherOrgId.ToString());

        var error = CliAssert.Failed(result, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").GetArrayLength(), Is.Zero);
            Assert.That(string.Join(",", run.Calls()), Is.EqualTo("GET /account/orgs"));
        });
    }

    // org remove-user takes an email address, and the account ID goes after --id. The value is the
    // ID of a user in the organisation whose email address is another, so it matches no email
    // address: exit 2 invalid_argument with no candidates, and nothing is removed. The second run
    // gives that user's email address and removes them.
    [Test]
    public async Task Org_remove_user_argument_is_read_as_an_email_address_even_when_it_is_a_guid()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("users"), 200, ApiJson.Users((AccountId, "sam@example.com")));
        run.Stub("DELETE", TestData.OrgPath($"users/{AccountId:N}"));
        run.Stub("DELETE", TestData.OrgPath($"users/{AccountId:D}"));

        var rejected = await run.RunAsync("org", "remove-user", AccountId.ToString());

        var error = CliAssert.Failed(rejected, "invalid_argument");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(error, "candidates").GetArrayLength(), Is.Zero);
            Assert.That(run.Requests.Where(request => request.Method != "GET"), Is.Empty);
        });

        var accepted = await run.RunAsync("org", "remove-user", "sam@example.com");

        CliAssert.Succeeded(accepted);
        Assert.That(run.Requests.Count(request => request.Method == "DELETE"), Is.EqualTo(1));
    }

    // --user-id takes an account ID, a GUID, and neither an email address nor a number. Arguments
    // are checked before anything else ("Errors and exit codes"), so the refusal comes although a
    // partner customer command cannot run; the second run, with a GUID, passes the check and
    // reaches not_implemented, the outcome of every partner customer command without partner clients.
    [TestCase("add-admin", "alex@example.com")]
    [TestCase("remove-admin", "12")]
    public async Task User_id_option_takes_only_a_guid(string verb, string userId)
    {
        using var run = CliRun.Start();
        var partner = TestData.PartnerId.ToString();

        var rejected = await run.RunAsync("partner", "customer", verb, "Globex Ltd", "--user-id", userId, "--partner-id", partner);

        CliAssert.Rejected(run, rejected);

        var accepted = await run.RunAsync("partner", "customer", verb, "Globex Ltd", "--user-id", AccountId.ToString(), "--partner-id", partner);

        CliAssert.NotImplemented(run, accepted);
    }

    // A name and its ID option given together contradict each other, and nothing in the values says
    // which was meant, so the command exits 2 and sends nothing (proposed-cli-surface.md "Details").
    // The second run gives the ID alone and shows the command works.
    [TestCaseSource(nameof(NameWithIdCases))]
    public async Task Name_given_with_its_id_option_exits_2_and_sends_nothing(string[] rejectedArgs, string[] acceptedArgs, string method, string path, string response)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, 200, response);
        run.Stub("GET", "/account/orgs", 200, ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", TestData.OrgPath("policies"), 200, ApiJson.Page(ApiJson.Policy(17, "old vpn")));
        run.Stub("GET", TestData.OrgPath("enrolment-keys"), 200, ApiJson.Page(ApiJson.Key(12, "build agents")));

        await CliAssert.RejectedThenAcceptedAsync(run, rejectedArgs, acceptedArgs, method, path);
    }

    private static IEnumerable<TestCaseData> NameWithIdCases()
    {
        var otherOrg = TestData.OtherOrgId.ToString();

        yield return NameWithId("policy delete <name> --id", ["policy", "delete", "old vpn", "--id", "17"], ["policy", "delete", "--id", "17"], "DELETE", TestData.OrgPath("policies"), ApiJson.Bulk("policiesDeleted", 1));
        yield return NameWithId("key disable <name> --id", ["key", "disable", "build agents", "--id", "12"], ["key", "disable", "--id", "12"], "PUT", TestData.OrgPath("enrolment-keys/disable"), ApiJson.Bulk("keysModified", 1));
        yield return NameWithId("--org with --org-id", ["system", "disable", "ABCDE", "--org", TestData.OtherOrgName, "--org-id", otherOrg], ["system", "disable", "ABCDE", "--org-id", otherOrg], "PUT", $"/org/{TestData.OtherOrgId:N}/systems/disable", ApiJson.Bulk("systemsUpdated", 1));
    }

    private static TestCaseData NameWithId(string name, string[] rejectedArgs, string[] acceptedArgs, string method, string path, string response) =>
        new TestCaseData(rejectedArgs, acceptedArgs, method, path, response).SetArgDisplayNames(name);
}
