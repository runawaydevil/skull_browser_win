using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SkullWins.App.Browser;
using SkullWins.Core;
using SkullWins.Protocols;
using SkullWins.Storage;

namespace SkullWins.App;

public partial class MainWindow : Window
{
    private const string Scheme = "skull";
    /// <summary>
    /// Where a fresh window and a fresh tab land. Google by default; rc.lua can
    /// point it anywhere, including skull://newtab for the built-in start page.
    /// </summary>
    public static string StartUri { get; set; } = "https://www.google.com";

    private CoreWebView2Environment? _env;
    private readonly List<Tab> _tabs = new();
    private int _current = -1;

    private readonly Locale _locale = new();
    private readonly Dictionary<string, BindTable> _modes = new(StringComparer.Ordinal);
    private BindTable _active = new();
    private string _mode = "normal";
    private string _buffer = "";
    private string _pendingCommand = "";
    private string _searchTerm = "";
    private bool _searchForward = true;

    private HistoryStore? _history;
    private BookmarkStore? _bookmarks;
    private readonly GopherClient _gopher = new();
    private string _inputScript = "";

    public MainWindow()
    {
        InitializeComponent();
        CmdBox.PreviewKeyDown += OnCommandBarKey;
        Loaded += async (_, _) => await Boot();
    }

    private Tab? Current => _current >= 0 && _current < _tabs.Count ? _tabs[_current] : null;

    // --------------------------------------------------------------- startup

    private async Task Boot()
    {
        Profile.EnsureDir();
        LoadLocales();
        Binds.Install(this, _modes, _locale);
        UseMode("normal");

        _inputScript = Profile.Read("skull-input.js") ?? "";

        try
        {
            _history = new HistoryStore(Profile.HistoryDb);
            _bookmarks = new BookmarkStore(Profile.BookmarksDb);
        }
        catch (Exception ex)
        {
            // Losing history is bad; refusing to launch is worse.
            Log("storage unavailable: " + ex.Message);
        }

        // Custom schemes are frozen once the browser process starts, so every
        // scheme the browser will ever understand is declared right here.
        var schemes = new List<CoreWebView2CustomSchemeRegistration>
        {
            new(Scheme) { TreatAsSecure = true, HasAuthorityComponent = true },
            new("gopher") { TreatAsSecure = false, HasAuthorityComponent = true },
            new("gemini") { TreatAsSecure = false, HasAuthorityComponent = true },
        };

        try
        {
            _env = await CoreWebView2Environment.CreateAsync(
                null, Profile.WebViewProfile,
                new CoreWebView2EnvironmentOptions(customSchemeRegistrations: schemes));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "WebView2 runtime not available.\n\n" + ex.Message,
                "Skull Wins", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown(1);
            return;
        }

        RunUserConfig();

        var startUris = App.StartupUris.Count > 0 ? App.StartupUris : new List<string> { StartUri };
        foreach (var uri in startUris) { await NewTab(uri); }

        // Windows refuses to let one process hand the foreground to another, but
        // a window may always raise itself. Without this the browser can open
        // behind whatever was already on screen and swallow the first keys.
        Activate();
        Current?.View.Focus();


        UpdateStatus();
    }

    private void LoadLocales()
    {
        foreach (var language in new[] { "en", "pt_BR" })
        {
            var source = Profile.Read("locale/" + language + ".lua");
            if (source is null) { continue; }
            _locale.Load(language, LuaTables.ParseFlat(source));
        }

        _locale.Use(_locale.Choose(
            App.LocaleOverride, Environment.GetEnvironmentVariable("SKULL_LOCALE")));
    }

    /// <summary>
    /// Run the user's rc.lua if they have one. A broken config is reported and
    /// skipped; it never stops the browser from opening.
    /// </summary>
    private void RunUserConfig()
    {
        if (!File.Exists(Profile.RcLua)) { return; }

        using var lua = new LuaRuntime(_locale, Log);
        var error = lua.DoFile(Profile.RcLua);
        if (error is not null)
        {
            Notify("rc.lua: " + error);
            Log("rc.lua failed, using defaults: " + error);
        }
    }

    // ------------------------------------------------------------------ tabs

    /// <summary>Fire and forget wrapper, so a bind can stay synchronous.</summary>
    public void OpenTab(string uri) => _ = NewTab(uri);

    /// <summary>
    /// A page can forge the key messages that open tabs, because the injected
    /// script and a hostile copy of it look identical on the wire. It cannot be
    /// told apart, so it is capped instead: past this point a runaway page
    /// annoys the user rather than exhausting the machine.
    /// </summary>
    public const int MaxTabs = 50;

