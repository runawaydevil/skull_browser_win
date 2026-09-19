using System.Text;

namespace SkullWins.Protocols;

// Gopher, RFC 1436. The wire format is a plain-text menu: one item per line,
// fields split by tabs, the whole thing terminated by a line holding a single
// dot. This file turns raw bytes into a list of items and nothing more. No
// sockets, no rendering, no I/O; that keeps it testable against recorded
// captures. The host does the networking, Lua does the HTML.

/// <summary>One entry in a gopher menu.</summary>
public sealed record GopherItem(
    char Type,
    string Display,
    string Selector,
    string Host,
    int Port)
{
    /// <summary>
    /// A gopher URL for this item, or an http/https/other URL when the item is
    /// an "h" type whose selector is a URL: link (the common web-link escape
    /// hatch gopher servers use).
    /// </summary>
    public string ToUri()
    {
        if (Type == 'h' && Selector.StartsWith("URL:", StringComparison.OrdinalIgnoreCase))
        {
            // A gopher server is unauthenticated and unencrypted, so this field
            // is fully attacker controlled. Left alone it will happily hand back
            // javascript: or file:, and clicking the link would run script in the
            // page's origin or open a local file. Only the web schemes get out.
            var target = Selector[4..];
            return Gopher.IsSafeLinkTarget(target) ? target : "";
        }

        var host = Host;
        if (Port != 70) { host = host + ":" + Port; }
        return "gopher://" + host + "/" + Type + Selector;
    }

    /// <summary>Items of type i (info) and 3 (error) are labels, not links.</summary>
    public bool IsLink => Type is not ('i' or '3');
}

public static class Gopher
{
    public const int DefaultPort = 70;

    /// <summary>
    /// Parse a gopher menu. Tolerant on purpose: real servers send short lines,
    /// stray blank lines, missing ports and a mix of CRLF and LF. A malformed
    /// line becomes an info item rather than throwing, because a browser that
    /// refuses to render a slightly broken menu is worse than one that shows it.
    /// </summary>
    public static IReadOnlyList<GopherItem> ParseMenu(ReadOnlySpan<byte> bytes)
    {
        var text = DecodeUtf8Lossy(bytes);
        var items = new List<GopherItem>();

        foreach (var rawLine in SplitLines(text))
        {
            var line = rawLine.TrimEnd('\r');

            // The lone dot ends the menu. Anything after it is ignored.
            if (line == ".") { break; }

            // A truly empty line is spacing; keep it as blank info so the
            // rendered page preserves the author's layout.
            if (line.Length == 0)
            {
                items.Add(new GopherItem('i', "", "", "", DefaultPort));
                continue;
            }

            var type = line[0];
            var rest = line[1..];
            var fields = rest.Split('\t');

            var display = fields.Length > 0 ? fields[0] : "";
            var selector = fields.Length > 1 ? fields[1] : "";
            var host = fields.Length > 2 ? fields[2] : "";
            var port = ParsePort(fields.Length > 3 ? fields[3] : "");

            items.Add(new GopherItem(type, display, selector, host, port));
        }

        return items;
    }

    /// <summary>
    /// A gopher request is the selector followed by CRLF. A search request
    /// (item type 7) appends a tab and the query before the CRLF.
    ///
    /// The selector arrives percent-decoded from the URL, which means a crafted
    /// link can carry CR, LF, NUL or tab inside it. Written straight to the
    /// socket those bytes end the request line early and let the rest be read as
    /// a second command, which is how a gopher URL becomes a way to speak
    /// Redis, SMTP or anything else listening on a port. Strip them.
    /// </summary>
    public static byte[] BuildRequest(string selector, string? query = null)
    {
        var line = query is null
            ? Sanitize(selector)
            : Sanitize(selector) + "\t" + Sanitize(query);

        return Encoding.UTF8.GetBytes(line + "\r\n");
    }

    /// <summary>
    /// Remove every control character. Only the framing this method adds may
    /// contain CR, LF or tab.
    /// </summary>
    public static string Sanitize(string field)
    {
        if (field.Length == 0) { return field; }

        var clean = new StringBuilder(field.Length);
        foreach (var c in field)
        {
            if (!char.IsControl(c)) { clean.Append(c); }
        }
        return clean.ToString();
    }

    /// <summary>
    /// Ports that are not gopher and where a half-controlled request line does
    /// real damage. Same idea as the blocked-port list every browser keeps for
    /// http. Gopher itself is 70; the rest of the range stays open so a server
    /// on 7070 still works.
    /// </summary>
    private static readonly HashSet<int> BlockedPorts =
    [
        1, 7, 9, 11, 13, 15, 17, 19, 20, 21, 22, 23, 25, 37, 42, 43, 53, 69,
        77, 79, 87, 95, 101, 102, 103, 104, 109, 110, 111, 113, 115, 117, 119,
        123, 135, 137, 138, 139, 143, 161, 179, 389, 427, 445, 465, 512, 513,
        514, 515, 526, 530, 531, 532, 540, 548, 554, 556, 563, 587, 601, 636,
        989, 990, 993, 995, 1719, 1720, 1723, 2049, 3306, 3659, 4045, 5060,
        5061, 5432, 6000, 6379, 6566, 6665, 6666, 6667, 6668, 6669, 6697,
        10080, 11211, 27017, 27018, 27019,
    ];

    public static bool IsBlockedPort(int port) => BlockedPorts.Contains(port);

    /// <summary>
    /// Whether a gopher "h" item's URL: target is safe to render as a link.
    /// Web schemes only: no javascript:, no file:, no data:.
    /// </summary>
    public static bool IsSafeLinkTarget(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)) { return false; }

        return uri.Scheme is "http" or "https" or "gopher";
    }

    /// <summary>
    /// Split a gopher URL into its parts. Shapes handled:
    ///   gopher://host
    ///   gopher://host:port
    ///   gopher://host/Tselector
    ///   gopher://host:port/Tselector
    /// The type char defaults to 1 (menu) when the path is empty.
    /// </summary>
    public static (string Host, int Port, char Type, string Selector) ParseUri(string uri)
    {
        var s = uri;
        if (s.StartsWith("gopher://", StringComparison.OrdinalIgnoreCase))
        {
            s = s["gopher://".Length..];
        }

        var slash = s.IndexOf('/');
        var authority = slash < 0 ? s : s[..slash];
        var path = slash < 0 ? "" : s[(slash + 1)..];

        var host = authority;
        var port = DefaultPort;
        var colon = authority.LastIndexOf(':');
        if (colon >= 0 && int.TryParse(authority[(colon + 1)..], out var p))
        {
            host = authority[..colon];
            port = p;
        }

        var type = '1';
        var selector = "";
        if (path.Length > 0)
        {
            type = path[0];
            selector = path[1..];
        }

        return (host, port, type, Uri.UnescapeDataString(selector));
    }

    // ------------------------------------------------------------- internals

    private static int ParsePort(string field)
    {
        var trimmed = field.Trim();
        return int.TryParse(trimmed, out var p) && p > 0 ? p : DefaultPort;
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                yield return text[start..i];
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }

    /// <summary>
    /// Gopher predates UTF-8 and servers send whatever they like. Decode as
    /// UTF-8 but replace invalid sequences instead of throwing, so one bad byte
    /// does not sink the whole menu.
    /// </summary>
    private static string DecodeUtf8Lossy(ReadOnlySpan<byte> bytes)
    {
        var decoder = Encoding.GetEncoding(
            "utf-8",
            EncoderFallback.ReplacementFallback,
            DecoderFallback.ReplacementFallback);
        return decoder.GetString(bytes);
    }
}
