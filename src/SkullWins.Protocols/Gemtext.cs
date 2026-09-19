namespace SkullWins.Protocols;

public enum GemtextKind
{
    Text,
    Link,
    Preformatted,
    /// <summary>A ``` line. Carries its alt text, if any.</summary>
    PreformatToggle,
    Heading,
    ListItem,
    Quote,
}

/// <summary>
/// One line of gemtext. Level is the heading depth, one to three, and zero for
/// everything else. Target is the link URL as written, before resolution.
/// </summary>
public sealed record GemtextLine(
    GemtextKind Kind,
    string Text,
    string Target = "",
    int Level = 0);

/// <summary>
/// The text/gemini format, which is line-oriented and has one bit of state.
///
/// Three rules from the specification are easy to get wrong and are what most
/// of the tests here exist to hold down:
///
///   blank lines are meaningful and must not be collapsed
///   consecutive text lines must not be merged into one
///   an unrecognised prefix is a text line, never an error
///
/// The one bit of state is whether we are inside a preformatted block. Inside
/// it nothing is interpreted: a line starting with # is text, and whitespace is
/// preserved exactly, because that is the point of the block.
/// </summary>
public static class Gemtext
{
    public const string ToggleFence = "```";

    public static IReadOnlyList<GemtextLine> Parse(string source)
    {
        var lines = new List<GemtextLine>();
        var preformatted = false;

        foreach (var raw in SplitLines(source))
        {
            // The fence must start the line, with no leading whitespace.
            if (raw.StartsWith(ToggleFence, StringComparison.Ordinal))
            {
                lines.Add(new GemtextLine(
                    GemtextKind.PreformatToggle, raw[ToggleFence.Length..].Trim()));
                preformatted = !preformatted;
                continue;
            }

            if (preformatted)
            {
                // Untouched, including its whitespace. This is ascii art and
                // source code; reformatting it would destroy it.
                lines.Add(new GemtextLine(GemtextKind.Preformatted, raw));
                continue;
            }

            lines.Add(ParseLine(raw));
        }

        return lines;
    }

    private static GemtextLine ParseLine(string raw)
    {
        if (raw.StartsWith("=>", StringComparison.Ordinal))
        {
            var rest = raw[2..].TrimStart();
            if (rest.Length == 0) { return new GemtextLine(GemtextKind.Text, raw); }

            // The URL runs to the first whitespace; anything after it is the
            // label. A link with no label shows its URL, which is what every
            // other client does and what makes a bare link still usable.
            var split = rest.IndexOfAny([' ', '\t']);
            var target = split < 0 ? rest : rest[..split];
            var label = split < 0 ? "" : rest[(split + 1)..].Trim();

            return new GemtextLine(
                GemtextKind.Link, label.Length > 0 ? label : target, target);
        }

        if (raw.StartsWith("###", StringComparison.Ordinal))
        {
            return new GemtextLine(GemtextKind.Heading, raw[3..].Trim(), Level: 3);
        }

        if (raw.StartsWith("##", StringComparison.Ordinal))
        {
            return new GemtextLine(GemtextKind.Heading, raw[2..].Trim(), Level: 2);
        }

        if (raw.StartsWith('#'))
        {
            return new GemtextLine(GemtextKind.Heading, raw[1..].Trim(), Level: 1);
        }

        // An asterisk on its own is not a list item; the specification says
        // asterisk followed by space.
        if (raw.StartsWith("* ", StringComparison.Ordinal))
        {
            return new GemtextLine(GemtextKind.ListItem, raw[2..].Trim());
        }

        if (raw.StartsWith('>'))
        {
            return new GemtextLine(GemtextKind.Quote, raw[1..].Trim());
        }

        return new GemtextLine(GemtextKind.Text, raw);
    }

    /// <summary>
    /// Resolve a link target against the page it appeared on. Relative links
    /// are the common case in gemtext, and a capsule's whole navigation
    /// depends on them landing on the right absolute URL.
    /// </summary>
    public static string Resolve(string pageUri, string target)
    {
        if (string.IsNullOrWhiteSpace(target)) { return ""; }

        try
        {
            return Uri.TryCreate(new Uri(pageUri, UriKind.Absolute), target, out var absolute)
                ? absolute.ToString()
                : target;
        }
        catch
        {
            return target;
        }
    }

    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                var end = i > start && text[i - 1] == '\r' ? i - 1 : i;
                yield return text[start..end];
                start = i + 1;
            }
        }

        // A document that does not end in a newline still has that last line.
        if (start < text.Length)
        {
            yield return text[start..].TrimEnd('\r');
        }
    }
}
