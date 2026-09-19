using SkullWins.Core;

namespace SkullWins.App.Browser;

/// <summary>
/// The default key map, taken from luakit's lib/binds.lua rather than invented.
/// Where luakit has a key for something, that key is used here even when a
/// different one would have been nicer, because muscle memory is the whole
/// point of a modal browser. Keys marked "not in luakit" below are ones this
/// browser needed and upstream has no equivalent for.
///
/// Descriptions are catalogue keys, not prose. They feed skull://help as well
/// as the status bar, and they are the largest block of translatable text in
/// the project. A key that is not in the catalogue resolves to itself, so
/// someone writing their own bind in rc.lua just writes plain English.
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
        var follow = new BindTable();

        // ---------------------------------------------------------- every mode

        all.Add("Escape", "bind.escape", _ => { w.UseMode("normal"); return true; });
        all.Add("<control-[>", "bind.escape", _ => { w.UseMode("normal"); return true; });

        // --------------------------------------------------------------- modes

        normal.Add("i", "bind.insert", _ => { w.UseMode("insert"); return true; });
        normal.Add(":", "bind.command", _ => { w.UseMode("command"); return true; });
        normal.Add("<control-z>", "bind.passthrough",
            _ => { w.UseMode("passthrough"); return true; });

        // -------------------------------------------------------------- motion

        normal.Add("j", "bind.scroll_down", c => Scroll(w, 0, 60 * c.Count));
        normal.Add("k", "bind.scroll_up", c => Scroll(w, 0, -60 * c.Count));
        normal.Add("h", "bind.scroll_left", c => Scroll(w, -60 * c.Count, 0));
        normal.Add("l", "bind.scroll_right", c => Scroll(w, 60 * c.Count, 0));
        normal.Add("Down", "bind.scroll_down", _ => Scroll(w, 0, 60));
        normal.Add("Up", "bind.scroll_up", _ => Scroll(w, 0, -60));
        normal.Add("Left", "bind.scroll_left", _ => Scroll(w, -60, 0));
        normal.Add("Right", "bind.scroll_right", _ => Scroll(w, 60, 0));

        normal.Add("^", "bind.scroll_far_left", _ => Eval(w, "window.scrollTo(0,window.scrollY)"));
        normal.Add("$", "bind.scroll_far_right",
            _ => Eval(w, "window.scrollTo(document.body.scrollWidth,window.scrollY)"));
        normal.Add("0", "bind.scroll_top", _ => Eval(w, "window.scrollTo(window.scrollX,0)"));

        normal.Add("gg", "bind.scroll_top", _ => Eval(w, "window.scrollTo(0,0)"));
        normal.Add("G", "bind.scroll_bottom",
            _ => Eval(w, "window.scrollTo(0,document.body.scrollHeight)"));
        normal.Add("%", "bind.scroll_percent",
            c => Eval(w, $"window.scrollTo(0,document.body.scrollHeight*{c.Count}/100)"));

        normal.Add("<control-d>", "bind.half_down",
            _ => Eval(w, "window.scrollBy(0,window.innerHeight/2)"));
        normal.Add("<control-u>", "bind.half_up",
            _ => Eval(w, "window.scrollBy(0,-window.innerHeight/2)"));
        normal.Add("<control-f>", "bind.page_down",
            _ => Eval(w, "window.scrollBy(0,window.innerHeight*0.9)"));
        normal.Add("<control-b>", "bind.page_up",
            _ => Eval(w, "window.scrollBy(0,-window.innerHeight*0.9)"));
        normal.Add("space", "bind.page_down",
            _ => Eval(w, "window.scrollBy(0,window.innerHeight*0.9)"));
        normal.Add("<shift-space>", "bind.page_up",
            _ => Eval(w, "window.scrollBy(0,-window.innerHeight*0.9)"));
        normal.Add("Page_Down", "bind.page_down",
            _ => Eval(w, "window.scrollBy(0,window.innerHeight*0.9)"));
        normal.Add("Page_Up", "bind.page_up",
            _ => Eval(w, "window.scrollBy(0,-window.innerHeight*0.9)"));
        normal.Add("Home", "bind.scroll_top", _ => Eval(w, "window.scrollTo(0,0)"));
        normal.Add("End", "bind.scroll_bottom",
            _ => Eval(w, "window.scrollTo(0,document.body.scrollHeight)"));

        // ---------------------------------------------------------- navigation

        normal.Add("H", "bind.back", c => { w.Back(c.Count); return true; });
        normal.Add("L", "bind.forward", c => { w.Forward(c.Count); return true; });
        normal.Add("<control-o>", "bind.back", _ => { w.Back(1); return true; });
        normal.Add("<control-i>", "bind.forward", _ => { w.Forward(1); return true; });
        normal.Add("BackSpace", "bind.back", _ => { w.Back(1); return true; });
        normal.Add("r", "bind.reload", _ => { w.Reload(false); return true; });
        normal.Add("R", "bind.reload_nocache", _ => { w.Reload(true); return true; });
        normal.Add("<control-c>", "bind.stop", _ => { w.Stop(); return true; });

        // -------------------------------------------------------------- opening

        normal.Add("o", "bind.open", _ => { w.OpenCommand("open "); return true; });
        normal.Add("t", "bind.open_tab", _ => { w.OpenCommand("tabopen "); return true; });
        normal.Add("O", "bind.open_here",
            _ => { w.OpenCommand("open " + w.CurrentUri); return true; });
        normal.Add("T", "bind.open_tab_here",
            _ => { w.OpenCommand("tabopen " + w.CurrentUri); return true; });

        // ---------------------------------------------------------------- tabs

        normal.Add("J", "bind.tab_next", _ => { w.NextTab(); return true; });
        normal.Add("K", "bind.tab_prev", _ => { w.PrevTab(); return true; });
        normal.Add("gt", "bind.tab_next", _ => { w.NextTab(); return true; });
        normal.Add("gT", "bind.tab_prev", _ => { w.PrevTab(); return true; });
        normal.Add("g0", "bind.tab_first", _ => { w.SelectTab(0); return true; });
        normal.Add("g$", "bind.tab_last", _ => { w.SelectTab(int.MaxValue); return true; });
        normal.Add("<control-Tab>", "bind.tab_next", _ => { w.NextTab(); return true; });
        normal.Add("<control-shift-Tab>", "bind.tab_prev", _ => { w.PrevTab(); return true; });
        normal.Add("<control-Page_Down>", "bind.tab_next", _ => { w.NextTab(); return true; });
        normal.Add("<control-Page_Up>", "bind.tab_prev", _ => { w.PrevTab(); return true; });
        normal.Add("<control-t>", "bind.tab_new",
            _ => { w.OpenTab(MainWindow.StartUri); return true; });
        normal.Add("<control-w>", "bind.tab_close", _ => { w.CloseTab(); return true; });
        normal.Add("d", "bind.tab_close", _ => { w.CloseTab(); return true; });
        normal.Add("<", "bind.tab_move_left", _ => { w.MoveTab(-1); return true; });
        normal.Add(">", "bind.tab_move_right", _ => { w.MoveTab(1); return true; });
        normal.Add("gy", "bind.tab_duplicate",
            _ => { w.OpenTab(w.CurrentUri); return true; });

        // ------------------------------------------------------- following links

        normal.Add("f", "bind.follow", _ => { Follow.Start(w, newTab: false); return true; });
        normal.Add("F", "bind.follow_tab", _ => { Follow.Start(w, newTab: true); return true; });

        // --------------------------------------------------------------- search

        normal.Add("/", "bind.search", _ => { w.OpenSearch(forward: true); return true; });
        normal.Add("?", "bind.search_back", _ => { w.OpenSearch(forward: false); return true; });
        normal.Add("n", "bind.search_next", _ => { w.FindAgain(true); return true; });
        normal.Add("N", "bind.search_prev", _ => { w.FindAgain(false); return true; });

        // --------------------------------------------------------------- yank

        normal.Add("y", "bind.yank", _ => { w.YankUri(); return true; });

        // ------------------------------------------------------------ internal

        // luakit uses gh for the homepage and gH for the homepage in a new tab.
        normal.Add("gh", "bind.home", _ => { w.Navigate(MainWindow.StartUri); return true; });
        normal.Add("gH", "bind.home_tab",
            _ => { w.OpenTab(MainWindow.StartUri); return true; });

        // Not in luakit: it reaches these through :commands and chrome pages.
        // A browser with no address bar needs them one key away.
        normal.Add("gA", "bind.about", _ => { w.Navigate("skull://about"); return true; });
        normal.Add("gb", "bind.bookmarks", _ => { w.Navigate("skull://bookmarks"); return true; });
        normal.Add("gB", "bind.bookmark", _ => { w.ToggleBookmark(); return true; });
        normal.Add("gi", "bind.history", _ => { w.Navigate("skull://history"); return true; });
        normal.Add("F1", "bind.help", _ => { w.Navigate("skull://help"); return true; });

        // ----------------------------------------------------------------- zoom

        normal.Add("zi", "bind.zoom_in", _ => { w.Zoom(0.1); return true; });
        normal.Add("zo", "bind.zoom_out", _ => { w.Zoom(-0.1); return true; });
        normal.Add("zz", "bind.zoom_reset", _ => { w.Zoom(0); return true; });
        normal.Add("<control-plus>", "bind.zoom_in", _ => { w.Zoom(0.1); return true; });
        normal.Add("<control-minus>", "bind.zoom_out", _ => { w.Zoom(-0.1); return true; });
        normal.Add("<control-0>", "bind.zoom_reset", _ => { w.Zoom(0); return true; });

        // --------------------------------------------------------------- quit

        normal.Add("ZZ", "bind.quit", _ => { w.Close(); return true; });
        normal.Add("ZQ", "bind.quit", _ => { w.Close(); return true; });

        // Follow mode is empty on purpose: while hints are up the page owns the
        // keyboard, and the overlay reports back when it is finished.
        follow.Add("Escape", "bind.escape", _ => { Follow.Cancel(w); return true; });

        modes["all"] = all;
        modes["normal"] = normal;
        modes["insert"] = insert;
        modes["command"] = command;
        modes["passthrough"] = passthrough;
        modes["follow"] = follow;
    }

    private static bool Scroll(MainWindow w, int dx, int dy)
        => Eval(w, $"window.scrollBy({dx},{dy})");

    private static bool Eval(MainWindow w, string script)
    {
        w.Eval(script);
        return true;
    }
}
