using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Resources;

// trust create <description> (proposed-cli-surface.md "Command options"). The body is the API's
// TrustRequirementCreateModel: description, type, notes, and settings holding a configuration and
// a list of conditions, every value a string (portal TrustRequirementSettingsModel, and portal-spa
// redux/sagas/business/trust-requirements/createTrustRequirement.ts sends the same four fields).
// The type comes from the flags: --authority makes a sign-in requirement (UserAuthentication), the
// --allow-* and --block-* flags a public IP one (PublicIp). The configuration keys are the API's:
// authority, tenantId, authorityUri, clientId and aud (portal UserAuthenticationConstants.cs;
// portal-spa appConstants/trustRequirements.ts:73-75).

/// <summary>
/// Tests for trust create.
/// </summary>
public partial class TrustCommandTests
{
    private const string Tenant = "9b1c3a52-7f0e-4d8a-b0a4-2c6e1d5f8a31";

    // Example 26. azure takes --tenant, and its configuration holds the authority and tenantId only
    // (portal TrustRequirementSettingsUserAuthValidator.cs:35-38); a claim condition is
    // { claim, value } (same file, 55).
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

    // Example 56 is the portal case. portal and google take no settings ("Command options"), so the
    // configuration holds the authority alone (portal TrustRequirementSettingsUserAuthValidator.cs:40-43
    // for portal).
    [TestCase("portal")]
    [TestCase("google")]
    public async Task Trust_create_with_an_authority_that_takes_no_settings_sends_the_authority_alone(string authority)
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "sign in"));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "sign in", "--authority", authority);

        var settings = JsonAssert.Property(request.BodyJson, "settings");
        var configuration = JsonAssert.Property(settings, "configuration");
        Assert.Multiple(() =>
        {
            Assert.That(Text(request.BodyJson, "type"), Is.EqualTo("UserAuthentication"));
            Assert.That(Set(JsonRead.PropertyNames(configuration)), Is.EqualTo("authority"));
            Assert.That(Text(configuration, "authority"), Is.EqualTo(authority));
            Assert.That(Text(settings, "conditions"), Is.EqualTo(None).Or.EqualTo("json:[]"));
        });
    }

    // Example 57. A generic OIDC provider requires its https address and a client ID (portal
    // TrustRequirementSettingsUserAuthValidator.cs:45-48), sent as authorityUri and clientId.
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

    // okta, jumpcloud, duo and oidc take --audience, which the API reads as aud (portal
    // UserAuthenticationConstants.AudienceKey; TrustRequirementSettingsUserAuthValidator.cs:49).
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

    // A generic authority requires --authority-uri and --client-id (portal
    // TrustRequirementSettingsUserAuthValidator.cs:47-48; "Command options"). Each case gives one,
    // or neither.
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

    // The authority's address must be https (portal TrustRequirementSettingsUserAuthValidator.cs:47,
    // KeyMustBeHttpsUri; "Command options").
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

    // Each authority takes only its own settings, as the API does, and a setting the authority does
    // not take exits 2 ("Command options"; portal TrustRequirementSettingsUserAuthValidator.cs:34-48).
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

    // --claim repeats, and a token must carry every claim given.
    [Test]
    public async Task Trust_create_sends_every_claim_given()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(3, "entra admins"));

        var request = await CliAssert.AcceptedAsync(
            run, "POST", TrustsPath, "trust", "create", "entra admins", "--authority", "azure", "--tenant", Tenant, "--claim", "groups=admins", "--claim", "roles=operator");

        Assert.That(Claims(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions")), Is.EqualTo(Set("groups=admins", "roles=operator")));
    }

    // --authority takes portal, azure, google, okta, jumpcloud, duo or oidc ("Command options"),
    // the authorities the API knows (portal TrustRequirementSettingsUserAuthValidator.cs:13-26).
    [Test]
    public async Task Trust_create_rejects_an_authority_outside_the_allowed_values()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(6, "sign in"));

        await CliAssert.RejectedThenAcceptedAsync(
            run, ["trust", "create", "sign in", "--authority", "ldap"], ["trust", "create", "sign in", "--authority", "google"], "POST", TrustsPath);
    }

    // Example 27. Each condition is { type, value, isBlocked, description } with type ip or country
    // (portal TrustRequirementSettingsPublicIpValidator.cs:27). isBlocked is sent on every
    // condition, since the check skips an IP condition without it (services
    // Enclave.Discover/TrustValidators/PublicIpValidator.cs:163-167).
    [Test]
    public async Task Trust_create_with_allow_and_block_flags_sends_a_public_ip_requirement_with_isblocked_on_every_condition()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: PublicIpTrust(5, "uk only", ("country", "GB", false, null), ("ip", "203.0.113.0/24", true, null)));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "uk only", "--allow-country", "GB", "--block-ip", "203.0.113.0/24");

        var body = request.BodyJson;
        Assert.Multiple(() =>
        {
            Assert.That(Text(body, "description"), Is.EqualTo("uk only"));
            Assert.That(Text(body, "type"), Is.EqualTo("PublicIp"));
            Assert.That(Conditions(JsonAssert.Property(JsonAssert.Property(body, "settings"), "conditions")), Is.EqualTo(Set("country|GB|false|(none)", "ip|203.0.113.0/24|true|(none)")));
        });
    }

    // Country codes are matched ignoring case ("Command options"), so gb is the United Kingdom.
    [Test]
    public async Task Trust_create_takes_a_country_code_in_either_case()
    {
        using var run = CliRun.Start();
        run.Stub("POST", TrustsPath, json: ApiJson.Trust(5, "uk only"));

        var request = await CliAssert.AcceptedAsync(run, "POST", TrustsPath, "trust", "create", "uk only", "--allow-country", "gb");

        Assert.That(Conditions(JsonAssert.Property(JsonAssert.Property(request.BodyJson, "settings"), "conditions")), Is.EqualTo("country|GB|false|(none)"));
    }

    // Example 82. The label after "=" is the condition's description ("Command options").
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

    // Example 83, with a comma in one label. Country flags repeat, one country each, never a comma
    // list, since a label can hold a comma ("Command options").
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

    // An empty label is sent as a null description ("Details").
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

    // The type comes from the flags, and a requirement is either a sign-in or a public IP one, so
    // mixing the two exits 2 ("Command options").
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
}
