using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// `commands`, given nothing or a command's words, is how an agent learns the CLI without reading
// help text: every command, option, allowed value and error code, as JSON (proposed-cli-surface.md
// "Commands", "Details"; AGENTS.md requires it to stay complete). The document is
// {"commands": [...], "errors": [...]} ("Details"). Each command entry holds "command" (its full
// name, such as "dns list-zones"), "description", "arguments" (each with "name", in order) and
// "options" (each with "name", the long form with its dashes, and "values" when the option takes
// one of a fixed set). Each error entry holds "code" and "exitCode". With --search-keys and the
// words of system list, key list, policy list or tag list, the one entry also holds "searchKeys",
// the API's search keys for that list ("Details"). Agents read these by their exact names, so the
// lookups here are exact.
public class CommandsTests
{
    // Every command in proposed-cli-surface.md "Commands", with its arguments named as the tree
    // names them. `org use <name> | --id <orgId>` and `org remove-user <email> | --id <accountId>`
    // take the name or email address as the argument and the ID through --id. The arguments test
    // leaves out `commands` itself.
    private static readonly Dictionary<string, string[]> CommandTree = new(StringComparer.Ordinal)
    {
        ["login"] = [],
        ["logout"] = [],
        ["status"] = [],
        ["org list"] = [],
        ["org use"] = ["name"],
        ["org show"] = [],
        ["org update"] = [],
        ["org list-users"] = [],
        ["org remove-user"] = ["email"],
        ["org list-invites"] = [],
        ["org invite"] = ["email"],
        ["org cancel-invite"] = ["email"],
        ["partner use"] = [],
        ["partner customer list"] = [],
        ["partner customer show"] = ["customer"],
        ["partner customer create"] = ["name"],
        ["partner customer update"] = ["customer"],
        ["partner customer convert"] = ["customer"],
        ["partner customer list-admins"] = ["customer"],
        ["partner customer add-admin"] = ["customer"],
        ["partner customer remove-admin"] = ["customer"],
        ["partner customer list-invites"] = ["customer"],
        ["partner customer invite"] = ["customer"],
        ["partner customer cancel-invite"] = ["customer"],
        ["partner customer enable-auto-sync"] = ["customer"],
        ["partner customer disable-auto-sync"] = ["customer"],
        ["system list"] = [],
        ["system show"] = ["systemId"],
        ["system update"] = ["systemId"],
        ["system approve"] = ["systemId"],
        ["system decline"] = ["systemId"],
        ["system enable"] = ["systemId"],
        ["system disable"] = ["systemId"],
        ["system revoke"] = ["systemId"],
        ["key list"] = [],
        ["key show"] = ["key"],
        ["key create"] = ["description"],
        ["key update"] = ["key"],
        ["key enable"] = ["key"],
        ["key disable"] = ["key"],
        ["key delete"] = ["key"],
        ["policy list"] = [],
        ["policy show"] = ["policy"],
        ["policy create"] = ["description"],
        ["policy update"] = ["policy"],
        ["policy enable"] = ["policy"],
        ["policy disable"] = ["policy"],
        ["policy delete"] = ["policy"],
        ["tag list"] = [],
        ["tag show"] = ["tag"],
        ["tag set"] = ["tag"],
        ["tag delete"] = ["tag"],
        ["dns show"] = [],
        ["dns list-zones"] = [],
        ["dns show-zone"] = ["zone"],
        ["dns create-zone"] = ["zone"],
        ["dns update-zone"] = ["zone"],
        ["dns delete-zone"] = ["zone"],
        ["dns list-hostnames"] = [],
        ["dns show-hostname"] = ["hostname"],
        ["dns create-hostname"] = ["hostname"],
        ["dns update-hostname"] = ["hostname"],
        ["dns delete-hostname"] = ["hostname"],
        ["trust list"] = [],
        ["trust show"] = ["trust"],
        ["trust create"] = ["description"],
        ["trust update"] = ["trust"],
        ["trust delete"] = ["trust"],
        ["log"] = [],
        ["commands"] = [],
    };

    // The commands that change something through the API, which take --dry-run ("Options on every
    // command").
    private static readonly string[] ChangeCommands =
    [
        "org update", "org remove-user", "org invite", "org cancel-invite",
        "partner customer create", "partner customer update", "partner customer convert",
        "partner customer add-admin", "partner customer remove-admin", "partner customer invite",
        "partner customer cancel-invite", "partner customer enable-auto-sync", "partner customer disable-auto-sync",
        "system update", "system approve", "system decline", "system enable", "system disable", "system revoke",
        "key create", "key update", "key enable", "key disable", "key delete",
        "policy create", "policy update", "policy enable", "policy disable", "policy delete",
        "tag set", "tag delete",
        "dns create-zone", "dns update-zone", "dns delete-zone", "dns create-hostname", "dns update-hostname", "dns delete-hostname",
        "trust create", "trust update", "trust delete",
    ];

