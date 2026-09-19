using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SkullWins.App;

/// <summary>
/// Facts about the running program and the machine under it, gathered for the
/// about page.
///
/// Every reader is wrapped: a missing registry key or a refused query must
/// produce a dash in one row, never an exception that takes down the page that
/// people open when something is already wrong.
/// </summary>
public static class SystemInfo
{
    public static string BuildDate => BuildInfo.Date + " UTC";
    public static string Commit => BuildInfo.Commit;
    public static string Branch => BuildInfo.Branch;
    public static string Configuration => BuildInfo.Configuration;

    public static string BuildYear
    {
        get
        {
            var ok = DateTime.TryParse(
                BuildInfo.Date, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var when);
            return ok ? when.Year.ToString(CultureInfo.InvariantCulture) : "";
        }
    }

    public static string DotNet => RuntimeInformation.FrameworkDescription;

    public static string Architecture => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    public static string OsArchitecture => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

    /// <summary>
    /// The marketing name plus the build number, because "Windows 10.0.26200"
    /// tells a bug report less than "Windows 11 Pro (build 26200)".
    /// </summary>
    public static string Os
    {
        get
        {
            var name = Registry("ProductName") ?? "Windows";
            var build = Registry("CurrentBuildNumber");
            var display = Registry("DisplayVersion");

            // Windows 11 still reports "Windows 10 ..." in ProductName.
            if (int.TryParse(build, out var b) && b >= 22000)
            {
                name = name.Replace("Windows 10", "Windows 11", StringComparison.Ordinal);
            }

            var parts = new List<string> { name };
            if (!string.IsNullOrEmpty(display)) { parts.Add(display); }
            if (!string.IsNullOrEmpty(build)) { parts.Add("build " + build); }
            return string.Join(" ", parts);
        }
    }

    public static string Memory
    {
        get
        {
            try
            {
                var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                return bytes > 0
                    ? (bytes / 1024.0 / 1024 / 1024).ToString("0.#", CultureInfo.InvariantCulture) + " GB"
                    : "-";
            }
            catch { return "-"; }
        }
    }

    public static string Cpus => Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);

    public static string Uptime
    {
        get
        {
            try
            {
                var span = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
                if (span.TotalHours >= 1) { return (int)span.TotalHours + "h " + span.Minutes + "m"; }
                if (span.TotalMinutes >= 1) { return (int)span.TotalMinutes + "m " + span.Seconds + "s"; }
                return (int)span.TotalSeconds + "s";
            }
            catch { return "-"; }
        }
    }

    /// <summary>
    /// Where the executable actually is. Under a single-file build this is the
    /// real .exe, not the temporary folder its contents are unpacked into.
    /// </summary>
    public static string ExecutablePath
    {
        get
        {
            try { return Environment.ProcessPath ?? "-"; }
            catch { return "-"; }
        }
    }

    public static bool IsSingleFile
    {
        get
        {
            try
            {
                // A single-file build has no managed assembly on disk next to it.
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                return string.IsNullOrEmpty(asm.Location);
            }
            catch { return false; }
        }
    }

    private static string? Registry(string value)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue(value)?.ToString();
        }
        catch { return null; }
    }
}
