using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// An update sends only the fields given, and the rest are left as they are (proposed-cli-surface.md
// "Create and update"). The API changes the fields a patch body holds and leaves the others (portal
// Enclave.Api.Scaffolding/Models/PatchModel.cs, WasSet), so a field sent that the caller did not give
// would overwrite what the item has. A field that needs no read is set with the one PATCH call
// ("Calls per command"). An update with no change flag has nothing to send and exits 2 ("Details").
public class UpdateFieldsTests
{
    // The item can also be read, so a CLI that read it first would succeed and the extra request,
    // and no error, is what fails the test.
    [TestCaseSource(nameof(Updates))]
    public async Task Update_sends_one_patch_holding_only_the_fields_given(string[] command, string path, string item, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        using var run = CliRun.Start();
        run.Stub("GET", path, json: item);
        run.Stub("PATCH", path, json: item);
        var fields = expected.Split('|').Select(pair => pair.Split('=', 2)).ToArray();
        var names = fields.Select(field => field[0]).ToArray();

        var result = await run.RunAsync(command);

        CliAssert.Succeeded(result);
        var request = run.SingleRequest();
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(names).IgnoreCase);

            foreach (var field in fields)
            {
                Assert.That(JsonAssert.Property(body, field[0]).GetString(), Is.EqualTo(field[1]), field[0]);
            }
        });
    }

    // Arguments are checked before any call ("Errors and exit codes"), so an update given by name
    // with no change flag makes no lookup either. The update with a change flag run next in the same
    // sandbox shows the command exists and the rejection comes from the missing flag.
    [TestCase("system update ABCDE", "system update ABCDE --notes Reviewed", "systems/ABCDE")]
    [TestCase("system update XYZ12 --pending", "system update XYZ12 --pending --notes Reviewed", "unapproved-systems/XYZ12")]
    [TestCase("dns update-zone --id 4", "dns update-zone --id 4 --notes Reviewed", "dns/zones/4")]
    [TestCase("dns update-zone internal", "dns update-zone --id 4 --notes Reviewed", "dns/zones/4")]
    [TestCase("dns update-hostname --id 7", "dns update-hostname --id 7 --notes Reviewed", "dns/records/7")]
    [TestCase("org update", "org update --phone 020", null)]
    public async Task Update_with_no_change_flag_exits_2_without_a_request(string rejected, string accepted, string? suffix)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        ArgumentNullException.ThrowIfNull(accepted);
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("systems/ABCDE"), json: ApiJson.System("ABCDE"));
        run.Stub("PATCH", TestData.OrgPath("unapproved-systems/XYZ12"), json: ApiJson.PendingSystem("XYZ12"));
        run.Stub("GET", TestData.OrgPath("dns/zones"), json: ApiJson.Page(ApiJson.Zone(4, "internal")));
        run.Stub("PATCH", TestData.OrgPath("dns/zones/4"), json: ApiJson.Zone(4, "internal"));
        run.Stub("PATCH", TestData.OrgPath("dns/records/7"), json: ApiJson.HostnameRecord(7, "db", 4, "internal"));
        run.Stub("PATCH", TestData.OrgPath(), json: ApiJson.OrgProperties(TestData.OrgName));

        await CliAssert.RejectedThenAcceptedAsync(run, rejected.Split(' '), accepted.Split(' '), "PATCH", TestData.OrgPath(suffix ?? string.Empty));
    }

    // One update with a field that needs no read for each command that is a vehicle for this rule,
    // and one with two fields; expected is the patch body as Field=value pairs joined with "|".
    private static IEnumerable<TestCaseData> Updates()
    {
        yield return Update("system update --notes", ["system", "update", "ABCDE", "--notes", "Reviewed by IT"], "systems/ABCDE", ApiJson.System("ABCDE"), "Notes=Reviewed by IT");
        yield return Update("system update --description --notes", ["system", "update", "ABCDE", "--description", "web-02", "--notes", "Reviewed by IT"], "systems/ABCDE", ApiJson.System("ABCDE"), "Description=web-02|Notes=Reviewed by IT");
        yield return Update("system update --pending --description", ["system", "update", "XYZ12", "--pending", "--description", "kiosk-01"], "unapproved-systems/XYZ12", ApiJson.PendingSystem("XYZ12"), "Description=kiosk-01");
        yield return Update("dns update-zone --notes", ["dns", "update-zone", "--id", "4", "--notes", "Reviewed by IT"], "dns/zones/4", ApiJson.Zone(4, "internal"), "Notes=Reviewed by IT");
        yield return Update("dns update-hostname --notes", ["dns", "update-hostname", "--id", "7", "--notes", "Reviewed by IT"], "dns/records/7", ApiJson.HostnameRecord(7, "db", 4, "internal"), "Notes=Reviewed by IT");
        yield return Update("org update --phone", ["org", "update", "--phone", "+44 20 7946 0000"], string.Empty, ApiJson.OrgProperties(TestData.OrgName), "Phone=+44 20 7946 0000");
    }

    private static TestCaseData Update(string name, string[] command, string suffix, string item, string expected) =>
        new TestCaseData(command, TestData.OrgPath(suffix), item, expected).SetArgDisplayNames(name);
}