    // Reads have nothing to preview, and login, logout, org use and partner use change only local
    // files ("Dry run").
    private static readonly string[] CommandsWithoutDryRun =
    [
        "login", "logout", "org use", "partner use",
        "status", "org list", "org show", "org list-users", "org list-invites",
        "partner customer list", "partner customer show", "partner customer list-admins", "partner customer list-invites",
        "system list", "system show", "key list", "key show", "policy list", "policy show", "tag list", "tag show",
        "dns show", "dns list-zones", "dns show-zone", "dns list-hostnames", "dns show-hostname",
        "trust list", "trust show", "log", "commands",
    ];

    // The commands that act within an organisation, which take --org and --org-id ("Options on
    // every command", "Context").
    private static readonly string[] OrganisationCommands =
    [
        "org show", "org update", "org list-users", "org remove-user", "org list-invites", "org invite", "org cancel-invite",
        "system list", "system show", "system update", "system approve", "system decline", "system enable", "system disable", "system revoke",
        "key list", "key show", "key create", "key update", "key enable", "key disable", "key delete",
        "policy list", "policy show", "policy create", "policy update", "policy enable", "policy disable", "policy delete",
        "tag list", "tag show", "tag set", "tag delete",
        "dns show", "dns list-zones", "dns show-zone", "dns create-zone", "dns update-zone", "dns delete-zone",
        "dns list-hostnames", "dns show-hostname", "dns create-hostname", "dns update-hostname", "dns delete-hostname",
        "trust list", "trust show", "trust create", "trust update", "trust delete",
        "log",
    ];

    // Options the specification removed or never had: output formats, confirmation, file and
    // template input, the token as an argument, --all and --search on lists, and the old name of
    // --then ("Changes to AGENTS.md").
    private static readonly string[] OptionsNoCommandHas =
    [
        "-o", "--output", "--yes", "--from-file", "--template", "--token", "--all", "--search", "--expiry-action",
    ];

    // The `system list` row of "Command options", with --pending and the `system list --pending`
    // row's --waiting-for, and the options every organisation command takes.
    private static readonly string[] SystemListOptions =
    [
        "--filter", "--tag", "--state", "--os", "--type", "--gateway", "--key", "--key-id", "--dns-name", "--not-seen-for",
        "--include-disabled", "--sort", "--pending", "--waiting-for", "--org", "--org-id", "--verbose",
    ];

    // SystemQuerySortMode (portal Enclave.Configuration.Data/Modules/Systems/Enums) in lower-case,
    // hyphenated form. With --pending, --sort takes the waiting systems' values ("Details"),
    // UnapprovedSystemQuerySortMode, whose members are a subset of these, so these are every value
    // --sort on `system list` takes.
    private static readonly string[] SystemSortValues =
    [
        "recently-enrolled", "recently-connected", "description", "description-or-hostname", "enrolment-key-used",
    ];

    private static readonly string[] SystemStateValues = ["connected", "disconnected"];

