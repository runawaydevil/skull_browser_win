using System.Text;

namespace SkullWins.Protocols;

/// <summary>
/// Gemini, the wire format only. No sockets here, so every branch can be tested
/// against recorded bytes rather than a live capsule.
///
/// A response is a header line and then a body: two digits, optionally a space
/// and a meta string, then CRLF. What the meta means depends on the status. For
/// a 20 it is the MIME type; for a 3x it is where to go; for a 1x it is the
/// question to ask; for the failures it is the server explaining itself.
/// </summary>
public static class Gemini
{
    public const int DefaultPort = 1965;

    /// <summary>The specification's hard limit on a request URI.</summary>
    public const int MaxRequestBytes = 1024;

    /// <summary>The specification requires clients to stop at five.</summary>
    public const int MaxRedirects = 5;

    // ------------------------------------------------------------- requests

    /// <summary>
    /// A request is the absolute URI and CRLF, nothing else.
    ///
    /// Control characters are stripped for the same reason they are in gopher:
    /// a CR or LF inside the URI would end the line early and let the rest be
    /// read as something the client never meant to send.
    /// </summary>
    public static byte[] BuildRequest(string uri)
    {
        var clean = new StringBuilder(uri.Length);
        foreach (var c in uri)
        {
            if (!char.IsControl(c)) { clean.Append(c); }
        }

        return Encoding.UTF8.GetBytes(clean.ToString() + "\r\n");
    }

    /// <summary>
    /// Whether a URI fits in a request. Checked before a socket is opened,
    /// because the alternative is learning about it from a 59 after the
    /// connection has already been made.
    /// </summary>
    public static bool FitsInRequest(string uri)
        => Encoding.UTF8.GetByteCount(uri) <= MaxRequestBytes;

    /// <summary>
    /// Split a gemini URI into the parts a connection needs. Port defaults to
    /// 1965 and the path keeps its query, because the query is how an answer
    /// to a 1x input prompt is carried back.
    /// </summary>
    public static (string Host, int Port, string Path) ParseUri(string uri)
    {
        var parsed = new Uri(uri, UriKind.Absolute);
        return (parsed.Host,
                parsed.IsDefaultPort ? DefaultPort : parsed.Port,
                parsed.PathAndQuery);
    }

    // ------------------------------------------------------------ responses

    /// <summary>
    /// Read the header line off the front of a response.
    ///
    /// Returns the status, the meta, and where the body starts. A status of
    /// zero means the header was not valid, which a client should treat as a
    /// broken server rather than as any particular protocol outcome.
    /// </summary>
    public static (int Status, string Meta, int BodyOffset) ParseHeader(ReadOnlySpan<byte> bytes)
    {
        // The header ends at the first CRLF. A header longer than this is not
        // a header; refusing early avoids scanning a large body for it.
        const int maxHeader = 2 + 1 + 1024 + 2;

        var limit = Math.Min(bytes.Length, maxHeader);
        var end = -1;
        for (var i = 0; i + 1 < limit; i++)
        {
            if (bytes[i] == (byte)'\r' && bytes[i + 1] == (byte)'\n') { end = i; break; }
        }

        if (end < 2) { return (0, "", 0); }

        var line = Encoding.UTF8.GetString(bytes[..end]);
        if (!char.IsAsciiDigit(line[0]) || !char.IsAsciiDigit(line[1])) { return (0, "", 0); }

        var status = ((line[0] - '0') * 10) + (line[1] - '0');

        // A header may carry no meta at all, in which case there is nothing
        // after the digits.
        var meta = line.Length > 2 ? line[2..].TrimStart(' ') : "";

        return (status, meta, end + 2);
    }

    /// <summary>The class of a status, which is what a client actually acts on.</summary>
    public static GeminiClass ClassOf(int status) => status switch
    {
        >= 10 and <= 19 => GeminiClass.Input,
        >= 20 and <= 29 => GeminiClass.Success,
        >= 30 and <= 39 => GeminiClass.Redirect,
        >= 40 and <= 49 => GeminiClass.TemporaryFailure,
        >= 50 and <= 59 => GeminiClass.PermanentFailure,
        >= 60 and <= 69 => GeminiClass.CertificateRequired,
        _ => GeminiClass.Invalid,
    };

    /// <summary>Status 11 asks for something that must not be echoed back.</summary>
    public static bool IsSensitiveInput(int status) => status == 11;

    /// <summary>
    /// A plain-language name for a status, so an error page says something
    /// more useful than a number. The server's own meta is shown alongside.
    /// </summary>
    public static string Describe(int status) => status switch
    {
        10 => "input required",
        11 => "sensitive input required",
        20 => "success",
        30 => "temporary redirect",
        31 => "permanent redirect",
        40 => "temporary failure",
        41 => "server unavailable",
        42 => "cgi error",
        43 => "proxy error",
        44 => "slow down",
        50 => "permanent failure",
        51 => "not found",
        52 => "gone",
        53 => "proxy request refused",
        59 => "bad request",
        60 => "client certificate required",
        61 => "certificate not authorised",
        62 => "certificate not valid",
        _ => "status " + status,
    };
}

public enum GeminiClass
{
    Invalid,
    Input,
    Success,
    Redirect,
    TemporaryFailure,
    PermanentFailure,
    CertificateRequired,
}
