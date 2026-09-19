using Microsoft.Data.Sqlite;

namespace SkullWins.Storage;

public sealed record PinnedHost(
    string Host, int Port, string Fingerprint, long NotAfter, long FirstSeen, long LastSeen);

/// <summary>What happened when a certificate was checked against the pin.</summary>
public enum TrustVerdict
{
    /// <summary>Never seen before. Pinned now.</summary>
    Pinned,

    /// <summary>Same certificate as last time.</summary>
    Known,

    /// <summary>
    /// A different certificate while the pinned one had not yet expired. This
    /// is the case the specification calls out: it is what an interception
    /// looks like, and it is refused.
    /// </summary>
    Changed,

    /// <summary>
    /// A different certificate after the pinned one expired. Ordinary rotation.
    /// Re-pinned.
    /// </summary>
    Rotated,
}

/// <summary>
/// Trust on first use, which is how gemini does certificates.
///
/// Servers are overwhelmingly self-signed, so the usual chain validation would
/// reject almost every capsule that exists. Instead the fingerprint is
/// remembered the first time a host is seen and compared afterwards. That
/// gives no protection on the very first visit and full protection after it,
/// which is the trade the protocol chose.
/// </summary>
public sealed class TrustStore : IDisposable
{
    private readonly SqliteConnection _db;

    public TrustStore(string path)
    {
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA busy_timeout=3000;");
        Exec("""
            CREATE TABLE IF NOT EXISTS pinned (
                host        TEXT NOT NULL,
                port        INTEGER NOT NULL,
                fingerprint TEXT NOT NULL,
                not_after   INTEGER NOT NULL,
                first_seen  INTEGER NOT NULL,
                last_seen   INTEGER NOT NULL,
                PRIMARY KEY (host, port)
            );
            """);
    }

    public static TrustStore InMemory() => new(":memory:");

    /// <summary>
    /// Compare a certificate against what is pinned for this host, and record
    /// the outcome.
    ///
    /// <paramref name="now"/> is a parameter so expiry can be tested without
    /// waiting a year for a certificate to lapse.
    /// </summary>
    public TrustVerdict Check(
        string host, int port, string fingerprint, long notAfter, long? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var existing = Find(host, port);

        if (existing is null)
        {
            Insert(host, port, fingerprint, notAfter, moment);
            return TrustVerdict.Pinned;
        }

        if (string.Equals(existing.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            Touch(host, port, moment);
            return TrustVerdict.Known;
        }

        // A new certificate while the old one was still valid is the shape of
        // an interception. A new one after the old expired is a renewal.
        if (existing.NotAfter > moment) { return TrustVerdict.Changed; }

        Replace(host, port, fingerprint, notAfter, moment);
        return TrustVerdict.Rotated;
    }

    /// <summary>
    /// Accept a changed certificate on purpose. Only ever called because the
    /// user ran the command after being shown what changed.
    /// </summary>
    public void Accept(string host, int port, string fingerprint, long notAfter, long? now = null)
    {
        var moment = now ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Find(host, port) is null) { Insert(host, port, fingerprint, notAfter, moment); }
        else { Replace(host, port, fingerprint, notAfter, moment); }
    }

    public PinnedHost? Find(string host, int port)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT host, port, fingerprint, not_after, first_seen, last_seen
            FROM pinned WHERE host = $host AND port = $port;
            """;
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) { return null; }

        return new PinnedHost(
            reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    public IReadOnlyList<PinnedHost> All()
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            SELECT host, port, fingerprint, not_after, first_seen, last_seen
            FROM pinned ORDER BY host, port;
            """;

        var rows = new List<PinnedHost>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new PinnedHost(
                reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5)));
        }
        return rows;
    }

    public bool Forget(string host, int port)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM pinned WHERE host = $host AND port = $port;";
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);
        return cmd.ExecuteNonQuery() > 0;
    }

    // ------------------------------------------------------------- internals

    private void Insert(string host, int port, string fingerprint, long notAfter, long now)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO pinned (host, port, fingerprint, not_after, first_seen, last_seen)
            VALUES ($host, $port, $fp, $notAfter, $now, $now);
            """;
        Bind(cmd, host, port, fingerprint, notAfter, now);
        cmd.ExecuteNonQuery();
    }

    private void Replace(string host, int port, string fingerprint, long notAfter, long now)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = """
            UPDATE pinned SET fingerprint = $fp, not_after = $notAfter, last_seen = $now
            WHERE host = $host AND port = $port;
            """;
        Bind(cmd, host, port, fingerprint, notAfter, now);
        cmd.ExecuteNonQuery();
    }

    private void Touch(string host, int port, long now)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText =
            "UPDATE pinned SET last_seen = $now WHERE host = $host AND port = $port;";
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);
        cmd.Parameters.AddWithValue("$now", now);
        cmd.ExecuteNonQuery();
    }

    private static void Bind(
        SqliteCommand cmd, string host, int port, string fingerprint, long notAfter, long now)
    {
        cmd.Parameters.AddWithValue("$host", host);
        cmd.Parameters.AddWithValue("$port", port);
        cmd.Parameters.AddWithValue("$fp", fingerprint);
        cmd.Parameters.AddWithValue("$notAfter", notAfter);
        cmd.Parameters.AddWithValue("$now", now);
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _db.Dispose();
}