    private static readonly string[] SystemListOnly = ["system list"];

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    // Each case names options the command must list and options it must not, so a command that
    // lists options it does not take fails as well as one that misses its own ("Command options").
    // The lists are space-separated to keep the cases on one line each.
    public static IEnumerable<TestCaseData> OptionSets()
    {
        yield return Options("system list", string.Join(' ', SystemListOptions), "--dry-run --partner-id --limit --id");
        yield return Options("system update", "--description --notes --set-tags --add-tags --remove-tags --enable-gateway-for --disable-gateway --pending --dry-run --org --org-id", "--tags --limit");
        yield return Options("system enable", "--for --until --then --dry-run --org --org-id", "--limit");
        yield return Options("key list", "--filter --tag --approval --state --include-disabled --sort", "--dry-run --limit");
        yield return Options("key show", "--id", "--dry-run");
        yield return Options("key create", "--ephemeral --auto-approve --uses --tags --allow-ip --keep-disconnected --for --until --then --notes --dry-run", "--description --type --approval-mode --uses-remaining");
        yield return Options("key update", "--id --description --notes --auto-approve --require-approval --uses --set-tags --add-tags --remove-tags --set-allow-ip --keep-disconnected --dry-run", "--tags --allow-ip");
        yield return Options("key enable", "--id --for --until --then --dry-run", "--limit");
        yield return Options("policy list", "--filter --tag --state --include-disabled --sort", "--dry-run");
        yield return Options("policy create", "--senders --receivers --acl --trust --trust-id --gateway --mode --subnet-filter --active-hours --for --until --then --notes --disabled --dry-run", "--description --sender-tags --receiver-tags");
        yield return Options("policy update", "--id --description --notes --set-senders --set-receivers --set-acl --set-trust --set-trust-id --set-gateway --mode --set-subnet-filter --set-active-hours --dry-run", "--senders --receivers --acl");
        yield return Options("policy enable", "--id --for --until --then --dry-run", "--limit");
        yield return Options("tag list", "--filter --sort", "--dry-run");
        yield return Options("tag set", "--name --colour --trust --trust-id --notes --dry-run", "--id");
        yield return Options("dns create-zone", "--auto-dns-tags --notes --dry-run", "--id");
        yield return Options("dns update-zone", "--id --name --set-auto-dns-tags --notes --dry-run", "--auto-dns-tags");
        yield return Options("dns list-hostnames", "--zone --zone-id --filter", "--dry-run");
        yield return Options("dns create-hostname", "--tags --systems --notes --dry-run", "--type");
        yield return Options("dns update-hostname", "--id --name --set-tags --add-tags --remove-tags --set-systems --notes --dry-run", "--tags --systems");
        yield return Options("trust list", "--filter --type --sort", "--dry-run");
        yield return Options("trust create", "--notes --authority --tenant --authority-uri --client-id --audience --claim --allow-ip --block-ip --allow-country --block-country --dry-run", "--description");
        yield return Options("trust update", "--id --description --notes --set-claim --set-allow-ip --set-block-ip --set-allow-country --set-block-country --dry-run", "--claim --allow-ip --allow-country");
        yield return Options("org update", "--name --website --phone --dry-run --org --org-id", "--limit");
        yield return Options("org use", "--id", "--dry-run");
        yield return Options("org remove-user", "--id --dry-run", "--limit");
        yield return Options("partner use", "--id", "--dry-run");
        yield return Options("partner customer create", "--owner --domain --contact --systems --gateways --industry-discount --hard-limit --auto-sync --partner-id --dry-run", "--limit");
        yield return Options("partner customer update", "--org-id --name --contact --systems --gateways --industry-discount --no-industry-discount --hard-limit --no-hard-limit --partner-id --dry-run", "--auto-sync");
        yield return Options("partner customer convert", "--org-id --billing-months --partner-id --dry-run", "--limit");
        yield return Options("partner customer add-admin", "--org-id --user-id --partner-id --dry-run", "--user");
        yield return Options("partner customer remove-admin", "--org-id --user --user-id --partner-id --dry-run", "--limit");
        yield return Options("partner customer invite", "--org-id --email --partner-id --dry-run", "--limit");
        yield return Options("log", "--limit --since --until --user --level --filter --org --org-id", "--dry-run");
        yield return Options("login", "--token-stdin", "--dry-run --org --org-id --partner-id");
        yield return Options("logout", "--verbose", "--dry-run --org --org-id --partner-id");
        yield return Options("status", "--org --org-id --partner-id", "--dry-run");
        yield return Options("commands", "--search-keys --pending --org --org-id --verbose", "--dry-run --partner-id");
    }

    // The lists whose search keys Enclave.Sdk.Api 1.1.0 reads (GetSearchKeysAsync on
    // ISystemsClient, IUnapprovedSystemsClient, IEnrolmentKeysClient, IPoliciesClient and
    // ITagsClient), each with the route the call takes and a body for the fake API to serve there
    // ("Details"). --pending chooses the systems waiting for approval, as it does on system list.
    public static IEnumerable<TestCaseData> SearchKeyLists()
    {
        yield return SearchKeys("system list", "system list", "systems/meta/search-keys", ApiJson.SystemSearchKeys());
        yield return SearchKeys("system list --pending", "system list", "unapproved-systems/meta/search-keys", ApiJson.SystemSearchKeys());
        yield return SearchKeys("key list", "key list", "enrolment-keys/meta/search-keys", ApiJson.KeySearchKeys());
        yield return SearchKeys("policy list", "policy list", "policies/meta/search-keys", ApiJson.PolicySearchKeys());
        yield return SearchKeys("tag list", "tag list", "tags/meta/search-keys", ApiJson.TagSearchKeys());
    }

