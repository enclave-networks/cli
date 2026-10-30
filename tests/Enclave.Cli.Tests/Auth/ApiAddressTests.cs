using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Auth;

// spec/cli-surface.md "Options on every command", "Login, logout and status". Each variable points
// at a second fake API, so a request sent to the run's own fake shows the variable was passed over.
public class ApiAddressTests
{
    private const string ApiUrlVariable = "ENCLAVE_API_URL";

    private const string PartnerApiUrlVariable = "ENCLAVE_PARTNER_API_URL";

    private const string FileToken = "file-token-7c31d9";

    private static readonly string OrgSystems = TestData.OrgPath("systems");

    private static readonly string Customers = TestData.PartnerPath("customers");

    private static readonly string[] OrgLookupOnly = ["GET /account/orgs"];

    // The file's baseUrl is the run's own fake, so a request there shows the file won.
    [TestCase(false)]
    [TestCase(true)]
    public async Task ENCLAVE_API_URL_takes_precedence_over_credentials_json_and_the_default(bool withFile)
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", OrgSystems, 200, ApiJson.Page());
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Environment[ApiUrlVariable] = otherUrl;

        if (withFile)
        {
            run.SaveCredentials(FileToken, run.ApiBaseUrl);
        }

        var result = await run.RunAsync("system", "list");

        var received = LoopbackApi.Received(otherApi);

        CliAssert.Succeeded(result);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The request went to the address in credentials.json or the default.");
            Assert.That(received.Select(request => request.Call), Is.EqualTo(new[] { $"GET {OrgSystems}" }));
            Assert.That(received.Select(request => request.Authorization), Is.EqualTo(new[] { $"Bearer {TestData.Token}" }));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ENCLAVE_PARTNER_API_URL_takes_precedence_over_credentials_json_and_the_default(bool withFile)
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", Customers, 200, ApiJson.Page());
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        run.Environment[PartnerApiUrlVariable] = otherUrl;

        if (withFile)
        {
            run.SaveCredentials(FileToken, partnerApiBaseUrl: run.PartnerApiBaseUrl);
        }

