using Microsoft.Data.Sqlite;

namespace SkullWins.Storage;

public sealed record Bookmark(string Uri, string Title, long Added);

/// <summary>Bookmarks in SQLite. Same open-and-recover posture as history.</summary>
public sealed class BookmarkStore : IDisposable
{
    private readonly SqliteConnection _db;

    public BookmarkStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("""
            CREATE TABLE IF NOT EXISTS bookmarks (
                uri   TEXT PRIMARY KEY,
                title TEXT NOT NULL DEFAULT '',
                added INTEGER NOT NULL
            );
            """);
    }

    public static BookmarkStore InMemory() => new(":memory:");

    /// <summary>Add or update; returns true when it was a new bookmark.</summary>
    public bool Add(string uri, string title)
    {
        var existed = Contains(uri);
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO bookmarks (uri, title, added)
            VALUES ($uri, $title, $added)
            ON CONFLICT(uri) DO UPDATE SET title = $title;
            """;
        cmd.Parameters.AddWithValue("$uri", uri);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$added", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
        return !existed;
    }

    public bool Remove(string uri)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM bookmarks WHERE uri = $uri;";
        cmd.Parameters.AddWithValue("$uri", uri);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool Contains(string uri)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM bookmarks WHERE uri = $uri;";
        cmd.Parameters.AddWithValue("$uri", uri);
        return cmd.ExecuteScalar() is not null;
    }

    public IReadOnlyList<Bookmark> All()
        => Query("SELECT uri, title, added FROM bookmarks ORDER BY added DESC");

    public IReadOnlyList<Bookmark> Search(string term, int limit = 25)
        => Query("SELECT uri, title, added FROM bookmarks "
                 + "WHERE uri LIKE $like OR title LIKE $like "
                 + "ORDER BY added DESC LIMIT $limit",
                 ("$like", "%" + term + "%"), ("$limit", limit));

    private IReadOnlyList<Bookmark> Query(string sql, params (string, object)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) { cmd.Parameters.AddWithValue(name, value); }

        var rows = new List<Bookmark>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new Bookmark(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        }
        return rows;
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
