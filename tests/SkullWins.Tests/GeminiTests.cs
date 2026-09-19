using System.Text;
using SkullWins.Protocols;
using Xunit;

namespace SkullWins.Tests;

public class GeminiHeaderTests
{
    private static (int Status, string Meta, int Offset) Parse(string response)
        => Gemini.ParseHeader(Encoding.UTF8.GetBytes(response));

    [Theory]
    [InlineData("20 text/gemini\r\n", 20, "text/gemini")]
    [InlineData("31 gemini://elsewhere/\r\n", 31, "gemini://elsewhere/")]
    [InlineData("51 Not found\r\n", 51, "Not found")]
    [InlineData("60 Certificate required\r\n", 60, "Certificate required")]
    [InlineData("44 10\r\n", 44, "10")]
    public void Reads_status_and_meta(string response, int status, string meta)
    {
        var (s, m, _) = Parse(response);
        Assert.Equal(status, s);
        Assert.Equal(meta, m);
    }

    [Fact]
    public void A_header_may_carry_no_meta()
    {
        var (status, meta, _) = Parse("20\r\n");
        Assert.Equal(20, status);
        Assert.Equal("", meta);
    }

    [Fact]
    public void The_body_starts_after_the_header()
    {
        var response = "20 text/gemini\r\n# Hello";
        var (_, _, offset) = Parse(response);
        Assert.Equal("# Hello", response[offset..]);
    }

    [Fact]
    public void A_body_containing_crlf_does_not_confuse_the_header()
    {
        var (status, meta, offset) = Parse("20 text/gemini\r\nline one\r\nline two\r\n");
        Assert.Equal(20, status);
        Assert.Equal("text/gemini", meta);
        Assert.Equal(16, offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("hello\r\n")]
    [InlineData("2x text/gemini\r\n")]
    [InlineData("20 text/gemini")]      // never terminated
    public void A_broken_header_reports_zero(string response)
        => Assert.Equal(0, Parse(response).Status);

    [Theory]
    [InlineData(10, GeminiClass.Input)]
    [InlineData(11, GeminiClass.Input)]
    [InlineData(20, GeminiClass.Success)]
    [InlineData(30, GeminiClass.Redirect)]
    [InlineData(31, GeminiClass.Redirect)]
    [InlineData(44, GeminiClass.TemporaryFailure)]
    [InlineData(51, GeminiClass.PermanentFailure)]
    [InlineData(60, GeminiClass.CertificateRequired)]
    [InlineData(99, GeminiClass.Invalid)]
    public void Classifies_every_status(int status, GeminiClass expected)
        => Assert.Equal(expected, Gemini.ClassOf(status));

    [Fact]
    public void Eleven_is_the_one_that_must_not_be_echoed()
    {
        Assert.True(Gemini.IsSensitiveInput(11));
        Assert.False(Gemini.IsSensitiveInput(10));
    }
}

public class GeminiRequestTests
{
    [Fact]
    public void A_request_is_the_uri_and_one_crlf()
        => Assert.Equal(
            "gemini://example.org/\r\n",
            Encoding.UTF8.GetString(Gemini.BuildRequest("gemini://example.org/")));

    [Theory]
    [InlineData("gemini://h/\r\nsomething else")]
    [InlineData("gemini://h/\nsomething")]
    [InlineData("gemini://h/\0x")]
    public void Control_characters_never_reach_the_wire(string uri)
    {
        // Same reasoning as gopher: a CR inside the URI would end the request
        // line early and let the rest be read as another command.
        var wire = Encoding.UTF8.GetString(Gemini.BuildRequest(uri));
        Assert.Equal(1, wire.Split("\r\n").Length - 1);
        Assert.DoesNotContain(wire[..^2], char.IsControl);
    }

    [Fact]
    public void A_uri_at_the_limit_fits()
        => Assert.True(Gemini.FitsInRequest(new string('a', 1024)));

    [Fact]
    public void A_uri_over_the_limit_is_refused_before_connecting()
        => Assert.False(Gemini.FitsInRequest(new string('a', 1025)));

    [Fact]
    public void The_limit_counts_bytes_not_characters()
    {
        // The specification gives a limit in bytes. This character is two bytes
        // in UTF-8, so 600 of them is 1200 bytes: well under the limit if you
        // count characters, over it if you count what actually goes on the wire.
        var uri = new string('é', 600);
        Assert.True(uri.Length < Gemini.MaxRequestBytes);
        Assert.False(Gemini.FitsInRequest(uri));
    }

    [Theory]
    [InlineData("gemini://example.org/", "example.org", 1965, "/")]
    [InlineData("gemini://example.org:1966/x", "example.org", 1966, "/x")]
    [InlineData("gemini://example.org/search?term", "example.org", 1965, "/search?term")]
    public void Splits_a_uri(string uri, string host, int port, string path)
    {
        var (h, p, pa) = Gemini.ParseUri(uri);
        Assert.Equal(host, h);
        Assert.Equal(port, p);
        Assert.Equal(path, pa);
    }

    [Fact]
    public void The_redirect_limit_is_the_one_the_specification_requires()
        => Assert.Equal(5, Gemini.MaxRedirects);
}

public class GemtextTests
{
    private static IReadOnlyList<GemtextLine> Parse(string source) => Gemtext.Parse(source);

