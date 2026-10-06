using System.Text.Json;
using Enclave.Api.Modules.SystemManagement.UnapprovedSystems.Models;
using Enclave.Cli.Tests.Support;
using Enclave.Configuration.Data.Enums;
using Enclave.Configuration.Data.Modules.EnrolmentKeys.Enums;
using Enclave.Configuration.Data.Modules.Policies.Enums;
using Enclave.Configuration.Data.Modules.Systems.Enums;
using Enclave.Configuration.Data.Modules.Tags.Enums;
using Enclave.Configuration.Data.Modules.TrustRequirements.Enums;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// `commands [<noun>]` is how an agent learns the CLI without reading help text: every command,
// option, value and error code, as JSON (proposal "Commands"; AGENTS.md requires it to stay
// complete). The document is {"commands": [...], "errors": [...]}; each command entry holds
// "command" (its full name), "description", "arguments" (each with "name", in order) and "options"
// (each with "name", the long form with its dashes, and "values" for an enum), and each error entry
// holds "code" and "exitCode".
[Category(TestCategory.Pending)]
public class CommandsTests
{
    // Every command in the proposal's tree ("Commands").
    private static readonly string[] ProposalCommands =
    [
        "login", "logout", "status",
        "org list", "org use", "org show", "org update",
        "org user list", "org user remove",
        "org invite list", "org invite send", "org invite cancel",
        "partner list", "partner use", "partner show", "partner update",
        "partner user list", "partner user update", "partner user remove",
        "partner invite list", "partner invite send", "partner invite update", "partner invite cancel",
        "partner customer list", "partner customer show", "partner customer create", "partner customer update", "partner customer convert",
        "partner customer admin list", "partner customer admin add", "partner customer admin remove",
        "partner customer invite list", "partner customer invite send", "partner customer invite cancel",
        "partner customer auto-sync enable", "partner customer auto-sync disable",
        "system list", "system show", "system update", "system enable", "system disable", "system revoke",
        "pending list", "pending show", "pending update", "pending approve", "pending decline",
        "key list", "key show", "key create", "key update", "key enable", "key disable", "key delete",
        "policy list", "policy show", "policy create", "policy update", "policy enable", "policy disable", "policy delete",
        "tag list", "tag show", "tag create", "tag update", "tag delete",
        "dns show",
        "dns zone list", "dns zone show", "dns zone create", "dns zone update", "dns zone delete",
        "dns record list", "dns record show", "dns record create", "dns record update", "dns record delete",
        "trust list", "trust show", "trust create", "trust update", "trust delete",
        "log list",
        "commands",
    ];

    // The arguments of every command, named as the proposal's tree names them. `org use` takes
    // `<orgId|name>`, which is not one name, so it is left out.
    private static readonly Dictionary<string, string[]> ProposalArguments = BuildProposalArguments();

    // The members of SystemQuerySortMode (portal Enclave.Configuration.Data/Modules/Systems/Enums,
    // Enclave.Sdk.Api.Data 304.48.0), written out so that one check does not depend on reflection.
    private static readonly string[] SystemSortValues = ["RecentlyEnrolled", "RecentlyConnected", "Description", "DescriptionOrHostname", "EnrolmentKeyUsed"];

    private static readonly string[] OutputFormats = ["json", "table", "id"];

    public static IEnumerable<TestCaseData> OptionSets()
    {
        yield return Options(
            "system list",
            ["--search", "--key", "--dns-name", "--include-disabled", "--sort", "--org", "--verbose", "--limit", "--all"],
            ["--dry-run", "--yes", "--partner", "--from-file", "--template"]);
        yield return Options(
            "system update",
            ["--description", "--notes", "--set-tags", "--from-file", "--template", "--dry-run", "--org", "--verbose"],
            ["--limit", "--all", "--partner"]);
        yield return Options(
            "system enable",
            ["--until", "--expiry-action", "--dry-run", "--yes", "--org", "--verbose"],
            ["--limit", "--all", "--partner"]);
        yield return Options(
            "system revoke",
            ["--dry-run", "--yes", "--org", "--verbose"],
            ["--limit", "--all", "--partner", "--from-file"]);
        yield return Options(
            "pending list",
            ["--search", "--key", "--sort", "--org", "--limit", "--all"],
            ["--include-disabled", "--dns-name", "--dry-run", "--yes"]);
        yield return Options(
            "key create",
            ["--description", "--type", "--approval-mode", "--uses-remaining", "--tags", "--notes", "--from-file", "--template", "--dry-run", "--org"],
            ["--limit", "--all", "--partner"]);
        yield return Options(
            "policy create",
            ["--from-file", "--template", "--dry-run", "--org"],
            ["--description", "--notes", "--limit", "--all"]);
        yield return Options(
            "tag update",
            ["--name", "--colour", "--notes", "--from-file", "--template", "--dry-run", "--org"],
            ["--limit", "--all"]);
        yield return Options(
            "dns record list",
            ["--zone", "--search", "--org", "--limit", "--all"],
            ["--dry-run", "--yes"]);
        yield return Options(
            "dns record create",
            ["--zone", "--type", "--tags", "--systems", "--notes", "--from-file", "--template", "--dry-run", "--org"],
            ["--limit", "--all"]);
        yield return Options(
            "org update",
            ["--name", "--website", "--phone", "--from-file", "--template", "--dry-run"],
            ["--limit", "--all", "--partner"]);
        yield return Options(
            "log list",
            ["--org", "--limit", "--all", "--verbose"],
            ["--search", "--dry-run", "--yes"]);
        yield return Options(
            "partner customer list",
            ["--partner", "--limit", "--all", "--verbose"],
            ["--org", "--dry-run", "--yes"]);
        yield return Options(
            "commands",
            ["--verbose"],
            ["--org", "--partner", "--limit", "--all", "--dry-run", "--yes"]);
    }

