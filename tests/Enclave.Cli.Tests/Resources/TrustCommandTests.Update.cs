using System.Text.Json;
using System.Text.Json.Nodes;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static Enclave.Cli.Tests.Support.FieldText;

namespace Enclave.Cli.Tests.Resources;

// The API's patch replaces the whole settings object (portal TrustRequirementPatchModel), so a
// --set- flag reads the requirement and sends the rest back as it was. Enclave.Sdk.Api keys a PATCH
// body by the C# property name (PatchClient.Set).

/// <summary>
/// Tests for trust update.
/// </summary>
public partial class TrustCommandTests
{
    // Example 58. FR is not given, so it goes; the ranges and the blocked country are other kinds and
    // stay.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Trust_update_set_allow_country_replaces_only_the_allowed_countries(bool byDescription)
    {
        using var run = CliRun.Start();
        var trust = PublicIpTrust(
            5,
            "uk only",
            ("country", "GB", false, "UK offices"),
            ("country", "FR", false, "Paris office"),
            ("country", "RU", true, "No RU access"),
            ("ip", "198.51.100.0/24", false, "Head office"),
            ("ip", "203.0.113.0/24", true, "Known bad range"));
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(4, "uk only (old)"), ApiJson.Trust(5, "uk only")));
        run.Stub("GET", TrustPath(5), json: trust);
        run.Stub("PATCH", TrustPath(5), json: trust);

        var result = await run.RunAsync(["trust", "update", .. NameLookup.Target(byDescription, "uk only", 5), "--set-allow-country", "GB", "--set-allow-country", "IE"]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(5));
        Assert.That(patch, Has.Count.EqualTo(1));
        var expected = Set(
            "country|GB|false|UK offices",
            "country|IE|false|(none)",
            "country|RU|true|No RU access",
            "ip|198.51.100.0/24|false|Head office",
            "ip|203.0.113.0/24|true|Known bad range");
        Assert.Multiple(() =>
        {
            Assert.That(Set(JsonRead.PropertyNames(patch[0].BodyJson)), Is.EqualTo("Settings").IgnoreCase);
            Assert.That(PatchedConditions(patch[0]), Is.EqualTo(expected));
            Assert.That(run.RequestsTo("GET", TrustsPath), Has.Count.EqualTo(byDescription ? 1 : 0));
        });
    }

    [Test]
    public async Task Trust_update_matches_a_country_ignoring_case_and_keeps_its_label()
    {
        using var run = CliRun.Start();
        var trust = PublicIpTrust(5, "uk only", ("country", "GB", false, "UK offices"));
        run.Stub("GET", TrustPath(5), json: trust);
        run.Stub("PATCH", TrustPath(5), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "5", "--set-allow-country", "gb", "--set-allow-country", "ie");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(5));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.That(PatchedConditions(patch[0]), Is.EqualTo(Set("country|GB|false|UK offices", "country|IE|false|(none)")));
    }

    [Test]
    public async Task Trust_update_with_an_empty_label_removes_the_label_as_a_null_description()
    {
        using var run = CliRun.Start();
        var trust = PublicIpTrust(5, "uk only", ("country", "GB", false, "UK offices"), ("ip", "203.0.113.0/24", true, "Known bad range"));
        run.Stub("GET", TrustPath(5), json: trust);
        run.Stub("PATCH", TrustPath(5), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "5", "--set-allow-country", "GB=");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(5));
        Assert.That(patch, Has.Count.EqualTo(1));
        var conditions = JsonAssert.Property(JsonAssert.Property(patch[0].BodyJson, "Settings"), "conditions");
        Assert.Multiple(() =>
        {
            Assert.That(Conditions(conditions), Is.EqualTo(Set("country|GB|false|(none)", "ip|203.0.113.0/24|true|Known bad range")));
            Assert.That(
                conditions.EnumerateArray().Where(condition => Text(condition, "type") == "country").Select(condition => JsonAssert.Property(condition, "description").ValueKind),
                Has.All.EqualTo(JsonValueKind.Null));
        });
    }

    // Example 84. RU is not given, so it goes; the other kinds keep their labels.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Trust_update_set_block_country_replaces_only_the_blocked_countries(bool byDescription)
    {
        using var run = CliRun.Start();
        var trust = PublicIpTrust(
            9,
            "uk and ie",
            ("country", "GB", false, "UK offices"),
            ("country", "IE", false, "Dublin office"),
            ("country", "RU", true, "No RU access"),
            ("ip", "198.51.100.0/24", true, "Shared hosting"));
        run.Stub("GET", TrustsPath, json: ApiJson.Page(ApiJson.Trust(9, "uk and ie")));
        run.Stub("GET", TrustPath(9), json: trust);
        run.Stub("PATCH", TrustPath(9), json: trust);

        var result = await run.RunAsync(["trust", "update", .. NameLookup.Target(byDescription, "uk and ie", 9), "--set-block-country", "US=No US access"]);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(9));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.That(
            PatchedConditions(patch[0]),
            Is.EqualTo(Set("country|GB|false|UK offices", "country|IE|false|Dublin office", "country|US|true|No US access", "ip|198.51.100.0/24|true|Shared hosting")));
    }

    [Test]
    public async Task Trust_update_set_allow_ip_replaces_only_the_allowed_ranges_and_keeps_the_labels_of_those_that_stay()
    {
        using var run = CliRun.Start();
        var trust = PublicIpTrust(
            10,
            "office only",
            ("ip", "203.0.113.0/24", false, "Office"),
            ("ip", "192.0.2.0/24", false, "Old office"),
            ("ip", "203.0.113.66/32", true, "Guest Wi-Fi NAT"),
            ("country", "GB", false, "UK offices"));
        run.Stub("GET", TrustPath(10), json: trust);
        run.Stub("PATCH", TrustPath(10), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "10", "--set-allow-ip", "203.0.113.0/24", "--set-allow-ip", "198.51.100.0/24=Data centre");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(10));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.That(
            PatchedConditions(patch[0]),
            Is.EqualTo(Set("ip|203.0.113.0/24|false|Office", "ip|198.51.100.0/24|false|Data centre", "ip|203.0.113.66/32|true|Guest Wi-Fi NAT", "country|GB|false|UK offices")));
    }

    [Test]
    public async Task Trust_update_set_block_ip_replaces_only_the_blocked_ranges()
    {
        using var run = CliRun.Start();
        var trust = PublicIpTrust(
            10,
            "office only",
            ("ip", "203.0.113.0/24", false, "Office"),
            ("ip", "203.0.113.66/32", true, "Guest Wi-Fi NAT"),
            ("ip", "203.0.113.70/32", true, "Old printer"),
            ("country", "RU", true, "No RU access"));
        run.Stub("GET", TrustPath(10), json: trust);
        run.Stub("PATCH", TrustPath(10), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "10", "--set-block-ip", "203.0.113.66/32", "--set-block-ip", "203.0.113.67/32");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(10));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.That(
            PatchedConditions(patch[0]),
            Is.EqualTo(Set("ip|203.0.113.0/24|false|Office", "ip|203.0.113.66/32|true|Guest Wi-Fi NAT", "ip|203.0.113.67/32|true|(none)", "country|RU|true|No RU access")));
    }

    [Test]
    public async Task Trust_update_set_claim_replaces_the_claims_and_keeps_the_configuration()
    {
        using var run = CliRun.Start();
        var trust = SignInTrust(3, "entra staff", Azure(Tenant), ("groups", "engineering"));
        run.Stub("GET", TrustPath(3), json: trust);
        run.Stub("PATCH", TrustPath(3), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "3", "--set-claim", "groups=platform", "--set-claim", "roles=operator");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(3));
        Assert.That(patch, Has.Count.EqualTo(1));
        var settings = JsonAssert.Property(patch[0].BodyJson, "Settings");
        var configuration = JsonAssert.Property(settings, "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(Claims(JsonAssert.Property(settings, "conditions")), Is.EqualTo(Set("groups=platform", "roles=operator")));
            Assert.That(Set(JsonRead.PropertyNames(configuration)), Is.EqualTo(Set("authority", "tenantId")));
            Assert.That(Text(configuration, "authority"), Is.EqualTo("azure"));
            Assert.That(Text(configuration, "tenantId"), Is.EqualTo(Tenant));
        });
    }

    [Test]
    public async Task Trust_update_set_tenant_replaces_the_tenant_and_keeps_the_claims()
    {
        using var run = CliRun.Start();
        var trust = SignInTrust(3, "entra staff", Azure(Tenant), ("groups", "engineering"));
        run.Stub("GET", TrustPath(3), json: trust);
        run.Stub("PATCH", TrustPath(3), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "3", "--set-tenant", "0f6d2c41-5a8e-4b93-8c27-d1e4f9a3b605");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(3));
        Assert.That(patch, Has.Count.EqualTo(1));
        var settings = JsonAssert.Property(patch[0].BodyJson, "Settings");
        var configuration = JsonAssert.Property(settings, "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(Text(configuration, "authority"), Is.EqualTo("azure"));
            Assert.That(Text(configuration, "tenantId"), Is.EqualTo("0f6d2c41-5a8e-4b93-8c27-d1e4f9a3b605"));
            Assert.That(Claims(JsonAssert.Property(settings, "conditions")), Is.EqualTo("groups=engineering"));
        });
    }

    [Test]
    public async Task Trust_update_set_client_id_replaces_the_client_id_and_keeps_the_rest()
    {
        using var run = CliRun.Start();
        var configuration = new JsonObject { ["authority"] = "oidc", ["authorityUri"] = "https://sso.example.com", ["clientId"] = "enclave-portal", ["aud"] = "api://enclave" };
        var trust = SignInTrust(7, "sso", configuration, ("hd", "example.com"));
        run.Stub("GET", TrustPath(7), json: trust);
        run.Stub("PATCH", TrustPath(7), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "7", "--set-client-id", "enclave-cli");

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(7));
        Assert.That(patch, Has.Count.EqualTo(1));
        var settings = JsonAssert.Property(patch[0].BodyJson, "Settings");
        var patched = JsonAssert.Property(settings, "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(Set(JsonRead.PropertyNames(patched)), Is.EqualTo(Set("authority", "authorityUri", "clientId", "aud")));
            Assert.That(Text(patched, "authority"), Is.EqualTo("oidc"));
            Assert.That(Text(patched, "authorityUri"), Is.EqualTo("https://sso.example.com"));
            Assert.That(Text(patched, "clientId"), Is.EqualTo("enclave-cli"));
            Assert.That(Text(patched, "aud"), Is.EqualTo("api://enclave"));
            Assert.That(Claims(JsonAssert.Property(settings, "conditions")), Is.EqualTo("hd=example.com"));
        });
    }

    // The API refuses a blank client ID or audience and an address that is not https, and stores a
    // blank tenant (portal TrustRequirementSettingsUserAuthValidator; DictionaryValidationExtensions
    // MustHaveKeyWithValue, KeyNotEmptyIfSet). A generic authority requires an address and a client
    // ID, so "" cannot clear them. The run without a token shows the check comes first.
    [TestCase("oidc", "--set-client-id", "")]
    [TestCase("oidc", "--set-client-id", " ")]
    [TestCase("oidc", "--set-authority-uri", "")]
    [TestCase("oidc", "--set-authority-uri", " ")]
    [TestCase("oidc", "--set-audience", "  ")]
    [TestCase("azure", "--set-tenant", " \t ")]
    public async Task Trust_update_rejects_a_blank_sign_in_setting_before_the_token(string authority, string option, string blank)
    {
        using var run = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");
        var trust = authority == "azure"
            ? SignInTrust(5, "entra staff", Azure(Tenant))
            : SignInTrust(5, "sso", new JsonObject { ["authority"] = "oidc", ["authorityUri"] = "https://sso.example.com", ["clientId"] = "enclave-portal", ["aud"] = "api://enclave" });
        run.Stub("GET", TrustPath(5), json: trust);
        run.Stub("PATCH", TrustPath(5), json: trust);
        string[] rejected = ["trust", "update", "--id", "5", option, blank];
        var value = option switch
        {
            "--set-client-id" => "enclave-cli",
            "--set-authority-uri" => "https://login.example.com",
            "--set-audience" => "api://enclave-cli",
            _ => "0f6d2c41-5a8e-4b93-8c27-d1e4f9a3b605",
        };

        CliAssert.Rejected(run, await run.RunAsync(rejected));
        CliAssert.Rejected(withoutToken, await withoutToken.RunAsync(rejected));

        CliAssert.Succeeded(await run.RunAsync("trust", "update", "--id", "5", option, value));
        Assert.That(run.RequestsTo("PATCH", TrustPath(5)), Has.Count.EqualTo(1));
    }

    // Each kind has one condition here, so the patch holds the other three.
    [TestCase("--set-allow-ip", "ip|203.0.113.0/24|false|Office")]
    [TestCase("--set-block-ip", "ip|203.0.113.66/32|true|Guest Wi-Fi NAT")]
    [TestCase("--set-allow-country", "country|GB|false|UK offices")]
    [TestCase("--set-block-country", "country|RU|true|No RU access")]
    public async Task Trust_update_set_list_flag_given_an_empty_value_removes_only_the_conditions_of_its_kind(string option, string removed)
    {
        using var run = CliRun.Start();
        var trust = PublicIpTrust(
            10,
            "office only",
            ("ip", "203.0.113.0/24", false, "Office"),
            ("ip", "203.0.113.66/32", true, "Guest Wi-Fi NAT"),
            ("country", "GB", false, "UK offices"),
            ("country", "RU", true, "No RU access"));
        string[] all = ["ip|203.0.113.0/24|false|Office", "ip|203.0.113.66/32|true|Guest Wi-Fi NAT", "country|GB|false|UK offices", "country|RU|true|No RU access"];
        run.Stub("GET", TrustPath(10), json: trust);
        run.Stub("PATCH", TrustPath(10), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "10", option, string.Empty);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(10));
        Assert.That(patch, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(Set(JsonRead.PropertyNames(patch[0].BodyJson)), Is.EqualTo("Settings").IgnoreCase);
            Assert.That(PatchedConditions(patch[0]), Is.EqualTo(Set([.. all.Where(condition => condition != removed)])));
        });
    }

    [Test]
    public async Task Trust_update_set_claim_given_an_empty_value_removes_every_claim_and_keeps_the_configuration()
    {
        using var run = CliRun.Start();
        var trust = SignInTrust(3, "entra staff", Azure(Tenant), ("groups", "engineering"), ("roles", "operator"));
        run.Stub("GET", TrustPath(3), json: trust);
        run.Stub("PATCH", TrustPath(3), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "3", "--set-claim", string.Empty);

        CliAssert.Succeeded(result);
        var patch = run.RequestsTo("PATCH", TrustPath(3));
        Assert.That(patch, Has.Count.EqualTo(1));
        var settings = JsonAssert.Property(patch[0].BodyJson, "Settings");
        var configuration = JsonAssert.Property(settings, "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(settings, "conditions").ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(JsonAssert.Property(settings, "conditions").GetArrayLength(), Is.Zero);
            Assert.That(Set(JsonRead.PropertyNames(configuration)), Is.EqualTo(Set("authority", "tenantId")));
            Assert.That(Text(configuration, "authority"), Is.EqualTo("azure"));
            Assert.That(Text(configuration, "tenantId"), Is.EqualTo(Tenant));
        });
    }

    // The answer differs from the read and from the request, so output built from either does not
    // match.
    [Test]
    public async Task Trust_update_prints_the_requirement_the_api_answers_the_patch_with()
    {
        using var run = CliRun.Start();
        var read = PublicIpTrust(5, "uk only", ("country", "GB", false, "UK offices"));
        var answer = JsonNode.Parse(PublicIpTrust(5, "uk only", ("country", "GB", false, "UK offices"), ("country", "IE", false, null)))!.AsObject();
        answer["usedInPolicies"] = 3;
        answer["modified"] = "2026-03-04T10:15:00Z";
        run.Stub("GET", TrustPath(5), json: read);
        run.Stub("PATCH", TrustPath(5), json: answer.ToJsonString());

        var result = await run.RunAsync("trust", "update", "--id", "5", "--set-allow-country", "GB", "--set-allow-country", "IE");

        CliAssert.Succeeded(result);
        var output = result.StdoutJson;
        Assert.Multiple(() =>
        {
            Assert.That(run.RequestsTo("PATCH", TrustPath(5)), Has.Count.EqualTo(1));
            Assert.That(output.GetProperty("id").GetInt32(), Is.EqualTo(5));
            Assert.That(output.GetProperty("usedInPolicies").GetInt32(), Is.EqualTo(3));
            Assert.That(output.GetProperty("modified").GetString(), Does.StartWith("2026-03-04T10:15:00"));
            Assert.That(output.GetProperty("settings").GetProperty("conditions").GetArrayLength(), Is.EqualTo(2));
        });
    }

    // Without a --set- flag there are no conditions to keep, so there is no read.
    [Test]
    public async Task Trust_update_patches_only_the_description_and_notes_given_without_a_read()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TrustPath(5), json: ApiJson.Trust(5, "uk only"));

        var request = await CliAssert.AcceptedAsync(run, "PATCH", TrustPath(5), "trust", "update", "--id", "5", "--description", "uk only", "--notes", "Reviewed");

        Assert.Multiple(() =>
        {
            Assert.That(Set(JsonRead.PropertyNames(request.BodyJson)), Is.EqualTo(Set("Description", "Notes")).IgnoreCase);
            Assert.That(Text(request.BodyJson, "Description"), Is.EqualTo("uk only"));
            Assert.That(Text(request.BodyJson, "Notes"), Is.EqualTo("Reviewed"));
        });
    }

    [Test]
    public async Task Trust_update_without_a_change_flag_exits_2()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TrustPath(5), json: ApiJson.Trust(5, "uk only"));

        await CliAssert.RejectedThenAcceptedAsync(run, ["trust", "update", "--id", "5"], ["trust", "update", "--id", "5", "--notes", "Reviewed"], "PATCH", TrustPath(5));
    }

    // A new requirement replaces one whose authority changes ("Command options").
    [Test]
    public async Task Trust_update_takes_no_option_that_changes_the_authority()
    {
        using var run = CliRun.Start();
        run.Stub("PATCH", TrustPath(5), json: ApiJson.Trust(5, "entra staff"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["trust", "update", "--id", "5", "--set-authority", "google"], ["trust", "update", "--id", "5", "--notes", "Reviewed"], "PATCH", TrustPath(5));

        var described = await run.RunAsync("commands", "trust", "update");
        CliAssert.Succeeded(described);
        var options = JsonAssert.Property(JsonAssert.Property(described.StdoutJson, "commands")[0], "options")
            .EnumerateArray()
            .Select(option => JsonAssert.Property(option, "name").GetString());
        Assert.That(options, Has.Member("--set-authority-uri").And.No.Member("--set-authority"));
    }

    // The type and authority are known only once the requirement is read.
    [TestCase("public-ip", "--set-claim", "groups=engineering")]
    [TestCase("oidc", "--set-tenant", Tenant)]
    [TestCase("azure", "--set-client-id", "enclave-cli")]
    public async Task Trust_update_with_a_set_flag_the_requirement_does_not_take_exits_2_without_a_change(string kind, string option, string value)
    {
        using var run = CliRun.Start();
        var trust = kind switch
        {
            "public-ip" => PublicIpTrust(5, "uk only", ("country", "GB", false, "UK offices")),
            "oidc" => SignInTrust(5, "sso", new JsonObject { ["authority"] = "oidc", ["authorityUri"] = "https://sso.example.com", ["clientId"] = "enclave-portal" }),
            _ => SignInTrust(5, "entra staff", Azure(Tenant)),
        };
        run.Stub("GET", TrustPath(5), json: trust);
        run.Stub("PATCH", TrustPath(5), json: trust);

        var result = await run.RunAsync("trust", "update", "--id", "5", option, value);

        CliAssert.Failed(result, "invalid_argument");
        Assert.That(run.RequestsTo("PATCH", TrustPath(5)), Is.Empty);
    }

    // Flags of both types cannot apply to one requirement, whichever type it has.
    [Test]
    public async Task Trust_update_rejects_sign_in_and_public_ip_flags_together()
    {
        using var run = CliRun.Start();
        run.Stub("GET", TrustPath(5), json: PublicIpTrust(5, "uk only", ("country", "GB", false, null)));

        var result = await run.RunAsync("trust", "update", "--id", "5", "--set-claim", "groups=engineering", "--set-allow-ip", "203.0.113.0/24");

        CliAssert.Rejected(run, result, "invalid_argument");
    }

    // The read is a single-item call ("Several IDs").
    [Test]
    public async Task Trust_update_with_an_unknown_id_exits_5_without_a_change()
    {
        using var run = CliRun.Start();
        run.StubProblem("GET", TrustPath(5), 404, "Not Found", "Trust requirement 5 does not exist.");
        run.Stub("PATCH", TrustPath(5), json: ApiJson.Trust(5, "uk only"));

        var result = await run.RunAsync("trust", "update", "--id", "5", "--set-allow-country", "GB");

        CliAssert.Failed(result, "not_found");
        Assert.That(run.RequestsTo("PATCH", TrustPath(5)), Is.Empty);
    }

    private static JsonObject Azure(string tenant) => new() { ["authority"] = "azure", ["tenantId"] = tenant };

    private static string PatchedConditions(RecordedRequest patch) =>
        Conditions(JsonAssert.Property(JsonAssert.Property(patch.BodyJson, "Settings"), "conditions"));
}
