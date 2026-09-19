using SkullWins.Core;

namespace SkullWins.App.Browser;

/// <summary>
/// The default key map.
///
/// Descriptions are catalogue keys, not prose. They feed skull://help as well as
/// the status bar, and they are the largest block of translatable text in the
/// project, so they go through i18n like everything else. A key that is not in
/// the catalogue resolves to itself, which means someone writing their own bind
/// in rc.lua just writes plain English and it works.
/// </summary>
public static class Binds
{
    public static void Install(
        MainWindow w, Dictionary<string, BindTable> modes, Locale locale)
    {
        var all = new BindTable();
        var normal = new BindTable();
        var insert = new BindTable();
        var command = new BindTable();
        var passthrough = new BindTable();

        // Escape belongs to every mode; that is what makes it a reliable exit.
        all.Add("Escape", "bind.escape", _ => { w.UseMode("normal"); return true; });

        // motion
        normal.Add("j", "bind.scroll_down", _ => Scroll(w, 0, 60));
        normal.Add("k", "bind.scroll_up", _ => Scroll(w, 0, -60));
        normal.Add("h", "bind.scroll_left", _ => Scroll(w, -60, 0));
        normal.Add("l", "bind.scroll_right", _ => Scroll(w, 60, 0));
        normal.Add("Down", "bind.scroll_down", _ => Scroll(w, 0, 60));
        normal.Add("Up", "bind.scroll_up", _ => Scroll(w, 0, -60));
        normal.Add("gg", "bind.scroll_top", _ => Eval(w, "window.scrollTo(0,0)"));
        normal.Add("G", "bind.scroll_bottom",
            _ => Eval(w, "window.scrollTo(0,document.body.scrollHeight)"));
        normal.Add("<control-d>", "bind.half_down",
            _ => Eval(w, "window.scrollBy(0,window.innerHeight/2)"));
        normal.Add("<control-u>", "bind.half_up",
            _ => Eval(w, "window.scrollBy(0,-window.innerHeight/2)"));
        normal.Add("space", "bind.page_down",
            _ => Eval(w, "window.scrollBy(0,window.innerHeight*0.9)"));
        normal.Add("Page_Down", "bind.page_down",
            _ => Eval(w, "window.scrollBy(0,window.innerHeight*0.9)"));
        normal.Add("Page_Up", "bind.page_up",
            _ => Eval(w, "window.scrollBy(0,-window.innerHeight*0.9)"));

        // navigation
        normal.Add("H", "bind.back", _ => { w.Back(); return true; });
        normal.Add("L", "bind.forward", _ => { w.Forward(); return true; });
        normal.Add("r", "bind.reload", _ => { w.Reload(); return true; });
        normal.Add("<control-c>", "bind.stop", _ => { w.Stop(); return true; });

        // modes
        normal.Add("i", "bind.insert", _ => { w.UseMode("insert"); return true; });
        normal.Add(":", "bind.command", _ => { w.UseMode("command"); return true; });
        normal.Add("<control-z>", "bind.passthrough",
            _ => { w.UseMode("passthrough"); return true; });

        // following links
        normal.Add("f", "bind.follow", _ => { Follow.Start(w, newTab: false); return true; });
        normal.Add("F", "bind.follow_tab", _ => { Follow.Start(w, newTab: true); return true; });

        // tabs
        normal.Add("t", "bind.tab_new", _ => { w.OpenTab("skull://newtab"); return true; });
        normal.Add("<control-w>", "bind.tab_close", _ => { w.CloseTab(); return true; });
        normal.Add("gt", "bind.tab_next", _ => { w.NextTab(); return true; });
        normal.Add("gT", "bind.tab_prev", _ => { w.PrevTab(); return true; });
        normal.Add("J", "bind.tab_prev", _ => { w.PrevTab(); return true; });
        normal.Add("K", "bind.tab_next", _ => { w.NextTab(); return true; });

        // bookmarks and internal pages
        normal.Add("d", "bind.bookmark", _ => { w.ToggleBookmark(); return true; });
        normal.Add("gA", "bind.about", _ => { w.Navigate("skull://about"); return true; });
        normal.Add("gH", "bind.help", _ => { w.Navigate("skull://help"); return true; });
        normal.Add("gh", "bind.history", _ => { w.Navigate("skull://history"); return true; });
        normal.Add("gb", "bind.bookmarks", _ => { w.Navigate("skull://bookmarks"); return true; });

        // zoom
        normal.Add("<control-plus>", "bind.zoom_in", _ => { w.Zoom(0.1); return true; });
        normal.Add("<control-minus>", "bind.zoom_out", _ => { w.Zoom(-0.1); return true; });
        normal.Add("<control-0>", "bind.zoom_reset", _ => { w.Zoom(0); return true; });

        // Follow mode is empty on purpose: while hints are up the page owns the
        // keyboard, and the overlay reports back when it is finished.
        var follow = new BindTable();
        follow.Add("Escape", "bind.escape", _ => { Follow.Cancel(w); return true; });

        modes["follow"] = follow;
        modes["all"] = all;
        modes["normal"] = normal;
        modes["insert"] = insert;
        modes["command"] = command;
        modes["passthrough"] = passthrough;
    }

    private static bool Scroll(MainWindow w, int dx, int dy)
        => Eval(w, $"window.scrollBy({dx},{dy})");

    private static bool Eval(MainWindow w, string script)
    {
        w.Eval(script);
        return true;
    }
}
