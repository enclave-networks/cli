using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// create and update accept --from-file <path|-> holding the Enclave.Sdk.Api create or patch model
// as JSON (AGENTS.md, "CLI contract"; proposal, "Create and update"). The files below use the
// camelCase property names and enum member names of the Enclave.Sdk.Api.Data 304.48.0 models,
// which is how Enclave.Sdk.Api 1.0.4 reads and writes them (Constants.JsonSerializerOptions), so a
// file read into the model and written back by the SDK gives the same JSON for every field.
[Category(TestCategory.Pending)]
public class FromFileTests
{
    // PolicyCreateModel for a general policy. Every nested object carries all of its properties, so
    // the SDK writing the model back cannot add a property the file lacks.
    private const string GeneralPolicyCreateFile = """
        {
          "type": "General",
          "description": "Developers to web servers",
          "isEnabled": true,
          "senderTags": ["developers"],
          "receiverTags": ["web-servers", "databases"],
          "acls": [
            { "protocol": "Tcp", "ports": "443", "description": "HTTPS" },
            { "protocol": "Udp", "ports": "53", "description": "DNS" }
          ],
          "senderTrustRequirements": [3],
          "notes": "Created from a file",
          "activeHours": {
            "timeZoneId": "Europe/London",
            "daysOfWeek": ["Monday", "Friday"],
            "startTime": { "hours": 8, "minutes": 0 },
            "endTime": { "hours": 18, "minutes": 30 }
          },
          "autoExpire": { "timeZoneId": "Europe/London", "expiryDateTime": "2026-12-31T17:00:00", "expiryAction": "Disable" }
        }
        """;

    // PolicyCreateModel for a gateway policy, which carries the gateway fields a general policy
    // leaves empty.
    private const string GatewayPolicyCreateFile = """
        {
          "type": "Gateway",
          "description": "Office LAN through the gateway",
          "isEnabled": false,
          "senderTags": ["staff"],
          "receiverTags": [],
          "acls": [{ "protocol": "Any", "ports": "", "description": "All traffic" }],
          "gateways": [{ "systemId": "GW4X7", "routes": ["192.168.1.0/24", "10.10.0.0/16"] }],
          "gatewayAllowedIpRanges": [{ "ipRange": "192.168.1.0/24", "description": "Office LAN" }],
          "gatewayTrafficDirection": "Exit",
          "gatewayPriority": "Prioritised",
          "notes": "Gateway policy from a file"
        }
        """;

    // TrustRequirementCreateModel for a user authentication requirement. The configuration and
    // condition keys are the ones the API's validator accepts for the azure authority (portal
    // TrustRequirementSettingsUserAuthValidator.cs).
    private const string UserTrustCreateFile = """
        {
          "description": "Signed in to Microsoft",
          "type": "UserAuthentication",
          "notes": "Created from a file",
          "settings": {
            "configuration": { "authority": "azure", "tenantId": "6d5f1c2a-0b7e-4f39-9d1a-2c3b4e5f6a7b" },
            "conditions": [{ "claim": "groups", "value": "engineering" }]
          }
        }
        """;

    // TrustRequirementCreateModel for a public IP requirement, whose conditions use the keys of
    // portal TrustRequirementSettingsPublicIpValidator.cs.
    private const string PublicIpTrustCreateFile = """
        {
          "description": "Office public IP",
          "type": "PublicIp",
          "notes": "Created from a file",
          "settings": {
            "configuration": {},
            "conditions": [
              { "type": "ip", "value": "203.0.113.0/24", "description": "Head office" },
              { "type": "country", "value": "GB" }
            ]
          }
        }
        """;

    private const string KeyCreateFile = """
        {
          "type": "Ephemeral",
          "approvalMode": "Manual",
          "description": "Build agents",
          "usesRemaining": 25,
          "ipConstraints": [{ "range": "198.51.100.0/24", "description": "CI subnet" }],
          "tags": ["build", "ci"],
          "disconnectedRetentionMinutes": 60,
          "notes": "Created from a file"
        }
        """;

    private const string TagCreateFile = """
        { "tag": "web-servers", "colour": "#3F51B5", "notes": "Created from a file", "trustRequirements": [3] }
        """;

