using WireMock.Server;

namespace Enclave.Cli.Tests.Support;

// Fake staging API and partner API on loopback, given to a run as staging's addresses
// (CliRun.StagingApiUrl, StagingPartnerApiUrl), for a test that sends a token to staging.
internal sealed class StagingFakes : IDisposable
{
    private StagingFakes(CliRun run)
    {
        (Api, var apiUrl) = LoopbackApi.Start();
        (PartnerApi, var partnerApiUrl) = LoopbackApi.Start();
        run.StagingApiUrl = new Uri(apiUrl);
        run.StagingPartnerApiUrl = new Uri(partnerApiUrl);
    }

    public WireMockServer Api { get; }

    public WireMockServer PartnerApi { get; }

    // Both fakes' requests, the API's first.
    public IReadOnlyList<RecordedRequest> Requests => [.. LoopbackApi.Received(Api), .. LoopbackApi.Received(PartnerApi)];

    public static StagingFakes Start(CliRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new StagingFakes(run);
    }

    public void Dispose()
    {
        Api.Stop();
        Api.Dispose();
        PartnerApi.Stop();
        PartnerApi.Dispose();
    }
}
