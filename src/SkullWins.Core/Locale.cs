using System.Globalization;

namespace SkullWins.Core;

/// <summary>
/// Translation catalogue. Keys in the code, one table per language, English as
/// the fallback.
///
/// A key missing from the active language falls back to English. Missing from
/// both, the key itself is returned and a warning is logged. The browser never
/// breaks because a translation is late.
///
/// Bind descriptions go through here too. In luakit the desc field of a bind is
/// documentation as well as UI text, and it is the largest block of translatable
/// string in the project. A desc that is not a known key resolves to itself,
/// which means a user writing a bind in their own rc.lua just writes plain text
/// and it works. One convention serves both cases without a branch.
/// </summary>
public sealed class Locale
{
    public const string Fallback = "en";

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new();
    private string _active = Fallback;

    public string Active => _active;

    public IReadOnlyCollection<string> Languages => _tables.Keys;

    public void Load(string language, IDictionary<string, string> table)
        => _tables[language] = new Dictionary<string, string>(table, StringComparer.Ordinal);

    public bool Has(string language) => _tables.ContainsKey(language);

    /// <summary>Select a language, falling back to English when unknown.</summary>
    public void Use(string language)
        => _active = _tables.ContainsKey(language) ? language : Fallback;

    /// <summary>
    /// Pick a language from an explicit setting, an environment override, then
    /// the Windows UI culture. Tries the full tag first (pt_BR) then the bare
    /// language (pt), then falls back to English.
    /// </summary>
    public string Choose(string? configured, string? environment)
    {
        foreach (var candidate in new[] { configured, environment, CurrentCultureTag() })
        {
            if (string.IsNullOrWhiteSpace(candidate)) { continue; }

            var normalised = candidate.Replace('-', '_');
            if (_tables.ContainsKey(normalised)) { return normalised; }

            var bare = normalised.Split('_')[0];
            if (_tables.ContainsKey(bare)) { return bare; }
        }

        return Fallback;
    }

    private static string CurrentCultureTag()
    {
        try { return CultureInfo.CurrentUICulture.Name.Replace('-', '_'); }
        catch { return Fallback; }
    }

    /// <summary>Look up a key and format it with the given arguments.</summary>
    public string Translate(string key, params object?[] args)
    {
        var text = Lookup(key);
        if (args.Length == 0) { return text; }

        try { return string.Format(CultureInfo.CurrentCulture, text, args); }
        catch (FormatException) { return text; }
    }

    private string Lookup(string key)
    {
        if (_tables.TryGetValue(_active, out var active)
            && active.TryGetValue(key, out var hit))
        {
            return hit;
        }

        if (_active != Fallback
            && _tables.TryGetValue(Fallback, out var english)
            && english.TryGetValue(key, out var fallbackHit))
        {
            return fallbackHit;
        }

        // Unknown key resolves to itself. This is what lets a user's own bind
        // description be plain prose instead of a catalogue entry.
        return key;
    }

    /// <summary>
    /// Keys present in one language but not the other. The test suite fails the
    /// build on any difference, because a catalogue rots within weeks otherwise.
    /// </summary>
    public (IReadOnlyList<string> MissingFromA, IReadOnlyList<string> MissingFromB)
        Compare(string languageA, string languageB)
    {
        var a = _tables.TryGetValue(languageA, out var ta) ? ta.Keys.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);
        var b = _tables.TryGetValue(languageB, out var tb) ? tb.Keys.ToHashSet(StringComparer.Ordinal) : new HashSet<string>(StringComparer.Ordinal);

        return (b.Except(a).Order(StringComparer.Ordinal).ToList(),
                a.Except(b).Order(StringComparer.Ordinal).ToList());
    }
}