    private const string ZoneCreateFile = """
        { "name": "internal.example", "notes": "Created from a file", "autoDnsTags": ["web-servers"] }
        """;

    private const string RecordCreateFile = """
        { "name": "intranet", "zoneId": 4, "type": "ENCLAVE", "tags": ["web-servers"], "systems": ["AB12C", "DE34F"], "notes": "Created from a file" }
        """;

    private const string SystemPatchFile = """
        { "description": "web-01", "tags": ["web-servers", "production"] }
        """;

    private const string PendingPatchFile = """
        { "description": "laptop-07", "notes": "Laptop for the new starter" }
        """;

    private const string KeyPatchFile = """
        { "approvalMode": "Automatic", "usesRemaining": 10, "ipConstraints": [{ "range": "198.51.100.0/24", "description": "Office" }] }
        """;

    private const string PolicyPatchFile = """
        { "description": "Developers to web servers", "senderTags": ["developers", "contractors"], "acls": [{ "protocol": "Tcp", "ports": "22", "description": "SSH" }] }
        """;

    private const string TagPatchFile = """
        { "colour": "#FF9800", "notes": "Front-end servers" }
        """;

    private const string ZonePatchFile = """
        { "autoDnsTags": ["web-servers", "databases"] }
        """;

    private const string RecordPatchFile = """
        { "systems": ["AB12C"], "notes": "Moved to one system" }
        """;

    private const string TrustPatchFile = """
        { "notes": "Reviewed", "settings": { "configuration": { "authority": "portal" }, "conditions": [] } }
        """;

    private const string OrgPatchFile = """
        { "website": "https://acme.example", "phone": "020 7946 0000" }
        """;

