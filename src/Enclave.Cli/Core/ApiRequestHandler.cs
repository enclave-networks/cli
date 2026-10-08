using System.Diagnostics.CodeAnalysis;

namespace Enclave.Cli.Core;

/// <summary>
/// The handler every request of an Enclave.Sdk.Api client goes through
/// (EnclaveClientOptions.HttpMessageHandler). Under --verbose it logs each request's method and URL;
/// under --dry-run it sends the reads and captures each change in place of sending it.
/// </summary>
// Enclave.Sdk.Api puts this handler beneath its problem details handler, so it sees each request
// with the Authorization header set (EnclaveClient.SetupHttpClient, Enclave.Sdk.Api 1.1.0). It logs
// and captures the method, URL and body only, so the token stays out of every diagnostic and of the
// dry-run output.
internal sealed class ApiRequestHandler : DelegatingHandler
{
    private readonly CliOutput _output;

    private readonly DryRun? _dryRun;

    // HttpClientHandler is the handler Enclave.Sdk.Api sends through when given none
    // (ProblemDetailsHttpMessageHandler, Enclave.Sdk.Api 1.1.0), so requests reach the network as
    // they would without this one. DelegatingHandler disposes its inner handler when it is disposed
    // (dotnet/runtime src/libraries/System.Net.Http/src/System/Net/Http/DelegatingHandler.cs,
    // Dispose(bool), .NET 10), and CliContext disposes this handler after the command. CA2000 does
    // not follow ownership through a base constructor, so it is suppressed here.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "DelegatingHandler disposes its inner handler.")]
    public ApiRequestHandler(CliOutput output, DryRun? dryRun)
        : base(new HttpClientHandler())
    {
        _output = output;
        _dryRun = dryRun;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var url = request.RequestUri?.AbsoluteUri;

        // The reads a change depends on run under --dry-run: name lookups, the reads that keep tags,
        // labels and conditions, and the read tag set makes to choose between update and create
        // (proposed-cli-surface.md "Dry run"). Every Enclave.Sdk.Api read is a GET, and every change
        // a POST, PUT, PATCH or DELETE.
        if (_dryRun is not null && request.Method != HttpMethod.Get)
        {
            _output.Verbose($"{request.Method} {url} (not sent: --dry-run)");
            await _dryRun.CaptureAsync(request, cancellationToken);
            return DryRun.Answer(request);
        }

        _output.Verbose($"{request.Method} {url}");
        return await base.SendAsync(request, cancellationToken);
    }
}