    public async Task NewTab(string uri)
    {
        try { await CreateTab(uri); }
        catch (Exception ex)
        {
            // Called from async void handlers, so an escape here takes the whole
            // process with it. One tab failing must cost one tab.
            Log("could not open " + uri + ": " + ex);
            Notify(_locale.Translate("notify.tabfailed", ex.Message));
        }
    }

    private async Task CreateTab(string uri)
    {
        if (_env is null) { return; }

        if (_tabs.Count >= MaxTabs)
        {
            Notify(_locale.Translate("notify.toomanytabs", MaxTabs));
            return;
        }

        var view = new WebView2 { Visibility = Visibility.Collapsed };
        TabArea.Children.Add(view);

        var tab = new Tab(view);
        _tabs.Add(tab);

        await view.EnsureCoreWebView2Async(_env);

        // The tab was reachable while that await was pending, so it may have
        // been closed already. Touching a disposed control here would throw
        // from a continuation nobody is watching.
        if (!_tabs.Contains(tab)) { return; }

        var core = view.CoreWebView2;
        if (core is null) { return; }

        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        core.AddWebResourceRequestedFilter(Scheme + "://*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("gopher://*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("gemini://*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(_inputScript);

        // Web messages carry no proof of origin: the keyboard script runs in
        // every page, so a hostile page can forge anything the real one sends.
        // The tab is passed through so the handler can tell whether the message
        // came from the tab the user is actually looking at.
        core.WebMessageReceived += (_, e) => OnWebMessage(e.WebMessageAsJson, tab);
        core.FrameCreated += (_, e) =>
            e.Frame.WebMessageReceived += (_, fe) => OnWebMessage(fe.WebMessageAsJson, tab);

        core.DocumentTitleChanged += (_, _) => { tab.Title = core.DocumentTitle; RenderTabs(); };
        core.SourceChanged += (_, _) => UpdateStatus();
        core.NavigationCompleted += (_, _) => OnNavigated(tab);
        core.NewWindowRequested += async (_, e) =>
        {
            e.Handled = true;
            await NewTab(e.Uri);
        };
        core.ProcessFailed += (_, e) =>
        {
            switch (e.ProcessFailedKind)
            {
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                    Notify(_locale.Translate("notify.tabcrashed"));
                    break;

                // The shared engine process died, so every tab is dead with it.
                // Nothing here can be recovered by reloading one of them.
                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    Log("browser process exited: " + e.Reason);
                    Notify(_locale.Translate("notify.enginedied"));
                    break;

                default:
                    Log("webview process failed: " + e.ProcessFailedKind + " " + e.Reason);
                    break;
            }
        };

        core.Navigate(uri);
        Select(_tabs.Count - 1);
    }

    public void CloseTab()
    {
        if (_tabs.Count == 0) { return; }

        // Closing the last tab closes the browser, same as every other browser.
        if (_tabs.Count == 1) { Close(); return; }

        var tab = _tabs[_current];
        TabArea.Children.Remove(tab.View);
        tab.View.Dispose();
        _tabs.RemoveAt(_current);
        Select(Math.Min(_current, _tabs.Count - 1));
    }

    public void NextTab() => Select((_current + 1) % Math.Max(_tabs.Count, 1));

    public void PrevTab() => Select((_current - 1 + _tabs.Count) % Math.Max(_tabs.Count, 1));

    private void Select(int index)
    {
        if (_tabs.Count == 0) { _current = -1; return; }

        _current = Math.Clamp(index, 0, _tabs.Count - 1);
        for (var i = 0; i < _tabs.Count; i++)
        {
            _tabs[i].View.Visibility = i == _current ? Visibility.Visible : Visibility.Collapsed;
        }

        Current?.View.Focus();
        RenderTabs();
        UpdateStatus();
    }

    private void OnNavigated(Tab tab)
    {
        PushMode();
        tab.View.Focus();
        UpdateStatus();

        var uri = tab.Uri;
        if (_history is not null && uri.Length > 0
            && !uri.StartsWith("skull://", StringComparison.OrdinalIgnoreCase))
        {
            try { _history.Add(uri, tab.Title); } catch { /* history is not worth a crash */ }
        }
    }

    // ----------------------------------------------------------------- modes

    /// <summary>
    /// Open the command bar with something already typed. Without this, the one
    /// thing every new user wants to do first, type an address, has no
    /// discoverable key at all.
    /// </summary>
    public void OpenCommand(string prefill)
    {
        _pendingCommand = prefill;
        UseMode("command");
    }

    public void UseMode(string mode)
    {
        _mode = mode;
        _buffer = "";

        // Rebuilt per mode change, not per keystroke: the "all" table folds into
        // whichever mode is active, and the mode wins on conflict.
        var table = _modes.TryGetValue(mode, out var m) ? m : new BindTable();
        var all = _modes.TryGetValue("all", out var a) ? a : new BindTable();
        _active = table.MergedWith(all);

        if (mode == "command")
        {
            // WebView2 hosts its own child window, and that window holds the
            // Win32 keyboard focus. CmdBox.Focus() moves only WPF's logical
            // focus, so without this the typing still goes to the page and the
            // command bar sits there empty.
            TakeKeyboardFromWebView();

            CmdBox.Visibility = Visibility.Visible;
            CmdBox.Text = _pendingCommand.StartsWith('/') || _pendingCommand.StartsWith('?')
                ? _pendingCommand
                : ":" + _pendingCommand;
            CmdBox.CaretIndex = CmdBox.Text.Length;
            CmdBox.Focus();
            _pendingCommand = "";
        }
        else
        {
            CmdBox.Visibility = Visibility.Collapsed;
            CmdBox.Text = "";
            Current?.View.Focus();
        }

        PushMode();
        UpdateStatus();
    }

    /// <summary>
    /// Mirror the mode into every frame. The injected script has to decide
    /// synchronously whether to swallow a key, and the mode lives out here, so
    /// it gets pushed down on every change rather than asked for.
    /// </summary>
    private void PushMode()
    {
        var json = JsonSerializer.Serialize(new { type = "mode", mode = _mode });
        foreach (var tab in _tabs)
        {
            try { tab.View.CoreWebView2?.PostWebMessageAsJson(json); } catch { }
        }
    }

    private void OnWebMessage(string json, Tab from)
    {
        // A background tab has no business driving the browser. This alone
        // stops a page in another tab from acting while the user is elsewhere.
        if (!ReferenceEquals(from, Current)) { return; }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch { return; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return; }
            if (!root.TryGetProperty("type", out var kind)) { return; }

            switch (kind.GetString())
            {
                case "hello":
                    PushMode();
                    break;

                // The hint overlay runs its own key loop inside the page and
                // tells us when it is done, so follow mode never gets stuck.
                case "mode-request":
                    // The overlay uses this to say it is finished. It can only
                    // ever return to normal, and only from follow mode, so a
                    // page cannot push the browser into a mode of its choosing.
                    if (_mode == "follow") { UseMode("normal"); }
                    break;

                // luakit switches to insert mode the moment a form field takes
                // focus, and without that, clicking a search box and typing
                // fires key bindings instead of writing text. Entering insert
                // is a reduction in what the browser will act on, so a page
                // asking for it cannot gain anything by lying.
                case "editable":
                {
                    var editable = root.TryGetProperty("on", out var on) && on.GetBoolean();
                    if (editable && _mode == "normal") { UseMode("insert"); }
                    else if (!editable && _mode == "insert") { UseMode("normal"); }
                    break;
                }

                case "follow":
                {
                    // Only meaningful while the hint overlay is up. Outside that
                    // window it is a forgery, and even inside it the target is
                    // attacker-influenced, so it goes through the allow-list.
                    if (_mode != "follow") { break; }

                    var target = root.TryGetProperty("uri", out var u) ? u.GetString() : null;
                    UseMode("normal");

                    if (target is not null && Trust.IsPageNavigable(target))
                    {
                        OpenTab(target);
                    }
                    break;
                }

                case "key":
                    var key = root.GetProperty("key").GetString() ?? "";
                    var mods = ReadModifiers(root);
                    var result = SkullWins.Core.Dispatcher.Feed(
                        _active, key, mods, _buffer, bufferEnabled: _mode == "normal");
                    _buffer = result.Buffer;
                    UpdateStatus();
                    break;
            }
        }
    }