    [TestCaseSource(nameof(CreateCases))]
    public async Task Create_from_file_sends_every_field_of_the_file_unchanged_in_the_post_body(string[] command, string path, string file, string response)
    {
        using var run = CliRun.Start();
        run.Stub("POST", path, json: response);
        var filePath = run.WriteFile("create.json", file);

        var result = await run.RunAsync([.. command, "--from-file", filePath]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(result.StdoutJson.ValueKind, Is.EqualTo(JsonValueKind.Object));
            AssertCarriesEveryField(request.BodyJson, JsonRead.Parse(file));
        });
    }

    // "-" reads the model from stdin, so an agent can pipe a generated model in without writing a
    // file.
    [TestCaseSource(nameof(CreateCases))]
    public async Task Create_from_file_dash_reads_the_model_from_stdin(string[] command, string path, string file, string response)
    {
        using var run = CliRun.Start();
        run.Stub("POST", path, json: response);
        run.StdinText = file;

        var result = await run.RunAsync([.. command, "--from-file", "-"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(path));
            AssertCarriesEveryField(request.BodyJson, JsonRead.Parse(file));
        });
    }

    // In a patch file, fields present are set and absent fields are left as they are (proposal,
    // "Create and update"). The PATCH body therefore holds the file's fields and no others: a
    // field sent with a default value would overwrite the resource's current value.
    [TestCaseSource(nameof(UpdateCases))]
    public async Task Update_from_file_sends_a_patch_holding_exactly_the_fields_in_the_file(string[] command, string path, string file, string response)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", path, json: response);
        var filePath = run.WriteFile("patch.json", file);

        var result = await run.RunAsync([.. command, "--from-file", filePath]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            Assert.That(result.StdoutJson.ValueKind, Is.EqualTo(JsonValueKind.Object));
            AssertHoldsExactlyTheFields(request.BodyJson, JsonRead.Parse(file));
        });
    }

    [TestCaseSource(nameof(UpdateCases))]
    public async Task Update_from_file_dash_reads_the_patch_from_stdin(string[] command, string path, string file, string response)
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", path, json: response);
        run.StdinText = file;

        var result = await run.RunAsync([.. command, "--from-file", "-"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));
            AssertHoldsExactlyTheFields(request.BodyJson, JsonRead.Parse(file));
        });
    }

    // Policies and trust requirements are created from a file only, because their content is
    // nested (proposal, "Create and update" and "Command options"). The nested values reach the
    // API as written.
    [Test]
    public async Task Policy_create_from_file_sends_the_acls_sender_tags_and_receiver_tags_unchanged()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("policies"), json: ApiJson.Policy(7));
        var filePath = run.WriteFile("policy.json", GeneralPolicyCreateFile);

        var result = await run.RunAsync("policy", "create", "--from-file", filePath);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var body = run.SingleRequest().BodyJson;
        string[] senderTags = ["developers"];
        string[] receiverTags = ["web-servers", "databases"];
        const string acls = """[{ "protocol": "Tcp", "ports": "443", "description": "HTTPS" }, { "protocol": "Udp", "ports": "53", "description": "DNS" }]""";
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "senderTags")), Is.EqualTo(senderTags));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(body, "receiverTags")), Is.EqualTo(receiverTags));
            AssertJsonEqual(JsonAssert.Property(body, "acls"), acls);
            AssertJsonEqual(JsonAssert.Property(body, "senderTrustRequirements"), "[3]");
        });
    }

    [Test]
    public async Task Trust_create_from_file_sends_the_settings_unchanged()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TestData.OrgPath("trust-requirements"), json: ApiJson.Trust(3));
        var filePath = run.WriteFile("trust.json", UserTrustCreateFile);

        var result = await run.RunAsync("trust", "create", "--from-file", filePath);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var body = run.SingleRequest().BodyJson;
        const string settings = """
            {
              "configuration": { "authority": "azure", "tenantId": "6d5f1c2a-0b7e-4f39-9d1a-2c3b4e5f6a7b" },
              "conditions": [{ "claim": "groups", "value": "engineering" }]
            }
            """;
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(body, "type").GetString(), Is.EqualTo("UserAuthentication"));
            AssertJsonEqual(JsonAssert.Property(body, "settings"), settings);
        });
    }

    // policy create and trust create take --from-file only (proposal, "Command options"), so
    // without it there is nothing to create.
    [TestCaseSource(nameof(FileOnlyCreateCases))]
    public async Task Create_of_a_policy_or_trust_requirement_without_from_file_exits_2_without_a_request(string[] command, string method, string path, string file, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);

            var result = await run.RunAsync(command);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    // Unknown fields exit 2 (proposal, "Create and update"). A misspelt field would otherwise be
    // dropped without notice, and the change the caller meant would not be made. The second run,
    // with the same file less that field, shows the rejection comes from the field.
    [TestCaseSource(nameof(AllCases))]
    public async Task From_file_with_an_unknown_field_exits_2_without_a_request(string[] command, string method, string path, string file, string response)
    {
        var withUnknownField = JsonNode.Parse(file)!.AsObject();
        withUnknownField["unknownField"] = "value";

        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);
            var filePath = run.WriteFile("model.json", withUnknownField.ToJsonString());

            var result = await run.RunAsync([.. command, "--from-file", filePath]);

            CliAssert.Rejected(run, result);
            Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain("unknownField"));
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    // The SDK's patch call rejects null (Enclave.Sdk.Api 1.0.4, Data/PatchClient.cs, Set), so a
    // null in a patch file exits 2 before any call (proposal, "Create and update"). The error names
    // the field so the caller can fix the file.
    [TestCaseSource(nameof(UpdateCases))]
    public async Task Update_from_file_with_a_null_value_exits_2_without_a_request(string[] command, string path, string file, string response)
    {
        var withNull = JsonNode.Parse(file)!.AsObject();
        var field = withNull.First().Key;
        withNull[field] = null;

        using (var run = CliRun.Start())
        {
            run.Stub("PATCH", path, json: response);
            var filePath = run.WriteFile("patch.json", withNull.ToJsonString());

            var result = await run.RunAsync([.. command, "--from-file", filePath]);

            CliAssert.Rejected(run, result);
            Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain(field));
        }

        await AssertSentAsync(command, "PATCH", path, file, response);
    }

    [TestCaseSource(nameof(AllCases))]
    public async Task From_file_holding_invalid_json_exits_2_without_a_request(string[] command, string method, string path, string file, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);
            var filePath = run.WriteFile("model.json", """{ "description": "unterminated""");

            var result = await run.RunAsync([.. command, "--from-file", filePath]);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    // The file holds one model, a JSON object. An empty file, an array, a bare value or JSON null
    // holds no model to send.
    [TestCaseSource(nameof(NotAModelCases))]
    public async Task From_file_holding_json_other_than_an_object_exits_2_without_a_request(string[] command, string method, string path, string file, string response, string content)
    {
        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);
            var filePath = run.WriteFile("model.json", content);

            var result = await run.RunAsync([.. command, "--from-file", filePath]);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    [TestCaseSource(nameof(AllCases))]
    public async Task From_file_naming_a_file_that_does_not_exist_exits_2_without_a_request(string[] command, string method, string path, string file, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);
            var missing = Path.Combine(run.WorkDirectory, "missing.json");

            var result = await run.RunAsync([.. command, "--from-file", missing]);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    // The CLI never waits for input (AGENTS.md, "CLI contract"). With stdin a terminal there is no
    // piped model to read, so "-" exits 2 at once, as an ID argument of "-" does (proposal,
    // "Several IDs").
    [TestCaseSource(nameof(StdinCases))]
    public async Task From_file_dash_when_stdin_is_a_terminal_exits_2_without_a_request(string[] command, string method, string path, string file, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);
            run.StdinIsTerminal = true;
            run.StdinText = file;

            var result = await run.RunAsync([.. command, "--from-file", "-"]);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    [TestCaseSource(nameof(StdinCases))]
    public async Task From_file_dash_with_empty_stdin_exits_2_without_a_request(string[] command, string method, string path, string file, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);
            run.StdinText = string.Empty;

            var result = await run.RunAsync([.. command, "--from-file", "-"]);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    // Flags and --from-file together exit 2 (proposal, "Create and update"): with both, either
    // source could be meant to win, and a guess changes the resource in a way the caller did not
    // ask for. The second run, with the file alone, shows the rejection comes from the flag.
    [TestCaseSource(nameof(FlagCases))]
    public async Task Flags_combined_with_from_file_exit_2_without_a_request(string[] command, string[] flag, string method, string path, string file, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub(method, path, json: response);
            var filePath = run.WriteFile("model.json", file);

            var result = await run.RunAsync([.. command, "--from-file", filePath, .. flag]);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, method, path, file, response);
    }

    // The create model carries the tag or zone or record name, so with --from-file the name comes
    // from the file. A name given as an argument as well is a second source for the same field,
    // which exits 2 as a flag does.
    [TestCaseSource(nameof(NameArgumentCases))]
    public async Task Create_with_a_name_argument_and_from_file_exits_2_without_a_request(string[] command, string name, string path, string file, string response)
    {
        using (var run = CliRun.Start())
        {
            run.Stub("POST", path, json: response);
            var filePath = run.WriteFile("model.json", file);

            var result = await run.RunAsync([.. command, name, "--from-file", filePath]);

            CliAssert.Rejected(run, result);
        }

        await AssertSentAsync(command, "POST", path, file, response);
    }

    private static IEnumerable<TestCaseData> CreateCases() =>
        Creates().Select(edit => new TestCaseData(edit.Command, edit.Path, edit.File, edit.Response).SetArgDisplayNames(edit.Name));

    private static IEnumerable<TestCaseData> UpdateCases() =>
        Updates().Select(edit => new TestCaseData(edit.Command, edit.Path, edit.File, edit.Response).SetArgDisplayNames(edit.Name));

    // Every create and update case, with the HTTP method each sends.
    private static IEnumerable<TestCaseData> AllCases() =>
        Creates().Concat(Updates()).Select(WithMethod);

    // One create and one update command stand for the rest: reading the model from stdin or a
    // file is shared by every command that takes --from-file.
    private static IEnumerable<TestCaseData> StdinCases() =>
        Representatives().Select(WithMethod);

    private static IEnumerable<TestCaseData> FileOnlyCreateCases() =>
        Creates()
            .Where(edit => edit.Name is "policy create, general" or "trust create, user authentication")
            .Select(WithMethod);

    private static IEnumerable<TestCaseData> NotAModelCases()
    {
        string[] contents = [string.Empty, "[]", "null", "\"web-01\""];

        foreach (var edit in Representatives())
        {
            foreach (var content in contents)
            {
                var description = content.Length == 0 ? "empty file" : content;
                yield return new TestCaseData(edit.Command, edit.Method, edit.Path, edit.File, edit.Response, content)
                    .SetArgDisplayNames($"{edit.Name}, {description}");
            }
        }
    }

    // The create commands whose model holds the name the command also takes as an argument.
    private static IEnumerable<TestCaseData> NameArgumentCases()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tag create"] = "web-servers",
            ["dns zone create"] = "internal.example",
            ["dns record create"] = "intranet",
        };

        return Creates()
            .Where(edit => names.ContainsKey(edit.Name))
            .Select(edit => new TestCaseData(edit.Command, names[edit.Name], edit.Path, edit.File, edit.Response).SetArgDisplayNames(edit.Name));
    }

    // Each command with one of its flags, from the proposal's "Command options" table. The file
    // is one the command accepts on its own.
    private static IEnumerable<TestCaseData> FlagCases()
    {
        var flags = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["system update"] = ["--description", "web-02"],
            ["pending update"] = ["--notes", "Approved by IT"],
            ["key create"] = ["--description", "Build agents"],
            ["key update"] = ["--set-tags", "laptops"],
            ["policy update"] = ["--notes", "Reviewed"],
            ["tag create"] = ["--colour", "#FF9800"],
            ["tag update"] = ["--name", "frontend"],
            ["dns zone create"] = ["--notes", "Internal names"],
            ["dns zone update"] = ["--name", "corp.example"],
            ["dns record create"] = ["--zone", "4"],
            ["dns record update"] = ["--set-systems", "DE34F"],
            ["trust update"] = ["--description", "Signed in"],
            ["org update"] = ["--website", "https://acme.example"],
        };

        return Creates().Concat(Updates())
            .Where(edit => flags.ContainsKey(edit.Name))
            .Select(edit => new TestCaseData(edit.Command, flags[edit.Name], edit.Method, edit.Path, edit.File, edit.Response)
                .SetArgDisplayNames($"{edit.Name} {flags[edit.Name][0]}"));
    }

    private static IEnumerable<Edit> Creates()
    {
        yield return new("key create", ["key", "create"], "POST", TestData.OrgPath("enrolment-keys"), KeyCreateFile, ApiJson.Key(12, "Build agents"));
        yield return new("policy create, general", ["policy", "create"], "POST", TestData.OrgPath("policies"), GeneralPolicyCreateFile, ApiJson.Policy(7));
        yield return new("policy create, gateway", ["policy", "create"], "POST", TestData.OrgPath("policies"), GatewayPolicyCreateFile, ApiJson.Policy(8));
        yield return new("tag create", ["tag", "create"], "POST", TestData.OrgPath("tags"), TagCreateFile, ApiJson.Tag("web-servers"));
        yield return new("dns zone create", ["dns", "zone", "create"], "POST", TestData.OrgPath("dns/zones"), ZoneCreateFile, ApiJson.Zone(4, "internal.example"));
        yield return new("dns record create", ["dns", "record", "create"], "POST", TestData.OrgPath("dns/records"), RecordCreateFile, ApiJson.Record(9, "intranet"));
        yield return new("trust create, user authentication", ["trust", "create"], "POST", TestData.OrgPath("trust-requirements"), UserTrustCreateFile, ApiJson.Trust(3));
        yield return new("trust create, public IP", ["trust", "create"], "POST", TestData.OrgPath("trust-requirements"), PublicIpTrustCreateFile, ApiJson.Trust(4));
    }

    private static IEnumerable<Edit> Updates()
    {
        yield return new("system update", ["system", "update", "AB12C"], "PATCH", TestData.OrgPath("systems/AB12C"), SystemPatchFile, ApiJson.System("AB12C"));
        yield return new("pending update", ["pending", "update", "CD34E"], "PATCH", TestData.OrgPath("unapproved-systems/CD34E"), PendingPatchFile, ApiJson.PendingSystem("CD34E"));
        yield return new("key update", ["key", "update", "12"], "PATCH", TestData.OrgPath("enrolment-keys/12"), KeyPatchFile, ApiJson.Key(12));
        yield return new("policy update", ["policy", "update", "7"], "PATCH", TestData.OrgPath("policies/7"), PolicyPatchFile, ApiJson.Policy(7));
        yield return new("tag update", ["tag", "update", "web-servers"], "PATCH", TestData.OrgPath("tags/web-servers"), TagPatchFile, ApiJson.Tag("web-servers"));
        yield return new("dns zone update", ["dns", "zone", "update", "4"], "PATCH", TestData.OrgPath("dns/zones/4"), ZonePatchFile, ApiJson.Zone(4, "internal.example"));
        yield return new("dns record update", ["dns", "record", "update", "9"], "PATCH", TestData.OrgPath("dns/records/9"), RecordPatchFile, ApiJson.Record(9, "intranet"));
        yield return new("trust update", ["trust", "update", "3"], "PATCH", TestData.OrgPath("trust-requirements/3"), TrustPatchFile, ApiJson.Trust(3));
        yield return new("org update", ["org", "update"], "PATCH", TestData.OrgPath(), OrgPatchFile, ApiJson.OrgProperties(TestData.OrgName));
    }

    private static IEnumerable<Edit> Representatives() =>
        Creates().Concat(Updates()).Where(edit => edit.Name is "key create" or "system update");

    private static TestCaseData WithMethod(Edit edit) =>
        new TestCaseData(edit.Command, edit.Method, edit.Path, edit.File, edit.Response).SetArgDisplayNames(edit.Name);

    // Runs the command with the file as given and asserts the CLI sends the request. A test that
    // expects a rejection runs this as well, so the rejection is shown to come from the input it
    // varies and not from a missing command.
    private static async Task AssertSentAsync(string[] command, string method, string path, string file, string response)
    {
        using var run = CliRun.Start();
        run.Stub(method, path, json: response);
        var filePath = run.WriteFile("model.json", file);

        var result = await run.RunAsync([.. command, "--from-file", filePath]);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, $"The same command with the file alone must succeed.{result}");
            Assert.That(run.Calls(), Is.EqualTo(new[] { $"{method} {path}" }));
        });
    }

    // Each field of the file appears in the body with the same JSON value. The body may hold more
    // fields: the SDK writes the whole create model, defaults included.
    private static void AssertCarriesEveryField(JsonElement body, JsonElement file)
    {
        foreach (var field in file.EnumerateObject())
        {
            var sent = JsonAssert.Property(body, field.Name);
            Assert.That(JsonElement.DeepEquals(sent, field.Value), Is.True, $"\"{field.Name}\": the file holds {field.Value}, the request body holds {sent}");
        }
    }

    // The body holds the file's fields and no others, each with the same JSON value. Field names
    // are compared ignoring case because the SDK keys a PATCH body by C# property name
    // (Enclave.Sdk.Api 1.0.4, Data/PatchClient.cs, Set).
    private static void AssertHoldsExactlyTheFields(JsonElement body, JsonElement file)
    {
        var bodyFields = body.EnumerateObject().Select(field => field.Name.ToUpperInvariant()).Order();
        var fileFields = file.EnumerateObject().Select(field => field.Name.ToUpperInvariant()).Order();
        Assert.That(bodyFields, Is.EqualTo(fileFields), $"The PATCH body holds {body}");

        foreach (var field in file.EnumerateObject())
        {
            var sent = JsonAssert.Property(body, field.Name);
            Assert.That(JsonElement.DeepEquals(sent, field.Value), Is.True, $"\"{field.Name}\": the file holds {field.Value}, the request body holds {sent}");
        }
    }

    private static void AssertJsonEqual(JsonElement actual, string expected)
    {
        var expectedJson = JsonRead.Parse(expected);
        Assert.That(JsonElement.DeepEquals(actual, expectedJson), Is.True, $"Expected {expectedJson}, found {actual}");
    }

    // One command that sends a model: the request it makes, a file it accepts and the model the
    // fake API answers with.
    private sealed record Edit(string Name, string[] Command, string Method, string Path, string File, string Response);
}