        var result = await run.RunAsync("partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString());

        var received = LoopbackApi.Received(otherApi);

        CliAssert.List(result, "customer");
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The request went to the address in credentials.json or the default.");
            Assert.That(received.Select(request => request.Call), Is.EqualTo(new[] { $"GET {Customers}" }));
            Assert.That(received.Select(request => request.Authorization), Is.EqualTo(new[] { $"Bearer {TestData.Token}" }));
        });
    }

    // An empty variable is what a CI job gets when its setting is missing. credentials.json points
    // at the second fake, so the first run shows the variable is read and the second that an empty
    // one leaves the address to the file.
    [Test]
    public async Task ENCLAVE_API_URL_is_used_when_set_and_passed_over_when_empty()
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", OrgSystems, 200, ApiJson.Page());
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.SaveCredentials(FileToken, otherUrl);

        run.Environment[ApiUrlVariable] = run.ApiBaseUrl;
        CliAssert.Succeeded(await run.RunAsync("system", "list"));
        var set = (Own: run.Requests.Count, Other: LoopbackApi.Received(otherApi).Count);

        run.Environment[ApiUrlVariable] = string.Empty;
        CliAssert.Succeeded(await run.RunAsync("system", "list"));
        var empty = (Own: run.Requests.Count - set.Own, Other: LoopbackApi.Received(otherApi).Count - set.Other);

        Assert.Multiple(() =>
        {
            Assert.That(set, Is.EqualTo((1, 0)), "Set: requests to the variable's address and the file's.");
            Assert.That(empty, Is.EqualTo((0, 1)), "Empty: requests to the variable's address and the file's.");
        });
    }

    [Test]
    public async Task ENCLAVE_PARTNER_API_URL_is_used_when_set_and_passed_over_when_empty()
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", Customers, 200, ApiJson.Page());
        PartnerApiFake.StubCustomerList(run, TestData.PartnerId);
        run.SaveCredentials(FileToken, partnerApiBaseUrl: otherUrl);
        string[] args = ["partner", "customer", "list", "--partner-id", TestData.PartnerId.ToString()];

        run.Environment[PartnerApiUrlVariable] = run.PartnerApiBaseUrl;
        CliAssert.List(await run.RunAsync(args), "customer");
        var set = (Own: run.Requests.Count, Other: LoopbackApi.Received(otherApi).Count);

        run.Environment[PartnerApiUrlVariable] = string.Empty;
        CliAssert.List(await run.RunAsync(args), "customer");
        var empty = (Own: run.Requests.Count - set.Own, Other: LoopbackApi.Received(otherApi).Count - set.Other);

        Assert.Multiple(() =>
        {
            Assert.That(set, Is.EqualTo((1, 0)), "Set: requests to the variable's address and the file's.");
            Assert.That(empty, Is.EqualTo((0, 1)), "Empty: requests to the variable's address and the file's.");
        });
    }

    // Enclave.Sdk.Api builds the partner API's HttpClient when the EnclaveClient is made
    // (EnclaveClient.SetupPartnerHttpClient), so either bad address fails every command.
    [TestCase(ApiUrlVariable, "not a url")]
    [TestCase(ApiUrlVariable, "api.enclave.io")]
    [TestCase(ApiUrlVariable, "/org")]
    [TestCase(ApiUrlVariable, "ftp://api.example")]
    [TestCase(PartnerApiUrlVariable, "not a url")]
    [TestCase(PartnerApiUrlVariable, "ftp://partner-api.example")]
    public async Task An_address_that_is_not_an_absolute_http_or_https_url_exits_2_naming_the_variable(string variable, string value)
    {
        using var run = CliRun.Start();
        run.Stub("GET", OrgSystems, json: ApiJson.Page());
        run.Environment[variable] = value;

        var result = await run.RunAsync("system", "list");

        CliAssert.Rejected(run, result);
        Assert.That(JsonAssert.Property(result.Error, "detail").GetString(), Does.Contain(variable));
    }

    // The file's baseUrl is the run's own fake, so a check or a saved address there shows the file
    // won.
    [Test]
    public async Task Login_checks_the_token_at_ENCLAVE_API_URL_and_saves_it_as_the_base_url()
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        LoopbackApi.Stub(otherApi, "GET", "/account/orgs", 200, OneOrg());
        run.Stub("GET", "/account/orgs", json: OneOrg());
        run.SaveCredentials(FileToken, run.ApiBaseUrl);
        run.Environment[ApiUrlVariable] = otherUrl;

        var result = await run.RunAsync("login");

        var checks = LoopbackApi.Received(otherApi).Select(request => request.Call).ToArray();

        CliAssert.Succeeded(result);
        var credentials = SavedCredentials(run);
        Assert.Multiple(() =>
        {
            Assert.That(run.Requests, Is.Empty, "The token was checked at the address in credentials.json.");
            Assert.That(checks, Is.EqualTo(OrgLookupOnly));
            Assert.That(new Uri(JsonAssert.Property(credentials, "baseUrl").GetString()!), Is.EqualTo(new Uri(otherUrl)));
            Assert.That(JsonAssert.Property(credentials, "personalAccessToken").GetString(), Is.EqualTo(TestData.Token));
        });
    }

    // login makes no partner API call, so the second fake receives nothing.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Login_saves_ENCLAVE_PARTNER_API_URL_as_the_partner_api_base_url(bool withFile)
    {
        using var run = CliRun.Start();
        var (other, otherUrl) = LoopbackApi.Start();
        using var otherApi = other;
        run.Stub("GET", "/account/orgs", json: OneOrg());
        run.Environment[PartnerApiUrlVariable] = otherUrl;

        if (withFile)
        {
            run.SaveCredentials(FileToken, partnerApiBaseUrl: run.PartnerApiBaseUrl);
        }

        var result = await run.RunAsync("login");

        CliAssert.Succeeded(result);
        var credentials = SavedCredentials(run);
        Assert.Multiple(() =>
        {
            Assert.That(run.Calls(), Is.EqualTo(OrgLookupOnly));
            Assert.That(LoopbackApi.Received(otherApi), Is.Empty);
            Assert.That(new Uri(JsonAssert.Property(credentials, "partnerApiBaseUrl").GetString()!), Is.EqualTo(new Uri(otherUrl)));
            Assert.That(new Uri(JsonAssert.Property(credentials, "baseUrl").GetString()!), Is.EqualTo(run.ApiUrl));
        });
    }

    private static string OneOrg() => ApiJson.Orgs((TestData.OrgId, TestData.OrgName));

    private static JsonElement SavedCredentials(CliRun run) =>
        JsonRead.Parse(run.Files.ReadText(run.CredentialsPath) ?? throw new AssertionException("login wrote no credentials.json."));
}
