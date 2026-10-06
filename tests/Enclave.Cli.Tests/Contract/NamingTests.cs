using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// Nouns are singular, and a plural name is accepted as a hidden alias so a guess works; help and
// `commands` show the singular name only (proposal "Shape and naming").
[Category(TestCategory.Pending)]
public class NamingTests
{
    private static readonly string[] PluralNouns =
    [
        "orgs", "partners", "users", "invites", "customers", "admins", "systems", "pendings", "keys",
        "policies", "tags", "zones", "records", "trusts", "logs",
    ];

    public static IEnumerable<TestCaseData> PluralAliases()
    {
        yield return Alias("system list", "systems list", TestData.OrgPath("systems"), ApiJson.Page(ApiJson.System("SYS7001")));
        yield return Alias("system show SYS7001", "systems show SYS7001", TestData.OrgPath("systems/SYS7001"), ApiJson.System("SYS7001"));
        yield return Alias("key list", "keys list", TestData.OrgPath("enrolment-keys"), ApiJson.Page(ApiJson.Key(7001)));
        yield return Alias("policy list", "policies list", TestData.OrgPath("policies"), ApiJson.Page(ApiJson.Policy(7001)));
        yield return Alias("tag list", "tags list", TestData.OrgPath("tags"), ApiJson.Page(ApiJson.Tag("web")));
        yield return Alias("log list", "logs list", TestData.OrgPath("logs"), ApiJson.Page(ApiJson.Log("event-0")));
        yield return Alias("org list", "orgs list", "/account/orgs", ApiJson.Orgs((TestData.OrgId, TestData.OrgName)));
        yield return Alias("org user list", "org users list", TestData.OrgPath("users"), ApiJson.Users((TestData.OtherOrgId, "admin@acme.example")));
        yield return Alias("org invite list", "org invites list", TestData.OrgPath("invites"), ApiJson.Invites("new@acme.example"));
        yield return Alias("dns zone list", "dns zones list", TestData.OrgPath("dns/zones"), ApiJson.Page(ApiJson.Zone(7001, "internal")));
        yield return Alias("dns zone show 7001", "dns zones show 7001", TestData.OrgPath("dns/zones/7001"), ApiJson.Zone(7001, "internal"));
        yield return Alias("dns record list", "dns records list", TestData.OrgPath("dns/records"), ApiJson.Page(ApiJson.Record(7001, "host")));
    }

    // Partner commands report not_implemented (Enclave.Sdk.Api 1.0.4 has no partner clients), so
    // the alias is shown to run the same command by giving the same result.
    public static IEnumerable<TestCaseData> PartnerPluralAliases()
    {
        var customerId = TestData.OtherOrgId.ToString();
        yield return PartnerAlias("partner list", "partners list");
        yield return PartnerAlias("partner user list", "partner users list");
        yield return PartnerAlias("partner invite list", "partner invites list");
        yield return PartnerAlias("partner customer list", "partner customers list");
        yield return PartnerAlias($"partner customer admin list {customerId}", $"partner customer admins list {customerId}");
        yield return PartnerAlias($"partner customer invite list {customerId}", $"partner customer invites list {customerId}");
    }

    [TestCaseSource(nameof(PluralAliases))]
    public async Task A_plural_noun_runs_the_same_command_as_the_singular_noun(string singular, string plural, string path, string json)
    {
        ArgumentNullException.ThrowIfNull(singular);
        ArgumentNullException.ThrowIfNull(plural);
        using var singularRun = CliRun.Start();
        using var pluralRun = CliRun.Start();
        singularRun.Stub("GET", path, json: json);
        pluralRun.Stub("GET", path, json: json);

        var singularResult = await singularRun.RunAsync(singular.Split(' '));
        var pluralResult = await pluralRun.RunAsync(plural.Split(' '));

        var singularRequest = singularRun.SingleRequest();
        var pluralRequest = pluralRun.SingleRequest();
        Assert.Multiple(() =>
        {
            CliAssert.Succeeded(singularResult);
            CliAssert.Succeeded(pluralResult);
            Assert.That(singularRequest.Method, Is.EqualTo("GET"));
            Assert.That(singularRequest.Path, Is.EqualTo(path));
            Assert.That(pluralRequest.Method, Is.EqualTo(singularRequest.Method));
            Assert.That(pluralRequest.Path, Is.EqualTo(singularRequest.Path));
            Assert.That(pluralRequest.Query, Is.EquivalentTo(singularRequest.Query));
            Assert.That(pluralResult.Stdout, Is.EqualTo(singularResult.Stdout));
            Assert.That(pluralResult.Stderr, Is.EqualTo(singularResult.Stderr));
        });
    }

    [TestCaseSource(nameof(PartnerPluralAliases))]
    public async Task A_plural_partner_noun_runs_the_same_command_as_the_singular_noun(string singular, string plural)
    {
        ArgumentNullException.ThrowIfNull(singular);
        ArgumentNullException.ThrowIfNull(plural);
        await AssertNotImplementedAsync(singular);
        await AssertNotImplementedAsync(plural);
    }

    // `commands` describes the CLI for agents, so it names each command once, by its singular nouns.
    [Test]
    public async Task Commands_output_names_every_command_by_singular_nouns_only()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("commands");

        CliAssert.Succeeded(result);
        var names = result.StdoutJson.GetProperty("commands").EnumerateArray()
            .Select(entry => entry.GetProperty("command").GetString()!)
            .ToArray();
        var words = names.SelectMany(name => name.Split(' ')).Distinct(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("system list").And.Contain("dns record delete").And.Contain("partner customer admin add"));
            Assert.That(words.Intersect(PluralNouns), Is.Empty);
        });
    }

    private static async Task AssertNotImplementedAsync(string command)
    {
        using var run = CliRun.Start();
        run.Environment["ENCLAVE_PARTNER"] = TestData.PartnerId.ToString();

        var result = await run.RunAsync(command.Split(' '));

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(1), $"{command}{Environment.NewLine}{result}");
            Assert.That(result.Stdout, Is.Empty, command);
            Assert.That(run.Requests, Is.Empty, command);
        });

        Assert.That(result.Error.GetProperty("code").GetString(), Is.EqualTo("not_implemented"), command);
    }

    private static TestCaseData Alias(string singular, string plural, string path, string json) =>
        new TestCaseData(singular, plural, path, json).SetArgDisplayNames(singular, plural);

    private static TestCaseData PartnerAlias(string singular, string plural) =>
        new TestCaseData(singular, plural).SetArgDisplayNames(singular, plural);
}