    // Each case is a command line --search-keys or --pending makes wrong, the option the error names,
    // and a corrected command line. --search-keys takes one of the four lists by its words, so no
    // words, a noun, a command that is not a list, and a list Enclave.Sdk.Api reads no search keys
    // for each exit 2. --pending chooses the waiting systems' keys, so it needs --search-keys and
    // system list. The organisation options choose the organisation the search keys come from, and
    // without --search-keys `commands` acts within none ("Details").
    public static IEnumerable<TestCaseData> SearchKeyRejections()
    {
        yield return Rejection("--search-keys", "--search-keys", "system list --search-keys", "systems/meta/search-keys");
        yield return Rejection("system --search-keys", "--search-keys", "system list --search-keys", "systems/meta/search-keys");
        yield return Rejection("system show --search-keys", "--search-keys", "system list --search-keys", "systems/meta/search-keys");
        yield return Rejection("trust list --search-keys", "--search-keys", "tag list --search-keys", "tags/meta/search-keys");
        yield return Rejection("dns list-hostnames --search-keys", "--search-keys", "policy list --search-keys", "policies/meta/search-keys");
        yield return Rejection("log --search-keys", "--search-keys", "key list --search-keys", "enrolment-keys/meta/search-keys");
        yield return Rejection("partner customer list --search-keys", "--search-keys", "key list --search-keys", "enrolment-keys/meta/search-keys");
        yield return Rejection("commands --search-keys", "--search-keys", "tag list --search-keys", "tags/meta/search-keys");
        yield return Rejection("key list --pending --search-keys", "--pending", "system list --pending --search-keys", "unapproved-systems/meta/search-keys");
        yield return Rejection("system list --pending", "--pending", "system list --pending --search-keys", "unapproved-systems/meta/search-keys");
        yield return Rejection($"system list --org-id {TestData.OrgId}", "--org-id", $"system list --org-id {TestData.OrgId} --search-keys", "systems/meta/search-keys");
        yield return Rejection($"system list --org {TestData.OrgName}", "--org", $"system list --org-id {TestData.OrgId} --search-keys", "systems/meta/search-keys");
    }

    // --search-keys acts within an organisation, so --org and --org-id choose it over ENCLAVE_ORG_ID
    // ("Context"). The environment names Acme and the options Globex, and the fake API serves both
    // organisations' routes, so a CLI that ignored the options would ask for Acme's keys. A name
    // costs one lookup call; an ID costs none.
    public static IEnumerable<TestCaseData> SearchKeyOrganisations()
    {
        var path = OtherOrgPath("enrolment-keys/meta/search-keys");

        yield return new TestCaseData("--org-id", TestData.OtherOrgId.ToString(), new[] { $"GET {path}" }).SetArgDisplayNames("--org-id");
        yield return new TestCaseData("--org", TestData.OtherOrgName, new[] { "GET /account/orgs", $"GET {path}" }).SetArgDisplayNames("--org");
    }

    // The values are the ones the option table in "Command options" lists, and for --sort the API
    // enum's members in lower-case, hyphenated form ("Options on every command"): SystemQuerySortMode,
    // EnrolmentKeySortOrder, PolicySortOrder, TagQuerySortOrder and TrustRequirementSortOrder in the
    // portal's Enclave.Configuration.Data/Modules/*/Enums.
    public static IEnumerable<TestCaseData> AllowedValues()
    {
        yield return Values("system list", "--sort", SystemSortValues);
        yield return Values("system list", "--state", SystemStateValues);
        yield return Values("system list", "--os", "windows", "linux", "mac");
        yield return Values("system list", "--type", "general", "ephemeral");
        yield return Values("system enable", "--then", "disable", "revoke");
        yield return Values("key list", "--sort", "description", "last-used", "approval-mode", "uses-remaining");
        yield return Values("key list", "--approval", "automatic", "manual");
        yield return Values("key list", "--state", "enabled", "disabled", "no-uses");
        yield return Values("key create", "--then", "disable", "delete");
        yield return Values("key enable", "--then", "disable", "delete");
        yield return Values("policy list", "--sort", "description", "recently-created");
        yield return Values("policy list", "--state", "enabled", "disabled");
        yield return Values("policy create", "--then", "disable", "delete");
        yield return Values("policy create", "--mode", "balanced", "ordered", "geographic");
        yield return Values("policy update", "--mode", "balanced", "ordered", "geographic");
        yield return Values("policy enable", "--then", "disable", "delete");
        yield return Values("tag list", "--sort", "alphabetical", "recently-used", "referenced-systems");
        yield return Values("trust list", "--sort", "description", "recently-created");
        yield return Values("trust list", "--type", "user-auth", "public-ip");
        yield return Values("trust create", "--authority", "portal", "azure", "google", "okta", "jumpcloud", "duo", "oidc");
        yield return Values("partner customer convert", "--billing-months", "1", "12", "24", "36");
        yield return Values("log", "--level", "information", "warning", "error");
    }

