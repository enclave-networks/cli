using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Api.Modules.SystemManagement.Dns.Models;
using Enclave.Api.Modules.SystemManagement.EnrolmentKeys.Models;
using Enclave.Api.Modules.SystemManagement.Policies.Models;
using Enclave.Api.Modules.SystemManagement.Tags.Models;
using Enclave.Api.Modules.SystemManagement.TrustRequirements.Models;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Editing;

// --template prints a model to edit and pass back with --from-file (AGENTS.md, "CLI contract";
// proposal, "Create and update"). create --template prints the create model; update <id>
// --template prints the resource's current values in patch-model shape, read with one GET.
// Templates use the camelCase property names and enum member names that the CLI's JSON output
// and --from-file use (Enclave.Sdk.Api 1.0.4, Constants.JsonSerializerOptions).
public class TemplateTests
{
    // The GET responses below are Enclave.Sdk.Api.Data 304.48.0 models with a value in each field
    // the patch model has, so the template has a current value to show for each. Tags, trust
    // requirements and DNS record systems come back as objects (IUsedTagModel,
    // IUsedTrustRequirementModel, ISystemReferenceModel), and the patch models take their names
    // or IDs.
    private const string SystemJson = """
        {
          "systemId": "AB12C",
          "description": "web-01",
          "systemType": "GeneralPurpose",
          "state": "Connected",
          "connectedAt": "2026-01-02T03:04:05+00:00",
          "lastSeen": "2026-01-02T03:04:05+00:00",
          "enrolledAt": "2026-01-01T03:04:05+00:00",
          "enrolmentKeyId": 12,
          "enrolmentKeyDescription": "Servers",
          "enrolmentKeyIsDeleted": false,
          "isEnabled": true,
          "connectedFrom": "203.0.113.10",
          "virtualAddress": "100.64.0.2",
          "virtualNetwork": "100.64.0.0/10",
          "hostname": "web-01",
          "platformType": "Linux",
          "osVersion": "Ubuntu 24.04",
          "enclaveVersion": "2026.10.1",
          "gatewayRoutes": [{ "subnet": "192.168.1.0/24", "userEntered": true, "weight": 10, "name": "Office LAN" }],
          "tags": [
            { "tag": "web-servers", "ref": "ref:1a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#FF9800" },
            { "tag": "production", "ref": "ref:2a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#F44336" }
          ],
          "dns": [],
          "knownSubnets": [],
          "notes": "Front-end web server",
          "disconnectedRetentionMinutes": null,
          "autoExpire": null
        }
        """;

    private const string SystemTemplate = """
        {
          "description": "web-01",
          "isEnabled": true,
          "gatewayRoutes": [{ "subnet": "192.168.1.0/24", "userEntered": true, "weight": 10, "name": "Office LAN" }],
          "tags": ["web-servers", "production"],
          "notes": "Front-end web server"
        }
        """;

    private const string PendingJson = """
        {
          "systemId": "CD34E",
          "systemType": "GeneralPurpose",
          "description": "laptop-07",
          "enrolledFrom": "203.0.113.20",
          "enrolledAt": "2026-01-01T03:04:05Z",
          "tags": [{ "tag": "laptops", "ref": "ref:3a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#4CAF50" }],
          "enrolmentKeyId": 12,
          "enrolmentKeyDescription": "Staff laptops",
          "enrolmentKeyIsDeleted": false,
          "notes": "Awaiting approval",
          "hostname": "laptop-07",
          "platformType": "Windows",
          "osVersion": "Windows 11",
          "enclaveVersion": "2026.10.1",
          "connectedFrom": "203.0.113.20"
        }
        """;

    private const string PendingTemplate = """
        { "description": "laptop-07", "tags": ["laptops"], "notes": "Awaiting approval" }
        """;

    private const string KeyJson = """
        {
          "id": 12,
          "created": "2026-01-01T03:04:05Z",
          "lastUsed": null,
          "type": "GeneralPurpose",
          "approvalMode": "Manual",
          "status": "Enabled",
          "key": "SECRET-KEY-VALUE",
          "description": "Staff laptops",
          "isEnabled": true,
          "usesRemaining": 40,
          "enrolledCount": 3,
          "unapprovedCount": 1,
          "tags": [{ "tag": "laptops", "ref": "ref:3a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#4CAF50" }],
          "disconnectedRetentionMinutes": 120,
          "ipConstraints": [{ "range": "198.51.100.0/24", "description": "Office" }],
          "notes": "For staff laptops",
          "autoExpire": { "timeZoneId": "Europe/London", "expiryDateTime": "2026-12-31T17:00:00", "expiryAction": "Disable" }
        }
        """;

