using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public sealed record BackupInfo(string Name, string Path, long Size, DateTime CreatedAt);

public sealed record DbStats(long FileSize, int SchemaVersion, IReadOnlyList<KeyValuePair<string, int>> TableCounts)
{
    public int TotalRows
    {
        get
        {
            int sum = 0;
            foreach (var kv in TableCounts)
            {
                sum += kv.Value;
            }
            return sum;
        }
    }
}

public sealed class BackupException : Exception
{
    public BackupException(string message) : base(message)
    {
    }

    public BackupException(string message, Exception inner) : base(message, inner)
    {
    }
}

public static class BackupService
{
    private const int Keep = 10;

    public static string BackupDir { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkfuAdmin", "backups");

    private static string Quote(string s) => "'" + s.Replace("'", "''") + "'";

    public static BackupInfo CreateBackup(string? reason)
    {
        if (!File.Exists(Db.Path))
        {
            throw new BackupException("База данных ещё не создана, резервная копия невозможна.");
        }
        try
        {
            Directory.CreateDirectory(BackupDir);
            string name = "skfu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".db";
            string path = System.IO.Path.Combine(BackupDir, name);
            int n = 1;
            while (File.Exists(path))
            {
                name = "skfu-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + n++ + ".db";
                path = System.IO.Path.Combine(BackupDir, name);
            }
            using (var conn = Db.Open())
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "VACUUM INTO " + Quote(path);
                cmd.ExecuteNonQuery();
            }
            if (!string.IsNullOrWhiteSpace(reason))
            {
                File.WriteAllText(path + ".txt", reason!.Trim());
            }
            var fi = new FileInfo(path);
            return new BackupInfo(fi.Name, fi.FullName, fi.Length, fi.LastWriteTime);
        }
        catch (SqliteException ex)
        {
            throw new BackupException("Не удалось создать резервную копию: " + ex.Message, ex);
        }
        catch (IOException ex)
        {
            throw new BackupException("Не удалось записать резервную копию: " + ex.Message, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BackupException("Нет доступа к папке резервных копий: " + ex.Message, ex);
        }
    }

    public static IReadOnlyList<BackupInfo> List()
    {
        var list = new List<BackupInfo>();
        if (!Directory.Exists(BackupDir))
        {
            return list;
        }
        foreach (string file in Directory.GetFiles(BackupDir, "*.db"))
        {
            var fi = new FileInfo(file);
            list.Add(new BackupInfo(fi.Name, fi.FullName, fi.Length, fi.LastWriteTime));
        }
        list.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
        return list;
    }

    public static void Restore(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new BackupException("Файл резервной копии не найден: " + path);
        }
        string check;
        try
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            check = Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase)) throw new BackupException("Резервная копия повреждена: " + check);
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('institutes','schedule','settings');";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != 3) throw new BackupException("Файл не содержит обязательные таблицы базы данных.");
            cmd.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) != Schema.Version) throw new BackupException("Версия схемы резервной копии не поддерживается.");
        }
        catch (SqliteException ex)
        {
            throw new BackupException("Файл не является корректной базой данных: " + ex.Message, ex);
        }
        if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackupException("Резервная копия повреждена: " + check);
        }
        try
        {
            using (var conn = Db.Open()) using (var cmd = conn.CreateCommand()) { cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);"; cmd.ExecuteNonQuery(); }
            if (File.Exists(Db.Path)) CreateBackup("Перед восстановлением");
            SqliteConnection.ClearAllPools();
            string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Db.Path)) ?? ".";
            Directory.CreateDirectory(dir);
            string temp = System.IO.Path.Combine(dir, "restore-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.Copy(path, temp, true);
            Validate(temp);
            string old = Db.Path + ".before-restore";
            if (File.Exists(old)) File.Delete(old);
            if (File.Exists(Db.Path)) File.Replace(temp, Db.Path, old); else File.Move(temp, Db.Path, true);
            if (File.Exists(old)) File.Delete(old);
            foreach (string suffix in new[] { "-wal", "-shm" }) { string side = Db.Path + suffix; if (File.Exists(side)) File.Delete(side); }
        }
        catch (BackupException)
        {
            throw;
        }
        catch (IOException ex)
        {
            throw new BackupException("Не удалось заменить файл базы данных (возможно, он занят): " + ex.Message, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new BackupException("Нет доступа к файлу базы данных: " + ex.Message, ex);
        }
    }

    private static void Validate(string path)
    {
        var csb = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using var conn = new SqliteConnection(csb.ToString()); conn.Open(); using var cmd = conn.CreateCommand(); cmd.CommandText = "PRAGMA integrity_check;"; if (!string.Equals(Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase)) throw new BackupException("Временная копия повреждена.");
    }

    public static void AutoBackup()
    {
        try
        {
            if (!File.Exists(Db.Path))
            {
                return;
            }
            var all = List();
            if (all.Count == 0 || DateTime.Now - all[0].CreatedAt > TimeSpan.FromDays(1))
            {
                CreateBackup("Автоматическая копия");
            }
            Rotate();
        }
        catch (BackupException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void Rotate()
    {
        var all = List();
        for (int i = Keep; i < all.Count; i++)
        {
            try
            {
                File.Delete(all[i].Path);
                if (File.Exists(all[i].Path + ".txt"))
                {
                    File.Delete(all[i].Path + ".txt");
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static DbStats GetStats()
    {
        long size = 0;
        if (File.Exists(Db.Path))
        {
            size = new FileInfo(Db.Path).Length;
        }
        var counts = new List<KeyValuePair<string, int>>();
        int version = 0;
        using var conn = Db.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            version = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        foreach (var t in TableCatalog.All)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM " + t.Name;
            counts.Add(new KeyValuePair<string, int>(t.Name, Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)));
        }
        return new DbStats(size, version, counts);
    }

    public static string IntegrityCheck()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        var lines = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            lines.Add(r.GetString(0));
        }
        return string.Join(Environment.NewLine, lines);
    }

    public static void Vacuum()
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }
}
