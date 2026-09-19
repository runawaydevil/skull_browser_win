using SkullWins.Core;
using Xunit;

namespace SkullWins.Tests;

public class SignalTests
{
    [Fact]
    public void Handlers_run_in_insertion_order()
    {
        var s = new Signals();
        var seen = new List<int>();
        s.Add("go", _ => { seen.Add(1); return null; });
        s.Add("go", _ => { seen.Add(2); return null; });
        s.Add("go", _ => { seen.Add(3); return null; });
        s.Emit("go");
        Assert.Equal(new[] { 1, 2, 3 }, seen);
    }

    [Fact]
    public void First_non_null_return_stops_the_chain()
    {
        var s = new Signals();
        var reached = false;
        s.Add("go", _ => null);
        s.Add("go", _ => "veto");
        s.Add("go", _ => { reached = true; return null; });

        Assert.Equal("veto", s.Emit("go"));
        Assert.False(reached);
    }

    [Fact]
    public void All_handlers_declining_returns_null()
    {
        var s = new Signals();
        s.Add("go", _ => null);
        s.Add("go", _ => null);
        Assert.Null(s.Emit("go"));
    }

    [Fact]
    public void False_is_a_veto_because_it_is_not_null()
    {
        // This is how adblock blocks a request: it returns false from
        // send-request and the chain stops there.
        var s = new Signals();
        s.Add("request", _ => false);
        Assert.Equal(false, s.Emit("request"));
    }

    [Fact]
    public void A_handler_may_remove_handlers_mid_dispatch()
    {
        var s = new Signals();
        Func<object?[], object?> second = _ => null;
        s.Add("go", _ => { s.Remove("go", second); return null; });
        s.Add("go", second);
        var ex = Record.Exception(() => s.Emit("go"));
        Assert.Null(ex);
    }

    [Fact]
    public void Emitting_an_unknown_signal_is_harmless()
        => Assert.Null(new Signals().Emit("nobody-listens"));

    [Fact]
    public void Arguments_reach_the_handler()
    {
        var s = new Signals();
        s.Add("nav", args => args[0]);
        Assert.Equal("https://a.test", s.Emit("nav", "https://a.test"));
    }

    [Theory]
    [InlineData("load-status", true)]
    [InlineData("property::uri", true)]
    [InlineData("scheme-request::gopher", true)]
    [InlineData("", false)]
    [InlineData("bad name", false)]
    [InlineData("bad!", false)]
    public void Validates_signal_names(string name, bool ok)
        => Assert.Equal(ok, Signals.IsValidName(name));
}

public class TriggerTests
{
    [Theory]
    [InlineData("j", "j")]
    [InlineData("J", "<shift-j>")]
    [InlineData("<Control-c>", "<control-c>")]
    [InlineData("<C-c>", "<control-c>")]
    [InlineData("<ctrl-c>", "<control-c>")]
    [InlineData("<A-x>", "<mod1-x>")]
    [InlineData("<Alt-x>", "<mod1-x>")]
    [InlineData("Escape", "Escape")]
    [InlineData("gg", "gg")]
    public void Normalises_triggers(string input, string expected)
        => Assert.Equal(expected, Triggers.Normalise(input));

    [Fact]
    public void Modifier_order_does_not_matter()
        => Assert.Equal(
            Triggers.Normalise("<shift-control-a>"),
            Triggers.Normalise("<control-shift-a>"));

    [Fact]
    public void Uppercase_key_inside_brackets_implies_shift()
        => Assert.Equal("<control-shift-a>", Triggers.Normalise("<control-A>"));
}

public class BindTableTests
{
    private static BindTable TableWith(params string[] triggers)
    {
        var t = new BindTable();
        foreach (var trigger in triggers) { t.Add(trigger, "", _ => true); }
        return t;
    }

    [Fact]
    public void Adding_the_same_trigger_replaces_it()
    {
        var t = new BindTable();
        t.Add("j", "first", _ => true);
        t.Add("j", "second", _ => true);
        Assert.Equal(1, t.Count);
        Assert.Equal("second", t.Find("j")!.Description);
    }

    [Fact]
    public void Find_normalises_before_looking_up()
    {
        var t = TableWith("<Control-c>");
        Assert.NotNull(t.Find("<C-c>"));
    }

    [Fact]
    public void Partial_match_tracks_sequences()
    {
        var t = TableWith("gg", "gt");
        Assert.True(t.HasPartialMatch("g"));
        Assert.False(t.HasPartialMatch("z"));
        Assert.False(t.HasPartialMatch("gg"));
    }

    [Fact]
    public void Merge_lets_the_mode_win_over_the_global()
    {
        var mode = new BindTable();
        mode.Add("x", "mode", _ => true);
        var all = new BindTable();
        all.Add("x", "all", _ => true);
        all.Add("y", "all-only", _ => true);

        var merged = mode.MergedWith(all);
        Assert.Equal("mode", merged.Find("x")!.Description);
        Assert.NotNull(merged.Find("y"));
    }
}

public class DispatcherTests
{
    [Fact]
    public void A_bound_key_is_handled_and_clears_the_buffer()
    {
        var t = new BindTable();
        t.Add("j", "scroll", _ => true);
        var r = Dispatcher.Feed(t, "j", "", "", bufferEnabled: true);
        Assert.True(r.Handled);
        Assert.Equal("", r.Buffer);
    }