    private const string KeyTemplate = """
        {
          "description": "Staff laptops",
          "isEnabled": true,
          "approvalMode": "Manual",
          "usesRemaining": 40,
          "ipConstraints": [{ "range": "198.51.100.0/24", "description": "Office" }],
          "tags": ["laptops"],
          "disconnectedRetentionMinutes": 120,
          "notes": "For staff laptops",
          "autoExpire": { "timeZoneId": "Europe/London", "expiryDateTime": "2026-12-31T17:00:00", "expiryAction": "Disable" }
        }
        """;

    private const string GeneralPolicyJson = """
        {
          "id": 7,
          "created": "2026-01-02T03:04:05Z",
          "type": "General",
          "description": "Developers to web servers",
          "isEnabled": true,
          "state": "Active",
          "senderTags": [{ "tag": "developers", "ref": "ref:0f4e9a3c5b6d4e7f8a9b0c1d2e3f4a5b", "colour": "#3F51B5" }],
          "receiverTags": [{ "tag": "web-servers", "ref": "ref:1a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#FF9800" }],
          "acls": [{ "protocol": "Tcp", "ports": "443", "description": "HTTPS" }],
          "senderTrustRequirements": [{ "id": 3, "description": "Signed in to Microsoft", "type": "UserAuthentication" }],
          "gatewayAllowedIpRanges": [],
          "gateways": [],
          "gatewayTrafficDirection": null,
          "gatewayPriority": null,
          "notes": "Reviewed quarterly",
          "autoExpire": null,
          "activeHours": null
        }
        """;

    // PolicyPatchModel has no Type or GatewayTrafficDirection, and fields whose current value is
    // null are left out: a null in a patch file exits 2, so a template holding one could not be
    // passed back.
    private const string GeneralPolicyTemplate = """
        {
          "description": "Developers to web servers",
          "isEnabled": true,
          "senderTags": ["developers"],
          "receiverTags": ["web-servers"],
          "acls": [{ "protocol": "Tcp", "ports": "443", "description": "HTTPS" }],
          "notes": "Reviewed quarterly",
          "senderTrustRequirements": [3],
          "gatewayAllowedIpRanges": [],
          "gateways": []
        }
        """;

    private const string GatewayPolicyJson = """
        {
          "id": 8,
          "created": "2026-01-02T03:04:05Z",
          "type": "Gateway",
          "description": "Office LAN through the gateway",
          "isEnabled": true,
          "state": "Active",
          "senderTags": [{ "tag": "staff", "ref": "ref:4a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#9C27B0" }],
          "receiverTags": [],
          "acls": [{ "protocol": "Any", "ports": "", "description": "All traffic" }],
          "senderTrustRequirements": [],
          "gatewayAllowedIpRanges": [{ "ipRange": "192.168.1.0/24", "description": "Office LAN" }],
          "gateways": [
            {
              "systemId": "GW4X7",
              "systemName": "gateway-01",
              "machineName": "gw01",
              "routes": [{ "route": "192.168.1.0/24", "gatewayWeight": 10, "gatewayName": "Office LAN" }]
            }
          ],
          "gatewayTrafficDirection": "Exit",
          "gatewayPriority": "Prioritised",
          "notes": "Gateway policy",
          "autoExpire": null,
          "activeHours": null
        }
        """;

    // PolicyModel returns each gateway as a PolicyGatewayDetailModel with route objects;
    // PolicyPatchModel takes PolicyGateway, a system ID and route strings.
    private const string GatewayPolicyTemplate = """
        {
          "description": "Office LAN through the gateway",
          "isEnabled": true,
          "senderTags": ["staff"],
          "receiverTags": [],
          "acls": [{ "protocol": "Any", "ports": "", "description": "All traffic" }],
          "notes": "Gateway policy",
          "senderTrustRequirements": [],
          "gatewayAllowedIpRanges": [{ "ipRange": "192.168.1.0/24", "description": "Office LAN" }],
          "gateways": [{ "systemId": "GW4X7", "routes": ["192.168.1.0/24"] }],
          "gatewayPriority": "Prioritised"
        }
        """;

