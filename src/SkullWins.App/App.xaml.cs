using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using SkullWins.App.Browser;
using SkullWins.Core;

namespace SkullWins.App;

public partial class App : Application
{
    public static List<string> StartupUris { get; } = new();
    public static string? LocaleOverride { get; private set; }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    protected override void OnStartup(StartupEventArgs e)
    {
        if (HandleConsoleCommands(e.Args)) { return; }

        // Last resort. Everything that can fail is guarded where it fails, but
        // a browser that vanishes without a word is the worst way to lose a
        // session. This turns an unexpected escape into a message and a line in
        // the log, and keeps the window open.
        DispatcherUnhandledException += (_, args) =>
        {
            SkullWins.App.MainWindow.Log("unhandled: " + args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);
        new MainWindow().Show();
    }

    /// <summary>
    /// The switches that print something and exit. A WPF app has no console of
    /// its own, so it borrows the parent's before writing.
    ///
    /// Returns true when the process is done and no window should open.
    /// </summary>
    private bool HandleConsoleCommands(string[] args)
    {
        foreach (var arg in args)
        {
            switch (arg)
            {
                case "--help" or "-h":
                    Print(Usage());
                    Shutdown(0);
                    return true;

                case "--version" or "-v":
                    Print("skull " + Pages.Version);
                    Shutdown(0);
                    return true;

                case "--init":
                    Print(Init());
                    Shutdown(0);
                    return true;

                case "--sysinfo":
                    Print(SysInfo());
                    Shutdown(0);
                    return true;

                case "--check":
                    var problem = Check();
                    Print(problem ?? "configuration ok");
                    Shutdown(problem is null ? 0 : 1);
                    return true;
            }

            if (arg.StartsWith("--locale=", StringComparison.Ordinal))
            {
                LocaleOverride = arg["--locale=".Length..];
                continue;
            }

            if (!arg.StartsWith('-')) { StartupUris.Add(Uris.Resolve(arg)); }
        }

        return false;
    }

    private static void Print(string text)
    {
        AttachConsole(-1);
        Console.WriteLine(text);
    }

    private static string Usage() => """
        skull - a keyboard-driven browser for Windows

        usage: skull [options] [url ...]

          --init            write the configuration into %APPDATA%\skull
          --sysinfo         print what skull://about reports, and exit
          --check           validate rc.lua and exit
          --locale=LANG     force a language, e.g. --locale=pt_BR
          --version, -v     print the version
          --help, -h        print this

        Configuration lives in %APPDATA%\skull. The browser runs from its own
        built-in copy until you run --init, after which files on disk win.
        """;

    /// <summary>
    /// The same facts skull://about shows, on the console. Worth having on its
    /// own: a bug report can paste this without a screenshot, and it proves the
    /// about page reads the machine rather than reciting constants.
    /// </summary>
    private static string SysInfo()
    {
        string runtime;
        try
        {
            runtime = Microsoft.Web.WebView2.Core.CoreWebView2Environment
                .GetAvailableBrowserVersionString() ?? "not installed";
        }
        catch (Exception ex)
        {
            runtime = "not installed (" + ex.GetType().Name + ")";
        }

        var rows = new (string, string)[]
        {
            ("version", Pages.Version + " (" + Pages.Codename + ")"),
            ("author", Pages.Author + ", " + Pages.Homepage),
            ("built", SystemInfo.BuildDate),
            ("commit", SystemInfo.Commit + " on " + SystemInfo.Branch),
            ("configuration", SystemInfo.Configuration),
            ("packaging", SystemInfo.IsSingleFile ? "single file, portable" : "folder"),
            ("engine", "WebView2 " + runtime),
            ("framework", SystemInfo.DotNet),
            ("architecture", SystemInfo.Architecture),
            ("operating system", SystemInfo.Os + " " + SystemInfo.OsArchitecture),
            ("processors", SystemInfo.Cpus),
            ("memory", SystemInfo.Memory),
            ("profile", Profile.Dir),
            ("executable", SystemInfo.ExecutablePath),
        };

        var sb = new StringBuilder();
        foreach (var (label, value) in rows)
        {
            sb.AppendLine(label.PadRight(18) + value);
        }
        return sb.ToString().TrimEnd();
    }

    private static string Init()
    {
        var written = Profile.WriteFactoryCopy();
        if (written.Count == 0)
        {
            return "nothing to do, " + Profile.Dir + " is already populated";
        }

        var sb = new StringBuilder();
        sb.AppendLine("wrote " + written.Count + " file(s) to " + Profile.Dir);
        foreach (var name in written) { sb.AppendLine("  " + name); }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Validate the user's rc.lua without opening a window.</summary>
    private static string? Check()
    {
        if (!File.Exists(Profile.RcLua)) { return null; }

        var locale = new Locale();
        using var lua = new LuaRuntime(locale, _ => { });
        var error = lua.DoFile(Profile.RcLua);
        return error is null ? null : Profile.RcLua + ": " + error;
    }
}
