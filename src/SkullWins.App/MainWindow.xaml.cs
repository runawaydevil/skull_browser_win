using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private const string StartUri = "skull://newtab";

    private CoreWebView2Environment? _env;
    private readonly List<Tab> _tabs = new();
    private int _current = -1;

    private readonly Locale _locale = new();
    private readonly Signals _signals = new();
    private readonly Dictionary<string, BindTable> _modes = new(StringComparer.Ordinal);
    private BindTable _active = new();
    private string _mode = "normal";
    private string _buffer = "";

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

    public async Task NewTab(string uri)
    {
        if (_env is null) { return; }

        var view = new WebView2 { Visibility = Visibility.Collapsed };
        TabArea.Children.Add(view);

        var tab = new Tab(view);
        _tabs.Add(tab);

        await view.EnsureCoreWebView2Async(_env);
        var core = view.CoreWebView2;

        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        core.AddWebResourceRequestedFilter(Scheme + "://*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("gopher://*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("gemini://*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        await core.AddScriptToExecuteOnDocumentCreatedAsync(_inputScript);

        core.WebMessageReceived += (_, e) => OnWebMessage(e.WebMessageAsJson);
        core.FrameCreated += (_, e) =>
            e.Frame.WebMessageReceived += (_, fe) => OnWebMessage(fe.WebMessageAsJson);

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
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
            {
                Notify("tab crashed, press r to reload");
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
            CmdBox.Visibility = Visibility.Visible;
            CmdBox.Text = ":";
            CmdBox.CaretIndex = 1;
            CmdBox.Focus();
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

    private void OnWebMessage(string json)
    {
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
                    UseMode(root.GetProperty("mode").GetString() ?? "normal");
                    break;

                case "follow":
                    OpenTab(root.GetProperty("uri").GetString() ?? "skull://newtab");
                    UseMode("normal");
                    break;

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

    private void OnCommandBarKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            UseMode("normal");
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            var line = CmdBox.Text.TrimStart(':').Trim();
            UseMode("normal");
            Commands.Run(this, line);
            e.Handled = true;
        }
    }

    // ------------------------------------------------------------ page logic

    public void Eval(string script)
    {
        try { _ = Current?.View.CoreWebView2?.ExecuteScriptAsync(script); } catch { }
    }

    public void Navigate(string input)
    {
        var uri = Uris.Resolve(input);
        try { Current?.View.CoreWebView2?.Navigate(uri); }
        catch (Exception ex) { Notify(ex.Message); }
    }

    public void Reload() => Current?.View.CoreWebView2?.Reload();
    public void Stop() => Current?.View.CoreWebView2?.Stop();
    public void Back() => Current?.View.GoBack();
    public void Forward() => Current?.View.GoForward();

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
            "about" => Pages.About(_locale, _env?.BrowserVersionString ?? "?"),
            "help" or "binds" => Pages.Help(_locale, HelpRows()),
            "history" => Pages.History(_locale, _history?.Recent(200) ?? []),
            "bookmarks" => Pages.Bookmarks(_locale, _bookmarks?.All() ?? []),
            "newtab" or "" => Pages.NewTab(_locale),
            "log" => Pages.GopherText("skull://log", ReadLog()),
            _ => Pages.Error(_locale, "error.scheme", "no such internal page: " + page, uri),
        };
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
        StatusUri.Text = Current?.Uri ?? "";

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
