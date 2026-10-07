using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// Nouns are singular, and a plural name is accepted as a hidden alias so a guess works; help and
// `commands` show the singular name only. A noun's own parts take hyphenated verbs, and `partner
// customer` is the one second-level noun (proposed-cli-surface.md "Shape and naming"). Commands
// the old shape had, such as `dns zone list`, are parse errors, in ParseErrorTests.
[Category(TestCategory.Pending)]
public class NamingTests
{
    // Every noun with a plural (AGENTS.md "Architecture" lists the nouns; dns has no plural).
    private static readonly (string Singular, string Plural)[] PluralAliases =
    [
        ("org", "orgs"),
        ("partner", "partners"),
        ("customer", "customers"),
        ("system", "systems"),
        ("key", "keys"),
        ("policy", "policies"),
        ("tag", "tags"),
        ("trust", "trusts"),
        ("log", "logs"),
    ];

    // The top-level commands of "Commands".
    private static readonly string[] TopLevelNames =
    [
        "login", "logout", "status", "org", "partner", "system", "key", "policy", "tag", "dns", "trust", "log", "commands",
    ];

    private static readonly string[] Plurals = PluralAliases.Select(alias => alias.Plural).ToArray();

    // Each case gives the fake API the same answer for both runs, so any difference in the requests
    // or the output comes from the CLI.
    public static IEnumerable<TestCaseData> PluralCommands()
    {
        yield return Alias("system list", "systems list", "GET", TestData.OrgPath("systems"), ApiJson.Page(ApiJson.System("ABCDE"), ApiJson.System("FGHIJ")));
        yield return Alias("system list --pending", "systems list --pending", "GET", TestData.OrgPath("unapproved-systems"), ApiJson.Page(ApiJson.PendingSystem("ABCDE")));
        yield return Alias("system show ABCDE", "systems show ABCDE", "GET", TestData.OrgPath("systems/ABCDE"), ApiJson.System("ABCDE"));
        yield return Alias("key list", "keys list", "GET", TestData.OrgPath("enrolment-keys"), ApiJson.Page(ApiJson.Key(12)));
        yield return Alias("policy list", "policies list", "GET", TestData.OrgPath("policies"), ApiJson.Page(ApiJson.Policy(42)));
        yield return Alias("policy disable --id 42", "policies disable --id 42", "PUT", TestData.OrgPath("policies/disable"), ApiJson.Bulk("policiesUpdated", 1));
        yield return Alias("tag list", "tags list", "GET", TestData.OrgPath("tags"), ApiJson.Page(ApiJson.Tag("web")));
        yield return Alias("trust list", "trusts list", "GET", TestData.OrgPath("trust-requirements"), ApiJson.Page(ApiJson.Trust(5)));
        yield return Alias("log", "logs", "GET", TestData.OrgPath("logs"), ApiJson.Page(ApiJson.Log("System ABCDE enrolled")));
        yield return Alias("org list", "orgs list", "GET", "/account/orgs", ApiJson.Orgs((TestData.OrgId, TestData.OrgName), (TestData.OtherOrgId, TestData.OtherOrgName)));
        yield return Alias("org list-users", "orgs list-users", "GET", TestData.OrgPath("users"), ApiJson.Users((TestData.OtherOrgId, "sam@acme.example")));
    }

    // A noun's own parts take a hyphenated verb in place of another level ("Shape and naming").
    public static IEnumerable<TestCaseData> HyphenatedVerbs()
    {
        yield return Hyphenated("dns list-zones", "dns/zones", ApiJson.Page(ApiJson.Zone(4, "internal")));
        yield return Hyphenated("dns list-hostnames", "dns/records", ApiJson.Page(ApiJson.Record(7, "db")));
        yield return Hyphenated("org list-users", "users", ApiJson.Users((TestData.OtherOrgId, "sam@acme.example")));
        yield return Hyphenated("org list-invites", "invites", ApiJson.Invites("sam@acme.example"));
    }

