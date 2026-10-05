using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// On update, --set-tags and --set-systems replace the whole list, and --set-tags "" clears it
// (proposal, "Create and update"). Tags decide policy membership, so the flag's name says it
// replaces: an update has no --tags, which a caller could read as "add these tags". On create
// there is no list to replace, and the options are --tags and --systems (proposal, "Command
// options").
public class ReplaceListTests
{
    [TestCaseSource(nameof(SetTagsCases))]
    public async Task Set_tags_sends_the_given_tags_as_the_whole_tag_list(string[] command, string path, string response)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", path, json: response);

        var result = await run.RunAsync([.. command, "--set-tags", "web-servers,production"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            AssertPatchSetsOnly(request.BodyJson, "Tags", ["web-servers", "production"]);
        });
    }

    // --set-tags "" clears the list (proposal, "Create and update"), so it sends an empty list.
    [TestCaseSource(nameof(SetTagsCases))]
    public async Task Set_tags_with_an_empty_value_sends_an_empty_tag_list(string[] command, string path, string response)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", path, json: response);

        var result = await run.RunAsync([.. command, "--set-tags", string.Empty]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            AssertPatchSetsOnly(request.BodyJson, "Tags", []);
        });
    }

    [Test]
    public async Task Dns_record_update_set_systems_sends_the_given_systems_as_the_whole_system_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "intranet"));

        var result = await run.RunAsync("dns", "record", "update", "9", "--set-systems", "AB12C,DE34F");

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records/9")));
            AssertPatchSetsOnly(request.BodyJson, "Systems", ["AB12C", "DE34F"]);
        });
    }

    [Test]
    public async Task Dns_record_update_set_systems_with_an_empty_value_sends_an_empty_system_list()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TestData.OrgPath("dns/records/9"), json: ApiJson.Record(9, "intranet"));

        var result = await run.RunAsync("dns", "record", "update", "9", "--set-systems", string.Empty);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records/9")));
            AssertPatchSetsOnly(request.BodyJson, "Systems", []);
        });
    }

    // The option that would add to a list does not exist on update, so a caller who means to add
    // gets an error and no change. The second run, with the replacing option, shows the command
    // exists and the rejection comes from the option's name.
    [TestCaseSource(nameof(UpdateOptionNameCases))]
    public async Task Update_takes_the_replacing_option_and_has_no_option_without_set(string[] command, string rejected, string accepted, string value, string path, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub("PATCH", path, json: response);

            var result = await run.RunAsync([.. command, rejected, value]);

            AssertRejected(result, run);
        }

        using (var run = CliRun.Start())
        {
            run.Stub("PATCH", path, json: response);

            var result = await run.RunAsync([.. command, accepted, value]);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, $"{result}");
                Assert.That(run.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[] { $"PATCH {path}" }));
            });
        }
    }

    [Test]
    public async Task Key_create_sends_the_tags_option_as_the_tag_list_of_the_new_key()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("enrolment-keys"), json: ApiJson.Key(12, "Build agents"));

        var result = await run.RunAsync("key", "create", "--description", "Build agents", "--tags", "build,ci");

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        var body = request.BodyJson;
        string[] tags = ["build", "ci"];
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("enrolment-keys")));
            Assert.That(JsonAssert.Property(body, "description").GetString(), Is.EqualTo("Build agents"));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "tags")), Is.EqualTo(tags));
        });
    }

    [Test]
    public async Task Dns_record_create_sends_the_tags_and_systems_options_as_the_lists_of_the_new_record()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("dns/records"), json: ApiJson.Record(9, "intranet"));

        var result = await run.RunAsync("dns", "record", "create", "intranet", "--zone", "4", "--tags", "web-servers,databases", "--systems", "AB12C,DE34F");

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        var body = request.BodyJson;
        string[] tags = ["web-servers", "databases"];
        string[] systems = ["AB12C", "DE34F"];
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(TestData.OrgPath("dns/records")));
            Assert.That(JsonAssert.Property(body, "name").GetString(), Is.EqualTo("intranet"));
            Assert.That(JsonAssert.Property(body, "zoneId").GetInt32(), Is.EqualTo(4));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "tags")), Is.EqualTo(tags));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "systems")), Is.EqualTo(systems));
        });
    }

    // A new resource has no list to replace, so create names its options --tags and --systems and
    // has no --set- form. The second run, with the create option, shows the command exists and
    // the rejection comes from the option's name.
    [TestCaseSource(nameof(CreateOptionNameCases))]
    public async Task Create_takes_the_option_without_set_and_has_no_replacing_option(string[] command, string rejected, string accepted, string value, string path, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub("POST", path, json: response);

            var result = await run.RunAsync([.. command, rejected, value]);

            AssertRejected(result, run);
        }

        using (var run = CliRun.Start())
        {
            run.Stub("POST", path, json: response);

            var result = await run.RunAsync([.. command, accepted, value]);

            Assert.Multiple(() =>
            {
                Assert.That(result.ExitCode, Is.Zero, $"{result}");
                Assert.That(run.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[] { $"POST {path}" }));
            });
        }
    }

    private static IEnumerable<TestCaseData> SetTagsCases() =>
        SetTagsCommands().Select(update => new TestCaseData(update.Command, update.Path, update.Response).SetArgDisplayNames(update.Name));

    // Each update command with a list option: the name without "set-" is rejected and the
    // replacing name accepted, with a value that follows the tag rule (portal
    // TagValidationExtensions.cs:13) or the system ID format.
    private static IEnumerable<TestCaseData> UpdateOptionNameCases()
    {
        foreach (var update in SetTagsCommands())
        {
            yield return new TestCaseData(update.Command, "--tags", "--set-tags", "web-servers", update.Path, update.Response)
                .SetArgDisplayNames($"{update.Name} --tags");
        }

        string[] recordUpdate = ["dns", "record", "update", "9"];
        yield return new TestCaseData(recordUpdate, "--systems", "--set-systems", "AB12C", TestData.OrgPath("dns/records/9"), ApiJson.Record(9, "intranet"))
            .SetArgDisplayNames("dns record update --systems");
    }

    // The update commands with --set-tags (proposal, "Command options").
    private static IEnumerable<ListUpdate> SetTagsCommands()
    {
        yield return new("system update", ["system", "update", "AB12C"], TestData.OrgPath("systems/AB12C"), ApiJson.System("AB12C"));
        yield return new("pending update", ["pending", "update", "CD34E"], TestData.OrgPath("unapproved-systems/CD34E"), ApiJson.PendingSystem("CD34E"));
        yield return new("key update", ["key", "update", "12"], TestData.OrgPath("enrolment-keys/12"), ApiJson.Key(12));
        yield return new("dns record update", ["dns", "record", "update", "9"], TestData.OrgPath("dns/records/9"), ApiJson.Record(9, "intranet"));
    }

    // Each create command with a list option: the "set-" name is rejected and the plain name
    // accepted, with a value that follows the tag rule (portal TagValidationExtensions.cs:13) or
    // the system ID format.
    private static IEnumerable<TestCaseData> CreateOptionNameCases()
    {
        string[] keyCreate = ["key", "create", "--description", "Build agents"];
        string[] recordCreate = ["dns", "record", "create", "intranet", "--zone", "4"];

        yield return new TestCaseData(keyCreate, "--set-tags", "--tags", "build", TestData.OrgPath("enrolment-keys"), ApiJson.Key(12, "Build agents"))
            .SetArgDisplayNames("key create --set-tags");
        yield return new TestCaseData(recordCreate, "--set-tags", "--tags", "web-servers", TestData.OrgPath("dns/records"), ApiJson.Record(9, "intranet"))
            .SetArgDisplayNames("dns record create --set-tags");
        yield return new TestCaseData(recordCreate, "--set-systems", "--systems", "AB12C", TestData.OrgPath("dns/records"), ApiJson.Record(9, "intranet"))
            .SetArgDisplayNames("dns record create --set-systems");
    }

    // The body sets the one list and nothing else: a replacing flag leaves every other field as
    // it is. Field names are compared ignoring case because the SDK keys a PATCH body by C#
    // property name (Enclave.Sdk.Api 1.0.4, Data/PatchClient.cs, Set).
    private static void AssertPatchSetsOnly(JsonElement body, string field, string[] expected)
    {
        Assert.That(body.EnumerateObject().Select(property => property.Name.ToUpperInvariant()), Is.EqualTo(new[] { field.ToUpperInvariant() }), $"The PATCH body holds {body}");
        Assert.That(JsonAssert.Strings(JsonAssert.Property(body, field)), Is.EqualTo(expected));
    }

    private static void AssertRejected(CliResult result, CliRun run)
    {
        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(2), $"{result}");
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
        Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("invalid_argument"));
    }

    // One update command that takes a list option: the path its PATCH uses and the model the fake
    // API answers with.
    private sealed record ListUpdate(string Name, string[] Command, string Path, string Response);
}