    private const string TagJson = """
        {
          "tag": "web-servers",
          "ref": "ref:1a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6",
          "colour": "#FF9800",
          "created": "2026-01-01T03:04:05Z",
          "lastModified": "2026-01-02T03:04:05Z",
          "lastReferenced": null,
          "systems": 2,
          "keys": 1,
          "policies": 1,
          "dnsZones": 0,
          "dnsRecords": 1,
          "notes": "Front-end servers",
          "trustRequirements": [{ "id": 3, "description": "Signed in to Microsoft", "type": "UserAuthentication" }]
        }
        """;

    private const string TagTemplate = """
        { "tag": "web-servers", "colour": "#FF9800", "notes": "Front-end servers", "trustRequirements": [3] }
        """;

    private const string ZoneJson = """
        {
          "id": 4,
          "name": "internal.example",
          "created": "2026-01-01T03:04:05Z",
          "recordCount": 2,
          "recordTypeCounts": { "ENCLAVE": 2 },
          "autoDnsTags": [{ "tag": "web-servers", "ref": "ref:1a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#FF9800" }],
          "notes": "Internal names"
        }
        """;

    private const string ZoneTemplate = """
        { "name": "internal.example", "notes": "Internal names", "autoDnsTags": ["web-servers"] }
        """;

    private const string RecordJson = """
        {
          "id": 9,
          "name": "intranet",
          "type": "ENCLAVE",
          "zoneId": 4,
          "zoneName": "internal.example",
          "fqdn": "intranet.internal.example",
          "tags": [{ "tag": "web-servers", "ref": "ref:1a2b3c4d5e6f47a8b9c0d1e2f3a4b5c6", "colour": "#FF9800" }],
          "systems": [
            { "connectedFrom": "203.0.113.10", "id": "AB12C", "machineName": "web-01", "name": "web-01", "platformType": "Linux", "state": "Connected" }
          ],
          "autoGenerated": false,
          "notes": "Intranet site"
        }
        """;

    private const string RecordTemplate = """
        { "name": "intranet", "tags": ["web-servers"], "systems": ["AB12C"], "notes": "Intranet site" }
        """;

    private const string TrustJson = """
        {
          "id": 3,
          "description": "Signed in to Microsoft",
          "created": "2026-01-02T03:04:05Z",
          "modified": "2026-01-03T03:04:05Z",
          "type": "UserAuthentication",
          "usedInTags": 1,
          "usedInPolicies": 2,
          "notes": "Reviewed quarterly",
          "settings": {
            "configuration": { "authority": "azure", "tenantId": "6d5f1c2a-0b7e-4f39-9d1a-2c3b4e5f6a7b" },
            "conditions": [{ "claim": "groups", "value": "engineering" }]
          }
        }
        """;

    private const string TrustTemplate = """
        {
          "description": "Signed in to Microsoft",
          "notes": "Reviewed quarterly",
          "settings": {
            "configuration": { "authority": "azure", "tenantId": "6d5f1c2a-0b7e-4f39-9d1a-2c3b4e5f6a7b" },
            "conditions": [{ "claim": "groups", "value": "engineering" }]
          }
        }
        """;

    private const string OrgTemplate = """
        { "name": "Acme", "website": "https://acme.example", "phone": "020 7946 0000" }
        """;

