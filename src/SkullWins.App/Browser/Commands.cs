namespace SkullWins.App.Browser;

using SkullWins.Core;

/// <summary>
/// The colon commands. Aliases are listed in one place so skull://help and the
/// dispatcher cannot drift apart.
/// </summary>
public static class Commands
{
    public static void Run(MainWindow w, string line)
    {
        if (line.Length == 0) { return; }

        var space = line.IndexOf(' ');
        var name = (space < 0 ? line : line[..space]).ToLowerInvariant();
        var arg = space < 0 ? "" : line[(space + 1)..].Trim();

        switch (name)
        {
            case "o" or "open":
                if (arg.Length > 0) { w.Navigate(arg); }
                break;

            case "t" or "tabopen":
                _ = w.NewTab(arg.Length > 0 ? Uris.Resolve(arg) : MainWindow.StartUri);
                break;

            case "q" or "quit" or "close":
                w.CloseTab();
                break;

            case "qa" or "quitall":
                w.Close();
                break;

            case "r" or "reload":
                w.Reload();
                break;

            case "back":
                w.Back();
                break;

            case "forward":
                w.Forward();
                break;

            case "about":
                w.Navigate("skull://about");
                break;

            case "help" or "binds":
                w.Navigate("skull://help");
                break;

            case "history" or "hist":
                w.Navigate("skull://history");
                break;

            case "bookmarks" or "bm":
                w.Navigate("skull://bookmarks");
                break;

            case "bookmark":
                w.ToggleBookmark();
                break;

            case "log":
                w.Navigate("skull://log");
                break;

            case "identity" or "id":
            {
                var gap = arg.IndexOf(' ');
                var verb = (gap < 0 ? arg : arg[..gap]).ToLowerInvariant();
                var who = gap < 0 ? "" : arg[(gap + 1)..].Trim();

                switch (verb)
                {
                    case "new": w.CreateIdentity(who); break;
                    case "use": w.UseIdentity(who); break;
                    case "drop": w.DropIdentity(); break;
                    case "forget": w.ForgetIdentity(who); break;
                    default: w.ShowIdentities(); break;
                }
                break;
            }

            case "cert":
                switch (arg.ToLowerInvariant())
                {
                    case "accept": w.AcceptCertificate(); break;
                    case "forget": w.ForgetCertificate(); break;
                    default: w.ShowCertificate(); break;
                }
                break;

            case "gemini":
                w.Navigate(arg.Length > 0
                    ? (arg.StartsWith("gemini://", StringComparison.OrdinalIgnoreCase)
                        ? arg : "gemini://" + arg)
                    : "gemini://geminiprotocol.net/");
                break;

            case "gopher":
                w.Navigate(arg.Length > 0
                    ? (arg.StartsWith("gopher://", StringComparison.OrdinalIgnoreCase)
                        ? arg : "gopher://" + arg)
                    : "gopher://gopher.floodgap.com");
                break;

            default:
                w.Notify(w.Translator.Translate("notify.nocommand", name));
                break;
        }
    }
}
