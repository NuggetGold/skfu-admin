using Microsoft.Data.Sqlite;

namespace SkfuAdmin.Data;

public static class Db
{
    private static readonly object Gate = new();
    private static bool _initialized;
    public static string Path { get; } = BuildPath();
    private static string BuildPath()
    {
        string? overridePath = Environment.GetEnvironmentVariable("SKFU_DB_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkfuAdmin");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "skfu.db");
    }
    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection("Data Source=" + Path);
        conn.Open();
        conn.CreateFunction<string?, string?>("lower_ru", s => s?.ToLowerInvariant(), true);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        lock (Gate)
        {
            if (!_initialized)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA journal_mode=WAL;";
                cmd.ExecuteNonQuery();
                _initialized = true;
            }
        }
        return conn;
    }
}
