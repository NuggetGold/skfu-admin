using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public static class SettingsService
{
    public const string ThemeKey = "theme";

    public static string Get(string key, string defaultValue)
    {
        try
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key=@k";
            cmd.Parameters.AddWithValue("@k", key);
            object? o = cmd.ExecuteScalar();
            if (o == null || o == DBNull.Value)
            {
                return defaultValue;
            }
            return Convert.ToString(o) ?? defaultValue;
        }
        catch (SqliteException)
        {
            return defaultValue;
        }
    }

    public static void Set(string key, string value)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO settings(key, value) VALUES(@k, @v)";
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.ExecuteNonQuery();
    }

    public static string Theme
    {
        get
        {
            string v = Get(ThemeKey, "dark");
            return v == "light" ? "light" : "dark";
        }
        set => Set(ThemeKey, value == "light" ? "light" : "dark");
    }
}
