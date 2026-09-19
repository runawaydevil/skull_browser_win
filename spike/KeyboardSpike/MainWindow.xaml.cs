using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using NLua;

namespace SkullWins.Spike;

/// <summary>
/// Spike 0 do Skull Wins. Prova duas coisas antes de qualquer outra linha existir:
/// que teclas comuns podem ser capturadas antes da pagina, e que um esquema
/// proprio registrado na criacao do ambiente responde de verdade.
/// </summary>
public partial class MainWindow : Window
{
    private const string Scheme = "skull";
    private const string StartUri = "skull://spike/";

    private Lua? _lua;
    private CoreWebView2Environment? _env;
    private string _mode = "normal";
    private readonly List<CoreWebView2Frame> _frames = new();

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "skull-spike.log");

    /// <summary>
    /// Diario do spike. A statusbar serve para quem esta olhando a janela; o
    /// arquivo serve para conferir depois o que de fato chegou, em especial se
    /// a injecao alcancou os frames cross-origin.
    /// </summary>
    private static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line + Environment.NewLine);
        }
        catch
        {
            // Log que falha nao pode derrubar o browser.
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        CmdBox.PreviewKeyDown += OnCmdBoxKey;
    }

    // ---------------------------------------------------------------- startup

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Boot();
        }
        catch (Exception ex)
        {
            Log("boot FALHOU: " + ex);
            StatusLeft.Text = "falha na inicializacao";
            StatusRight.Text = ex.Message;
            MessageBox.Show(ex.ToString(), "Skull Wins spike");
        }
    }

    private async Task Boot()
    {
        // Esquemas customizados sao imutaveis depois que o processo do browser
        // sobe, entao tudo que o browser vai entender precisa ser declarado aqui.
        //
        // CustomSchemeRegistrations e somente-leitura e nasce nula: a lista entra
        // pelo construtor. Chamar .Add() nela da NullReferenceException.
        var schemes = new List<CoreWebView2CustomSchemeRegistration>
        {
            new(Scheme)
            {
                TreatAsSecure = true,
                HasAuthorityComponent = true,
            },
        };

        var options = new CoreWebView2EnvironmentOptions(
            customSchemeRegistrations: schemes);

        Log("boot: criando ambiente com esquema " + Scheme);
        var profile = Path.Combine(Path.GetTempPath(), "skull-spike-profile");
        _env = await CoreWebView2Environment.CreateAsync(null, profile, options);
        await Web.EnsureCoreWebView2Async(_env);

        var core = Web.CoreWebView2;
        Log("boot: ambiente ok, runtime " + _env.BrowserVersionString);

        // Desliga os atalhos nativos do Edge para que Ctrl+F e companhia possam
        // ser religados do lado do Lua. Criterio 5 do spike.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsWebMessageEnabled = true;

        core.AddWebResourceRequestedFilter(Scheme + "://*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        var script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "skull-input.js"));
        await core.AddScriptToExecuteOnDocumentCreatedAsync(script);

        core.WebMessageReceived += (_, args) => HandleMessage(args.WebMessageAsJson, "top");
        core.FrameCreated += OnFrameCreated;
        core.NavigationCompleted += (_, _) =>
        {
            PushMode();
            // Sem isto o foco de teclado fica no Window do WPF e a pagina
            // nunca ve um keydown, entao a captura em JS nunca roda.
            // O Windows impede que outro processo roube o foreground, mas a
            // propria janela pode se trazer para a frente. Sem isto o spike
            // abre atras de tudo e nenhuma tecla chega.
            Activate();
            Topmost = true;
            Topmost = false;
            Web.Focus();
            Log("foco devolvido ao webview");
        };

        InitLua();
        PushMode();
        Log("boot: pronto, navegando para " + StartUri);
        core.Navigate(StartUri);
    }

    private void OnFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        var frame = e.Frame;
        _frames.Add(frame);
        Log("frame criado: " + frame.Name);
        frame.WebMessageReceived += (_, args) => HandleMessage(args.WebMessageAsJson, "frame");
        frame.Destroyed += (_, _) => _frames.Remove(frame);
    }

    // ------------------------------------------------------------------- lua

    private void InitLua()
    {
        _lua = new Lua();
        _lua.State.Encoding = Encoding.UTF8;

        var self = GetType();
        _lua.RegisterFunction("set_mode", this, self.GetMethod(nameof(LuaSetMode))!);
        _lua.RegisterFunction("eval_js", this, self.GetMethod(nameof(LuaEvalJs))!);
        _lua.RegisterFunction("notify", this, self.GetMethod(nameof(LuaNotify))!);
        _lua.RegisterFunction("reload", this, self.GetMethod(nameof(LuaReload))!);

        _lua.DoFile(Path.Combine(AppContext.BaseDirectory, "spike.lua"));
    }

    public void LuaSetMode(string mode) => SetMode(mode);

    public void LuaNotify(string text) => StatusRight.Text = text;

    public void LuaReload() => Web.CoreWebView2?.Reload();

    public void LuaEvalJs(string source)
        => _ = Web.CoreWebView2?.ExecuteScriptAsync(source);

    /// <summary>
    /// A borda host para Lua. Erro de Lua vira aviso na statusbar, nunca derruba
    /// o processo. E o equivalente do pcall que o plano exige em toda chamada.
    /// </summary>
    private bool DispatchKey(string key, string mods)
    {
        if (_lua is null) { return false; }

        try
        {
            var fn = _lua.GetFunction("on_key");
            var ret = fn?.Call(key, mods);
            return ret is { Length: > 0 } && ret[0] is true;
        }
        catch (Exception ex)
        {
            StatusRight.Text = "erro de lua: " + ex.Message;
            return false;
        }
    }

    // -------------------------------------------------------------- mensagens

    private void HandleMessage(string json, string origin)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { return; }
            if (!root.TryGetProperty("type", out var typeProp)) { return; }

            switch (typeProp.GetString())
            {
                case "hello":
                {
                    var url = root.TryGetProperty("url", out var u) ? u.GetString() : "?";
                    var where = root.TryGetProperty("frame", out var f) ? f.GetString() : origin;
                    Log("injetado [" + where + "] " + url);
                    StatusRight.Text = "injetado [" + where + "] " + Shorten(url);
                    PushMode();
                    break;
                }

                case "key":
                {
                    var key = root.GetProperty("key").GetString() ?? "";
                    var mods = ReadMods(root);
                    var handled = DispatchKey(key, mods);
                    var label = mods.Length == 0 ? key : "<" + mods + "-" + key + ">";
                    Log("tecla " + label + " frame=" + origin + " modo=" + _mode
                        + (handled ? " TRATADA" : " nao ligada"));
                    StatusRight.Text = handled
                        ? label + " [" + origin + "]"
                        : label + " [" + origin + "] nao ligado";
                    break;
                }
            }
        }
    }

    private static string ReadMods(JsonElement root)
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

    private static string Shorten(string? s)
        => s is null ? "?" : (s.Length <= 48 ? s : s.Substring(0, 48) + "...");

    // ------------------------------------------------------------------ modos

    private void SetMode(string mode)
    {
        if (_mode == mode) { return; }
        _mode = mode;

        Log("modo -> " + mode);
        StatusLeft.Text = "-- " + mode.ToUpperInvariant() + " --";

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
            Web.Focus();
        }

        try { _lua?.GetFunction("on_mode_changed")?.Call(mode); }
        catch (Exception ex) { StatusRight.Text = "erro de lua: " + ex.Message; }

        PushMode();
    }

    /// <summary>
    /// Espelha o modo em cada frame. O JS precisa decidir de forma sincrona se
    /// engole a tecla, e o modo mora aqui, entao ele e empurrado a cada troca.
    /// </summary>
    private void PushMode()
    {
        var json = JsonSerializer.Serialize(new { type = "mode", mode = _mode });

        try { Web.CoreWebView2?.PostWebMessageAsJson(json); } catch { }

        foreach (var frame in _frames.ToArray())
        {
            try { frame.PostWebMessageAsJson(json); } catch { }
        }
    }

    private void OnCmdBoxKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SetMode("normal");
            e.Handled = true;
        }
        else if (e.Key == Key.Return)
        {
            StatusRight.Text = "comando: " + CmdBox.Text;
            SetMode("normal");
            e.Handled = true;
        }
    }

    // ------------------------------------------------------- esquema skull://

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (_env is null) { return; }

        // TODO Fase 5: embrulhar o stream numa classe que se descarta sozinha.
        // O WebView2 nao fecha o stream que recebe (WebView2Feedback #2513).
        var bytes = Encoding.UTF8.GetBytes(TestPage);
        var stream = new MemoryStream(bytes);

        e.Response = _env.CreateWebResourceResponse(
            stream, 200, "OK", "Content-Type: text/html; charset=utf-8");
    }

    private const string TestPage = @"<!doctype html>
