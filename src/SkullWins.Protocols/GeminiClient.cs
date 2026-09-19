using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SkullWins.Protocols;

public sealed record GeminiResponse(
    int Status, string Meta, byte[] Body, string? Error, CertificateFacts? Certificate)
{
    /// <summary>The exchange completed. The status may still be a failure.</summary>
    public bool Ok => Error is null;

    public GeminiClass Class => Gemini.ClassOf(Status);
}

/// <summary>What the server presented, for the trust decision and the log.</summary>
public sealed record CertificateFacts(
    string Fingerprint, long NotAfter, string Subject, string Issuer);

/// <summary>
/// Whether a certificate may be used, decided outside this class.
///
/// The client does not own the trust policy. It reports what the server
/// presented and asks; the caller consults the pinned record and answers. That
/// keeps the decision testable without a socket and keeps the socket code from
/// quietly becoming the security policy.
/// </summary>
public delegate bool TrustDecision(string host, int port, CertificateFacts certificate);

/// <summary>
/// Gemini over TLS.
///
/// Servers are self-signed as a matter of course, so ordinary chain validation
/// would refuse nearly every capsule in existence. Validation is therefore
/// replaced, not disabled: the callback below always inspects the certificate
/// and always asks the trust decision before allowing the handshake to stand.
///
/// The dangerous shortcut here is a callback that returns true and forgets to
/// ask. That produces a browser where TLS protects nothing and every page
/// still loads, so there is no symptom to notice. The callback keeps its
/// answer in a field the caller can read back for exactly that reason.
/// </summary>
public sealed class GeminiClient
{
    private readonly int _timeoutMs;
    private readonly int _maxBytes;

    public GeminiClient(int timeoutMs = 20000, int maxBytes = 32 * 1024 * 1024)
    {
        _timeoutMs = timeoutMs;
        _maxBytes = maxBytes;
    }

    public async Task<GeminiResponse> FetchAsync(
        string uri,
        TrustDecision trust,
        X509Certificate2? identity = null,
        CancellationToken ct = default)
    {
        if (!Gemini.FitsInRequest(uri))
        {
            // Checked here rather than learned from a 59 after connecting.
            return Fail("address is longer than the 1024 bytes gemini allows");
        }

        string host;
        int port;
        try
        {
            (host, port, _) = Gemini.ParseUri(uri);
        }
        catch (UriFormatException)
        {
            return Fail("not a valid gemini address");
        }

        if (string.IsNullOrWhiteSpace(host)) { return Fail("no host in the address"); }

        CertificateFacts? seen = null;
        var trusted = false;

        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeoutMs);

            await client.ConnectAsync(host, port, timeout.Token);

            await using var network = client.GetStream();
            await using var tls = new SslStream(network, leaveInnerStreamOpen: false);

            var options = new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,

                // Replaced, not disabled. Every handshake goes through the
                // decision below, and refusing there refuses the connection.
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null) { return false; }

                    seen = Describe(certificate);
                    trusted = trust(host, port, seen);
                    return trusted;
                },
            };

            if (identity is not null)
            {
                options.ClientCertificates = [identity];
            }

            await tls.AuthenticateAsClientAsync(options, timeout.Token);

            await tls.WriteAsync(Gemini.BuildRequest(uri), timeout.Token);
            await tls.FlushAsync(timeout.Token);

            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];

            while (true)
            {
                var read = await tls.ReadAsync(chunk, timeout.Token);
                if (read == 0) { break; }   // server closed: end of message

                buffer.Write(chunk, 0, read);
                if (buffer.Length > _maxBytes)
                {
                    return Fail("response larger than " + _maxBytes + " bytes", seen);
                }
            }

            var bytes = buffer.ToArray();
            var (status, meta, offset) = Gemini.ParseHeader(bytes);

            if (status == 0) { return Fail("the server did not send a valid gemini header", seen); }

            return new GeminiResponse(status, meta, bytes[offset..], null, seen);
        }
        catch (AuthenticationException)
        {
            // The handshake failed. When the trust decision said no, that is
            // the reason, and saying so is more useful than a TLS error.
            return Fail(
                trusted ? "the TLS handshake failed" : "the server's certificate was refused",
                seen);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Fail("timed out after " + (_timeoutMs / 1000) + "s", seen);
        }
        catch (SocketException ex)
        {
            return Fail(Describe(ex), seen);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, seen);
        }
    }

    private static GeminiResponse Fail(string error, CertificateFacts? seen = null)
        => new(0, "", [], error, seen);

    private static CertificateFacts Describe(X509Certificate certificate)
    {
        // Copying into X509Certificate2 gives the SHA-256 hash and the dates.
        using var full = new X509Certificate2(certificate);

        return new CertificateFacts(
            full.GetCertHashString(HashAlgorithmName.SHA256),
            new DateTimeOffset(full.NotAfter.ToUniversalTime()).ToUnixTimeSeconds(),
            full.Subject,
            full.Issuer);
    }

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
