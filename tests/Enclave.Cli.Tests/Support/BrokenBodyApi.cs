using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Enclave.Cli.Tests.Support;

/// <summary>
/// A fake API, listening on 127.0.0.1 only, that answers every request with a 200 whose headers
/// promise more body than it sends, then ends the connection: with a FIN, or with a RST when made
/// with reset set.
/// </summary>
// WireMock.Net cannot send this: it sets Content-Length from the body it holds and leaves out a
// Content-Length header given with a body, and its faults (FaultType.EMPTY_RESPONSE,
// MALFORMED_RESPONSE_CHUNK) send a complete response whose body is empty or garbled, which reaches
// the CLI as a JSON error (observed with WireMock.Net 2.18.0; OwinResponseMapper).
internal sealed class BrokenBodyApi : IDisposable
{
    private static readonly byte[] Answer = Encoding.ASCII.GetBytes(
        "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\n\r\n{\"items\":[");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    private readonly ConcurrentQueue<string> _requests = new();

    private readonly Task _serving;

    public BrokenBodyApi(bool reset)
    {
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        // On the thread pool, so the fake never waits on the test's own thread, which Dispose
        // blocks.
        _serving = Task.Run(() => ServeAsync(reset));
    }

    public string Url { get; }

    /// <summary>
    /// Each request received, as "METHOD path" without the query, in the order received.
    /// </summary>
    public IReadOnlyList<string> Requests => [.. _requests];

    public void Dispose()
    {
        _listener.Stop();
        _listener.Dispose();
        _serving.Wait(TimeSpan.FromSeconds(10));
    }

    private static async Task<string> ReadMethodAndPathAsync(Socket socket)
    {
        var buffer = new byte[8192];
        var head = new StringBuilder();

        while (!head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var count = await socket.ReceiveAsync(buffer);

            if (count == 0)
            {
                break;
            }

            head.Append(Encoding.ASCII.GetString(buffer, 0, count));
        }

        var parts = head.ToString().Split(' ', 3);
        return parts.Length < 2 ? string.Empty : $"{parts[0]} {parts[1].Split('?')[0]}";
    }

    // The pause before the connection ends lets the client read the headers and the partial
    // body, so the end reaches it while it reads the body. A RST that arrived before the headers
    // would fail SendAsync instead.
    private async Task ServeAsync(bool reset)
    {
        try
        {
            while (true)
            {
                using var socket = await _listener.AcceptSocketAsync();
                _requests.Enqueue(await ReadMethodAndPathAsync(socket));
                await socket.SendAsync(Answer);
                await Task.Delay(TimeSpan.FromMilliseconds(200));

                // A zero linger time makes Close send a RST in place of a FIN
                // (https://learn.microsoft.com/dotnet/api/system.net.sockets.socket.lingerstate).
                if (reset)
                {
                    socket.LingerState = new LingerOption(true, 0);
                }
                else
                {
                    socket.Shutdown(SocketShutdown.Send);
                }

                socket.Close();
            }
        }
        catch (SocketException)
        {
            // Stop ends AcceptSocketAsync with this once the test is done.
        }
        catch (ObjectDisposedException)
        {
            // As above, where the listener is disposed first.
        }
    }
}
