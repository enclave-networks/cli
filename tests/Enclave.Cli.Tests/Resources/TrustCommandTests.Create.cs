using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;
using static Enclave.Cli.Tests.Support.FieldText;

namespace Enclave.Cli.Tests.Resources;

// The configuration keys are the API's: authority, tenantId, authorityUri, clientId and aud (portal
// UserAuthenticationConstants).
public partial class TrustCommandTests
{
    private const string Tenant = "9b1c3a52-7f0e-4d8a-b0a4-2c6e1d5f8a31";

    // The fields of the API's TrustRequirementCreateModel and TrustRequirementSettingsModel.
    private static readonly string[] TrustCreateFields = ["description", "type", "notes", "settings"];

    private static readonly string[] TrustSettingsFields = ["configuration", "conditions"];

    // Example 26. The API allows only authority and tenantId for azure, and { claim, value } for a
    // claim (portal TrustRequirementSettingsUserAuthValidator).
    [Test]
    public async Task Trust_create_with_the_azure_authority_sends_a_sign_in_requirement_with_its_tenant_and_claim()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(3, "entra staff"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", TrustsPath, "trust", "create", "entra staff", "--authority", "azure", "--tenant", Tenant, "--claim", "groups=4e2d8c1a-0b7f-4c39-9a65-1f3e7d2b6c84");

        var body = request.BodyJson;
        var settings = JsonAssert.Property(body, "settings");
        var configuration = JsonAssert.Property(settings, "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(Text(body, "description"), Is.EqualTo("entra staff"));
            Assert.That(Text(body, "type"), Is.EqualTo("UserAuthentication"));
            Assert.That(Set(JsonRead.PropertyNames(configuration)), Is.EqualTo(Set("authority", "tenantId")));
            Assert.That(Text(configuration, "authority"), Is.EqualTo("azure"));
            Assert.That(Text(configuration, "tenantId"), Is.EqualTo(Tenant));
            Assert.That(Claims(JsonAssert.Property(settings, "conditions")), Is.EqualTo("groups=4e2d8c1a-0b7f-4c39-9a65-1f3e7d2b6c84"));
        });
    }

    [Test]
    public async Task Trust_create_prints_the_requirement_the_api_created()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "portal login"));

        var result = await run.RunAsync("trust", "create", "portal login", "--authority", "portal");

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(JsonAssert.Property(result.StdoutJson, "id").GetInt32(), Is.EqualTo(6));
            Assert.That(JsonAssert.Property(result.StdoutJson, "description").GetString(), Is.EqualTo("portal login"));
        });
    }

    // Example 56 is the portal case. --claim left out is sent as an empty list of conditions
    // ("Details").
    [TestCase("portal")]
    [TestCase("google")]
    public async Task Trust_create_with_an_authority_that_takes_no_settings_sends_the_authority_alone(string authority)
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "sign in"));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "sign in", "--authority", authority);

        var body = request.BodyJson;
        var settings = body.GetProperty("settings");
        var configuration = settings.GetProperty("configuration");
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(TrustCreateFields));
            Assert.That(JsonRead.PropertyNames(settings), Is.EquivalentTo(TrustSettingsFields));
            Assert.That(body.GetProperty("type").GetString(), Is.EqualTo("UserAuthentication"));
            Assert.That(body.GetProperty("description").GetString(), Is.EqualTo("sign in"));
            Assert.That(body.GetProperty("notes").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(Set(JsonRead.PropertyNames(configuration)), Is.EqualTo("authority"));
            Assert.That(configuration.GetProperty("authority").GetString(), Is.EqualTo(authority));
            Assert.That(settings.GetProperty("conditions").GetRawText(), Is.EqualTo("[]"));
        });
    }

    // Example 57.
    [Test]
    public async Task Trust_create_with_the_oidc_authority_sends_the_authority_uri_client_id_and_claim()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(7, "sso"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", TrustsPath, "trust", "create", "sso", "--authority", "oidc", "--authority-uri", "https://sso.example.com", "--client-id", "enclave-portal", "--claim", "hd=example.com");

        var settings = JsonAssert.Property(request.BodyJson, "settings");
        var configuration = JsonAssert.Property(settings, "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(Text(request.BodyJson, "type"), Is.EqualTo("UserAuthentication"));
            Assert.That(Set(JsonRead.PropertyNames(configuration)), Is.EqualTo(Set("authority", "authorityUri", "clientId")));
            Assert.That(Text(configuration, "authority"), Is.EqualTo("oidc"));
            Assert.That(Text(configuration, "authorityUri"), Is.EqualTo("https://sso.example.com"));
            Assert.That(Text(configuration, "clientId"), Is.EqualTo("enclave-portal"));
            Assert.That(Claims(JsonAssert.Property(settings, "conditions")), Is.EqualTo("hd=example.com"));
        });
    }

    // The API reads the audience as aud (portal UserAuthenticationConstants.AudienceKey).
    [TestCase("okta")]
    [TestCase("jumpcloud")]
    [TestCase("duo")]
    [TestCase("oidc")]
    public async Task Trust_create_with_a_generic_authority_sends_its_address_client_id_and_audience(string authority)
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(8, "idp staff"));

        var request = await CliAssert.AcceptedAsync(
            run,
            "POST",
            TrustsPath,
            "trust",
            "create",
            "idp staff",
            "--authority",
            authority,
            "--authority-uri",
            "https://idp.example.com/oauth2",
            "--client-id",
            "0oa1b2c3d4",
            "--audience",
            "api://enclave");

        var configuration = JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(Set(JsonRead.PropertyNames(configuration)), Is.EqualTo(Set("authority", "authorityUri", "clientId", "aud")));
            Assert.That(Text(configuration, "authority"), Is.EqualTo(authority));
            Assert.That(Text(configuration, "authorityUri"), Is.EqualTo("https://idp.example.com/oauth2"));
            Assert.That(Text(configuration, "clientId"), Is.EqualTo("0oa1b2c3d4"));
            Assert.That(Text(configuration, "aud"), Is.EqualTo("api://enclave"));
        });
    }

    // The API requires both for a generic authority (portal TrustRequirementSettingsUserAuthValidator).
    [TestCase("--client-id", "enclave-portal")]
    [TestCase("--authority-uri", "https://sso.example.com")]
    [TestCase(null, null)]
    public async Task Trust_create_with_the_oidc_authority_requires_an_authority_uri_and_a_client_id(string? option, string? value)
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(7, "sso"));
        string[] rejected = option is null ? ["trust", "create", "sso", "--authority", "oidc"] : ["trust", "create", "sso", "--authority", "oidc", option, value!];

        await CliAssert.RejectedThenAcceptedAsync(
            run, rejected, ["trust", "create", "sso", "--authority", "oidc", "--authority-uri", "https://sso.example.com", "--client-id", "enclave-portal"], "POST", TrustsPath);
    }

    // The API requires https (portal TrustRequirementSettingsUserAuthValidator, KeyMustBeHttpsUri).
    [Test]
    public async Task Trust_create_rejects_an_authority_uri_that_is_not_https()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(7, "sso"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["trust", "create", "sso", "--authority", "oidc", "--authority-uri", "http://sso.example.com", "--client-id", "enclave-portal"],
            ["trust", "create", "sso", "--authority", "oidc", "--authority-uri", "https://sso.example.com", "--client-id", "enclave-portal"],
            "POST",
            TrustsPath);
    }

    // The API refuses a blank client ID or audience and an address that is not https, and stores a
    // blank tenant (portal TrustRequirementSettingsUserAuthValidator; DictionaryValidationExtensions
    // MustHaveKeyWithValue, KeyNotEmptyIfSet). The run without a token shows the check comes first.
    [TestCase("oidc", "--authority-uri", "")]
    [TestCase("oidc", "--authority-uri", " ")]
    [TestCase("oidc", "--client-id", "")]
    [TestCase("oidc", "--client-id", " ")]
    [TestCase("oidc", "--audience", "")]
    [TestCase("oidc", "--audience", " \t ")]
    [TestCase("azure", "--tenant", "")]
    [TestCase("azure", "--tenant", "  ")]
    public async Task Trust_create_rejects_an_empty_or_blank_sign_in_setting_before_the_token(string authority, string option, string blank)
    {
        using var run = CliRun.Start();
        using var withoutToken = CliRun.Start();
        withoutToken.Environment.Remove("ENCLAVE_TOKEN");
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(7, "sso"));
        string[] rejected = ["trust", "create", "sso", "--authority", authority, .. SignInSettings(authority, option, blank)];

        await CliAssert.RejectedThenAcceptedAsync(run, rejected, ["trust", "create", "sso", "--authority", authority, .. SignInSettings(authority, null, blank)], "POST", TrustsPath);

        CliAssert.Rejected(withoutToken, await withoutToken.RunAsync(rejected));
    }

    // The API checks each condition on its own and accepts a repeat (portal
    // TrustRequirementSettingsPublicIpValidator), so the CLI sends both.
    [TestCase("--allow-ip")]
    [TestCase("--block-ip")]
    [TestCase("--allow-country")]
    [TestCase("--block-country")]
    public async Task Trust_create_sends_a_repeated_range_or_country_as_given(string option)
    {
        ArgumentNullException.ThrowIfNull(option);
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(5, "uk only"));
        var value = option.EndsWith("-ip", StringComparison.Ordinal) ? "203.0.113.0/24" : "GB";

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "uk only", option, value, option, value);

        Assert.That(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions").GetArrayLength(), Is.EqualTo(2));
    }

    // The API refuses such a setting for azure and portal only (portal
    // TrustRequirementSettingsUserAuthValidator, AllowedKeys), so the google case shows the CLI
    // refuses one the API would store.
    [TestCase("portal", "--tenant", Tenant)]
    [TestCase("google", "--client-id", "enclave-portal")]
    [TestCase("azure", "--client-id", "enclave-portal")]
    [TestCase("azure", "--audience", "api://enclave")]
    public async Task Trust_create_rejects_a_setting_the_authority_does_not_take(string authority, string option, string value)
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "sign in"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["trust", "create", "sign in", "--authority", authority, option, value], ["trust", "create", "sign in", "--authority", authority], "POST", TrustsPath);
    }

    [Test]
    public async Task Trust_create_rejects_a_tenant_on_a_generic_authority()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(7, "sso"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["trust", "create", "sso", "--authority", "oidc", "--authority-uri", "https://sso.example.com", "--client-id", "enclave-portal", "--tenant", Tenant],
            ["trust", "create", "sso", "--authority", "oidc", "--authority-uri", "https://sso.example.com", "--client-id", "enclave-portal"],
            "POST",
            TrustsPath);
    }

    [Test]
    public async Task Trust_create_sends_every_claim_given()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(3, "entra admins"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", TrustsPath, "trust", "create", "entra admins", "--authority", "azure", "--tenant", Tenant, "--claim", "groups=admins", "--claim", "roles=operator");

        Assert.That(Claims(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions")), Is.EqualTo(Set("groups=admins", "roles=operator")));
    }

    // The API knows no other authority (portal TrustRequirementSettingsUserAuthValidator,
    // _knownAuthorities).
    [Test]
    public async Task Trust_create_rejects_an_authority_outside_the_allowed_values()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "sign in"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["trust", "create", "sign in", "--authority", "ldap"], ["trust", "create", "sign in", "--authority", "google"], "POST", TrustsPath);
    }

    // Example 27. isBlocked is sent on every condition, since the check skips an IP condition without
    // it (services PublicIpValidator.GetIpRanges).
    [Test]
    public async Task Trust_create_with_allow_and_block_flags_sends_a_public_ip_requirement_with_isblocked_on_every_condition()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: PublicIpTrust(5, "uk only", ("country", "GB", false, null), ("ip", "203.0.113.0/24", true, null)));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "uk only", "--allow-country", "GB", "--block-ip", "203.0.113.0/24");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(JsonRead.PropertyNames(body), Is.EquivalentTo(TrustCreateFields));
            Assert.That(JsonRead.PropertyNames(body.GetProperty("settings")), Is.EquivalentTo(TrustSettingsFields));
            Assert.That(Text(body, "description"), Is.EqualTo("uk only"));
            Assert.That(Text(body, "type"), Is.EqualTo("PublicIp"));
            Assert.That(body.GetProperty("settings").GetProperty("configuration").GetRawText(), Is.EqualTo("{}"));
            Assert.That(Conditions(JsonAssert.Property(JsonAssert.Property(body, "settings"), "conditions")), Is.EqualTo(Set("country|GB|false|(none)", "ip|203.0.113.0/24|true|(none)")));
        });
    }

    [Test]
    public async Task Trust_create_takes_a_country_code_in_either_case()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(5, "uk only"));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "uk only", "--allow-country", "gb");

        Assert.That(Conditions(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions")), Is.EqualTo("country|GB|false|(none)"));
    }

    // Example 82.
    [Test]
    public async Task Trust_create_sends_each_ip_label_as_the_condition_description()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(10, "office only"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", TrustsPath, "trust", "create", "office only", "--allow-ip", "203.0.113.0/24=Office", "--block-ip", "203.0.113.66/32=Guest Wi-Fi NAT");

        Assert.That(
            Conditions(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions")),
            Is.EqualTo(Set("ip|203.0.113.0/24|false|Office", "ip|203.0.113.66/32|true|Guest Wi-Fi NAT")));
    }

    // Example 83. Country flags repeat in place of a comma list, so a label can hold a comma.
    [Test]
    public async Task Trust_create_sends_each_country_flag_as_one_labelled_condition()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(9, "uk and ie"));

        var request = await CliAssert.AcceptedAsync(
            run,
            "POST",
            TrustsPath,
            "trust",
            "create",
            "uk and ie",
            "--allow-country",
            "GB=UK offices, London and Leeds",
            "--allow-country",
            "IE=Dublin office",
            "--block-ip",
            "198.51.100.0/24=Shared hosting");

        Assert.That(
            Conditions(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions")),
            Is.EqualTo(Set("country|GB|false|UK offices, London and Leeds", "country|IE|false|Dublin office", "ip|198.51.100.0/24|true|Shared hosting")));
    }

    [Test]
    public async Task Trust_create_sends_an_empty_label_as_a_null_description()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(11, "no us"));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "no us", "--block-country", "US=");

        var conditions = JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions");
        Assert.Multiple(() =>
        {
            Assert.That(Conditions(conditions), Is.EqualTo("country|US|true|(none)"));
            Assert.That(conditions.EnumerateArray().Select(condition => JsonAssert.Property(condition, "description").ValueKind), Has.All.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task Trust_create_sends_a_blocked_country_as_a_blocked_country_condition()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(11, "no us"));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "no us", "--block-country", "US=No US access");

        Assert.That(Conditions(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions")), Is.EqualTo("country|US|true|No US access"));
    }

    [Test]
    public async Task Trust_create_sends_the_notes_given()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "portal login"));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "portal login", "--authority", "portal", "--notes", "Required for admins, see ticket 88");

        Assert.That(Text(request.BodyJson, "notes"), Is.EqualTo("Required for admins, see ticket 88"));
    }

    // The type comes from the flags, and a requirement has one type.
    [Test]
    public async Task Trust_create_rejects_sign_in_and_public_ip_flags_together()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(12, "office staff"));

        await CliAssert.RejectedThenAcceptedAsync(
            run,
            ["trust", "create", "office staff", "--authority", "portal", "--allow-ip", "203.0.113.0/24"],
            ["trust", "create", "office staff", "--allow-ip", "203.0.113.0/24"],
            "POST",
            TrustsPath);
    }

    // With neither kind of flag there is no type to create, and the API requires one (portal
    // TrustRequirementCreateModel.Type).
    [Test]
    public async Task Trust_create_without_a_sign_in_or_public_ip_flag_exits_2()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "portal login"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["trust", "create", "portal login"], ["trust", "create", "portal login", "--authority", "portal"], "POST", TrustsPath);
    }

    // The settings an authority takes, each with a value the API accepts, and the one named by
    // blanked given the blank value.
    private static string[] SignInSettings(string authority, string? blanked, string blank)
    {
        (string Option, string Value)[] settings = authority == "azure"
            ? [("--tenant", Tenant)]
            : [("--authority-uri", "https://sso.example.com"), ("--client-id", "enclave-portal"), ("--audience", "api://enclave")];

        return [.. settings.SelectMany(setting => new[] { setting.Option, setting.Option == blanked ? blank : setting.Value })];
    }
}
