using System.IO;
using System.Reflection;
using System.Text;

namespace SkullWins.App;

/// <summary>
/// Where the browser keeps its files, and how the shipped Lua is found.
///
/// The whole lua tree is embedded in the executable as the factory copy, so a
/// fresh install runs with nothing on disk. %APPDATA%\skull overrides it file by
/// file, and skull --init writes the factory copy out so it can be edited.
///
/// That is the same fallback the Linux version gets from /usr/local/etc/xdg,
/// rearranged to survive being a single file.
/// </summary>
public static class Profile
{
    public const string FolderName = "skull";

    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    public static string WebViewProfile => Path.Combine(Dir, "profile");
    public static string HistoryDb => Path.Combine(Dir, "history.db");
    public static string BookmarksDb => Path.Combine(Dir, "bookmarks.db");
    public static string RcLua => Path.Combine(Dir, "rc.lua");
    public static string ThemeLua => Path.Combine(Dir, "theme.lua");
    public static string LogFile => Path.Combine(Dir, "skull.log");

    public static void EnsureDir()
    {
        Directory.CreateDirectory(Dir);
        Directory.CreateDirectory(WebViewProfile);
    }

    /// <summary>
    /// Read a shipped file. The user's copy wins when it exists; otherwise the
    /// embedded factory copy is returned. Returns null when neither exists.
    /// </summary>
    public static string? Read(string relativePath)
    {
        var onDisk = Path.Combine(Dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(onDisk))
        {
            try { return File.ReadAllText(onDisk, Encoding.UTF8); }
            catch (IOException) { /* fall through to the factory copy */ }
        }

        return Embedded(relativePath);
    }

    /// <summary>Read an embedded resource by its logical path, e.g. "locale/en.lua".</summary>
    public static string? Embedded(string relativePath)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = "SkullWins.App.Resources." + relativePath.Replace('/', '.');

        using var stream = asm.GetManifestResourceStream(name);
        if (stream is null) { return null; }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public static IEnumerable<string> EmbeddedNames()
    {
        const string prefix = "SkullWins.App.Resources.";
        foreach (var name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                yield return name[prefix.Length..];
            }
        }
    }

    /// <summary>
    /// skull --init: write every embedded resource into the profile directory so
    /// the user can edit it. Existing files are left alone; this never clobbers
    /// work someone already did.
    /// </summary>
    public static IReadOnlyList<string> WriteFactoryCopy(bool overwrite = false)
    {
        EnsureDir();
        var written = new List<string>();

        foreach (var logical in EmbeddedNames())
        {
            // Resource names flatten directories, so locale.en.lua has to become
            // locale\en.lua again. Only the last dot is a real extension.
            var target = Path.Combine(Dir, LogicalToPath(logical));
            if (File.Exists(target) && !overwrite) { continue; }

            var body = Embedded(logical);
            if (body is null) { continue; }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, body, new UTF8Encoding(false));
            written.Add(LogicalToPath(logical));
        }

        return written;
    }

    private static string LogicalToPath(string logical)
    {
        var lastDot = logical.LastIndexOf('.');
        if (lastDot <= 0) { return logical; }

        var stem = logical[..lastDot].Replace('.', Path.DirectorySeparatorChar);
        return stem + logical[lastDot..];
    }
}