    [Test]
    public async Task Commands_describes_every_command_in_the_command_tree_and_no_other()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.Multiple(() =>
        {
            Assert.That(entries.Keys, Is.EquivalentTo(CommandTree.Keys));

            foreach (var (name, entry) in entries)
            {
                Assert.That(entry.GetProperty("description").GetString(), Is.Not.Null.And.Not.Empty, name);
                Assert.That(entry.GetProperty("arguments").ValueKind, Is.EqualTo(JsonValueKind.Array), name);
                Assert.That(entry.GetProperty("options").ValueKind, Is.EqualTo(JsonValueKind.Array), name);
            }
        });
    }

    [Test]
    public async Task Commands_names_the_arguments_of_every_command_in_order_as_the_command_tree_does()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.Multiple(() =>
        {
            // commands takes a command's words, three for a partner customer command ("Details"),
            // which the tree's `[<noun> [<verb>]]` does not name, so its own arguments are left out.
            foreach (var (name, expected) in CommandTree.Where(pair => !string.Equals(pair.Key, "commands", StringComparison.Ordinal)))
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

    [TestCaseSource(nameof(OptionSets))]
    public async Task Commands_lists_the_options_each_command_takes_and_no_others(string command, string[] present, string[] absent)
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
            Assert.That(names.Intersect(absent, StringComparer.Ordinal), Is.Empty);
        });
    }

    // --verbose applies to every command ("Options on every command"). Output is always JSON, no
    // command asks for confirmation, every field has a flag, the token never goes on the command
    // line, and lists read every page, so none of the removed options exists anywhere. log alone
    // keeps --limit ("Command options").
    [Test]
    public async Task Every_command_takes_verbose_and_none_takes_a_removed_option()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        Assert.Multiple(() =>
        {
            foreach (var (name, entry) in CommandEntries(result.StdoutJson))
            {
                var options = OptionNames(entry);
                Assert.That(options, Does.Contain("--verbose"), name);
                Assert.That(options.Intersect(OptionsNoCommandHas, StringComparer.Ordinal), Is.Empty, name);

                if (!string.Equals(name, "log", StringComparison.Ordinal))
                {
                    Assert.That(options, Does.Not.Contain("--limit"), name);
                }
            }
        });
    }

    // --dry-run belongs to the commands that change something through the API ("Options on every
    // command", "Dry run").
    [Test]
    public async Task Commands_that_change_something_through_the_api_take_dry_run_and_no_other_command_does()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.Multiple(() =>
        {
            foreach (var name in ChangeCommands)
            {
                Assert.That(entries, Does.ContainKey(name));
                Assert.That(OptionNamesOf(entries, name), Does.Contain("--dry-run"), name);
            }

            foreach (var name in CommandsWithoutDryRun)
            {
                Assert.That(entries, Does.ContainKey(name));
                Assert.That(OptionNamesOf(entries, name), Does.Not.Contain("--dry-run"), name);
            }
        });
    }

    // The organisation and the partner are chosen separately: commands that act within an
    // organisation take --org and --org-id and no --partner-id, and partner customer commands take
    // --partner-id ("Context").
    [Test]
    public async Task Organisation_commands_take_org_and_org_id_and_partner_customer_commands_take_partner_id()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        var partnerCommands = entries.Keys.Where(name => name.StartsWith("partner customer ", StringComparison.Ordinal)).ToArray();
        Assert.Multiple(() =>
        {
            foreach (var name in OrganisationCommands)
            {
                Assert.That(OptionNamesOf(entries, name), Does.Contain("--org").And.Contain("--org-id"), name);
                Assert.That(OptionNamesOf(entries, name), Does.Not.Contain("--partner-id"), name);
            }

            Assert.That(partnerCommands, Has.Length.EqualTo(13));

            foreach (var name in partnerCommands)
            {
                Assert.That(OptionNamesOf(entries, name), Does.Contain("--partner-id"), name);
            }
        });
    }

    // Tags are given by name only ("Names and IDs"), so no tag command has an --id.
    [Test]
    public async Task Tag_commands_take_no_id_option()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        var tagCommands = entries.Keys.Where(name => name.StartsWith("tag ", StringComparison.Ordinal)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(tagCommands, Has.Length.EqualTo(4));

            foreach (var name in tagCommands)
            {
                Assert.That(OptionNamesOf(entries, name), Does.Not.Contain("--id"), name);
            }
        });
    }

    // An agent reads the allowed values from `commands` before it builds a command ("Options on
    // every command": `commands` lists the allowed values).
    [TestCaseSource(nameof(AllowedValues))]
    public async Task Commands_lists_the_allowed_values_of_an_option_in_lower_case_hyphenated_form(string command, string option, string[] values)
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        Assert.That(OptionValues(CommandEntries(result.StdoutJson), command, option), Is.EquivalentTo(values));
    }

    // The error codes are a fixed set the CLI owns, so an agent can handle each one ("Errors and
    // exit codes").
    [Test]
    public async Task Commands_lists_the_fixed_error_codes_with_their_exit_codes()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        AssertSucceededWithoutRequests(run, result);
        var errors = result.StdoutJson.GetProperty("errors").EnumerateArray()
            .ToDictionary(
                error => error.GetProperty("code").GetString()!,
                error => error.GetProperty("exitCode").GetInt32(),
                StringComparer.Ordinal);
        Assert.That(errors, Is.EquivalentTo(CliAssert.ExitCodes));
    }

    // `commands` takes a command's words ("Details"), and the leading words of several commands
    // describe those commands, which keeps the answer small when an agent needs one noun. log is a
    // noun without verbs, so `commands log` describes the one command, log, and login is not a log
    // command. `partner customer` is the second-level noun, so its words name its commands.
    [TestCase("system")]
    [TestCase("key")]
    [TestCase("dns")]
    [TestCase("org")]
    [TestCase("log")]
    [TestCase("partner")]
    [TestCase("partner customer")]
    public async Task Commands_given_a_nouns_words_describes_only_that_nouns_commands(string words)
    {
        ArgumentNullException.ThrowIfNull(words);
        using var run = CliRun.Start();

        var result = await run.RunAsync(["commands", .. words.Split(' ')]);

        AssertSucceededWithoutRequests(run, result);
        var expected = CommandTree.Keys
            .Where(command => string.Equals(command, words, StringComparison.Ordinal) || command.StartsWith(words + " ", StringComparison.Ordinal))
            .ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(expected, Is.Not.Empty);
            Assert.That(CommandEntries(result.StdoutJson).Keys, Is.EquivalentTo(expected));
        });
    }

    // Every command can be described alone, the three-word partner customer commands included
    // ("Details": `commands partner customer create`). One run serves every command line, since
    // `commands` makes no call and changes nothing.
    [Test]
    public async Task Commands_given_the_words_of_any_command_describes_that_command_alone()
    {
        using var run = CliRun.Start();
        var results = new Dictionary<string, CliResult>(StringComparer.Ordinal);

        foreach (var command in CommandTree.Keys)
        {
            results[command] = await run.RunAsync(["commands", .. command.Split(' ')]);
        }

        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);

            foreach (var (command, result) in results)
            {
                Assert.That(result.ExitCode, Is.Zero, $"commands {command}{Environment.NewLine}{result}");
                Assert.That(result.Stderr, Is.Empty, $"commands {command}");

                if (result.ExitCode == 0)
                {
                    Assert.That(CommandEntries(result.StdoutJson).Keys, Is.EqualTo(new[] { command }), $"commands {command}");
                }
            }
        });
    }

    // Example 17: `commands system list` describes one command, its options and their allowed
    // values ("Output").
    [Test]
    public async Task Commands_system_list_describes_system_list_alone_with_its_options_and_their_values()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands", "system", "list");

        AssertSucceededWithoutRequests(run, result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.That(entries.Keys, Is.EqualTo(SystemListOnly));
        Assert.Multiple(() =>
        {
            Assert.That(OptionNames(entries["system list"]), Is.SupersetOf(SystemListOptions));
            Assert.That(OptionValues(entries, "system list", "--sort"), Is.EquivalentTo(SystemSortValues));
            Assert.That(OptionValues(entries, "system list", "--state"), Is.EquivalentTo(SystemStateValues));
        });
    }

    // A plural noun is a hidden alias wherever a noun is accepted ("Shape and naming"), and the
    // description names each command by its singular noun.
    [TestCase("systems", "system")]
    [TestCase("systems list", "system list")]
    [TestCase("policies", "policy")]
    public async Task Commands_given_a_plural_noun_describes_the_same_commands_as_the_singular_noun(string plural, string singular)
    {
        ArgumentNullException.ThrowIfNull(plural);
        ArgumentNullException.ThrowIfNull(singular);
        using var pluralRun = CliRun.Start();
        using var singularRun = CliRun.Start();

        var pluralResult = await pluralRun.RunAsync(["commands", .. plural.Split(' ')]);
        var singularResult = await singularRun.RunAsync(["commands", .. singular.Split(' ')]);

        AssertSucceededWithoutRequests(singularRun, singularResult);
        AssertSucceededWithoutRequests(pluralRun, pluralResult);
        Assert.That(pluralResult.Stdout, Is.EqualTo(singularResult.Stdout));
    }

    [TestCase("widget")]
    [TestCase("system frobnicate")]
    public async Task Commands_given_a_noun_or_verb_that_does_not_exist_exits_2_with_invalid_argument(string arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using var run = CliRun.Start();

        var result = await run.RunAsync(["commands", .. arguments.Split(' ')]);

        CliAssert.Rejected(run, result);
    }

    // An agent reads `commands` before it has a token or has chosen an organisation, so without
    // --search-keys the command needs neither and calls nothing.
    [TestCase("commands")]
    [TestCase("commands system list")]
    public async Task Commands_needs_no_token_or_organisation_and_makes_no_request(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG_ID");

        var result = await run.RunAsync(command.Split(' '));

        AssertSucceededWithoutRequests(run, result);
        Assert.That(CommandEntries(result.StdoutJson), Does.ContainKey("system list"));
    }

    // --search-keys tells an agent which keys a list's --filter takes, as the API gives them
    // ("Details"). The output is the one command's entry with "searchKeys" holding the API's
    // SearchKey models unchanged, so it is compared whole with the body the fake API served. The
    // fake API serves only the expected route, and WireMock.Net answers any other with 404, so a
    // call to the wrong list fails the command as well as the call check.
    [TestCaseSource(nameof(SearchKeyLists))]
    public async Task Commands_search_keys_adds_the_search_keys_the_api_gives_for_the_list_with_one_call(string command, string list, string route, string body)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(body);
        using var run = CliRun.Start();
        var path = TestData.OrgPath(route);
        run.Stub("GET", path, json: body);

        var result = await run.RunAsync(["commands", .. command.Split(' '), "--search-keys"]);

        CliAssert.Succeeded(result);
        var entries = CommandEntries(result.StdoutJson);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {path}" }));
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(entries.Keys, Is.EqualTo(new[] { list }));
        });

        var keys = entries[list].GetProperty("searchKeys").GetRawText();
        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(keys), JsonNode.Parse(body)), Is.True, keys);
            Assert.That(OptionNames(entries[list]), Does.Contain("--filter"));
        });
    }

    // A plural noun is a hidden alias wherever a noun is accepted ("Shape and naming"), --search-keys
    // included.
    [Test]
    public async Task Commands_search_keys_takes_a_plural_noun()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("policies/meta/search-keys");
        run.Stub("GET", path, json: ApiJson.PolicySearchKeys());

        await CliAssert.AcceptedAsync(run, "GET", path, "commands", "policies", "list", "--search-keys");
    }

    // Checks run in the order arguments, token, organisation, call ("Errors and exit codes"), so
    // with --search-keys and no token the command exits 3 and asks the API nothing.
    [Test]
    public async Task Commands_search_keys_without_a_token_exits_3_with_token_missing_and_makes_no_request()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Stub("GET", TestData.OrgPath("policies/meta/search-keys"), json: ApiJson.PolicySearchKeys());

        var result = await run.RunAsync("commands", "policy", "list", "--search-keys");

        CliAssert.Rejected(run, result, "token_missing");
    }

    // With no organisation chosen the command looks up the token's organisations, and several exit
    // 2 with no_org, as every organisation command does ("Context"). The fake API serves the search
    // keys too, so a CLI that skipped the organisation would show the call.
    [Test]
    public async Task Commands_search_keys_with_no_organisation_chosen_and_several_exits_2_with_no_org()
    {
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_ORG_ID");
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", TestData.OrgPath("tags/meta/search-keys"), json: ApiJson.TagSearchKeys());
        run.Stub("GET", OtherOrgPath("tags/meta/search-keys"), json: ApiJson.TagSearchKeys());

        var result = await run.RunAsync("commands", "tag", "list", "--search-keys");

        CliAssert.Failed(result, "no_org");
        Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
    }

    [TestCaseSource(nameof(SearchKeyOrganisations))]
    public async Task Commands_search_keys_reads_the_search_keys_of_the_organisation_the_options_choose(string option, string value, string[] calls)
    {
        using var run = CliRun.Start();
        run.Stub("GET", "/account/orgs", json: ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        run.Stub("GET", TestData.OrgPath("enrolment-keys/meta/search-keys"), json: ApiJson.KeySearchKeys());
        run.Stub("GET", OtherOrgPath("enrolment-keys/meta/search-keys"), json: ApiJson.KeySearchKeys());

        var result = await run.RunAsync("commands", "key", "list", "--search-keys", option, value);

        CliAssert.Succeeded(result);
        Assert.That(run.Calls(), Is.EqualTo(calls));
    }

    // Argument errors come before the token and the organisation ("Errors and exit codes"), so each
    // wrong command line exits 2 with no token and no organisation, and names the option at fault.
    // An unknown option is a parse error, which also exits 2, so the corrected command line, run in
    // the same sandbox once it has a token and an organisation, shows the options exist and that the
    // rejection withheld the call.
    [TestCaseSource(nameof(SearchKeyRejections))]
    public async Task Commands_search_keys_on_anything_but_one_of_the_four_lists_exits_2_before_the_token_is_read(string rejected, string key, string corrected, string route)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(corrected);
        ArgumentNullException.ThrowIfNull(route);
        using var run = CliRun.Start();
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG_ID");

        var result = await run.RunAsync(["commands", .. rejected.Split(' ')]);

        CliAssert.Rejected(run, result);
        Assert.That(JsonRead.PropertyNames(result.Error.GetProperty("errors")), Is.EqualTo(new[] { key }), result.ToString());

        run.Environment["ENCLAVE_TOKEN"] = TestData.Token;
        run.Environment["ENCLAVE_ORG_ID"] = TestData.OrgId.ToString();
        var path = TestData.OrgPath(route);
        run.Stub("GET", path, json: ApiJson.SystemSearchKeys());

        await CliAssert.AcceptedAsync(run, "GET", path, ["commands", .. corrected.Split(' ')]);
    }

    // stdout holds nothing unless the command succeeds ("Output"), and the API's 403 is forbidden,
    // exit 4 ("Errors and exit codes").
    [Test]
    public async Task Commands_search_keys_refused_by_the_api_exits_4_with_forbidden_and_prints_nothing_on_stdout()
    {
        using var run = CliRun.Start();
        var path = TestData.OrgPath("systems/meta/search-keys");
        run.StubProblem("GET", path, 403, "Forbidden");

        var result = await run.RunAsync("commands", "system", "list", "--search-keys");

        CliAssert.Failed(result, "forbidden");
        Assert.That(run.Calls(), Is.EqualTo(new[] { $"GET {path}" }));
    }

    private static TestCaseData Options(string command, string present, string absent) =>
        new TestCaseData(command, present.Split(' '), absent.Split(' ')).SetArgDisplayNames(command);

    private static TestCaseData Values(string command, string option, params string[] values) =>
        new TestCaseData(command, option, values).SetArgDisplayNames(command, option);

    private static TestCaseData SearchKeys(string command, string list, string route, string body) =>
        new TestCaseData(command, list, route, body).SetArgDisplayNames(command);

    private static TestCaseData Rejection(string rejected, string key, string corrected, string route) =>
        new TestCaseData(rejected, key, corrected, route).SetArgDisplayNames(rejected);

    // Enclave.Sdk.Api writes the organisation ID in a path as 32 hex digits (TestData.OrgPath).
    private static string OtherOrgPath(string suffix) => $"/org/{TestData.OtherOrgId:N}/{suffix}";

    private static Dictionary<string, JsonElement> CommandEntries(JsonElement document) =>
        document.GetProperty("commands").EnumerateArray()
            .ToDictionary(entry => entry.GetProperty("command").GetString()!, entry => entry, StringComparer.Ordinal);

    private static string[] OptionNames(JsonElement entry) =>
        entry.GetProperty("options").EnumerateArray()
            .Select(option => option.GetProperty("name").GetString()!)
            .ToArray();

    // A command missing from the document has no options here; the completeness test reports it.
    private static string[] OptionNamesOf(Dictionary<string, JsonElement> entries, string command) =>
        entries.TryGetValue(command, out var entry) ? OptionNames(entry) : [];

    private static string[] OptionValues(Dictionary<string, JsonElement> entries, string command, string option)
    {
        Assert.That(entries, Does.ContainKey(command));

        var match = entries[command].GetProperty("options").EnumerateArray()
            .Where(candidate => string.Equals(candidate.GetProperty("name").GetString(), option, StringComparison.Ordinal))
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
