using System.Collections.Concurrent;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// A network that fails every request with the exception it was given, and records each request,
/// for <see cref="CliRun.NetworkHandler"/>. Set <c>run.NetworkHandler = network.CreateHandler</c>.
/// </summary>
// A fake API on loopback answers every request it gets, so a failure that happens before an
// answer, such as HttpClient's timeout, is made here, beneath the CLI's own handler, where the
// network would be. The record outlives the handlers, which each command disposes with its
// ApiRequestHandler.
internal sealed class FailingNetwork
{
    private readonly Func<Exception> _failure;

    private readonly ConcurrentQueue<string> _calls = new();

    public FailingNetwork(Func<Exception> failure)
    {
        _failure = failure;
    }

    /// <summary>
    /// The requests the network was given, each as "METHOD path", in the order given.
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
