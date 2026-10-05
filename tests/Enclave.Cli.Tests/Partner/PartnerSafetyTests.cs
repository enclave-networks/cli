using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Partner;

// The confirmation and ID checks run before any call, so they apply to partner commands although the partner API call
// reports not_implemented. Each test sets a valid partner, so a command that fails a check here fails it for the reason
// under test.
public class PartnerSafetyTests
{
    // Values a GUID check must reject. `../x` is the case that matters most: Enclave.Sdk.Api 1.0.4 puts IDs into URL
    // paths unescaped and .NET resolves `..` against the base address (proposal, "ID checks"), so an unchecked ID can
    // send a request to a different resource.
    private static readonly string[] NotGuids = ["../x", "not-a-guid"];

    // Removing a person's access, giving an outside address or an account access, and converting a customer to paid
    // all exit 6 without --yes (proposal, "Confirmation"), so an agent cannot do them by accident and a permission rule
    // keyed on --yes can ask a human first.
    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.ApiCommandsThatNeedYes))]
    public async Task Partner_commands_that_need_confirmation_exit_6_naming_yes_without_it(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();

        var result = await PartnerCommandTests.RunAsync(run, args);

        PartnerCommandTests.AssertError(run, result, 6, "confirmation_required");
        Assert.That(result.Error.ToString(), Does.Contain("--yes"));
    }

    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.ApiCommandsThatNeedYes))]
    public async Task Partner_commands_that_need_confirmation_reach_the_partner_api_with_yes(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();

        var result = await PartnerCommandTests.RunAsync(run, [.. args, "--yes"]);

        PartnerCommandTests.AssertNotImplemented(run, result);
    }

    // --dry-run is checked before --yes (proposal, "Confirmation"): a dry run changes nothing, so it needs no
    // confirmation, and a partner dry run goes on to report not_implemented.
    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.ApiCommandsThatNeedYes))]
    public async Task Dry_run_on_a_partner_command_that_needs_confirmation_reports_not_implemented_without_yes(
        string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();

        var result = await PartnerCommandTests.RunAsync(run, [.. args, "--dry-run"]);

        PartnerCommandTests.AssertNotImplemented(run, result);
    }

    // A customer ID is its organisation ID and account IDs are GUIDs too (proposal, "ID checks"). --yes is given where
    // the command needs it, so the ID check is the only check that can fail.
    [TestCaseSource(nameof(ApiCommandsWithACustomerOrAccountIdThatIsNotAGuid))]
    public async Task Customer_and_account_ids_that_are_not_guids_exit_2_before_the_partner_api(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();

        var result = await PartnerCommandTests.RunAsync(run, args);

        PartnerCommandTests.AssertError(run, result, 2, "invalid_argument");
    }

    // ENCLAVE_PARTNER holds a valid ID, so the value given with --partner is the one that must be checked.
    [TestCaseSource(typeof(PartnerCommandTests), nameof(PartnerCommandTests.ApiCommandsThatNeedAPartner))]
    public async Task A_partner_option_that_is_not_a_guid_exits_2_before_the_partner_api(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();

        var result = await PartnerCommandTests.RunAsync(run, [.. args, "--partner", "../x"]);

        PartnerCommandTests.AssertError(run, result, 2, "invalid_argument");
    }

    [TestCase("../x")]
    [TestCase("not-a-guid")]
    [TestCase("Acme Partners")]
    public async Task An_ENCLAVE_PARTNER_that_is_not_a_guid_exits_2_before_the_partner_api(string value)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = value;

        var result = await run.RunAsync("partner", "customer", "list");

        PartnerCommandTests.AssertError(run, result, 2, "invalid_argument");
    }

    // The proposal leaves the partner API invite ID's exact format to the partner clients, but an invite ID goes into
    // the URL path like any other ID, so a value that would rewrite the path is rejected before any call.
    [TestCase("partner", "invite", "update", "../x", "--from-file", PartnerCommandTests.BodyFile)]
    [TestCase("partner", "invite", "cancel", "../x")]
    [TestCase("partner", "customer", "invite", "cancel", PartnerCommandTests.CustomerId, "../x")]
    public async Task Invite_ids_that_would_rewrite_the_request_path_exit_2_before_the_partner_api(params string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();

        var result = await PartnerCommandTests.RunAsync(run, args);

        PartnerCommandTests.AssertError(run, result, 2, "invalid_argument");
    }

    // --dry-run and --yes exist only on commands that change something, so on a read they are unknown options and exit
    // 2 like any other parse error. An agent that asks for a dry run of a read learns that the option means nothing
    // there.
    [TestCaseSource(nameof(ApiCommandsThatChangeNothingWithDryRunOrYes))]
    public async Task Dry_run_and_yes_are_unknown_options_on_partner_commands_that_change_nothing(string[] args)
    {
        using var run = CliRun.Start();
        run.Environment[PartnerCommandTests.PartnerVariable] = TestData.PartnerId.ToString();

        var result = await PartnerCommandTests.RunAsync(run, args);

        PartnerCommandTests.AssertError(run, result, 2, "invalid_argument");
    }

    private static IEnumerable<TestCaseData> ApiCommandsWithACustomerOrAccountIdThatIsNotAGuid()
    {
        foreach (var command in PartnerCommandTests.ApiCommands)
        {
            for (var position = 0; position < command.Arguments.Count; position++)
            {
                if (command.Arguments[position] is not (PartnerCommandTests.CustomerId or PartnerCommandTests.AccountId))
                {
                    continue;
                }

                foreach (var notGuid in NotGuids)
                {
                    var arguments = command.Arguments.ToArray();
                    arguments[position] = notGuid;

                    yield return PartnerCommandTests.Case(
                        command with { Arguments = arguments },
                        command.NeedsYes ? ["--yes"] : []);
                }
            }
        }
    }

    private static IEnumerable<TestCaseData> ApiCommandsThatChangeNothingWithDryRunOrYes() =>
        PartnerCommandTests.ApiCommands
            .Where(command => !command.Changes)
            .SelectMany(command => new[]
            {
                PartnerCommandTests.Case(command, "--dry-run"),
                PartnerCommandTests.Case(command, "--yes"),
            });
}