    // create --template prints the create model with every field present (proposal, "Create and
    // update"), so an agent learns the whole model from it. The fields are the properties of the
    // Enclave.Sdk.Api.Data 304.48.0 create model the command sends.
    [TestCaseSource(nameof(CreateCases))]
    public async Task Create_template_prints_every_field_of_the_create_model_without_a_request(string[] command, Type model, string path, string response)
    {
        using var run = CliRun.Start();
        run.Stub("POST", path, json: response);

        var result = await run.RunAsync([.. command, "--template"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var template = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty);
            Assert.That(template.ValueKind, Is.EqualTo(JsonValueKind.Object));
            Assert.That(template.EnumerateObject().Select(field => field.Name), Is.EquivalentTo(ModelFields(model)));
        });
    }

    // "Each list holding one example entry" (proposal, "Create and update"): an empty list shows
    // nothing of its items' shape, and nested items such as policy acls are where that shape is
    // hardest to guess. Lists inside the example entries are held to the same rule.
    [TestCaseSource(nameof(CreateCases))]
    public async Task Create_template_holds_one_example_entry_in_each_list(string[] command, Type model, string path, string response)
    {
        using var run = CliRun.Start();
        run.Stub("POST", path, json: response);

        var result = await run.RunAsync([.. command, "--template"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var lists = new List<(string Path, int Count)>();
        CollectLists(result.StdoutJson, "$", lists);
        Assert.Multiple(() =>
        {
            Assert.That(lists, Is.Not.Empty, $"The {model.Name} template holds no list.");
            Assert.That(lists.Where(list => list.Count != 1), Is.Empty, $"Lists without exactly one entry in {result.Stdout}");
        });
    }

    // A create template comes from the model alone, with no API call, so it works before a token
    // or an organisation is set up, when an agent first reads it to learn the model's shape.
    [TestCaseSource(nameof(CreateCases))]
    public async Task Create_template_needs_no_token_or_organisation(string[] command, Type model, string path, string response)
    {
        using var run = CliRun.Start();
        run.Stub("POST", path, json: response);
        run.Environment.Remove("ENCLAVE_TOKEN");
        run.Environment.Remove("ENCLAVE_ORG");

        var result = await run.RunAsync([.. command, "--template"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, $"{result}");
            Assert.That(result.Stderr, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
            Assert.That(result.StdoutJson.ValueKind, Is.EqualTo(JsonValueKind.Object));
        });
    }

    // The proposal's example of a nested create model: a policy's rules and tags are lists, and
    // each shows one entry to copy.
    [Test]
    public async Task Policy_create_template_holds_one_acl_one_sender_tag_and_one_receiver_tag()
    {
        using var run = CliRun.Start();

        var result = await run.RunAsync("policy", "create", "--template");

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var template = result.StdoutJson;
        var acls = JsonAssert.Property(template, "acls");
        Assert.Multiple(() =>
        {
            Assert.That(acls.GetArrayLength(), Is.EqualTo(1));
            Assert.That(acls[0].EnumerateObject().Select(field => field.Name), Is.EquivalentTo(ModelFields(typeof(PolicyAclModel))));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(template, "senderTags")), Has.Length.EqualTo(1));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(template, "receiverTags")), Has.Length.EqualTo(1));
            Assert.That(run.Requests, Is.Empty);
        });
    }

    // A template is for editing and passing back, so it holds valid enum names, numeric IDs and
    // no field the create model lacks: create --from-file accepts it as printed.
    [TestCaseSource(nameof(CreateCases))]
    public async Task Create_template_passes_back_through_from_file_unchanged(string[] command, Type model, string path, string response)
    {
        string templateText;
        using (var templateRun = CliRun.Start())
        {
            var templateResult = await templateRun.RunAsync([.. command, "--template"]);
            Assert.That(templateResult.ExitCode, Is.Zero, $"{templateResult}");
            templateText = templateResult.Stdout;
        }

        using var run = CliRun.Start();
        run.Stub("POST", path, json: response);
        run.StdinText = templateText;

        var result = await run.RunAsync([.. command, "--from-file", "-"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo(path));
        });

        var body = request.BodyJson;
        foreach (var field in Parse(templateText).EnumerateObject())
        {
            var sent = JsonAssert.Property(body, field.Name);
            Assert.That(JsonElement.DeepEquals(sent, field.Value), Is.True, $"\"{field.Name}\": the template holds {field.Value}, the request body holds {sent}");
        }
    }

    // update <id> --template reads the resource with one GET and prints its current values in
    // patch-model shape (proposal, "Create and update"). show output cannot be passed back: tag,
    // trust requirement and system references come back as objects, and the patch models take
    // names and IDs. The template therefore equals the GET response converted to the patch model,
    // with no field the patch model lacks (such as id, created or state).
    [TestCaseSource(nameof(UpdateCases))]
    public async Task Update_template_makes_one_get_and_prints_the_current_values_in_patch_model_shape(string[] command, string path, string model, string expected)
    {
        using var run = CliRun.Start();
        run.Stub("GET", path, json: model);
        run.Stub("PATCH", path, json: model);

        var result = await run.RunAsync([.. command, "--template"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var template = result.StdoutJson;
        var expectedJson = Parse(expected);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[] { $"GET {path}" }));
            Assert.That(JsonElement.DeepEquals(template, expectedJson), Is.True, $"Expected the template {expectedJson}, found {template}");
        });
    }

    // The template is "ready to edit and pass back" (proposal, "Create and update"): passed
    // unchanged to update --from-file, it sends a PATCH setting each field to the value it already
    // has.
    [TestCaseSource(nameof(UpdateCases))]
    public async Task Update_template_passes_back_through_from_file_as_a_patch_of_the_same_values(string[] command, string path, string model, string expected)
    {
        string templateText;
        using (var templateRun = CliRun.Start())
        {
            templateRun.Stub("GET", path, json: model);
            var templateResult = await templateRun.RunAsync([.. command, "--template"]);
            Assert.That(templateResult.ExitCode, Is.Zero, $"{templateResult}");
            templateText = templateResult.Stdout;
        }

        using var run = CliRun.Start();
        run.Stub("PATCH", path, json: model);
        run.StdinText = templateText;

        var result = await run.RunAsync([.. command, "--from-file", "-"]);

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var request = run.SingleRequest();
        var template = Parse(templateText);
        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo("PATCH"));
            Assert.That(request.Path, Is.EqualTo(path));

            // PATCH bodies are keyed by C# property name (Enclave.Sdk.Api 1.0.4, Data/PatchClient.cs,
            // Set), so names are compared ignoring case.
            Assert.That(
                body.EnumerateObject().Select(field => field.Name.ToUpperInvariant()),
                Is.EquivalentTo(template.EnumerateObject().Select(field => field.Name.ToUpperInvariant())),
                $"The PATCH body holds {body}");
        });

        foreach (var field in template.EnumerateObject())
        {
            var sent = JsonAssert.Property(body, field.Name);
            Assert.That(JsonElement.DeepEquals(sent, field.Value), Is.True, $"\"{field.Name}\": the template holds {field.Value}, the request body holds {sent}");
        }
    }

    // The proposal's own example of why show output cannot be passed back: PolicyModel returns
    // sender tags as objects and PolicyPatchModel takes tag names.
    [Test]
    public async Task Policy_update_template_holds_sender_and_receiver_tag_names()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TestData.OrgPath("policies/7"), json: GeneralPolicyJson);

        var result = await run.RunAsync("policy", "update", "7", "--template");

        Assert.That(result.ExitCode, Is.Zero, $"{result}");
        var template = result.StdoutJson;
        string[] senderTags = ["developers"];
        string[] receiverTags = ["web-servers"];
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Strings(JsonAssert.Property(template, "senderTags")), Is.EqualTo(senderTags));
            Assert.That(JsonAssert.Strings(JsonAssert.Property(template, "receiverTags")), Is.EqualTo(receiverTags));
            Assert.That(run.SingleRequest().Method, Is.EqualTo("GET"));
        });
    }

    // update is a single-ID command, and single-ID commands exit 5 for an unknown ID (proposal,
    // "Several IDs"). The template's GET is the only call made.
    [TestCaseSource(nameof(UpdateCasesWithAnId))]
    public async Task Update_template_for_an_unknown_id_exits_5_without_a_patch(string[] command, string path, string model, string expected)
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", path, 404, "Not Found");
        run.Stub("PATCH", path, json: model);

        var result = await run.RunAsync([.. command, "--template"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.EqualTo(5), $"{result}");
            Assert.That(result.Stdout, Is.Empty);
            Assert.That(run.Requests.Select(request => $"{request.Method} {request.Path}"), Is.EqualTo(new[] { $"GET {path}" }));
        });
        Assert.That(JsonAssert.Property(result.Error, "code").GetString(), Is.EqualTo("not_found"));
    }

    // --template prints a model and --from-file sends one; asked for both, the CLI cannot tell
    // which the caller meant, so it exits 2 as it does for flags with --from-file (proposal,
    // "Create and update"). The second run, with --template alone, shows the rejection comes from
    // the combination.
    [TestCaseSource(nameof(CombinationCases))]
    public async Task Template_combined_with_from_file_exits_2_without_a_request(string[] command, string path, string response, string[] flag)
    {
        using (var run = CliRun.Start())
        {
            StubEveryMethod(run, path, response);
            var filePath = run.WriteFile("model.json", """{ "description": "web-01" }""");

            var result = await run.RunAsync([.. command, "--template", "--from-file", filePath]);

            AssertRejected(result, run);
        }

        await AssertTemplatePrintedAsync(command, path, response);
    }

    // A template shows the model, so a flag that sets a field has nothing to act on. It exits 2
    // as a flag with --from-file does, so the caller does not take a printed template for a
    // change made.
    [TestCaseSource(nameof(CombinationCases))]
    public async Task Template_combined_with_a_flag_exits_2_without_a_request(string[] command, string path, string response, string[] flag)
    {
        using (var run = CliRun.Start())
        {
            StubEveryMethod(run, path, response);

            var result = await run.RunAsync([.. command, "--template", .. flag]);

            AssertRejected(result, run);
        }

        await AssertTemplatePrintedAsync(command, path, response);
    }

    private static IEnumerable<TestCaseData> CreateCases()
    {
        yield return CreateCase("key create", ["key", "create"], typeof(EnrolmentKeyCreateModel), "enrolment-keys", ApiJson.Key(12));
        yield return CreateCase("policy create", ["policy", "create"], typeof(PolicyCreateModel), "policies", ApiJson.Policy(7));
        yield return CreateCase("tag create", ["tag", "create"], typeof(TagCreateModel), "tags", ApiJson.Tag("web-servers"));
        yield return CreateCase("dns zone create", ["dns", "zone", "create"], typeof(DnsZoneCreateModel), "dns/zones", ApiJson.Zone(4, "internal.example"));
        yield return CreateCase("dns record create", ["dns", "record", "create"], typeof(DnsRecordCreateModel), "dns/records", ApiJson.Record(9, "intranet"));
        yield return CreateCase("trust create", ["trust", "create"], typeof(TrustRequirementCreateModel), "trust-requirements", ApiJson.Trust(3));
    }

    private static IEnumerable<TestCaseData> UpdateCases() =>
        Updates().Select(update => new TestCaseData(update.Command, update.Path, update.Model, update.Template).SetArgDisplayNames(update.Name));

    // org update acts on the organisation in use and takes no ID.
    private static IEnumerable<TestCaseData> UpdateCasesWithAnId() =>
        Updates()
            .Where(update => update.Name != "org update")
            .Select(update => new TestCaseData(update.Command, update.Path, update.Model, update.Template).SetArgDisplayNames(update.Name));

    // One create and one update command, each with one of its flags from the proposal's
    // "Command options" table.
    private static IEnumerable<TestCaseData> CombinationCases()
    {
        yield return CombinationCase("key create", ["key", "create"], TestData.OrgPath("enrolment-keys"), ApiJson.Key(12), ["--description", "Build agents"]);
        yield return CombinationCase("system update", ["system", "update", "AB12C"], TestData.OrgPath("systems/AB12C"), SystemJson, ["--notes", "Front-end web server"]);
    }

    private static IEnumerable<Update> Updates()
    {
        yield return new("system update", ["system", "update", "AB12C"], TestData.OrgPath("systems/AB12C"), SystemJson, SystemTemplate);
        yield return new("pending update", ["pending", "update", "CD34E"], TestData.OrgPath("unapproved-systems/CD34E"), PendingJson, PendingTemplate);
        yield return new("key update", ["key", "update", "12"], TestData.OrgPath("enrolment-keys/12"), KeyJson, KeyTemplate);
        yield return new("policy update, general", ["policy", "update", "7"], TestData.OrgPath("policies/7"), GeneralPolicyJson, GeneralPolicyTemplate);
        yield return new("policy update, gateway", ["policy", "update", "8"], TestData.OrgPath("policies/8"), GatewayPolicyJson, GatewayPolicyTemplate);
        yield return new("tag update", ["tag", "update", "web-servers"], TestData.OrgPath("tags/web-servers"), TagJson, TagTemplate);
        yield return new("dns zone update", ["dns", "zone", "update", "4"], TestData.OrgPath("dns/zones/4"), ZoneJson, ZoneTemplate);
        yield return new("dns record update", ["dns", "record", "update", "9"], TestData.OrgPath("dns/records/9"), RecordJson, RecordTemplate);
        yield return new("trust update", ["trust", "update", "3"], TestData.OrgPath("trust-requirements/3"), TrustJson, TrustTemplate);
        yield return new("org update", ["org", "update"], TestData.OrgPath(), OrgPropertiesJson(), OrgTemplate);
    }

    private static TestCaseData CreateCase(string name, string[] command, Type model, string resource, string response) =>
        new TestCaseData(command, model, TestData.OrgPath(resource), response).SetArgDisplayNames(name);

    private static TestCaseData CombinationCase(string name, string[] command, string path, string response, string[] flag) =>
        new TestCaseData(command, path, response, flag).SetArgDisplayNames(name);

    // The JSON names of a model's properties: camelCase, as Enclave.Sdk.Api 1.0.4 writes them
    // (Constants.JsonSerializerOptions, PropertyNamingPolicy = JsonNamingPolicy.CamelCase).
    private static string[] ModelFields(Type model) =>
        model.GetProperties().Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name)).ToArray();

    // Records the path and length of every array in the value, at any depth.
    private static void CollectLists(JsonElement value, string path, List<(string Path, int Count)> lists)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                lists.Add((path, value.GetArrayLength()));
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    CollectLists(item, $"{path}[{index++}]", lists);
                }

                break;

            case JsonValueKind.Object:
                foreach (var field in value.EnumerateObject())
                {
                    CollectLists(field.Value, $"{path}.{field.Name}", lists);
                }

                break;
        }
    }

    // An OrganisationPropertiesModel for TestData.OrgId with a website and phone number, so each
    // OrganisationPatchModel field has a current value for the template to show. OrganisationGuid
    // reads only the 32-digit form of a GUID ("N"); a hyphenated ID fails with "Cannot parse
    // OrganisationGuid" (Enclave.Sdk.Api.Data 304.48.0, OrganisationGuid's System.Text.Json
    // converter).
    private static string OrgPropertiesJson() => new JsonObject
    {
        ["id"] = TestData.OrgId.ToString("N"),
        ["created"] = "2026-01-01T00:00:00Z",
        ["name"] = TestData.OrgName,
        ["plan"] = "Business",
        ["billingIsPaidYearly"] = null,
        ["billingRate"] = null,
        ["billingDisableUpdate"] = null,
        ["billingCurrencySymbol"] = "$",
        ["website"] = "https://acme.example",
        ["phone"] = "020 7946 0000",
        ["maxSystems"] = 100,
        ["enrolledSystems"] = 2,
        ["unapprovedSystems"] = 0,
        ["featureLimits"] = new JsonArray(),
        ["trialExpiry"] = null,
        ["trialState"] = "None",
        ["isSuspendedByAdmin"] = false,
        ["partner"] = null,
    }.ToJsonString();

    // Any request the CLI makes is answered and recorded, so an assertion of no requests fails on
    // a request of any method.
    private static void StubEveryMethod(CliRun run, string path, string response)
    {
        run.Stub("GET", path, json: response);
        run.Stub("POST", path, json: response);
        run.Stub("PATCH", path, json: response);
    }

    // Runs the command with --template alone and asserts it prints a template and sends no
    // change. A test that expects a rejection runs this as well, so the rejection is shown to come
    // from the option it adds and not from a missing command.
    private static async Task AssertTemplatePrintedAsync(string[] command, string path, string response)
    {
        using var run = CliRun.Start();
        StubEveryMethod(run, path, response);

        var result = await run.RunAsync([.. command, "--template"]);

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Zero, $"The same command with --template alone must succeed.{result}");
            Assert.That(run.Requests.Where(request => request.Method != "GET"), Is.Empty);
            Assert.That(result.StdoutJson.ValueKind, Is.EqualTo(JsonValueKind.Object));
        });
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

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    // One update command: the path its GET and PATCH use, the model the GET returns and the
    // template that model gives.
    private sealed record Update(string Name, string[] Command, string Path, string Model, string Template);
}
