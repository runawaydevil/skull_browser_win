using System.Text.RegularExpressions;

namespace SkullWins.Core;

/// <summary>
/// Decide whether what the user typed is an address or a search. Getting this
/// wrong in either direction is annoying: searching for something that was a
/// URL, or navigating to a hostname that was two words.
/// </summary>
public static partial class Uris
{
    public const string SearchEngine = "https://duckduckgo.com/?q=";

    // A scheme needs at least two characters before the colon. One character is
    // a Windows drive letter, and treating C: as a scheme sends every local path
    // off to the search engine.
    [GeneratedRegex(@"^[a-z][a-z0-9+.\-]+:", RegexOptions.IgnoreCase)]
    private static partial Regex HasScheme();

    [GeneratedRegex(@"^[\w\-]+(\.[\w\-]+)+(:\d+)?(/.*)?$")]
    private static partial Regex LooksLikeHost();

    [GeneratedRegex(@"^localhost(:\d+)?(/.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex IsLocalhost();

    public static string Resolve(string input)
    {
        var text = input.Trim();
        if (text.Length == 0) { return "skull://newtab"; }

        // A Windows path is an address. Tested before the scheme check, because
        // C:\ looks like a scheme to anything that matches on the colon.
        if (text.Length > 2 && char.IsLetter(text[0]) && text[1] == ':'
            && (text[2] == '\\' || text[2] == '/'))
        {
            return "file:///" + text.Replace('\\', '/');
        }

        // localhost:8080 is a host and a port, not a scheme and a path. Same
        // reason it goes first.
        if (IsLocalhost().IsMatch(text)) { return "https://" + text; }

        // Already qualified: gopher://, https://, skull://, file:// and friends.
        if (HasScheme().IsMatch(text)) { return text; }

        // A bare host with a dot is an address.
        if (LooksLikeHost().IsMatch(text)) { return "https://" + text; }

        // Anything else is a search.
        return SearchEngine + Uri.EscapeDataString(text);
    }
}