    [Fact]
    public void Blank_lines_are_kept()
    {
        // The specification says they must not be collapsed. They are how an
        // author paragraphs a document.
        var lines = Parse("one\n\n\ntwo");
        Assert.Equal(4, lines.Count);
        Assert.Equal("", lines[1].Text);
        Assert.Equal("", lines[2].Text);
    }

    [Fact]
    public void Consecutive_text_lines_stay_separate()
    {
        var lines = Parse("first\nsecond\nthird");
        Assert.Equal(3, lines.Count);
        Assert.All(lines, l => Assert.Equal(GemtextKind.Text, l.Kind));
    }

    [Theory]
    [InlineData("# One", 1, "One")]
    [InlineData("## Two", 2, "Two")]
    [InlineData("### Three", 3, "Three")]
    [InlineData("#NoSpace", 1, "NoSpace")]
    public void Headings_carry_their_level(string source, int level, string text)
    {
        var line = Parse(source)[0];
        Assert.Equal(GemtextKind.Heading, line.Kind);
        Assert.Equal(level, line.Level);
        Assert.Equal(text, line.Text);
    }

    [Fact]
    public void Four_hashes_is_a_level_three_heading_not_an_error()
        => Assert.Equal(GemtextKind.Heading, Parse("#### deep")[0].Kind);

    [Fact]
    public void A_link_with_a_label_shows_the_label()
    {
        var line = Parse("=> gemini://example.org/ Example")[0];
        Assert.Equal(GemtextKind.Link, line.Kind);
        Assert.Equal("gemini://example.org/", line.Target);
        Assert.Equal("Example", line.Text);
    }

    [Fact]
    public void A_link_with_no_label_shows_its_url()
    {
        var line = Parse("=> gemini://example.org/")[0];
        Assert.Equal("gemini://example.org/", line.Target);
        Assert.Equal("gemini://example.org/", line.Text);
    }

    [Fact]
    public void A_link_tolerates_extra_whitespace()
    {
        var line = Parse("=>\tgemini://example.org/   spaced   out  ")[0];
        Assert.Equal("gemini://example.org/", line.Target);
        Assert.Equal("spaced   out", line.Text);
    }

    [Fact]
    public void An_empty_link_line_is_just_text()
        => Assert.Equal(GemtextKind.Text, Parse("=>")[0].Kind);

    [Fact]
    public void A_list_item_needs_the_space()
    {
        Assert.Equal(GemtextKind.ListItem, Parse("* item")[0].Kind);
        Assert.Equal(GemtextKind.Text, Parse("*emphasis*")[0].Kind);
    }

    [Fact]
    public void Quotes_are_recognised()
        => Assert.Equal(GemtextKind.Quote, Parse("> someone said this")[0].Kind);

    [Fact]
    public void An_unknown_prefix_is_text_not_an_error()
    {
        // The specification is explicit: unrecognised lines are text lines.
        Assert.Equal(GemtextKind.Text, Parse("@@ what is this")[0].Kind);
        Assert.Equal(GemtextKind.Text, Parse("=< backwards")[0].Kind);
    }

    [Fact]
    public void Preformatted_blocks_keep_their_whitespace_and_their_prefixes()
    {
        var lines = Parse("```art\n   # not a heading\n=> not a link\n```\nafter");

        Assert.Equal(GemtextKind.PreformatToggle, lines[0].Kind);
        Assert.Equal("art", lines[0].Text);
        Assert.Equal(GemtextKind.Preformatted, lines[1].Kind);
        Assert.Equal("   # not a heading", lines[1].Text);
        Assert.Equal(GemtextKind.Preformatted, lines[2].Kind);
        Assert.Equal(GemtextKind.PreformatToggle, lines[3].Kind);
        Assert.Equal(GemtextKind.Text, lines[4].Kind);
    }

    [Fact]
    public void An_unclosed_fence_swallows_the_rest_as_preformatted()
    {
        // Which is what the specification's one-bit state machine does. The
        // point of the test is that it does not throw or lose the lines.
        var lines = Parse("```\none\ntwo");
        Assert.Equal(3, lines.Count);
        Assert.Equal(GemtextKind.Preformatted, lines[1].Kind);
        Assert.Equal(GemtextKind.Preformatted, lines[2].Kind);
    }

    [Fact]
    public void Crlf_and_lf_both_work()
    {
        Assert.Equal(2, Parse("one\r\ntwo").Count);
        Assert.Equal("one", Parse("one\r\ntwo")[0].Text);
    }

    [Fact]
    public void A_document_with_no_trailing_newline_keeps_its_last_line()
        => Assert.Equal("last", Parse("first\nlast")[1].Text);

    [Theory]
    [InlineData("gemini://h/dir/page.gmi", "other.gmi", "gemini://h/dir/other.gmi")]
    [InlineData("gemini://h/dir/page.gmi", "/root.gmi", "gemini://h/root.gmi")]
    [InlineData("gemini://h/dir/page.gmi", "gemini://elsewhere/", "gemini://elsewhere/")]
    [InlineData("gemini://h/dir/page.gmi", "https://example.org/", "https://example.org/")]
    public void Relative_links_resolve_against_the_page(string page, string target, string expected)
        => Assert.Equal(expected, Gemtext.Resolve(page, target));

    [Fact]
    public void An_empty_target_resolves_to_nothing()
        => Assert.Equal("", Gemtext.Resolve("gemini://h/", "  "));
}
