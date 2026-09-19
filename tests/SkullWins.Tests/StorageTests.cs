using SkullWins.Storage;
using Xunit;

namespace SkullWins.Tests;

public class HistoryTests
{
    [Fact]
    public void First_visit_counts_one()
    {
        using var h = HistoryStore.InMemory();
        h.Add("https://a.test", "A");
        Assert.Equal(1, h.Recent()[0].Visits);
    }

    [Fact]
    public void Revisiting_increments_and_updates_title()
    {
        using var h = HistoryStore.InMemory();
        h.Add("https://a.test", "old");
        h.Add("https://a.test", "new");
        var e = h.Recent()[0];
        Assert.Equal(2, e.Visits);
        Assert.Equal("new", e.Title);
        Assert.Equal(1, h.Count());
    }

    [Fact]
    public void Recent_orders_by_last_visit()
    {
        using var h = HistoryStore.InMemory();
        h.Add("https://a.test", "A", whenUnixSeconds: 100);
        h.Add("https://b.test", "B", whenUnixSeconds: 200);
        var recent = h.Recent();
        Assert.Equal("https://b.test", recent[0].Uri);
    }

    [Fact]
    public void Search_ranks_by_visit_count()
    {
        using var h = HistoryStore.InMemory();
        h.Add("https://rare.test/wiki", "rare");
        h.Add("https://often.test/wiki", "often");
        h.Add("https://often.test/wiki", "often");
        var hits = h.Search("wiki");
        Assert.Equal("https://often.test/wiki", hits[0].Uri);
    }

    [Fact]
    public void Search_matches_title_too()
    {
        using var h = HistoryStore.InMemory();
        h.Add("https://x.test", "Hacker News");
        Assert.Single(h.Search("hacker"));
    }

    [Fact]
    public void Clear_empties_history()
    {
        using var h = HistoryStore.InMemory();
        h.Add("https://a.test", "A");
        h.Clear();
        Assert.Equal(0, h.Count());
    }
}

public class BookmarkTests
{
    [Fact]
    public void Add_reports_new_then_existing()
    {
        using var b = BookmarkStore.InMemory();
        Assert.True(b.Add("https://a.test", "A"));
        Assert.False(b.Add("https://a.test", "A again"));
    }

    [Fact]
    public void Contains_tracks_membership()
    {
        using var b = BookmarkStore.InMemory();
        Assert.False(b.Contains("https://a.test"));
        b.Add("https://a.test", "A");
        Assert.True(b.Contains("https://a.test"));
    }

    [Fact]
    public void Remove_reports_whether_it_removed()
    {
        using var b = BookmarkStore.InMemory();
        b.Add("https://a.test", "A");
        Assert.True(b.Remove("https://a.test"));
        Assert.False(b.Remove("https://a.test"));
    }

    [Fact]
    public void Update_keeps_one_row_new_title()
    {
        using var b = BookmarkStore.InMemory();
        b.Add("https://a.test", "old");
        b.Add("https://a.test", "new");
        var all = b.All();
        Assert.Single(all);
        Assert.Equal("new", all[0].Title);
    }

    [Fact]
    public void Search_matches_uri_and_title()
    {
        using var b = BookmarkStore.InMemory();
        b.Add("https://floodgap.com", "Gopher hub");
        Assert.Single(b.Search("gopher"));
        Assert.Single(b.Search("floodgap"));
    }
}