    private static string ReadModifiers(JsonElement root)
    {
        if (!root.TryGetProperty("mods", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var parts = new List<string>();
        foreach (var m in arr.EnumerateArray())
        {
            var s = m.GetString();
            if (!string.IsNullOrEmpty(s)) { parts.Add(s); }
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join('-', parts);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    /// <summary>
    /// Pull the Win32 keyboard focus back to the WPF window, out of the child
    /// window WebView2 creates for itself.
    /// </summary>
    private void TakeKeyboardFromWebView()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero) { SetFocus(handle); }
        }
        catch (Exception ex)
        {
            Log("could not move keyboard focus: " + ex.Message);
        }
    }

    private void OnCommandBarKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            UseMode("normal");
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            var text = CmdBox.Text;
            UseMode("normal");

            // The bar doubles as the search prompt, the way it does in vim.
            if (text.StartsWith('/') || text.StartsWith('?'))
            {
                _searchForward = text[0] == '/';
                Find(text[1..].Trim());
            }
            else
            {
                Commands.Run(this, text.TrimStart(':').Trim());
            }

            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ page logic

    public void Eval(string script)
    {
        try { _ = Current?.View.CoreWebView2?.ExecuteScriptAsync(script); } catch { }
    }

    /// <summary>
    /// Navigate from the command bar. A person typed this, so it gets the full
    /// resolver, including file: paths and search fallback.
    /// </summary>
    public void Navigate(string input)
    {
        var uri = Uris.Resolve(input);
        try { Current?.View.CoreWebView2?.Navigate(uri); }
        catch (Exception ex) { Notify(ex.Message); }
    }

    public void Reload(bool skipCache = false)
    {
        var core = Current?.View.CoreWebView2;
        if (core is null) { return; }

        // Reload() honours the cache. To skip it, ask the page to reload with
        // the flag, which is what R does in every browser with vim keys.
        if (skipCache) { Eval("location.reload(true)"); }
        else { core.Reload(); }
    }

    public void Stop() => Current?.View.CoreWebView2?.Stop();

    public void Back(int times = 1)
    {
        for (var i = 0; i < Math.Max(1, times); i++)
        {
            if (Current?.View.CanGoBack != true) { break; }
            Current.View.GoBack();
        }
    }

    public void Forward(int times = 1)
    {
        for (var i = 0; i < Math.Max(1, times); i++)
        {
            if (Current?.View.CanGoForward != true) { break; }
            Current.View.GoForward();
        }
    }

    /// <summary>Jump to a tab by index; out of range clamps to the ends.</summary>
    public void SelectTab(int index) => Select(index);

    /// <summary>Move the current tab left or right in the strip.</summary>
    public void MoveTab(int offset)
    {
        if (_tabs.Count < 2 || Current is null) { return; }

        var target = Math.Clamp(_current + offset, 0, _tabs.Count - 1);
        if (target == _current) { return; }

        var tab = _tabs[_current];
        _tabs.RemoveAt(_current);
        _tabs.Insert(target, tab);
        Select(target);
    }

    /// <summary>Copy the current address to the clipboard.</summary>
    public void YankUri()
    {
        var uri = CurrentUri;
        if (uri.Length == 0) { return; }

        try
        {
            Clipboard.SetText(uri);
            Notify(_locale.Translate("notify.yanked", uri));
        }
        catch (Exception ex)
        {
            // The clipboard is shared with every other program and can be
            // locked by any of them.
            Notify(_locale.Translate("notify.yankfailed", ex.Message));
        }
    }

    /// <summary>Open the command bar ready for an in-page search.</summary>
    public void OpenSearch(bool forward)
    {
        _searchForward = forward;
        OpenCommand(forward ? "/" : "?");
    }

    /// <summary>
    /// Run an in-page search. WebView2 has no find API of its own, so this
    /// drives the DevTools protocol, which is what the engine's own find bar
    /// uses underneath.
    /// </summary>
    public async void Find(string term)
    {
        var core = Current?.View.CoreWebView2;
        if (core is null) { return; }

        _searchTerm = term;
        if (term.Length == 0) { return; }

        try
        {
            var args = JsonSerializer.Serialize(new
            {
                text = term,
                findNext = false,
                searchInFrames = true,
                matchCase = false,
                backward = !_searchForward,
            });
            await core.CallDevToolsProtocolMethodAsync("Page.searchInResource", args);
            Eval(FindScript(term, _searchForward));
        }
        catch (Exception ex)
        {
            Log("find failed: " + ex.Message);
        }
    }

    public void FindAgain(bool forward)
    {
        if (_searchTerm.Length == 0) { return; }
        Eval(FindScript(_searchTerm, forward));
    }

    /// <summary>
    /// window.find is old, non-standard and present in every Chromium. For an
    /// in-page search that is exactly what is wanted: no UI, wraps around, and
    /// it moves the real selection so the page scrolls to the hit.
    /// </summary>
    private static string FindScript(string term, bool forward)
    {
        var json = JsonSerializer.Serialize(term);
        return $"window.find({json}, false, {(forward ? "false" : "true")}, true, false, true, false)";
    }

    public void Zoom(double delta)
    {
        if (Current is null) { return; }
        Current.View.ZoomFactor = delta == 0
            ? 1.0
            : Math.Clamp(Current.View.ZoomFactor + delta, 0.25, 5.0);
        UpdateStatus();
    }

    public void ToggleBookmark()
    {
        if (_bookmarks is null || Current is null) { return; }

        var uri = Current.Uri;
        if (uri.Length == 0) { return; }

        // A locked database is a routine event, not a reason to lose every tab.
        try
        {
            if (_bookmarks.Contains(uri))
            {
                _bookmarks.Remove(uri);
                Notify(_locale.Translate("notify.unbookmarked", Current.Title));
            }
            else
            {
                _bookmarks.Add(uri, Current.Title);
                Notify(_locale.Translate("notify.bookmarked", Current.Title));
            }
        }
        catch (Exception ex)
        {
            Notify(_locale.Translate("notify.storage", ex.Message));
        }
    }

    public string CurrentUri => Current?.Uri ?? "";
    public Locale Translator => _locale;
    public HistoryStore? History => _history;
    public BookmarkStore? Bookmarks => _bookmarks;
    public IReadOnlyDictionary<string, BindTable> Modes => _modes;

    // ------------------------------------------------------- scheme pipeline

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_env is null) { return; }

