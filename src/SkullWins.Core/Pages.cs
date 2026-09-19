using System.Net;
using System.Text;
using SkullWins.Protocols;
using SkullWins.Storage;

namespace SkullWins.Core;

/// <summary>
/// Everything served under skull:// plus the gopher-to-HTML renderer and the
/// error pages. One stylesheet for all of them, so the internal pages look like
/// one program instead of eight.
/// </summary>
public static class Pages
{
    public const string Version = "0.12";
    public const string Author = "Pablo Murad";
    public const string Homepage = "https://pablomurad.com";
    public const string Repository = "https://github.com/runawaydevil/skull_browser_win";
    public const string Codename = "steady";

    public static string Style => """
        :root {
          --bg:#141414; --fg:#d8d8d8; --dim:#7a7a7a; --accent:#9fd18a;
          --line:#2a2a2a; --link:#8fb8e0;
        }
        * { box-sizing:border-box; }
        body { background:var(--bg); color:var(--fg); margin:0;
               padding:2.5rem 3rem; font:15px/1.65 Georgia, 'Times New Roman', serif; }
        h1 { font:600 20px/1.3 Consolas, 'Courier New', monospace;
             color:var(--accent); margin:0 0 .25rem; letter-spacing:.02em; }
        h2 { font:600 14px/1.3 Consolas, monospace; color:var(--fg);
             margin:2rem 0 .6rem; text-transform:uppercase; letter-spacing:.08em; }
        .sub { color:var(--dim); font:12px Consolas, monospace; margin:0 0 2rem; }
        a { color:var(--link); text-decoration:none; }
        a:hover { text-decoration:underline; }
        table { border-collapse:collapse; width:100%; font-size:14px; }
        td, th { text-align:left; padding:.4rem .8rem .4rem 0;
                 border-bottom:1px solid var(--line); vertical-align:top; }
        th { color:var(--dim); font:600 11px Consolas, monospace;
             text-transform:uppercase; letter-spacing:.08em; }
        td.num { color:var(--dim); font:13px Consolas, monospace; white-space:nowrap; }
        kbd { background:#222; border:1px solid var(--line); border-bottom-width:2px;
              border-radius:3px; padding:1px 6px; font:12px Consolas, monospace;
              color:var(--accent); }
        code { color:#e8c07d; font:13px Consolas, monospace; }
        .empty { color:var(--dim); font-style:italic; }
        .gopher { font:14px/1.6 Consolas, 'Courier New', monospace; white-space:pre-wrap; }
        .gopher .t { color:var(--dim); user-select:none; }
        .gemlink { margin:.15rem 0; }
        .gemlink a::before { content:"=> "; color:var(--dim); }
        blockquote { border-left:2px solid var(--line); margin:.6rem 0;
                     padding:.1rem 0 .1rem 1rem; color:var(--dim); font-style:italic; }
        pre { background:#1a1a1a; border:1px solid var(--line); padding:.7rem 1rem;
              overflow-x:auto; font:13px/1.45 Consolas, monospace; }
        ul { margin:.4rem 0; padding-left:1.4rem; }
        p:empty { min-height:.7rem; }
        .err { border-left:3px solid #d08a8a; padding:.2rem 0 .2rem 1rem; margin:1.5rem 0; }
        .err h1 { color:#d08a8a; }
        """;

    private static string Shell(string title, string body) => $"""
        <!doctype html>
        <html><head><meta charset="utf-8"><title>{Esc(title)}</title>
        <style>{Style}</style></head><body>{body}</body></html>
        """;

    public static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");

    // ------------------------------------------------------------- skull://

    public static string About(Locale t, AboutFacts f)
    {
        static string Row(string label, string value) =>
            $"<tr><th>{Esc(label)}</th><td>{value}</td></tr>";

        var rows = new StringBuilder();
        rows.Append(Row(t.Translate("about.version"), Esc(Version)));
        rows.Append(Row(t.Translate("about.author"),
            Esc(Author) + " &middot; <a href=\"" + Esc(Homepage) + "\">" + Esc(Homepage) + "</a>"));
        rows.Append(Row(t.Translate("about.license"),
            "<a href=\"https://www.gnu.org/licenses/gpl-3.0.html\">GNU GPLv3</a>"));
        rows.Append(Row(t.Translate("about.source"),
            "<a href=\"" + Esc(Repository) + "\">" + Esc(Repository) + "</a>"));
        rows.Append(Row(t.Translate("about.built"),
            Esc(f.BuildDate) + " &middot; <code>" + Esc(f.Commit) + "</code>"));
        rows.Append(Row(t.Translate("about.engine"), "WebView2 " + Esc(f.Runtime)));
        rows.Append(Row(t.Translate("about.framework"), Esc(f.DotNet)));
        rows.Append(Row(t.Translate("about.os"), Esc(f.Os)));
        rows.Append(Row(t.Translate("about.profile"),
            "<code>" + Esc(f.ProfileDir) + "</code> <span class=\"sub\">"
            + Esc(t.Translate("about." + f.ProfileKind)) + "</span>"));

        return Shell("about", $"""
            <h1>Skull Wins {Version}</h1>
            <p class="sub">{Esc(t.Translate("about.tagline"))}</p>
            <table>{rows}</table>
            <h2>{Esc(t.Translate("about.protocols"))}</h2>
            <p>https, http, <a href="gopher://gopher.floodgap.com">gopher</a>, skull</p>
            """);
    }

