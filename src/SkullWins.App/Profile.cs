using System.IO;
using System.Reflection;
using System.Text;
using SkullWins.Core;

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

    /// <summary>The folder written beside the executable when portable.</summary>
    public const string PortableFolder = "skull-data";

    private static ProfileChoice? _choice;

    /// <summary>
    /// The chosen root and how it was chosen. Resolved once, on first read, so
    /// the command line has already been parsed by then.
    ///
    /// Everything derived goes through here rather than through separate
    /// fields. Splitting them once meant --sysinfo printed the portable path
    /// while claiming it was roaming, because it read the path without ever
    /// asking how it had been decided.
    /// </summary>
    public static ProfileChoice Choice => _choice ??= Resolve();

    public static string Dir => Choice.Path;

    /// <summary>True when the profile sits beside the executable.</summary>
    public static bool IsPortable => Choice.IsPortable;

    public static RootKind Kind => Choice.Kind;

    /// <summary>Why that root was chosen, shown in about and --sysinfo.</summary>
    public static string Reason => Choice.Reason;

    public static string WebViewProfile => Path.Combine(Dir, "profile");
    public static string HistoryDb => Path.Combine(Dir, "history.db");
    public static string BookmarksDb => Path.Combine(Dir, "bookmarks.db");
    public static string TrustDb => Path.Combine(Dir, "trust.db");
    public static string IdentitiesDir => Path.Combine(Dir, "identities");
    public static string RcLua => Path.Combine(Dir, "rc.lua");
    public static string ThemeLua => Path.Combine(Dir, "theme.lua");
    public static string LogFile => Path.Combine(Dir, "skull.log");

    public static string RoamingDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    /// <summary>
    /// The folder beside the executable, or null under a build where that
    /// cannot be determined.
    /// </summary>
    public static string? BesideExecutable
    {
        get
        {
            try
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) { return null; }

                var dir = Path.GetDirectoryName(exe);
                return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, PortableFolder);
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Pick the profile root.
    ///
    /// Beside the executable when that can be written to, so a browser carried
    /// on a stick carries its history, its pinned certificates and its
    /// identities with it. %APPDATA% when it cannot, because a copy installed
    /// under Program Files must still open.
    /// </summary>
    /// <summary>
    /// Where the profile goes. The decision itself lives in
    /// <see cref="ProfileRoot"/> so it can be tested without a real disk; this
    /// only supplies the candidates and the writability probe.
    /// </summary>
    public static ProfileChoice Resolve() => ProfileRoot.Decide(
        new ProfileRoot.Request(App.ProfileOverride, App.ForcePortable, App.ForceRoaming),
        BesideExecutable,
        RoamingDir,
        CanWrite);

    /// <summary>
    /// Probe by writing, not by reading attributes. A network share or a group
    /// policy can refuse a write that the attributes say is allowed.
    /// </summary>
    private static bool CanWrite(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public static void EnsureDir()
    {
        Directory.CreateDirectory(Dir);
        Directory.CreateDirectory(WebViewProfile);
        Directory.CreateDirectory(IdentitiesDir);
    }

    /// <summary>
    /// Copy an existing %APPDATA% profile into a fresh portable one, once.
    ///
    /// Without this, someone who has been using the browser sees an empty
    /// history the first time they run a portable copy, which looks exactly
    /// like data loss. Only runs when the destination is genuinely new, so it
    /// can never overwrite work.
    /// </summary>
    public static IReadOnlyList<string> MigrateFromRoaming()
    {
        var moved = new List<string>();

        if (!IsPortable) { return moved; }
        if (string.Equals(Dir, RoamingDir, StringComparison.OrdinalIgnoreCase)) { return moved; }
        if (!Directory.Exists(RoamingDir)) { return moved; }

        // "Fresh" means no databases and no config. The probe file and the
        // directories EnsureDir just created do not count.
        var alreadyUsed = File.Exists(HistoryDb) || File.Exists(BookmarksDb)
            || File.Exists(RcLua) || File.Exists(ThemeLua);
        if (alreadyUsed) { return moved; }

        foreach (var name in new[] { "history.db", "bookmarks.db", "trust.db", "rc.lua", "theme.lua" })
        {
            var from = Path.Combine(RoamingDir, name);
            if (!File.Exists(from)) { continue; }

            try
            {
                File.Copy(from, Path.Combine(Dir, name), overwrite: false);
                moved.Add(name);
            }
            catch (IOException)
            {
                // A file in use is skipped rather than failing the whole move.
            }
        }

        return moved;
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
