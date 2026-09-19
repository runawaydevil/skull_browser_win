using System.Text;
using SkullWins.Protocols;
using Xunit;

namespace SkullWins.Tests;

// The dirty cases come first on purpose. A gopher parser that only handles
// well-formed menus is untested; real servers are messy.

public class GopherTests
{
    private static IReadOnlyList<GopherItem> Parse(string menu)
        => Gopher.ParseMenu(Encoding.UTF8.GetBytes(menu));

    [Fact]
    public void Parses_a_plain_menu()
    {
        var menu =
            "1Floodgap Home\t/\tgopher.floodgap.com\t70\r\n" +
            "0About\t/about.txt\tgopher.floodgap.com\t70\r\n" +
            ".\r\n";

        var items = Parse(menu);

        Assert.Equal(2, items.Count);
        Assert.Equal('1', items[0].Type);
        Assert.Equal("Floodgap Home", items[0].Display);
        Assert.Equal("/about.txt", items[1].Selector);
        Assert.Equal(70, items[1].Port);
    }

    [Fact]
    public void Stops_at_the_terminating_dot()
    {
        var items = Parse("0one\t/one\thost\t70\r\n.\r\n0two\t/two\thost\t70\r\n");
        Assert.Single(items);
    }

    [Fact]
    public void Survives_a_line_with_no_tabs()
    {
        // Some servers emit bare info lines with no field separators at all.
        var items = Parse("iThis is just a notice\r\n.\r\n");
        Assert.Single(items);
        Assert.Equal('i', items[0].Type);
        Assert.Equal("This is just a notice", items[0].Display);
    }

    [Fact]
    public void Missing_port_falls_back_to_seventy()
    {
        var items = Parse("1Menu\t/menu\thost\r\n");
        Assert.Equal(70, items[0].Port);
    }

    [Fact]
    public void Non_numeric_port_falls_back_to_seventy()
    {
        var items = Parse("1Menu\t/menu\thost\tnotaport\r\n");
        Assert.Equal(70, items[0].Port);
    }

    [Fact]
    public void Accepts_lf_without_cr()
    {
        var items = Parse("1a\t/a\th\t70\n0b\t/b\th\t70\n.\n");
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Invalid_utf8_does_not_throw()
    {
        var bytes = new byte[] { (byte)'i', 0xff, 0xfe, (byte)'\r', (byte)'\n', (byte)'.', };
        var items = Gopher.ParseMenu(bytes);
        Assert.Single(items);
        Assert.Equal('i', items[0].Type);
    }

    [Fact]
    public void Blank_line_becomes_blank_info_for_spacing()
    {
        var items = Parse("iheader\thost\t70\r\n\r\nifooter\thost\t70\r\n.\r\n");
        Assert.Equal(3, items.Count);
        Assert.Equal('i', items[1].Type);
        Assert.Equal("", items[1].Display);
    }

    [Fact]
    public void Info_and_error_items_are_not_links()
    {
        Assert.False(Parse("inote\t\t\t\r\n")[0].IsLink);
        Assert.False(Parse("3boom\t\t\t\r\n")[0].IsLink);
        Assert.True(Parse("0doc\t/d\th\t70\r\n")[0].IsLink);
    }

    [Theory]
    [InlineData("gopher://gopher.floodgap.com", "gopher.floodgap.com", 70, '1', "")]
    [InlineData("gopher://gopher.floodgap.com:70/1/world", "gopher.floodgap.com", 70, '1', "/world")]
    [InlineData("gopher://example.org:7070/0/notes.txt", "example.org", 7070, '0', "/notes.txt")]
    [InlineData("host.only/7/search", "host.only", 70, '7', "/search")]
    public void Parses_uris(string uri, string host, int port, char type, string selector)
    {
        var (h, p, t, s) = Gopher.ParseUri(uri);
        Assert.Equal(host, h);
        Assert.Equal(port, p);
        Assert.Equal(type, t);
        Assert.Equal(selector, s);
    }

    [Fact]
    public void Search_request_appends_the_query()
    {
        var req = Gopher.BuildRequest("/search", "skull browser");
        Assert.Equal("/search\tskull browser\r\n", Encoding.UTF8.GetString(req));
    }

    [Fact]
    public void Plain_request_is_selector_plus_crlf()
    {
        var req = Gopher.BuildRequest("/about.txt");
        Assert.Equal("/about.txt\r\n", Encoding.UTF8.GetString(req));
    }

    [Fact]
    public void Web_link_item_yields_its_http_url()
    {
        var items = Parse("hExample\tURL:https://example.com\thost\t70\r\n");
        Assert.Equal("https://example.com", items[0].ToUri());
    }

    [Fact]
    public void Menu_item_yields_a_gopher_url()
    {
        var items = Parse("1World\t/world\tgopher.floodgap.com\t70\r\n");
        Assert.Equal("gopher://gopher.floodgap.com/1/world", items[0].ToUri());
    }

    [Fact]
    public void Non_default_port_appears_in_the_url()
    {
        var items = Parse("0Doc\t/d\texample.org\t7070\r\n");
        Assert.Equal("gopher://example.org:7070/0/d", items[0].ToUri());
    }
}
