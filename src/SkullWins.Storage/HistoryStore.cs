using Microsoft.Data.Sqlite;

namespace SkullWins.Storage;

public sealed record HistoryEntry(string Uri, string Title, int Visits, long LastVisit);

/// <summary>
/// Browsing history in SQLite. One row per URI, a visit counter, and the last
/// time it was seen. Opened WAL so a crash mid-write does not corrupt the file;
/// if the database will not open at all the caller renames it aside and starts
/// fresh, because losing history is bad but refusing to launch is worse.
/// </summary>
public sealed class HistoryStore : IDisposable
{
    private readonly SqliteConnection _db;

    public HistoryStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        // Two copies of the browser can point at one profile. Without a
        // timeout the second one throws the moment their writes collide.
        Exec("PRAGMA busy_timeout=3000;");
        Exec("""
            CREATE TABLE IF NOT EXISTS history (
                uri        TEXT PRIMARY KEY,
                title      TEXT NOT NULL DEFAULT '',
                visits     INTEGER NOT NULL DEFAULT 1,
                last_visit INTEGER NOT NULL
            );
            """);
    }

    /// <summary>In-memory store for tests.</summary>
    public static HistoryStore InMemory() => new(":memory:");

    public void Add(string uri, string title, long? whenUnixSeconds = null)
    {
        var when = whenUnixSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO history (uri, title, visits, last_visit)
            VALUES ($uri, $title, 1, $when)
            ON CONFLICT(uri) DO UPDATE SET
                title = $title,
                visits = visits + 1,
                last_visit = $when;
            """;
        cmd.Parameters.AddWithValue("$uri", uri);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$when", when);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Most-recent first, capped.</summary>
    public IReadOnlyList<HistoryEntry> Recent(int limit = 100)
        => Query("SELECT uri, title, visits, last_visit FROM history "
                 + "ORDER BY last_visit DESC LIMIT $limit",
                 ("$limit", limit));

    /// <summary>
    /// Substring match on URI or title for command-bar completion, ranked by
    /// visit count so the pages you go to often float up.
    /// </summary>
    public IReadOnlyList<HistoryEntry> Search(string term, int limit = 25)
        => Query("SELECT uri, title, visits, last_visit FROM history "
                 + "WHERE uri LIKE $like OR title LIKE $like "
                 + "ORDER BY visits DESC, last_visit DESC LIMIT $limit",
                 ("$like", "%" + term + "%"), ("$limit", limit));

    public int Count()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM history;";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Clear() => Exec("DELETE FROM history;");

    // ------------------------------------------------------------- internals

    private IReadOnlyList<HistoryEntry> Query(string sql, params (string, object)[] args)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) { cmd.Parameters.AddWithValue(name, value); }

        var rows = new List<HistoryEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new HistoryEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt64(3)));
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
