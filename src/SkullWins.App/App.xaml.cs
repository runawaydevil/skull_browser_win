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
          --check           validate rc.lua and exit
          --locale=LANG     force a language, e.g. --locale=pt_BR
          --version, -v     print the version
          --help, -h        print this

        Configuration lives in %APPDATA%\skull. The browser runs from its own
        built-in copy until you run --init, after which files on disk win.
        """;

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