    public static string Help(Locale t, IEnumerable<(string Trigger, string Description)> binds)
    {
        var rows = new StringBuilder();
        foreach (var (trigger, description) in binds)
        {
            rows.Append($"<tr><td class=\"num\"><kbd>{Esc(trigger)}</kbd></td><td>{Esc(t.Translate(description))}</td></tr>");
        }

        return Shell("help", $"""
            <h1>{Esc(t.Translate("help.title"))}</h1>
            <p class="sub">{Esc(t.Translate("help.sub"))}</p>
            <h2>{Esc(t.Translate("help.keys"))}</h2>
            <table>{rows}</table>
            <h2>{Esc(t.Translate("help.config"))}</h2>
            <p>{Esc(t.Translate("help.config.body"))}</p>
            """);
    }

    public static string History(Locale t, IReadOnlyList<HistoryEntry> entries)
    {
        if (entries.Count == 0)
        {
            return Shell("history", $"<h1>{Esc(t.Translate("history.title"))}</h1>"
                + $"<p class=\"empty\">{Esc(t.Translate("history.empty"))}</p>");
        }

        var rows = new StringBuilder();
        foreach (var e in entries)
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(e.LastVisit).LocalDateTime;
            rows.Append($"""
                <tr><td class="num">{when:yyyy-MM-dd HH:mm}</td>
                <td><a href="{Esc(e.Uri)}">{Esc(e.Title.Length > 0 ? e.Title : e.Uri)}</a><br>
                <span class="sub">{Esc(e.Uri)}</span></td>
                <td class="num">{e.Visits}</td></tr>
                """);
        }

