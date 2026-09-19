using SkullWins.Core;
using Xunit;

namespace SkullWins.Tests;

/// <summary>
/// Where the profile lands. The decision is separated from the filesystem
/// precisely so these cases can be reached without a read-only stick, a
/// network share or an install under Program Files.
/// </summary>
public class ProfileRootTests
{
    private const string Beside = @"D:\stick\skull-data";
    private const string Roaming = @"C:\Users\someone\AppData\Roaming\skull";

    private static readonly Func<string, bool> Writable = _ => true;
    private static readonly Func<string, bool> ReadOnly = _ => false;

    private static ProfileChoice Decide(
        ProfileRoot.Request request, string? beside = Beside, Func<string, bool>? canWrite = null)
        => ProfileRoot.Decide(request, beside, Roaming, canWrite ?? Writable);

    [Fact]
    public void A_writable_folder_beside_the_executable_wins()
    {
        var choice = Decide(new ProfileRoot.Request());
        Assert.Equal(Beside, choice.Path);
        Assert.True(choice.IsPortable);
    }

    [Fact]
    public void A_read_only_folder_falls_back_instead_of_failing()
    {
        // Installed under Program Files, or run from a locked stick. The
        // browser still has to open.
        var choice = Decide(new ProfileRoot.Request(), canWrite: ReadOnly);
        Assert.Equal(Roaming, choice.Path);
        Assert.Equal(RootKind.Roaming, choice.Kind);
        Assert.Equal("not writable", choice.Reason);
    }

    [Fact]
    public void An_unknown_executable_directory_falls_back()
    {
        var choice = Decide(new ProfileRoot.Request(), beside: null);
        Assert.Equal(Roaming, choice.Path);
        Assert.Equal("no executable directory", choice.Reason);
    }

    [Fact]
    public void Explicit_path_beats_everything()
    {
        var choice = Decide(new ProfileRoot.Request(
            ExplicitPath: @"E:\elsewhere", ForcePortable: true, ForceRoaming: true));
        Assert.Equal(@"E:\elsewhere", choice.Path);
        Assert.Equal(RootKind.Explicit, choice.Kind);
    }

    [Fact]
    public void No_portable_forces_roaming_even_when_beside_is_writable()
    {
        var choice = Decide(new ProfileRoot.Request(ForceRoaming: true));
        Assert.Equal(Roaming, choice.Path);
        Assert.False(choice.IsPortable);
    }

    [Fact]
    public void Portable_forced_and_available_is_used()
    {
        var choice = Decide(new ProfileRoot.Request(ForcePortable: true));
        Assert.Equal(Beside, choice.Path);
        Assert.Equal("--portable", choice.Reason);
    }

    [Fact]
    public void Portable_forced_but_unwritable_fails_loudly()
    {
        // Asked for by name. Quietly using somewhere else would leave the user
        // believing their data travels with the binary when it does not.
        var ex = Assert.Throws<InvalidOperationException>(
            () => Decide(new ProfileRoot.Request(ForcePortable: true), canWrite: ReadOnly));
        Assert.Contains("cannot write", ex.Message);
    }

    [Fact]
    public void Portable_forced_with_no_executable_directory_fails_loudly()
        => Assert.Throws<InvalidOperationException>(
            () => Decide(new ProfileRoot.Request(ForcePortable: true), beside: null));

    [Fact]
    public void The_writability_probe_is_only_asked_about_the_portable_folder()
    {
        var asked = new List<string>();
        ProfileRoot.Decide(
            new ProfileRoot.Request(), Beside, Roaming,
            dir => { asked.Add(dir); return true; });

        Assert.Equal([Beside], asked);
    }
}
