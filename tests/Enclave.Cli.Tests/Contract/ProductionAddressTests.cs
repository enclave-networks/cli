using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// Outside tests the CLI calls production: the Enclave API at https://api.enclave.io, and the partner
// API at the address Enclave.Sdk.Api gives when the CLI passes none (proposed-cli-surface.md "Partner
// API"). Every other test points both at fakes, so these tests are the ones that check the
// addresses a released CLI uses.
public class ProductionAddressTests
{
    // Enclave.Sdk.Api 1.2.0's production addresses (src/Enclave.Sdk.Api/Constants.cs:27-29, ApiUrl
    // and PartnerApiUrl, which the EnclaveClientOptions constructor sets; EnclaveClient.cs:185
    // names the partner one).
    private const string ProductionApi = "https://api.enclave.io";

    private const string ProductionPartnerApi = "https://partner-api.enclave.io";

    private static readonly string[] PostOnly = ["POST"];

    // FromProcess replaces Console.Out and Console.Error when they are redirected, as they are under
    // the test host, so the test puts the test host's writers back. The host's environment, home
    // directory, files and stdin are the process's own, so the test reads only the two addresses
    // and the network the host sends through, which must be the network itself.
    [Test]
    public void The_host_outside_tests_calls_the_production_api_and_leaves_the_partner_api_to_Enclave_Sdk_Api()
    {
        var stdout = Console.Out;
        var stderr = Console.Error;
        CliHost host;

        try
        {
            host = CliHost.FromProcess();
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }

        Assert.Multiple(() =>
        {
            Assert.That(host.DefaultApiUrl, Is.EqualTo(new Uri(ProductionApi)));
            Assert.That(host.DefaultPartnerApiUrl, Is.Null);
            Assert.That(host.CreateNetworkHandler, Is.Null);
        });
    }

    // With no partner API address from the host or credentials.json, the CLI passes none, and
    // Enclave.Sdk.Api's default decides where partner calls go. --dry-run prints the URL the call
    // would go to and sends nothing, and create makes no read. The network beneath the CLI's
    // handler fails every request, so a read added to create would fail the test without reaching
    // the live partner API, and it records none.
    [Test]
    public async Task A_partner_command_with_no_partner_api_address_given_goes_to_the_production_partner_api()
    {
        using var run = PartnerApiFake.StartWithPartnerOnly();
        var network = new FailingNetwork(() => new HttpRequestException("A test sends nothing to the network."));
        run.DefaultPartnerApiUrl = null;
        run.NetworkHandler = network.CreateHandler;

        var result = await run.RunAsync("partner", "customer", "create", TestData.NewCustomerName, "--dry-run");

        CliAssert.Succeeded(result);
        var requests = JsonAssert.Property(result.StdoutJson, "requests");
        Assert.That(requests.ValueKind, Is.EqualTo(JsonValueKind.Array), result.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(requests.EnumerateArray().Select(request => request.GetProperty("method").GetString()), Is.EqualTo(PostOnly));
            Assert.That(requests.EnumerateArray().Select(request => request.GetProperty("url").GetString()), Is.EqualTo(new[] { ProductionPartnerApi + TestData.PartnerPath("customers") }));
            Assert.That(network.Calls, Is.Empty);
            Assert.That(run.Requests, Is.Empty);
        });
    }
}
