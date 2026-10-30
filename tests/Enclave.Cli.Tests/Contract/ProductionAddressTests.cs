using System.Text.Json;
using Enclave.Cli.Tests.Support;
using NUnit.Framework;

namespace Enclave.Cli.Tests.Contract;

// Every other test points both APIs at fakes, so these are the tests that check the addresses a
// released CLI uses (spec/cli-surface.md "Partner API").
public class ProductionAddressTests
{
    // Enclave.Sdk.Api Constants.ApiUrl and Constants.PartnerApiUrl.
    private const string ProductionApi = "https://api.enclave.io";

    private const string ProductionPartnerApi = "https://partner-api.enclave.io";

    private static readonly string[] PostOnly = ["POST"];

    // FromProcess replaces Console.Out and Console.Error when they are redirected, as under the test
    // host, so the test puts them back. The host's environment, home directory, files and stdin are
    // the process's own, so the test reads only the addresses and the network handler.
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

    // --dry-run prints the URL without sending, and create makes no read. The network beneath the
    // CLI's handler fails every request, so a read added to create would fail the test without
    // reaching the live partner API.
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
