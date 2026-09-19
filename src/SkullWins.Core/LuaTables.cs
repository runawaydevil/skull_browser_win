using System.Text.RegularExpressions;

namespace SkullWins.Core;

/// <summary>
/// Reads a flat Lua table of string pairs, the shape a locale file returns.
///
/// This is deliberately a parser and not an eval. Locale files are data, they
/// load before anything else, and standing up the whole VM to read sixty string
/// pairs would mean a syntax error in a translation could stop the browser from
/// starting. rc.lua is real Lua and runs on the real VM; catalogues are not.
/// </summary>
public static partial class LuaTables
{
    [GeneratedRegex("""\[\s*"((?:[^"\\]|\\.)*)"\s*\]\s*=\s*"((?:[^"\\]|\\.)*)"\s*,?""")]
    private static partial Regex Pair();

    public static Dictionary<string, string> ParseFlat(string source)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in source.Split('\n'))
        {
            var text = line.TrimStart();
            if (text.StartsWith("--", StringComparison.Ordinal)) { continue; }

            var m = Pair().Match(line);
            if (m.Success)
            {
                table[Unescape(m.Groups[1].Value)] = Unescape(m.Groups[2].Value);
            }
        }

        return table;
    }

    private static string Unescape(string s) => s
        .Replace("\\n", "\n")
        .Replace("\\t", "\t")
        .Replace("\\\"", "\"")
        .Replace("\\\\", "\\");
}