        var uri = e.Request.Uri;

        if (uri.StartsWith("skull://", StringComparison.OrdinalIgnoreCase))
        {
            Respond(e, Internal(uri), "text/html");
            return;
        }

        if (uri.StartsWith("gopher://", StringComparison.OrdinalIgnoreCase))
        {
            // Gopher needs the network, and this event is synchronous. The
            // deferral keeps the request open until the socket answers, which is
            // the same shape as request:finish() in the Linux version.
            var deferral = e.GetDeferral();
            _ = FetchGopher(uri, e, deferral);
            return;
        }

        if (uri.StartsWith("gemini://", StringComparison.OrdinalIgnoreCase))
        {
            Respond(e, Pages.Error(_locale, "error.scheme",
                "gemini is registered but not implemented in 0.01", uri), "text/html");
        }
    }

    private async Task FetchGopher(
        string uri, CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Deferral deferral)
    {
        try
        {
            var response = await _gopher.FetchAsync(uri);
            var (_, _, type, _) = Gopher.ParseUri(uri);

            string html;
            if (!response.Ok)
            {
                html = Pages.Error(_locale, "error.gopher", response.Error!, uri);
            }
            else if (type is '1' or '7')
            {
                html = Pages.GopherMenu(_locale, uri, Gopher.ParseMenu(response.Body));
            }
            else if (type is '0')
            {
                html = Pages.GopherText(uri, Encoding.UTF8.GetString(response.Body));
            }
            else
            {
                // Binary item: hand the raw bytes over and let the engine decide
                // whether to render or download it.
                Respond(e, response.Body, "application/octet-stream");
                return;
            }

            Respond(e, html, "text/html");
        }
        catch (Exception ex)
        {
            Respond(e, Pages.Error(_locale, "error.gopher", ex.Message, uri), "text/html");
        }
        finally
        {
            deferral.Complete();
        }
    }

    private string Internal(string uri)
    {
        var page = uri["skull://".Length..].TrimEnd('/');
        var slash = page.IndexOf('/');
        if (slash >= 0) { page = page[..slash]; }

        return page.ToLowerInvariant() switch
        {
            "about" => Pages.About(_locale, Facts()),
            "help" or "binds" => Pages.Help(_locale, HelpRows()),
            "history" => Read(() => Pages.History(_locale, _history?.Recent(200) ?? [])),
            "bookmarks" => Read(() => Pages.Bookmarks(_locale, _bookmarks?.All() ?? [])),
            "newtab" or "" => Pages.NewTab(_locale),
            "log" => Pages.GopherText("skull://log", ReadLog()),
            _ => Pages.Error(_locale, "error.scheme", "no such internal page: " + page, uri),
        };
    }

    /// <summary>Gather everything skull://about reports.</summary>
    private AboutFacts Facts() => new(
        BuildDate: SystemInfo.BuildDate,
        Commit: SystemInfo.Commit,
        Runtime: _env?.BrowserVersionString ?? "-",
        DotNet: SystemInfo.DotNet,
        Os: SystemInfo.Os,
        ProfileDir: Profile.Dir);

    /// <summary>
    /// Render a page that reads the database. A failure here shows an error
    /// page, which is what the user opened the page to find out about anyway.
    /// </summary>
    private string Read(Func<string> render)
    {
        try { return render(); }
        catch (Exception ex)
        {
            Log("page read failed: " + ex.Message);
            return Pages.Error(_locale, "error.storage", ex.Message, "skull://");
        }
    }

    private IEnumerable<(string, string)> HelpRows()
    {
        foreach (var mode in new[] { "normal", "insert", "command" })
        {
            if (!_modes.TryGetValue(mode, out var table)) { continue; }
            foreach (var bind in table.All)
            {
                if (bind.Description.Length > 0) { yield return (bind.Trigger, bind.Description); }
            }
        }
    }

    private void Respond(CoreWebView2WebResourceRequestedEventArgs e, string html, string mime)
        => Respond(e, Encoding.UTF8.GetBytes(html), mime);

    private void Respond(CoreWebView2WebResourceRequestedEventArgs e, byte[] body, string mime)
    {
        try
        {
            e.Response = _env!.CreateWebResourceResponse(
                new SelfClosingStream(new MemoryStream(body)),
                200, "OK", "Content-Type: " + mime + "; charset=utf-8");
        }
        catch (Exception ex)
        {
            Log("failed to answer " + e.Request.Uri + ": " + ex.Message);
        }
    }

    // --------------------------------------------------------------- chrome

    private void RenderTabs()
    {
        TabStrip.Items.Clear();

        for (var i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var selected = i == _current;
            var label = tab.Title.Length > 0 ? tab.Title : tab.Uri;
            if (label.Length > 34) { label = label[..33] + "…"; }

            TabStrip.Items.Add(new TextBlock
            {
                Text = " " + (i + 1) + " " + label + " ",
                Padding = new Thickness(6, 4, 6, 4),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(selected
                    ? Color.FromRgb(0x9f, 0xd1, 0x8a)
                    : Color.FromRgb(0x7a, 0x7a, 0x7a)),
                Background = new SolidColorBrush(selected
                    ? Color.FromRgb(0x22, 0x22, 0x22)
                    : Colors.Transparent),
            });
        }
    }

    private void UpdateStatus()
    {
        var modeLabel = _locale.Translate("mode." + _mode);
        StatusMode.Text = modeLabel.Length > 0 ? modeLabel : "";

        // With no tab open there is nothing to show and nothing to guess from,
        // so the status bar says how to get somewhere.
        StatusUri.Text = Current?.Uri is { Length: > 0 } uri
            ? uri
            : _locale.Translate("status.hint");

        var right = new StringBuilder();
        if (_buffer.Length > 0) { right.Append(_buffer).Append("  "); }
        if (_tabs.Count > 0) { right.Append('[').Append(_current + 1).Append('/').Append(_tabs.Count).Append(']'); }
        StatusRight.Text = right.ToString();
    }

    public void Notify(string message)
    {
        StatusUri.Text = message;
        Log(message);
    }

    public static void Log(string line)
    {
        try
        {
            Profile.EnsureDir();
            File.AppendAllText(Profile.LogFile,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
        }
        catch { /* a failing log must never take the browser with it */ }
    }

    private static string ReadLog()
    {
        try
        {
            if (!File.Exists(Profile.LogFile)) { return "(log is empty)"; }
            var lines = File.ReadAllLines(Profile.LogFile);
            return string.Join('\n', lines.TakeLast(400));
        }
        catch (IOException ex)
        {
            return "could not read the log: " + ex.Message;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var tab in _tabs) { try { tab.View.Dispose(); } catch { } }
        _history?.Dispose();
        _bookmarks?.Dispose();
        base.OnClosed(e);
    }
}

/// <summary>One tab: the control plus whatever the page last told us about itself.</summary>
public sealed class Tab(WebView2 view)
{
    public WebView2 View { get; } = view;
    public string Title { get; set; } = "";
    public string Uri => View.CoreWebView2?.Source ?? "";
}

/// <summary>
/// WebView2 reads the response stream and then walks away without closing it
/// (WebView2Feedback issue 2513). This wrapper disposes itself once the reader
/// hits the end, so a browsing session does not leak a stream per request.
/// </summary>
internal sealed class SelfClosingStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int read;
        try
        {
            read = inner.Read(buffer, offset, count);
        }
        catch
        {
            inner.Dispose();
            throw;
        }

        if (read == 0) { inner.Dispose(); }
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
