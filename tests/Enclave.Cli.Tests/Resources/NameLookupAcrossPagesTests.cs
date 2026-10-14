using System.Globalization;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// A name is looked up in the whole list, read page by page until the API reports no next page
// (proposed-cli-surface.md "Names and IDs", "Calls per command"), so a match on a later page is
// found, and matches on two pages are two candidates. Each case is one of the five name lookups: a
// key, policy or trust requirement by its description, a zone by its name, and a hostname by its
// full name, each through its show command, which reads the item found by its ID. The fake API
// serves the list two items to a page, so the third item is alone on page 1.
public class NameLookupAcrossPagesTests
{
    // The match is the third item, so a lookup that reads page 0 alone finds no match and exits 2.
    [TestCase("key show", "enrolment-keys", "build agents")]
    [TestCase("policy show", "policies", "web to db")]
    [TestCase("trust show", "trust-requirements", "uk only")]
    [TestCase("dns show-zone", "dns/zones", "internal")]
    [TestCase("dns show-hostname", "dns/records", "db.internal")]
    public async Task A_name_that_matches_an_item_on_the_second_page_finds_that_item(string command, string listSuffix, string name)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(name);
        using var run = CliRun.Start();
        var listPath = TestData.OrgPath(listSuffix);
        run.StubPages(listPath, 2, Item(listSuffix, 41, Other(listSuffix, 1)), Item(listSuffix, 42, Other(listSuffix, 2)), Item(listSuffix, 43, name));
        run.Stub("GET", ItemPath(listSuffix, 43), json: Item(listSuffix, 43, name));

        var result = await run.RunAsync([.. command.Split(' '), name]);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.PagesRequested(listPath), Is.EqualTo("0,1"));
            Assert.That(run.RequestsTo("GET", ItemPath(listSuffix, 43)), Has.Count.EqualTo(1));
            Assert.That(result.StdoutJson.GetProperty("id").GetInt32(), Is.EqualTo(43));
        });
    }

    // One item on page 0 and one on page 1 have the name, in different cases, and names match
    // ignoring case ("Names and IDs"), so the name names two items: exit 2 with both as candidates
    // ("Errors and exit codes"), and no item is read. A lookup that reads page 0 alone finds one
    // match and prints it.
    [TestCase("key show", "enrolment-keys", "build agents")]
    [TestCase("policy show", "policies", "web to db")]
    [TestCase("trust show", "trust-requirements", "uk only")]
    [TestCase("dns show-zone", "dns/zones", "internal")]
    [TestCase("dns show-hostname", "dns/records", "db.internal")]
    public async Task A_name_that_matches_an_item_on_each_of_two_pages_exits_2_with_both_as_candidates(string command, string listSuffix, string name)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(name);
        using var run = CliRun.Start();
        var listPath = TestData.OrgPath(listSuffix);
        var sameName = name.ToUpperInvariant();
        run.StubPages(listPath, 2, Item(listSuffix, 41, Other(listSuffix, 1)), Item(listSuffix, 42, name), Item(listSuffix, 43, sameName));
        run.Stub("GET", ItemPath(listSuffix, 42), json: Item(listSuffix, 42, name));
        run.Stub("GET", ItemPath(listSuffix, 43), json: Item(listSuffix, 43, sameName));

        var result = await run.RunAsync([.. command.Split(' '), name]);

        var error = CliAssert.Failed(result, "invalid_argument");
        int[] candidates = [42, 43];
        Assert.Multiple(() =>
        {
            Assert.That(error.GetProperty("candidates").EnumerateArray().Select(candidate => candidate.GetProperty("id").GetInt32()), Is.EquivalentTo(candidates));
            Assert.That(run.PagesRequested(listPath), Is.EqualTo("0,1"));
            Assert.That(run.Requests.Select(request => request.Path), Is.Not.Empty.And.All.EqualTo(listPath));
        });
    }

    private static string ItemPath(string listSuffix, int id) =>
        TestData.OrgPath(listSuffix + "/" + id.ToString(CultureInfo.InvariantCulture));

    // A name no lookup here asks for; a hostname's is a full name in the zone internal.
    private static string Other(string listSuffix, int number) =>
        listSuffix == "dns/records"
            ? string.Create(CultureInfo.InvariantCulture, $"other{number}.internal")
            : string.Create(CultureInfo.InvariantCulture, $"other {number}");

    // The list item, which is also the model the show command reads by ID, of each kind with the
    // given name. A hostname is the record named by the part before its first dot, in the zone named
    // by the rest.
    private static string Item(string listSuffix, int id, string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);

        return listSuffix switch
        {
            "enrolment-keys" => ApiJson.Key(id, name),
            "policies" => ApiJson.Policy(id, name),
            "trust-requirements" => ApiJson.Trust(id, name),
            "dns/zones" => ApiJson.Zone(id, name),
            "dns/records" => ApiJson.HostnameRecord(id, name[..dot], 4, name[(dot + 1)..]),
            _ => throw new ArgumentException($"No item for the list {listSuffix}.", nameof(listSuffix)),
        };
    }
}
