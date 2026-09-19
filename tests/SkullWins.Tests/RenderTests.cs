using System.Text;
using SkullWins.Core;
using SkullWins.Protocols;
using Xunit;

namespace SkullWins.Tests;

public class UriTests
{
    [Theory]
    [InlineData("https://example.com", "https://example.com")]
    [InlineData("gopher://floodgap.com", "gopher://floodgap.com")]
    [InlineData("skull://about", "skull://about")]
    [InlineData("example.com", "https://example.com")]
    [InlineData("news.ycombinator.com/news", "https://news.ycombinator.com/news")]
    [InlineData("localhost:8080", "https://localhost:8080")]
    public void Addresses_are_navigated(string input, string expected)
        => Assert.Equal(expected, Uris.Resolve(input));

    [Theory]
    [InlineData("how do i quit vim")]
    [InlineData("gopher protocol")]
    [InlineData("skull")]
    public void Prose_becomes_a_search(string input)
        => Assert.StartsWith(Uris.SearchEngine, Uris.Resolve(input));

    [Fact]
    public void Empty_input_opens_the_start_page()
        => Assert.Equal("skull://newtab", Uris.Resolve("   "));

    [Fact]
    public void Search_terms_are_escaped()
        => Assert.DoesNotContain(" ", Uris.Resolve("two words"));

    [Fact]
    public void A_windows_path_becomes_a_file_url()
        => Assert.StartsWith("file:///C:/", Uris.Resolve(@"C:\Users\pablo\notes.txt"));
}

public class LuaTableTests
{
    [Fact]
    public void Reads_a_flat_catalogue()
    {
        var table = LuaTables.ParseFlat("""
            -- a comment
            return {
                ["one"] = "first",
                ["two"] = "second",
            }
            """);

        Assert.Equal(2, table.Count);
        Assert.Equal("first", table["one"]);
    }

    [Fact]
    public void Skips_commented_out_entries()
    {
        var table = LuaTables.ParseFlat("""
            return {
                ["live"] = "yes",
            --  ["dead"] = "no",
            }
            """);

        Assert.True(table.ContainsKey("live"));
        Assert.False(table.ContainsKey("dead"));
    }

    [Fact]
    public void Unescapes_newlines_and_quotes()
    {
        var table = LuaTables.ParseFlat("""return { ["k"] = "a\nb \"q\"" }""");
        Assert.Equal("a\nb \"q\"", table["k"]);
    }

    [Fact]
    public void Garbage_yields_an_empty_table_rather_than_throwing()
        => Assert.Empty(LuaTables.ParseFlat("this is not lua at all {{{"));
}

public class PageTests
{
    private static Locale English()
    {
        var l = new Locale();
        l.Load("en", new Dictionary<string, string>());
        l.Use("en");
        return l;
    }

    private static IReadOnlyList<GopherItem> Menu(string text)
        => Gopher.ParseMenu(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void Gopher_menu_links_are_anchors()
    {
        var html = Pages.GopherMenu(English(), "gopher://h/",
            Menu("1World\t/world\th\t70\r\n"));
        Assert.Contains("<a href=\"gopher://h/1/world\">World</a>", html);
    }

    [Fact]
    public void Info_lines_are_not_links()
    {
        var html = Pages.GopherMenu(English(), "gopher://h/",
            Menu("ijust a note\t\t\t\r\n"));
        Assert.DoesNotContain("<a href", html);
        Assert.Contains("just a note", html);
    }

    [Fact]
    public void Menu_html_is_escaped()
    {
        var html = Pages.GopherMenu(English(), "gopher://h/",
            Menu("i<script>alert(1)</script>\t\t\t\r\n"));
        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Gopher_text_is_escaped_too()
    {
        var html = Pages.GopherText("gopher://h/0/x", "<b>not bold</b>");
        Assert.DoesNotContain("<b>not bold</b>", html);
        Assert.Contains("&lt;b&gt;", html);
    }

    [Fact]
    public void About_reports_version_and_author()
    {
        var html = Pages.About(English(), "153.0.0.0");
        Assert.Contains("0.01", html);
        Assert.Contains("Pablo Murad", html);
        Assert.Contains("153.0.0.0", html);
    }

    [Fact]
    public void Empty_history_says_so_instead_of_rendering_a_blank_table()
    {
        var html = Pages.History(English(), []);
        Assert.DoesNotContain("<table>", html);
    }

    [Fact]
    public void Error_page_shows_the_detail_and_the_uri()
    {
        var html = Pages.Error(English(), "error.gopher", "host not found", "gopher://nope.test");
        Assert.Contains("host not found", html);
        Assert.Contains("gopher://nope.test", html);
    }
}
