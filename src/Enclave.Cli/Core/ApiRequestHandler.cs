using System.Diagnostics.CodeAnalysis;

namespace Enclave.Cli.Core;

// It sits beneath Enclave.Sdk.Api's problem details handler, so it sees the Authorization header
// (EnclaveClient.SetupHttpClient), and takes only the method, URL and body to keep the token out of
// the trace and the dry-run output.
internal sealed class ApiRequestHandler : DelegatingHandler
{
    private readonly CliTrace _trace;

    private readonly DryRun? _dryRun;

    // HttpClientHandler is what Enclave.Sdk.Api sends through when given no handler
    // (ProblemDetailsHttpMessageHandler). CA2000 does not follow ownership through a base
    // constructor: DelegatingHandler disposes its inner handler (dotnet/runtime release/10.0,
    // DelegatingHandler.Dispose), and CliContext disposes this one.
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "DelegatingHandler disposes its inner handler.")]
    public ApiRequestHandler(CliTrace trace, DryRun? dryRun, HttpMessageHandler? network = null)
        : base(network ?? new HttpClientHandler())
    {
        _trace = trace;
        _dryRun = dryRun;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The reads a change depends on run under --dry-run (spec/cli-surface.md "Dry run"). Every
        // Enclave.Sdk.Api read is a GET.
        if (_dryRun is not null && request.Method != HttpMethod.Get)
        {
            await _dryRun.CaptureAsync(request, cancellationToken);
            return DryRun.Answer(request);
        }

        _trace.Sent(request);
        return await base.SendAsync(request, cancellationToken);
    }
}
