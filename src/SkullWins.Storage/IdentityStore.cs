using Microsoft.Data.Sqlite;

namespace SkullWins.Storage;

/// <summary>
/// An identity attached to part of a capsule. Scope is a directory path, and
/// it covers everything below it.
/// </summary>
public sealed record IdentityScope(
    string Name, string Host, int Port, string Path, long Created);

/// <summary>
/// Which client certificate belongs to which part of which capsule.
///
/// The specification scopes a certificate to the host and port that asked for
/// it plus the path of the request and everything below. That is stored rather
/// than guessed, because getting it wrong in the generous direction sends a
/// credential to a capsule that never asked for it.
/// </summary>
public sealed class IdentityStore : IDisposable
{
    private readonly SqliteConnection _db;

    public IdentityStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA busy_timeout=3000;");
        Exec("""
            CREATE TABLE IF NOT EXISTS scopes (
                name    TEXT NOT NULL,
                host    TEXT NOT NULL,
                port    INTEGER NOT NULL,
                path    TEXT NOT NULL,
                created INTEGER NOT NULL,
                PRIMARY KEY (host, port, path)
            );
            """);
    }

    public static IdentityStore InMemory() => new(":memory:");

    /// <summary>
    /// Attach an identity to a host, port and path. One identity per scope, so
    /// attaching again replaces rather than accumulates.
    /// </summary>
    public void Attach(string name, string host, int port, string path)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scopes (name, host, port, path, created)
            VALUES ($name, $host, $port, $path, $created)
            ON CONFLICT(host, port, path) DO UPDATE SET name = $name, created = $created;
            """;
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);
        cmd.Parameters.AddWithValue("$path", Normalise(path));
        cmd.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.ExecuteNonQuery();
    }

    public bool Detach(string host, int port, string path)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            "DELETE FROM scopes WHERE host = $host AND port = $port AND path = $path;";
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);
        cmd.Parameters.AddWithValue("$path", Normalise(path));
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Remove an identity from everywhere it was attached.</summary>
    public int DetachAll(string name)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM scopes WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Which identity, if any, should be offered for this request.
    ///
    /// A scope of "/a/" covers "/a/b" and "/a/b/c" but not "/c", and, the part
    /// that is easy to get wrong, not "/ab" either: comparing raw prefixes
    /// would match that and hand the certificate to a different part of the
    /// capsule. Both sides are normalised to end in a slash before comparing.
    ///
    /// When two scopes both match, the longer one wins, so a certificate
    /// attached deeper overrides one attached at the root.
    /// </summary>
    public string? Match(string host, int port, string path)
    {
        var target = Normalise(path);

        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT name, path FROM scopes
            WHERE host = $host AND port = $port
            ORDER BY length(path) DESC;
            """;
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var scope = reader.GetString(1);
            if (target.StartsWith(scope, StringComparison.Ordinal)) { return reader.GetString(0); }
        }

        return null;
    }

    public IReadOnlyList<IdentityScope> All()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            "SELECT name, host, port, path, created FROM scopes ORDER BY name, host, path;";

        var rows = new List<IdentityScope>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new IdentityScope(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.GetString(3), reader.GetInt64(4)));
        }
        return rows;
    }

    /// <summary>
    /// A path as a directory scope, always starting and ending with a slash.
    ///
    /// "/journal/2026/post.gmi" becomes "/journal/2026/", so attaching while
    /// reading one post covers its siblings and nothing above them. A query
    /// string is not part of the scope.
    /// </summary>
    public static string Normalise(string path)
    {
        var clean = path;

        var query = clean.IndexOf('?');
        if (query >= 0) { clean = clean[..query]; }

        if (!clean.StartsWith('/')) { clean = "/" + clean; }

        // Trim back to the last slash: a path naming a file scopes to its
        // directory, a path already ending in a slash is left alone.
        var lastSlash = clean.LastIndexOf('/');
        clean = clean[..(lastSlash + 1)];

        return clean.Length == 0 ? "/" : clean;
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