        return Shell("history", $"""
            <h1>{Esc(t.Translate("history.title"))}</h1>
            <p class="sub">{entries.Count} {Esc(t.Translate("history.entries"))}</p>
            <table>
              <tr><th>{Esc(t.Translate("history.when"))}</th>
                  <th>{Esc(t.Translate("history.page"))}</th>
                  <th>{Esc(t.Translate("history.visits"))}</th></tr>
              {rows}
            </table>
            """);
    }

    public static string Bookmarks(Locale t, IReadOnlyList<Bookmark> marks)
    {
        if (marks.Count == 0)
        {
            return Shell("bookmarks", $"<h1>{Esc(t.Translate("bookmarks.title"))}</h1>"
                + $"<p class=\"empty\">{Esc(t.Translate("bookmarks.empty"))}</p>");
        }

        var rows = new StringBuilder();
        foreach (var m in marks)
        {
            rows.Append($"""
                <tr><td><a href="{Esc(m.Uri)}">{Esc(m.Title.Length > 0 ? m.Title : m.Uri)}</a><br>
                <span class="sub">{Esc(m.Uri)}</span></td></tr>
                """);
        }

        return Shell("bookmarks", $"""
            <h1>{Esc(t.Translate("bookmarks.title"))}</h1>
            <p class="sub">{marks.Count} {Esc(t.Translate("bookmarks.saved"))}</p>
            <table>{rows}</table>
            """);
    }

    public static string NewTab(Locale t) => Shell("skull", $"""
        <h1>Skull Wins</h1>
        <p class="sub">{Esc(t.Translate("newtab.hint"))}</p>
        <h2>{Esc(t.Translate("newtab.start"))}</h2>
        <table>
          <tr><td><a href="gopher://gopher.floodgap.com">gopher.floodgap.com</a></td>
              <td class="sub">{Esc(t.Translate("newtab.gopher"))}</td></tr>
          <tr><td><a href="skull://help">skull://help</a></td>
              <td class="sub">{Esc(t.Translate("newtab.help"))}</td></tr>
          <tr><td><a href="skull://about">skull://about</a></td>
              <td class="sub">{Esc(t.Translate("newtab.about"))}</td></tr>
        </table>
        """);

    public static string Error(Locale t, string titleKey, string detail, string uri) => Shell("error", $"""
        <div class="err">
          <h1>{Esc(t.Translate(titleKey))}</h1>
          <p>{Esc(detail)}</p>
          <p class="sub">{Esc(uri)}</p>
        </div>
        """);

    // --------------------------------------------------------------- gopher

    /// <summary>
    /// Render a gopher menu as HTML. Kept close to how the menu looks on a
    /// terminal: fixed-width, a short type tag in the gutter, one item per line.
    /// </summary>
    public static string GopherMenu(Locale t, string uri, IReadOnlyList<GopherItem> items)
    {
        var body = new StringBuilder();
        body.Append($"<h1>{Esc(uri)}</h1><div class=\"gopher\">");

        foreach (var item in items)
        {
            var tag = TypeTag(item.Type);

            if (!item.IsLink)
            {
                body.Append($"<div><span class=\"t\">{tag}</span>{Esc(item.Display)}</div>");
                continue;
            }

            var target = item.ToUri();
            body.Append($"<div><span class=\"t\">{tag}</span>"
                + $"<a href=\"{Esc(target)}\">{Esc(item.Display)}</a></div>");
        }

        body.Append("</div>");
        return Shell(uri, body.ToString());
    }

    /// <summary>
    /// Render gemtext.
    ///
    /// The format is line-oriented with one bit of state, so this is one pass
    /// with a flag. The rules that need care come from the specification: a
    /// blank line is meaningful, two text lines never merge into one, and a
    /// prefix nobody recognises is text rather than an error.
    /// </summary>
    /// <summary>
    /// A gemini server asking a question, status 10 or 11.
    ///
    /// Rendered as a page rather than answered automatically: the answer goes
    /// back as the query part of the same URL, and the user has to see what is
    /// being asked before typing into it. A status 11 is asking for something
    /// that should not be echoed, and the field says so.
    /// </summary>
    public static string GeminiInput(Locale t, string uri, string prompt, bool sensitive)
    {
        var key = sensitive ? "gemini.input.sensitive" : "gemini.input";

        return Shell(uri, $"""
            <h1>{Esc(t.Translate("gemini.input.title"))}</h1>
            <p class="sub">{Esc(uri)}</p>
            <p>{Esc(prompt.Length > 0 ? prompt : t.Translate("gemini.input.noprompt"))}</p>
            <h2>{Esc(t.Translate(key))}</h2>
            <p>{Esc(t.Translate("gemini.input.how"))}</p>
            """);
    }

    public static string Gemtext(string uri, IReadOnlyList<GemtextLine> lines)
    {
        var body = new StringBuilder();
        body.Append("<h1>").Append(Esc(uri)).Append("</h1>");

        var inList = false;
        var inPre = false;

        void CloseList()
        {
            if (inList) { body.Append("</ul>"); inList = false; }
        }

        foreach (var line in lines)
        {
            if (line.Kind != GemtextKind.ListItem) { CloseList(); }

            switch (line.Kind)
            {
                case GemtextKind.PreformatToggle:
                    if (inPre) { body.Append("</pre>"); }
                    else
                    {
                        // The alt text is for anyone who cannot see the block.
                        body.Append("<pre");
                        if (line.Text.Length > 0)
                        {
                            body.Append(" aria-label=\"").Append(Esc(line.Text)).Append('"');
                        }
                        body.Append('>');
                    }
                    inPre = !inPre;
                    break;

                case GemtextKind.Preformatted:
                    body.Append(Esc(line.Text)).Append('\n');
                    break;

                case GemtextKind.Heading:
                    var tag = "h" + Math.Clamp(line.Level + 1, 2, 4);
                    body.Append('<').Append(tag).Append('>')
                        .Append(Esc(line.Text))
                        .Append("</").Append(tag).Append('>');
                    break;

                case GemtextKind.Link:
                    var target = Protocols.Gemtext.Resolve(uri, line.Target);
                    body.Append("<div class=\"gemlink\"><a href=\"").Append(Esc(target))
                        .Append("\">").Append(Esc(line.Text)).Append("</a></div>");
                    break;

                case GemtextKind.ListItem:
                    if (!inList) { body.Append("<ul>"); inList = true; }
                    body.Append("<li>").Append(Esc(line.Text)).Append("</li>");
                    break;

                case GemtextKind.Quote:
                    body.Append("<blockquote>").Append(Esc(line.Text)).Append("</blockquote>");
                    break;

                default:
                    // A blank line is a blank paragraph, not nothing: it is how
                    // an author separates their paragraphs.
                    body.Append("<p>").Append(Esc(line.Text)).Append("</p>");
                    break;
            }
        }

        CloseList();
        if (inPre) { body.Append("</pre>"); }

        return Shell(uri, body.ToString());
    }

    public static string GopherText(string uri, string text) => Shell(uri, $"""
        <h1>{Esc(uri)}</h1>
        <div class="gopher">{Esc(text)}</div>
        """);

    /// <summary>Four-character gutter tag per RFC 1436 item type.</summary>
    private static string TypeTag(char type) => type switch
    {
        '0' => "TXT ",
        '1' => "DIR ",
        '2' => "CCSO",
        '3' => "ERR ",
        '4' => "HEX ",
        '5' => "DOS ",
        '6' => "UENC",
        '7' => "FIND",
        '8' => "TEL ",
        '9' => "BIN ",
        'g' => "GIF ",
        'I' => "IMG ",
        'h' => "HTML",
        's' => "SND ",
        'd' => "DOC ",
        'i' => "    ",
        _ => "?   ",
    };
}

/// <summary>
/// Everything the about page reports. Gathered by the host, because most of it
/// needs Windows APIs that do not belong in a rendering module.
/// </summary>
public sealed record AboutFacts(
    string BuildDate,
    string Commit,
    string Runtime,
    string DotNet,
    string Os,
    string ProfileDir,
    string ProfileKind);
