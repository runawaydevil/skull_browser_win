using System.Net.Sockets;

namespace SkullWins.Protocols;

public sealed record GopherResponse(char Type, byte[] Body, string? Error)
{
    public bool Ok => Error is null;
    public bool IsMenu => Type is '1' or '7';
    public bool IsText => Type is '0';
}

/// <summary>
/// Gopher over TCP. Async on purpose: the Linux version does this with a
/// blocking LuaSocket call, which freezes the window while a slow server
/// thinks. Here the UI thread never waits.
///
/// The protocol has no length header and no status code. The server answers,
/// then closes the connection, and that close is the only end-of-message
/// signal there is. So a read timeout is the only defence against a server that
/// opens a socket and says nothing.
/// </summary>
public sealed class GopherClient
{
    private readonly int _timeoutMs;
    private readonly int _maxBytes;

    public GopherClient(int timeoutMs = 15000, int maxBytes = 16 * 1024 * 1024)
    {
        _timeoutMs = timeoutMs;
        _maxBytes = maxBytes;
    }

    public async Task<GopherResponse> FetchAsync(string uri, string? query = null, CancellationToken ct = default)
    {
        var (host, port, type, selector) = Gopher.ParseUri(uri);

        if (string.IsNullOrWhiteSpace(host))
        {
            return new GopherResponse(type, [], "no host in gopher url");
        }

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeoutMs);

            await client.ConnectAsync(host, port, timeout.Token);

            using var stream = client.GetStream();
            var request = Gopher.BuildRequest(selector, query);
            await stream.WriteAsync(request, timeout.Token);
            await stream.FlushAsync(timeout.Token);

            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];

            while (true)
            {
                var read = await stream.ReadAsync(chunk, timeout.Token);
                if (read == 0) { break; }           // server closed: end of message

                buffer.Write(chunk, 0, read);

                if (buffer.Length > _maxBytes)
                {
                    return new GopherResponse(type, [], "response larger than " + _maxBytes + " bytes");
                }
            }

            return new GopherResponse(type, buffer.ToArray(), null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new GopherResponse(type, [], "timed out after " + (_timeoutMs / 1000) + "s");
        }
        catch (SocketException ex)
        {
            return new GopherResponse(type, [], Describe(ex));
        }
        catch (Exception ex)
        {
            return new GopherResponse(type, [], ex.Message);
        }
    }

    /// <summary>
    /// Specific messages per failure kind. "Something went wrong" tells the user
    /// nothing; "host not found" tells them to check the address.
    /// </summary>
    private static string Describe(SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.HostNotFound => "host not found",
        SocketError.ConnectionRefused => "connection refused",
        SocketError.TimedOut => "connection timed out",
        SocketError.NetworkUnreachable => "network unreachable",
        SocketError.HostUnreachable => "host unreachable",
        _ => ex.Message,
    };
}
