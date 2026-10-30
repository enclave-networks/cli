using System.Diagnostics.CodeAnalysis;

namespace Enclave.Cli.Core;

// Logs each request as a diagnostic; under --dry-run sends the reads and captures each change in
// place of sending it.
// It sits beneath Enclave.Sdk.Api's problem details handler, so it sees the Authorization header
// (EnclaveClient.SetupHttpClient). It logs and captures only the method, URL and body, which keeps
// the token out of diagnostics and dry-run output.
internal sealed class ApiRequestHandler : DelegatingHandler
{
    private readonly CliOutput _output;

    private readonly DryRun? _dryRun;

    // HttpClientHandler is what Enclave.Sdk.Api sends through when given no handler
    // (ProblemDetailsHttpMessageHandler). CA2000 does not follow ownership through a base
    // constructor: DelegatingHandler disposes its inner handler (dotnet/runtime release/10.0,
    // DelegatingHandler.Dispose), and CliContext disposes this one.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "DelegatingHandler disposes its inner handler.")]
    public ApiRequestHandler(CliOutput output, DryRun? dryRun, HttpMessageHandler? network = null)
        : base(network ?? new HttpClientHandler())
    {
        _output = output;
        _dryRun = dryRun;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var url = request.RequestUri?.AbsoluteUri;

        // The reads a change depends on run under --dry-run (spec/cli-surface.md "Dry run"). Every
        // Enclave.Sdk.Api read is a GET.
        if (_dryRun is not null && request.Method != HttpMethod.Get)
        {
            _output.Diagnostic($"{request.Method} {url} (not sent: --dry-run)");
            await _dryRun.CaptureAsync(request, cancellationToken);
            return DryRun.Answer(request);
        }

        _output.Diagnostic($"{request.Method} {url}");
        return await base.SendAsync(request, cancellationToken);
    }
}