    [TestCaseSource(nameof(PluralCommands))]
    public async Task A_plural_noun_runs_the_same_command_as_the_singular_noun(string singular, string plural, string method, string path, string json)
    {
        ArgumentNullException.ThrowIfNull(singular);
        ArgumentNullException.ThrowIfNull(plural);
        using var singularRun = CliRun.Start();
        using var pluralRun = CliRun.Start();
        singularRun.Stub(method, path, json: json);
        pluralRun.Stub(method, path, json: json);

        var singularResult = await singularRun.RunAsync(singular.Split(' '));
        var pluralResult = await pluralRun.RunAsync(plural.Split(' '));

        CliAssert.Succeeded(singularResult);
        var singularRequests = singularRun.Requests;
        var pluralRequests = pluralRun.Requests;
        Assert.Multiple(() =>
        {
            Assert.That(singularRun.Calls(), Is.Not.Empty.And.All.EqualTo($"{method} {path}"));
            Assert.That(pluralRun.Calls(), Is.EqualTo(singularRun.Calls()));
            Assert.That(pluralRequests.Select(request => request.Query), Is.EqualTo(singularRequests.Select(request => request.Query)));
            Assert.That(pluralRequests.Select(request => request.Body), Is.EqualTo(singularRequests.Select(request => request.Body)));
            Assert.That(pluralResult.ExitCode, Is.EqualTo(singularResult.ExitCode));
            Assert.That(pluralResult.Stdout, Is.EqualTo(singularResult.Stdout));
            Assert.That(pluralResult.Stderr, Is.EqualTo(singularResult.Stderr));
        });
    }

    // Partner customer commands report not_implemented and make no call while Enclave.Sdk.Api has no
    // partner clients ("Errors and exit codes"), so the alias is shown to reach the same command by
    // the same result. An unknown noun would exit 2 with invalid_argument.
    [TestCase("partner customers list")]
    [TestCase("partners customer list")]
    [TestCase("partners customers list")]
    public async Task A_plural_partner_customer_noun_runs_the_same_command_as_the_singular_noun(string plural)
    {
        ArgumentNullException.ThrowIfNull(plural);
        using var singularRun = CliRun.Start();
        using var pluralRun = CliRun.Start();
        singularRun.Environment["ENCLAVE_PARTNER_ID"] = TestData.PartnerId.ToString();
        pluralRun.Environment["ENCLAVE_PARTNER_ID"] = TestData.PartnerId.ToString();

        var singularResult = await singularRun.RunAsync("partner", "customer", "list");
        var pluralResult = await pluralRun.RunAsync(plural.Split(' '));

        CliAssert.NotImplemented(singularRun, singularResult);
        CliAssert.NotImplemented(pluralRun, pluralResult);
        Assert.That(pluralResult.Stderr, Is.EqualTo(singularResult.Stderr));
    }

    // `partner use --id` saves the partner in ~/.enclave/cli.json, makes no call and prints
    // { "id" } ("Login, logout and status"), so the alias shows in the output and the file it
    // writes.
    [Test]
    public async Task Partners_use_runs_the_same_command_as_partner_use()
    {
        using var singularRun = CliRun.Start();
        using var pluralRun = CliRun.Start();
        var partnerId = TestData.PartnerId.ToString();

        var singularResult = await singularRun.RunAsync("partner", "use", "--id", partnerId);
        var pluralResult = await pluralRun.RunAsync("partners", "use", "--id", partnerId);

        CliAssert.Succeeded(singularResult);
        Assert.Multiple(() =>
        {
            Assert.That(singularRun.Requests, Is.Empty);
            Assert.That(pluralRun.Requests, Is.Empty);
            Assert.That(singularRun.Files.ReadText(singularRun.CliConfigPath), Is.Not.Null);
            Assert.That(pluralResult.ExitCode, Is.EqualTo(singularResult.ExitCode));
            Assert.That(pluralResult.Stdout, Is.EqualTo(singularResult.Stdout));
            Assert.That(pluralRun.Files.ReadText(pluralRun.CliConfigPath), Is.EqualTo(singularRun.Files.ReadText(singularRun.CliConfigPath)));
        });
    }