    [Test]
    public async Task Commands_describes_every_command_in_the_proposal_with_a_description_arguments_and_options()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.Multiple(() =>
        {
            Assert.That(entries.Keys, Is.EquivalentTo(ProposalCommands));

            foreach (var (name, entry) in entries)
            {
                Assert.That(entry.GetProperty("description").GetString(), Is.Not.Null.And.Not.Empty, name);
                Assert.That(entry.GetProperty("arguments").ValueKind, Is.EqualTo(JsonValueKind.Array), name);
                Assert.That(OptionNames(entry), Does.Contain("--verbose"), name);
            }
        });
    }

    [Test]
    public async Task Commands_lists_the_arguments_of_every_command_by_name_in_order()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.Multiple(() =>
        {
            foreach (var (name, expected) in ProposalArguments)
            {
                if (!entries.TryGetValue(name, out var entry))
                {
                    Assert.Fail($"`commands` does not describe {name}.");
                    continue;
                }

                var actual = entry.GetProperty("arguments").EnumerateArray()
                    .Select(argument => argument.GetProperty("name").GetString())
                    .ToArray();
                Assert.That(actual, Is.EqualTo(expected), name);
            }
        });
    }

    // Each command lists the options it accepts, including the shared ones that apply to it, and
    // leaves out the ones that do not ("Options on every command": --dry-run and --yes only where
    // something changes, --limit and --all on list only, --org within an organisation, --partner on
    // partner commands).
    [TestCaseSource(nameof(OptionSets))]
    public async Task Commands_lists_the_options_each_command_accepts(string command, string[] present, string[] absent)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.That(entries, Does.ContainKey(command));

        var names = OptionNames(entries[command]);
        Assert.Multiple(() =>
        {
            Assert.That(names, Is.SupersetOf(present));
            Assert.That(names.Intersect(absent), Is.Empty);
        });
    }

    // Option values that name an API enum take the API's names, and `commands` lists them (proposal
    // "Options on every command").
    [Test]
    public async Task Commands_lists_the_sort_values_of_system_list()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        Assert.That(OptionValues(CommandEntries(result.StdoutJson), "system list", "--sort"), Is.EquivalentTo(SystemSortValues));
    }

    // The expected values come from the Enclave.Sdk.Api.Data enum behind each option, so the list
    // follows the SDK version in use.
    [TestCase("system list", "--sort", typeof(SystemQuerySortMode))]
    [TestCase("pending list", "--sort", typeof(UnapprovedSystemQuerySortMode))]
    [TestCase("key list", "--sort", typeof(EnrolmentKeySortOrder))]
    [TestCase("policy list", "--sort", typeof(PolicySortOrder))]
    [TestCase("tag list", "--sort", typeof(TagQuerySortOrder))]
    [TestCase("trust list", "--sort", typeof(TrustRequirementSortOrder))]
    [TestCase("key create", "--type", typeof(EnrolmentKeyType))]
    [TestCase("key create", "--approval-mode", typeof(ApprovalMode))]
    [TestCase("system enable", "--expiry-action", typeof(ExpiryAction))]
    [TestCase("key enable", "--expiry-action", typeof(ExpiryAction))]
    [TestCase("policy enable", "--expiry-action", typeof(ExpiryAction))]
    public async Task Commands_lists_the_api_enum_names_as_the_values_of_an_enum_option(string command, string option, Type values)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        Assert.That(OptionValues(CommandEntries(result.StdoutJson), command, option), Is.EquivalentTo(Enum.GetNames(values)));
    }

    // -o takes json, table and id; an agent reads the allowed formats from `commands`.
    [Test]
    public async Task Commands_lists_the_output_formats_of_a_list_command()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var formats = CommandEntries(result.StdoutJson)["system list"].GetProperty("options").EnumerateArray()
            .Where(option => option.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
            .Select(option => JsonAssert.Strings(option.GetProperty("values")))
            .Where(values => values.Contains("json"));
        Assert.That(formats, Has.One.EquivalentTo(OutputFormats));
    }

    // The error codes are a fixed set the CLI owns, so an agent can handle each one (proposal
    // "Errors and exit codes"). not_implemented is the code partner commands report while
    // Enclave.Sdk.Api has no partner clients.
    [Test]
    public async Task Commands_lists_the_fixed_error_codes_with_their_exit_codes()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var errors = result.StdoutJson.GetProperty("errors").EnumerateArray()
            .ToDictionary(error => error.GetProperty("code").GetString()!, error => error.GetProperty("exitCode").GetInt32(), StringComparer.Ordinal);
        Assert.That(errors, Is.EquivalentTo(new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["api_error"] = 1,
            ["not_implemented"] = 1,
            ["invalid_argument"] = 2,
            ["no_org"] = 2,
            ["no_partner"] = 2,
            ["token_missing"] = 3,
            ["token_invalid"] = 3,
            ["forbidden"] = 4,
            ["not_found"] = 5,
            ["confirmation_required"] = 6,
            ["transient"] = 7,
        }));
    }

    // `commands <noun>` keeps the answer small when an agent needs one noun only.
    [TestCase("system")]
    [TestCase("dns")]
    [TestCase("partner")]
    public async Task Commands_with_a_noun_describes_only_that_nouns_commands(string noun)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands", noun);

        AssertSucceededWithoutRequests(run, result);
        Assert.That(
            CommandEntries(result.StdoutJson).Keys,
            Is.EquivalentTo(ProposalCommands.Where(command => command.StartsWith(noun + " ", StringComparison.Ordinal))));
    }

    // A plural noun is a hidden alias wherever a noun is accepted (proposal "Shape and naming").
    [Test]
    public async Task Commands_with_a_plural_noun_describes_the_singular_nouns_commands()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands", "systems");

        AssertSucceededWithoutRequests(run, result);
        Assert.That(
            CommandEntries(result.StdoutJson).Keys,
            Is.EquivalentTo(ProposalCommands.Where(command => command.StartsWith("system ", StringComparison.Ordinal))));
    }

    [Test]
    public async Task Commands_with_an_unknown_noun_exits_2_with_invalid_argument()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands", "widget");

        CliAssert.Rejected(run, result);
    }

    // An agent reads `commands` before it has a token, so the command needs none and calls nothing.
    [TestCase("commands")]
    [TestCase("commands system")]
    public async Task Commands_needs_no_token_and_makes_no_request(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG");

        var result = await run.RunAsync(command.Split(' '));

        AssertSucceededWithoutRequests(run, result);
        Assert.That(CommandEntries(result.StdoutJson), Does.ContainKey("system list"));
    }

    private static TestCaseData Options(string command, string[] present, string[] absent) =>
        new TestCaseData(command, present, absent).SetArgDisplayNames(command);

    private static Dictionary<string, string[]> BuildProposalArguments()
    {
        var arguments = ProposalCommands
            .Where(command => command != "org use")
            .ToDictionary(command => command, _ => Array.Empty<string>(), StringComparer.Ordinal);

        void Set(string argumentNames, params string[] commands)
        {
            foreach (var command in commands)
            {
                arguments[command] = argumentNames.Split(' ');
            }
        }

        Set("accountId", "org user remove", "partner user update", "partner user remove");
        Set("email", "org invite send", "org invite cancel", "partner invite send");
        Set("partnerId", "partner use");
        Set("inviteId", "partner invite update", "partner invite cancel");
        Set("customerId", "partner customer show", "partner customer update", "partner customer convert", "partner customer admin list", "partner customer invite list", "partner customer auto-sync enable", "partner customer auto-sync disable");
        Set("customerId accountId", "partner customer admin add", "partner customer admin remove");
        Set("customerId email", "partner customer invite send");
        Set("customerId inviteId", "partner customer invite cancel");
        Set("systemId", "system show", "system update", "system enable", "system disable", "system revoke", "pending show", "pending update", "pending approve", "pending decline");
        Set("keyId", "key show", "key update", "key enable", "key disable", "key delete");
        Set("policyId", "policy show", "policy update", "policy enable", "policy disable", "policy delete");
        Set("tag", "tag show", "tag create", "tag update", "tag delete");
        Set("zoneId", "dns zone show", "dns zone update", "dns zone delete");
        Set("recordId", "dns record show", "dns record update", "dns record delete");
        Set("name", "dns zone create", "dns record create");
        Set("trustId", "trust show", "trust update", "trust delete");
        Set("noun", "commands");

        return arguments;
    }

    private static Dictionary<string, JsonElement> CommandEntries(JsonElement document) =>
        document.GetProperty("commands").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("command").GetString()!, entry => entry, StringComparer.Ordinal);

    private static string[] OptionNames(JsonElement entry) =>
        entry.GetProperty("options").EnumerateArray()
            .Select(option => option.GetProperty("name").GetString()!)
            .ToArray();

    private static string[] OptionValues(Dictionary<string, JsonElement> entries, string command, string option)
    {
        Assert.That(entries, Does.ContainKey(command));

        var match = entries[command].GetProperty("options").EnumerateArray()
            .Where(candidate => candidate.GetProperty("name").GetString() == option)
            .ToArray();
        Assert.That(match, Has.Length.EqualTo(1), $"{command} {option}");

        return JsonAssert.Strings(match[0].GetProperty("values"));
    }

    private static void AssertSucceededWithoutRequests(CliRun run, CliResult result) =>
        Assert.Multiple(() =>
        {
            CliAssert.Succeeded(result);
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
}
