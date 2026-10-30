using System.Collections.Concurrent;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// A network that fails every request with the given exception and records it. Set
/// <c>run.NetworkHandler = network.CreateHandler</c>.
/// </summary>
// A fake API answers every request, so a failure before an answer, such as a timeout, is made here.
// The record outlives the handlers, which each command disposes with its ApiRequestHandler.
internal sealed class FailingNetwork
{
    private readonly Func<Exception> _failure;

    private readonly ConcurrentQueue<string> _calls = new();

    public FailingNetwork(Func<Exception> failure)
    {
        _failure = failure;
    }

    /// <summary>
    /// Each request as "METHOD path".
    /// </summary>
    public IReadOnlyList<string> Calls => _calls.ToArray();

    public HttpMessageHandler CreateHandler() => new Handler(this);

    private sealed class Handler(FailingNetwork network) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            network._calls.Enqueue($"{request.Method} {request.RequestUri?.AbsolutePath}");
            return Task.FromException<HttpResponseMessage>(network._failure());
        }
    }
}