    // System.CommandLine's help lists a command's name and aliases together in its first column,
    // joined with ", " (HelpBuilder.Default.cs, GetIdentifierSymbolUsageLabel, System.CommandLine
    // 2.0.12), so a plural registered as an ordinary alias shows beside the singular name.
    [Test]
    public async Task Help_names_the_commands_by_their_singular_nouns_only()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("--help");

        CliAssert.Succeeded(result);
        var words = Words(result.Stdout);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(words, Is.SupersetOf(TopLevelNames));

            foreach (var (singular, plural) in PluralAliases)
            {
                Assert.That(result.Stdout, Does.Not.Contain($"{singular}, {plural}").And.Not.Contain($"{plural}, {singular}"));
            }
        });
    }

    [Test]
    public async Task Partner_help_names_customer_by_its_singular_noun_only()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("partner", "--help");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(Words(result.Stdout), Does.Contain("customer"));
            Assert.That(result.Stdout, Does.Not.Contain("customer, customers").And.Not.Contain("customers, customer"));
        });
    }

    // `commands` describes the CLI for agents, so it names each command once, by its singular
    // nouns.
    [Test]
    public async Task Commands_names_every_command_by_its_singular_nouns_only()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        CliAssert.Succeeded(result);
        var words = CommandNames(result).SelectMany(name => name.Split(' ')).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(words, Is.SupersetOf(TopLevelNames));
            Assert.That(words.Intersect(Plurals, StringComparer.Ordinal), Is.Empty);
        });
    }

    // A command is a noun and a verb, `partner customer` being the one second-level noun: so every
    // name is one word (login, logout, status, log, commands) or two, and only partner customer
    // commands have three.
    [Test]
    public async Task Commands_has_partner_customer_as_the_only_second_level_noun()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        CliAssert.Succeeded(result);
        var names = CommandNames(result);
        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("dns list-zones").And.Contain("org remove-user").And.Contain("partner customer add-admin"));

            foreach (var name in names)
            {
                var words = name.Split(' ').Length;

                if (name.StartsWith("partner customer ", StringComparison.Ordinal))
                {
                    Assert.That(words, Is.EqualTo(3), name);
                }
                else
                {
                    Assert.That(words, Is.InRange(1, 2), name);
                }
            }
        });
    }

    [TestCaseSource(nameof(HyphenatedVerbs))]
    public async Task A_nouns_own_parts_take_a_hyphenated_verb(string command, string path, string json)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var run = CliRun.Start();
        run.Stub("GET", path, json: json);

        await CliAssert.AcceptedAsync(run, "GET", path, command.Split(' '));
    }

    // partner customer takes its own hyphenated verbs, and its commands report not_implemented
    // where an unknown command would exit 2 with invalid_argument.
    [Test]
    public async Task Partner_customer_takes_hyphenated_verbs()
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_PARTNER_ID"] = TestData.PartnerId.ToString();

        var result = await run.RunAsync("partner", "customer", "list-admins", "--org-id", TestData.OtherOrgId.ToString());

        CliAssert.NotImplemented(run, result);
    }

    private static TestCaseData Alias(string singular, string plural, string method, string path, string json) =>
        new TestCaseData(singular, plural, method, path, json).SetArgDisplayNames(plural);

    private static TestCaseData Hyphenated(string command, string pathSuffix, string json) =>
        new TestCaseData(command, TestData.OrgPath(pathSuffix), json).SetArgDisplayNames(command);

    private static string[] CommandNames(CliResult result) =>
        result.StdoutJson.GetProperty("commands").EnumerateArray()
            .Select(entry => entry.GetProperty("command").GetString()!)
            .ToArray();

    // Hyphens stay inside a word, so "list-zones" is one word, and punctuation around a name, such
    // as the comma between a name and an alias, is dropped.
    private static string[] Words(string text)
    {
        var separators = text.Where(c => !char.IsLetterOrDigit(c) && c != '-').Distinct().ToArray();

        return text.Split(separators, StringSplitOptions.RemoveEmptyEntries);
    }
}
