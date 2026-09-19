using SkullWins.Storage;
using Xunit;

namespace SkullWins.Tests;

/// <summary>
/// Which identity is offered for which request.
///
/// A mistake here is not a rendering bug, it is a credential handed to a
/// capsule that never asked for it. The prefix cases below are the ones that
/// matter: "/ab" must not match a scope of "/a/", and a naive string prefix
/// comparison says it does.
/// </summary>
public class IdentityScopeTests
{
    private const string Host = "capsule.test";
    private const int Port = 1965;

    [Theory]
    [InlineData("/journal/2026/post.gmi", "/journal/2026/")]
    [InlineData("/journal/2026/", "/journal/2026/")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("/page.gmi", "/")]
    [InlineData("/a/b/c.gmi?query=x", "/a/b/")]
    [InlineData("no/leading/slash.gmi", "/no/leading/")]
    public void A_path_scopes_to_its_directory(string path, string expected)
        => Assert.Equal(expected, IdentityStore.Normalise(path));

    [Fact]
    public void An_identity_covers_everything_below_its_scope()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/journal/");

        Assert.Equal("me", s.Match(Host, Port, "/journal/"));
        Assert.Equal("me", s.Match(Host, Port, "/journal/post.gmi"));
        Assert.Equal("me", s.Match(Host, Port, "/journal/2026/post.gmi"));
    }

    [Fact]
    public void An_identity_does_not_escape_its_scope()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/journal/");

        Assert.Null(s.Match(Host, Port, "/"));
        Assert.Null(s.Match(Host, Port, "/other/"));
    }

    [Fact]
    public void A_sibling_directory_with_a_shared_prefix_does_not_match()
    {
        // The bug this exists to prevent: "/journalism/" starts with the same
        // characters as "/journal/" but is a different part of the capsule.
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/journal/");

        Assert.Null(s.Match(Host, Port, "/journalism/post.gmi"));
        Assert.Null(s.Match(Host, Port, "/journal-archive/x.gmi"));
    }

    [Fact]
    public void A_different_host_never_matches()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/");
        Assert.Null(s.Match("elsewhere.test", Port, "/"));
    }

    [Fact]
    public void A_different_port_never_matches()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, 1965, "/");
        Assert.Null(s.Match(Host, 1966, "/"));
    }

    [Fact]
    public void The_deepest_matching_scope_wins()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("general", Host, Port, "/");
        s.Attach("special", Host, Port, "/journal/");

        Assert.Equal("general", s.Match(Host, Port, "/about.gmi"));
        Assert.Equal("special", s.Match(Host, Port, "/journal/post.gmi"));
    }

    [Fact]
    public void Attaching_twice_to_one_scope_replaces_rather_than_doubles()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("first", Host, Port, "/journal/");
        s.Attach("second", Host, Port, "/journal/");

        Assert.Equal("second", s.Match(Host, Port, "/journal/x"));
        Assert.Single(s.All());
    }

    [Fact]
    public void Detaching_removes_only_that_scope()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/");
        s.Attach("me", Host, Port, "/journal/");

        Assert.True(s.Detach(Host, Port, "/journal/"));
        Assert.Equal("me", s.Match(Host, Port, "/journal/x"));   // falls back to "/"
        Assert.Single(s.All());
    }

    [Fact]
    public void Detaching_something_unattached_reports_it()
    {
        using var s = IdentityStore.InMemory();
        Assert.False(s.Detach(Host, Port, "/nothing/"));
    }

    [Fact]
    public void Forgetting_an_identity_detaches_it_everywhere()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/");
        s.Attach("me", "other.test", Port, "/");
        s.Attach("someone-else", Host, Port, "/journal/");

        Assert.Equal(2, s.DetachAll("me"));
        Assert.Single(s.All());
        Assert.Null(s.Match(Host, Port, "/about.gmi"));
    }

    [Fact]
    public void A_file_path_attaches_to_its_directory()
    {
        // Attaching while reading one post should cover its siblings, which is
        // what a capsule's own links lead to next.
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/journal/2026/post.gmi");

        Assert.Equal("me", s.Match(Host, Port, "/journal/2026/another.gmi"));
        Assert.Null(s.Match(Host, Port, "/journal/2025/old.gmi"));
    }

    [Fact]
    public void A_query_string_is_not_part_of_the_scope()
    {
        using var s = IdentityStore.InMemory();
        s.Attach("me", Host, Port, "/search?term");
        Assert.Equal("me", s.Match(Host, Port, "/search?other"));
    }

    [Fact]
    public void Nothing_attached_means_nothing_offered()
    {
        using var s = IdentityStore.InMemory();
        Assert.Null(s.Match(Host, Port, "/"));
    }
}
