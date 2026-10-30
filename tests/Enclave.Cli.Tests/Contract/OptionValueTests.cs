using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// spec/cli-surface.md "Options on every command". A value is renamed where the API's name does not
// say what it does (--then revoke for Delete on a system); JSON output keeps the API's names.
public class OptionValueTests
{
    private static readonly string SystemEnableUntilPath = TestData.OrgPath("systems/ABCDE/enable-until");

    private static readonly string PolicyEnableUntilPath = TestData.OrgPath("policies/42/enable-until");

    // Enclave.Sdk.Api writes the API enum member name into the sort parameter (each list client's
    // BuildQueryString).
    public static IEnumerable<TestCaseData> SortValues()
    {
        yield return Sort("system list", "systems", "recently-enrolled", "RecentlyEnrolled");
        yield return Sort("system list", "systems", "recently-connected", "RecentlyConnected");
        yield return Sort("system list", "systems", "description", "Description");
        yield return Sort("system list", "systems", "description-or-hostname", "DescriptionOrHostname");
        yield return Sort("system list", "systems", "enrolment-key-used", "EnrolmentKeyUsed");
        yield return Sort("system list", "systems", "last-connected", "LastConnected");
        yield return Sort("system list", "systems", "state", "State");
        yield return Sort("system list", "systems", "version", "Version");
        yield return Sort("system list --pending", "unapproved-systems", "recently-enrolled", "RecentlyEnrolled");
        yield return Sort("system list --pending", "unapproved-systems", "description", "Description");
        yield return Sort("system list --pending", "unapproved-systems", "enrolment-key-used", "EnrolmentKeyUsed");
        yield return Sort("system list --pending", "unapproved-systems", "description-or-hostname", "DescriptionOrHostname");
        yield return Sort("key list", "enrolment-keys", "description", "Description");
        yield return Sort("key list", "enrolment-keys", "last-used", "LastUsed");
        yield return Sort("key list", "enrolment-keys", "approval-mode", "ApprovalMode");
        yield return Sort("key list", "enrolment-keys", "uses-remaining", "UsesRemaining");
        yield return Sort("policy list", "policies", "description", "Description");
        yield return Sort("policy list", "policies", "recently-created", "RecentlyCreated");
        yield return Sort("tag list", "tags", "alphabetical", "Alphabetical");
        yield return Sort("tag list", "tags", "recently-used", "RecentlyUsed");
        yield return Sort("tag list", "tags", "referenced-systems", "ReferencedSystems");
        yield return Sort("trust list", "trust-requirements", "description", "Description");
        yield return Sort("trust list", "trust-requirements", "recently-created", "RecentlyCreated");
    }

    // A search key goes into the search text as the API's search value, which is not always the
    // CLI's: manual is gated, no-uses is nouses ("Filters").
    public static IEnumerable<TestCaseData> CaseVariants()
    {
        yield return Variant("system list --sort recently-connected", "system list --sort Recently-Connected", "systems", "sort", "RecentlyConnected");
        yield return Variant("system list --sort recently-connected", "system list --sort RECENTLY-CONNECTED", "systems", "sort", "RecentlyConnected");
        yield return Variant("key list --sort last-used", "key list --sort Last-Used", "enrolment-keys", "sort", "LastUsed");
        yield return Variant("tag list --sort recently-used", "tag list --sort Recently-Used", "tags", "sort", "RecentlyUsed");
        yield return Variant("policy list --sort recently-created", "policy list --sort Recently-Created", "policies", "sort", "RecentlyCreated");
        yield return Variant("system list --state disconnected", "system list --state DISCONNECTED", "systems", "search", "state:disconnected");
        yield return Variant("system list --os windows", "system list --os WINDOWS", "systems", "search", "os:Windows");
        yield return Variant("system list --os mac", "system list --os Mac", "systems", "search", "os:Mac");
        yield return Variant("system list --type ephemeral", "system list --type Ephemeral", "systems", "search", "type:ephemeral");
        yield return Variant("key list --approval manual", "key list --approval MANUAL", "enrolment-keys", "search", "approval:gated");
        yield return Variant("key list --state no-uses", "key list --state No-Uses", "enrolment-keys", "search", "state:nouses");
        yield return Variant("policy list --state disabled", "policy list --state Disabled", "policies", "search", "state:disabled");
        yield return Variant("trust list --type public-ip", "trust list --type Public-IP", "trust-requirements", "search", "type:PublicIp");
        yield return Variant("trust list --type user-auth", "trust list --type USER-AUTH", "trust-requirements", "search", "type:UserAuthentication");
    }

