namespace SkullWins.Core;

/// <summary>
/// What the browser is willing to act on when the request comes from a page
/// rather than from the keyboard.
///
/// The keyboard layer is injected into every page, including hostile ones, and
/// web messages carry no proof of who sent them. So a page can forge any
/// message the real script sends. The defence is not to trust the channel but
/// to make every message it can carry harmless:
///
///   - navigation from a page goes through a scheme allow-list, which is what
///     keeps javascript: and file: out
///   - control messages are only honoured from the tab the user is looking at
///   - follow results are only honoured while follow mode is actually running
///
/// Any one of those breaks the drive-by chain; together they close it.
/// </summary>
public static class Trust
{
    /// <summary>
    /// Schemes a page is allowed to send the browser to. The address bar can do
    /// more, because a person typed it.
    ///
    /// file: is absent deliberately. A page that can open file:// URLs can read
    /// the profile directory, and there is no reason a remote document should
    /// reach the local disk.
    /// </summary>
    private static readonly string[] PageNavigable = ["http", "https", "gopher", "gemini", "skull"];

    public static bool IsPageNavigable(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) { return false; }
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) { return false; }

        return PageNavigable.Contains(parsed.Scheme, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An internal page, which is the only content the browser itself produced
    /// and therefore the only content whose messages are not suspect.
    /// </summary>
    public static bool IsInternal(string uri)
        => uri.StartsWith("skull://", StringComparison.OrdinalIgnoreCase);
}
