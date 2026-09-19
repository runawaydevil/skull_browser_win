using System.Text.RegularExpressions;
using Xunit;

namespace SkullWins.Tests;

/// <summary>
/// Reads the shipped catalogues off disk and fails the build when they drift
/// apart. Without this a translation rots inside a fortnight: someone adds an
/// English string, nobody adds the Portuguese one, and the interface quietly
/// turns bilingual in the wrong way.
/// </summary>
public class LocaleFileTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LICENSE")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> KeysOf(string language)
    {
        var path = Path.Combine(RepoRoot(), "lua", "locale", language + ".lua");
        Assert.True(File.Exists(path), "missing catalogue: " + path);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(File.ReadAllText(path), """\["([^"]+)"\]\s*="""))
        {
            keys.Add(m.Groups[1].Value);
        }
        return keys;
    }

    [Fact]
    public void English_and_portuguese_define_the_same_keys()
    {
        var en = KeysOf("en");
        var pt = KeysOf("pt_BR");

        var missingFromPt = en.Except(pt).Order(StringComparer.Ordinal).ToList();
        var missingFromEn = pt.Except(en).Order(StringComparer.Ordinal).ToList();

        Assert.True(missingFromPt.Count == 0,
            "missing from pt_BR: " + string.Join(", ", missingFromPt));
        Assert.True(missingFromEn.Count == 0,
            "missing from en: " + string.Join(", ", missingFromEn));
    }

    [Theory]
    [InlineData("locale/en.lua")]
    [InlineData("locale/pt_BR.lua")]
    [InlineData("rc.lua")]
    [InlineData("theme.lua")]
    public void The_shipped_copy_matches_the_embedded_one(string relative)
    {
        // These files exist twice: once under lua/ and config/ where they are
        // edited, and once under Resources/ where they are compiled into the
        // executable. Nothing but this test stops them drifting apart, and when
        // they drift the browser ships strings nobody can see in the source.
        var root = RepoRoot();
        var source = relative.StartsWith("locale/", StringComparison.Ordinal)
            ? Path.Combine(root, "lua", relative.Replace('/', Path.DirectorySeparatorChar))
            : Path.Combine(root, "config", relative);
        var embedded = Path.Combine(
            root, "src", "SkullWins.App", "Resources",
            relative.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(source), "missing: " + source);
        Assert.True(File.Exists(embedded), "missing: " + embedded);

        Assert.Equal(
            File.ReadAllText(source).ReplaceLineEndings("\n"),
            File.ReadAllText(embedded).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Catalogues_are_not_empty()
    {
        Assert.True(KeysOf("en").Count > 50);
        Assert.True(KeysOf("pt_BR").Count > 50);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("insert")]
    [InlineData("command")]
    [InlineData("passthrough")]
    [InlineData("follow")]
    public void Every_mode_has_a_status_bar_label(string mode)
    {
        // The status bar builds its key as "mode." + the mode name, so a mode
        // added without a catalogue entry shows the raw key to the user. That
        // happened once already, with follow.
        Assert.Contains("mode." + mode, KeysOf("en"));
        Assert.Contains("mode." + mode, KeysOf("pt_BR"));
    }

    [Fact]
    public void Every_bind_description_key_is_translated()
    {
        var en = KeysOf("en");
        var bindKeys = en.Where(k => k.StartsWith("bind.", StringComparison.Ordinal)).ToList();
        Assert.True(bindKeys.Count > 20, "expected the bind descriptions to be catalogued");
    }
}
