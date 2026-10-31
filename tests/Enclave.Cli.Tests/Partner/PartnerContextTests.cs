using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// spec/cli-surface.md "Context". The partner in use shows in the request path,
// /partner/{partnerId}/ (Enclave.Sdk.Api PartnerClient). No default partner is saved.
public class PartnerContextTests
{
    private const string PartnerIdOption = "--partner-id";

    private const string PartnerIdVariable = "ENCLAVE_CLI_PARTNER_ID";

    private static readonly Guid OtherPartnerId = new("0c5e7a91-2b4d-4f86-b3a0-9d1e6c48f2b7");

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    private static readonly string[] CustomerListOnly = [$"GET {TestData.PartnerPath("customers")}"];

    // Naming both ways lets an agent recover without reading help. CliRun sets ENCLAVE_CLI_ORG_ID, which
    // must not stand in for the partner.
    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.CommandsByName))]
    public async Task Every_partner_customer_command_exits_2_no_partner_naming_the_partner_id_option_and_variable_when_no_partner_is_chosen(string[] args)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result, "no_partner");
        Assert.That(result.Error.ToString(), Does.Contain(PartnerIdOption).And.Contain(PartnerIdVariable));
    }

    [TestCase(PartnerIdOption)]
    [TestCase(PartnerIdVariable)]
    public async Task A_partner_chosen_by_either_source_is_the_partner_a_partner_command_calls_the_api_for(string source)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        var options = ChooseTestPartner(run, source);

        var result = await run.RunAsync(["partner", "customer", "list", .. options]);

        PartnerApiFake.AssertSentToThePartnerApi(run, result);
    }

    // The variable holds a valid ID, so only a CLI that reads the option first sees the malformed
    // value. The second run shows the request goes to the option's partner.
    [Test]
    public async Task The_partner_id_option_is_used_over_ENCLAVE_CLI_PARTNER_ID()
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        PartnerApiFake.StubCustomerList(run, OtherPartnerId);
        _ = ChooseTestPartner(run, PartnerIdVariable);

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", "../x"));
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list", "--partner-id", OtherPartnerId.ToString()), OtherPartnerId);
    }

    // "Details". The first run shows the malformed variable fails the command when it is read.
    [Test]
    public async Task A_malformed_ENCLAVE_CLI_PARTNER_ID_is_not_read_when_the_partner_id_option_is_given()
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        run.Environment[PartnerIdVariable] = "not-a-guid";

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"));

        var options = ChooseTestPartner(run, PartnerIdOption);
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(["partner", "customer", "list", .. options]));
    }

    // With no other source it leaves no partner chosen.
    [Test]
    public async Task An_empty_ENCLAVE_CLI_PARTNER_ID_counts_as_unset()
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        run.Environment[PartnerIdVariable] = string.Empty;

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list"), "no_partner");

        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync("partner", "customer", "list", PartnerIdOption, TestData.PartnerId.ToString()));
    }

    // "Errors and exit codes". Each step removes the error the step before reported, so each check
    // is shown ahead of the next.
    [TestCaseSource(nameof(ArgumentErrors))]
    public async Task Checks_run_in_the_order_arguments_token_partner_then_the_partner_api(string[] rejected, string[] corrected)
    {
        using var run = CliRun.Start();
        PartnerApiFake.StubPartnerApi(run);
        run.Environment.Remove("ENCLAVE_CLI_PAT");

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.Rejected(run, await run.RunAsync(corrected), "token_missing");

        run.Environment["ENCLAVE_CLI_PAT"] = TestData.Token;
        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.Rejected(run, await run.RunAsync(corrected), "no_partner");

        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        PartnerApiFake.AssertSentToThePartnerApi(run, await run.RunAsync(corrected));
    }

    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.CommandsByName))]
    public async Task Every_partner_customer_command_exits_3_token_missing_without_a_token(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_PAT");
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(args);

        CliAssert.Rejected(run, result, "token_missing");
    }

    // --partner-id is an argument, so it is checked with the arguments, ahead of the token ("ID
    // checks").
    [Test]
    public async Task A_partner_id_option_that_is_not_a_guid_is_reported_before_a_missing_token()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_PAT");

        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", "../x"));
        CliAssert.Rejected(run, await run.RunAsync("partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString()), "token_missing");
    }

    // With no organisation chosen the token sees two, so a CLI that resolved the organisation would
    // exit 2 no_org.
    [Test]
    public async Task A_partner_customer_command_needs_no_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        BothOrgs.Stub(run);
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(CustomerListOnly));
    }

    // ENCLAVE_CLI_ORG names an organisation the token does not see, so a lookup would fail the command.
    [Test]
    public async Task A_partner_customer_command_does_not_look_up_the_organisation_ENCLAVE_CLI_ORG_names()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        run.Environment["ENCLAVE_CLI_ORG"] = "Initrode";
        run.Environment[PartnerIdVariable] = TestData.PartnerId.ToString();
        BothOrgs.Stub(run);
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(CustomerListOnly));
    }

    // A CLI that used the partner ID as the organisation would call /org/<partnerId>/systems.
    [Test]
    public async Task A_partner_is_not_used_as_the_organisation()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        BothOrgs.Stub(run);
        _ = ChooseTestPartner(run, PartnerIdVariable);
        var before = run.Requests.Count;

        var result = await run.RunAsync("system", "list");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(before), Is.EqualTo(OrgLookupOnly));
    }

    [TestCase("dns", "show", "dns")]
    public async Task The_partner_id_option_is_unknown_to_an_organisation_command(string noun, string verb, string pathSuffix)
    {
        ArgumentNullException.ThrowIfNull(pathSuffix);
        using var run = CliRun.Start();
        run.StubPages(TestData.OrgPath("systems"), 200, ApiJson.System("ABCDE"));
        run.Stub("GET", TestData.OrgPath("dns"), json: ApiJson.DnsSummary());

        _ = await CliAssert.RejectedThenAcceptedAsync(run, [noun, verb, PartnerIdOption, TestData.PartnerId.ToString()], [noun, verb], "GET", TestData.OrgPath(pathSuffix));
    }

    [Test]
    public async Task A_saved_organisation_is_not_used_as_the_partner()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_CLI_ORG_ID");
        BothOrgs.Stub(run);
        run.SaveLogin(defaultOrgId: TestData.OrgId.ToString());

        var result = await run.RunAsync("partner", "customer", "list");

        CliAssert.Rejected(run, result, "no_partner");
    }

    // One bad argument of each kind.
    private static IEnumerable<TestCaseData> ArgumentErrors()
    {
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("convert", "--billing-months", "6"), PartnerCommandTests.ForCustomer("convert", "--billing-months", "12"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.Line("list", "--no-such-option"), PartnerCommandTests.Line("list"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("update"), PartnerCommandTests.ForCustomer("update", "--systems", "30"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("update", "--hard-limit", "--no-hard-limit"), PartnerCommandTests.ForCustomer("update", "--hard-limit"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("show", "--org-id", TestData.CustomerOrgId), PartnerCommandTests.ForCustomer("show"));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.ForCustomer("add-admin", "--user", TestData.CustomerAdminEmail), PartnerCommandTests.ForCustomer("add-admin", "--user-id", TestData.PartnerStaffAccountId));
        yield return PartnerCommandTests.Pair(PartnerCommandTests.Line("show", "--org-id", "../x"), PartnerCommandTests.Line("show", "--org-id", TestData.CustomerOrgId));
    }

    // Chooses the test partner through source and returns the options a partner command then needs,
    // which are empty for the variable.
    private static string[] ChooseTestPartner(CliRun run, string source)
    {
        var partnerId = TestData.PartnerId.ToString();

        switch (source)
        {
            case PartnerIdOption:
                return [PartnerIdOption, partnerId];

            case PartnerIdVariable:
                run.Environment[PartnerIdVariable] = partnerId;
                return [];

            default:
                throw new ArgumentException($"\"{source}\" is not a way to choose a partner.", nameof(source));
        }
    }
}