    [Fact]
    public void Returning_false_means_not_handled()
    {
        // The inverted convention: false is "keep looking", not "failed".
        var t = new BindTable();
        t.Add("z", "declines", _ => false);
        var r = Dispatcher.Feed(t, "z", "", "", bufferEnabled: true);
        Assert.False(r.Handled);
    }

    [Fact]
    public void Sequence_fires_on_the_second_key()
    {
        var fired = false;
        var t = new BindTable();
        t.Add("gg", "top", _ => { fired = true; return true; });

        var first = Dispatcher.Feed(t, "g", "", "", bufferEnabled: true);
        Assert.False(first.Handled);
        Assert.Equal("g", first.Buffer);

        var second = Dispatcher.Feed(t, "g", "", first.Buffer, bufferEnabled: true);
        Assert.True(second.Handled);
        Assert.True(fired);
    }

    [Fact]
    public void Buffer_drops_when_nothing_can_match()
    {
        var t = new BindTable();
        t.Add("gg", "top", _ => true);
        var r = Dispatcher.Feed(t, "q", "", "", bufferEnabled: true);
        Assert.Equal("", r.Buffer);
    }

    [Fact]
    public void Digits_are_kept_as_a_pending_count()
    {
        var t = new BindTable();
        t.Add("gt", "tab", _ => true);
        var r = Dispatcher.Feed(t, "4", "", "", bufferEnabled: true);
        Assert.Equal("4", r.Buffer);
    }

    [Fact]
    public void Counted_motion_reaches_the_bind_with_its_count()
    {
        var count = 0;
        var t = new BindTable();
        t.Add("gt", "tab", ctx => { count = ctx.Count; return true; });

        var b = "";
        foreach (var key in new[] { "4", "2", "g", "t" })
        {
            var r = Dispatcher.Feed(t, key, "", b, bufferEnabled: true);
            b = r.Buffer;
        }

        Assert.Equal(42, count);
    }

    [Fact]
    public void Modified_keys_bypass_the_buffer()
    {
        var t = new BindTable();
        t.Add("<control-f>", "find", _ => true);
        var r = Dispatcher.Feed(t, "f", "control", "gg", bufferEnabled: true);
        Assert.True(r.Handled);
    }

    [Fact]
    public void Buffer_disabled_never_accumulates()
    {
        var t = new BindTable();
        t.Add("gg", "top", _ => true);
        var r = Dispatcher.Feed(t, "g", "", "", bufferEnabled: false);
        Assert.Equal("", r.Buffer);
    }
}

public class LocaleTests
{
    private static Locale Loaded()
    {
        var l = new Locale();
        l.Load("en", new Dictionary<string, string>
        {
            ["mode.insert"] = "-- INSERT --",
            ["bookmark.added"] = "Bookmark added: {0}",
            ["only.english"] = "english only",
        });
        l.Load("pt_BR", new Dictionary<string, string>
        {
            ["mode.insert"] = "-- INSERCAO --",
            ["bookmark.added"] = "Favorito adicionado: {0}",
        });
        return l;
    }

    [Fact]
    public void Translates_in_the_active_language()
    {
        var l = Loaded();
        l.Use("pt_BR");
        Assert.Equal("-- INSERCAO --", l.Translate("mode.insert"));
    }

    [Fact]
    public void Missing_key_falls_back_to_english()
    {
        var l = Loaded();
        l.Use("pt_BR");
        Assert.Equal("english only", l.Translate("only.english"));
    }

    [Fact]
    public void Unknown_key_resolves_to_itself()
    {
        // This is what lets a user's own bind description be plain prose.
        var l = Loaded();
        Assert.Equal("Scroll to the top", l.Translate("Scroll to the top"));
    }

    [Fact]
    public void Formats_arguments()
    {
        var l = Loaded();
        l.Use("pt_BR");
        Assert.Equal("Favorito adicionado: Hacker News",
            l.Translate("bookmark.added", "Hacker News"));
    }

    [Fact]
    public void Unknown_language_falls_back_to_english()
    {
        var l = Loaded();
        l.Use("kl_KL");
        Assert.Equal("en", l.Active);
    }

    [Fact]
    public void Choose_prefers_the_configured_value()
    {
        var l = Loaded();
        Assert.Equal("pt_BR", l.Choose("pt_BR", null));
    }

    [Fact]
    public void Choose_accepts_dashes_and_bare_language()
    {
        var l = Loaded();
        l.Load("pt", new Dictionary<string, string> { ["x"] = "x" });
        Assert.Equal("pt_BR", l.Choose("pt-BR", null));
        Assert.Equal("pt", l.Choose("pt-PT", null));
    }

    [Fact]
    public void Choose_falls_back_to_the_environment()
    {
        var l = Loaded();
        Assert.Equal("pt_BR", l.Choose(null, "pt_BR"));
    }

    [Fact]
    public void Compare_reports_both_directions()
    {
        var l = Loaded();
        var (missingFromEn, missingFromPt) = l.Compare("en", "pt_BR");
        Assert.Empty(missingFromEn);
        Assert.Equal(new[] { "only.english" }, missingFromPt);
    }
}