    // revoke is a system's name for Delete, so a policy does not take it and a system does not take
    // delete. UnapprovedSystemQuerySortMode has no RecentlyConnected.
    public static IEnumerable<TestCaseData> ValuesOutsideTheSet()
    {
        var systems = TestData.OrgPath("systems");
        var systemsPage = ApiJson.Page(ApiJson.System("ABCDE"));

        yield return CommandCases.RejectedThenCorrected("system list --sort newest", "system list --sort recently-connected", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system list --pending --sort recently-connected", "system list --pending --sort recently-enrolled", "GET", TestData.OrgPath("unapproved-systems"), ApiJson.Page(ApiJson.PendingSystem("ABCDE")));
        yield return CommandCases.RejectedThenCorrected("system list --state online", "system list --state connected", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("system list --os macos", "system list --os mac", "GET", systems, systemsPage);
        yield return CommandCases.RejectedThenCorrected("key list --approval auto", "key list --approval automatic", "GET", TestData.OrgPath("enrolment-keys"), ApiJson.Page(ApiJson.Key(12)));
        yield return CommandCases.RejectedThenCorrected("trust list --type ip", "trust list --type public-ip", "GET", TestData.OrgPath("trust-requirements"), ApiJson.Page(ApiJson.Trust(5)));
        yield return CommandCases.RejectedThenCorrected("system enable ABCDE --for 8h --then delete", "system enable ABCDE --for 8h --then revoke", "PUT", SystemEnableUntilPath, ApiJson.System("ABCDE"));
        yield return CommandCases.RejectedThenCorrected("policy enable --id 42 --for 8h --then revoke", "policy enable --id 42 --for 8h --then delete", "PUT", PolicyEnableUntilPath, ApiJson.Policy(42));
    }

    [TestCaseSource(nameof(SortValues))]
    public async Task A_sort_value_is_the_api_member_name_in_lower_case_hyphenated_form_and_is_sent_as_the_api_name(string command, string path, string value, string apiName)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.StubPages(path, 200);

        var result = await run.RunAsync([.. command.Split(' '), "--sort", value]);

        CliAssert.Succeeded(result);
        var requests = run.RequestsTo("GET", path);
        Assert.Multiple(() =>
        {
            Assert.That(requests, Is.Not.Empty);
            Assert.That(run.Requests, Has.Count.EqualTo(requests.Count));
            Assert.That(requests.Select(request => request.QueryValue("sort")), Is.All.EqualTo(apiName));
        });
    }

    [TestCaseSource(nameof(CaseVariants))]
    public async Task An_option_value_is_matched_ignoring_case(string lower, string other, string path, string parameter, string expected)
    {
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(other);
        using var lowerRun = CliRun.Start();
        using var otherRun = CliRun.Start();
        lowerRun.StubPages(path, 200);
        otherRun.StubPages(path, 200);

        var lowerResult = await lowerRun.RunAsync(lower.Split(' '));
        var otherResult = await otherRun.RunAsync(other.Split(' '));

        CliAssert.Succeeded(lowerResult);
        CliAssert.Succeeded(otherResult);
        var lowerRequest = lowerRun.SingleRequest();
        var otherRequest = otherRun.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(lowerRequest.Path, Is.EqualTo(path));
            Assert.That(lowerRequest.QueryValue(parameter), Is.EqualTo(expected));
            Assert.That(otherRequest.Path, Is.EqualTo(lowerRequest.Path));
            Assert.That(otherRequest.Query, Is.EquivalentTo(lowerRequest.Query));
            Assert.That(otherResult.Stdout, Is.EqualTo(lowerResult.Stdout));
        });
    }

    [TestCaseSource(nameof(ValuesOutsideTheSet))]
    public async Task An_option_value_outside_its_allowed_values_exits_2_without_a_request(string[] rejected, string[] corrected, string method, string path, string json)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: json);

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, corrected, method, path);
    }

    // ExpiryAction.Delete revokes a system, so the CLI names it revoke (portal ExpiryAction).
    [TestCase("revoke", "Delete")]
    [TestCase("REVOKE", "Delete")]
    [TestCase("disable", "Disable")]
    [TestCase("Disable", "Disable")]
    public async Task System_enable_then_sends_the_api_expiry_action(string value, string expiryAction)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", SystemEnableUntilPath, json: ApiJson.System("ABCDE"));

        var request = await CliAssert.AcceptedAsync(run, "PUT", SystemEnableUntilPath, "system", "enable", "ABCDE", "--for", "8h", "--then", value);

        Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo(expiryAction));
    }

    [TestCase("delete", "Delete")]
    [TestCase("DELETE", "Delete")]
    [TestCase("disable", "Disable")]
    public async Task Policy_enable_then_sends_the_api_expiry_action(string value, string expiryAction)
    {
        using var run = CliRun.Start();
        run.Stub("PUT", PolicyEnableUntilPath, json: ApiJson.Policy(42));

        var request = await CliAssert.AcceptedAsync(run, "PUT", PolicyEnableUntilPath, "policy", "enable", "--id", "42", "--for", "8h", "--then", value);

        Assert.That(JsonAssert.Property(request.BodyJson, "expiryAction").GetString(), Is.EqualTo(expiryAction));
    }

    [Test]
    public async Task Json_output_keeps_the_api_name_of_a_value_the_cli_renames()
    {
        using var run = CliRun.Start();
        var system = JsonNode.Parse(ApiJson.System("ABCDE"))!;
        system["autoExpire"] = new JsonObject
        {
            ["timeZoneId"] = null,
            ["expiryDateTime"] = "2030-01-01T08:00:00.0000000+00:00",
            ["expiryAction"] = "Delete",
        };
        run.Stub("PUT", SystemEnableUntilPath, json: system.ToJsonString());

        var result = await run.RunAsync("system", "enable", "ABCDE", "--for", "8h", "--then", "revoke");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.SingleRequest().Path, Is.EqualTo(SystemEnableUntilPath));
            Assert.That(result.StdoutJson.GetProperty("autoExpire").GetProperty("expiryAction").GetString(), Is.EqualTo("Delete"));
        });
    }

    // ApiJson.System gives state Connected and systemType GeneralPurpose.
    [Test]
    public async Task Json_output_keeps_the_api_enum_names_whatever_the_option_values_given()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems");
        run.StubPages(path, 200, ApiJson.System("ABCDE"));

        var result = await run.RunAsync("system", "list", "--state", "connected", "--type", "general");

        var items = CliAssert.List(result, "system");
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(path), Is.EqualTo("0"));
            Assert.That(JsonRead.StringFieldList(items, "state"), Is.EqualTo("Connected"));
            Assert.That(JsonRead.StringFieldList(items, "systemType"), Is.EqualTo("GeneralPurpose"));
        });
    }

    private static TestCaseData Sort(string command, string pathSuffix, string value, string apiName) =>
        new TestCaseData(command, TestData.OrgPath(pathSuffix), value, apiName).SetArgDisplayNames(command, value);

    private static TestCaseData Variant(string lower, string other, string pathSuffix, string parameter, string expected) =>
        new TestCaseData(lower, other, TestData.OrgPath(pathSuffix), parameter, expected).SetArgDisplayNames(other);
}