<html lang=""pt-BR"">
<head>
<meta charset=""utf-8"">
<title>Skull Wins :: spike</title>
<style>
  body { background:#141414; color:#ddd; font:15px/1.6 Georgia, serif;
         margin:0; padding:2rem 3rem; }
  h1 { font:600 22px Consolas, monospace; color:#9fd18a; }
  code { background:#222; padding:1px 5px; color:#e8c07d; }
  input { background:#1e1e1e; color:#eee; border:1px solid #444;
         padding:8px; font:14px Consolas, monospace; width:min(420px,100%); }
  .filler p { color:#888; }
  iframe { width:100%; height:180px; border:1px solid #444; background:#fff; }
  .box { border:1px solid #333; padding:1rem 1.2rem; margin:1.5rem 0; }
</style>
</head>
<body>

<h1>Skull Wins :: spike de teclado</h1>
<p>Servido por <code>skull://spike/</code>, ou seja, o esquema proprio registrado
   na criacao do ambiente esta respondendo.</p>

<div class=""box"">
  <p><b>1 e 3.</b> Clique na caixa abaixo para focar. Em NORMAL,
     <code>j</code> deve rolar a pagina e nao digitar nada aqui.</p>
  <input id=""probe"" type=""text"" placeholder=""foque aqui e tecle j"">
  <p><b>2.</b> Tecle <code>i</code>, volte na caixa: agora <code>j</code> digita.
     <code>Escape</code> volta para NORMAL com o foco ainda aqui dentro.</p>
</div>

<div class=""box"">
  <p><b>4.</b> <code>:</code> abre a barra de comando do WPF embaixo.<br>
     <b>5.</b> <code>Ctrl+F</code> nao pode abrir a busca do Edge.<br>
     <b>6.</b> O iframe abaixo e de outra origem. A statusbar diz se a injecao
     chegou nele.</p>
</div>

<div class=""box"">
  <p>iframe cross-origin:</p>
  <iframe src=""https://example.com""></iframe>
</div>

<div class=""filler"">
<p>rolagem: j desce, k sobe, d e u meia tela, G vai ao fim.</p>
</div>

<script>
  var filler = document.querySelector('.filler');
  for (var i = 1; i <= 60; i++) {
    var p = document.createElement('p');
    p.textContent = 'linha de rolagem ' + i;
    filler.appendChild(p);
  }
</script>

</body>
</html>";
}
