using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public sealed class ViewState
{
    public const int CurrentVersion = 1;

    public string Search { get; set; } = "";
    public Dictionary<string, string> Filters { get; set; } = new();
    public string? OrderBy { get; set; }
    public bool Desc { get; set; }
    public List<string> HiddenColumns { get; set; } = new();
}

public sealed record SavedView(long Id, string Table, string Name, int Version, DateTime CreatedAt);

public sealed record ViewLoadResult(ViewState? State, string? Error);

public sealed class SavedViewService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<SavedView> List(string table)
    {
        var list = new List<SavedView>();
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, table_name, name, version, created_at FROM saved_views WHERE table_name=@t ORDER BY name, id";
        cmd.Parameters.AddWithValue("@t", table);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTime created = DateTime.TryParse(r.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : DateTime.MinValue;
            list.Add(new SavedView(r.GetInt64(0), r.GetString(1), r.GetString(2), (int)r.GetInt64(3), created));
        }
        return list;
    }

    public long Save(string table, string name, ViewState state)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Укажите название представления.");
        }
        string json = JsonSerializer.Serialize(state, Options);
        string now = DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        using var conn = Db.Open();
        using var tx = conn.BeginTransaction();
        long? existing = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM saved_views WHERE table_name=@t AND lower_ru(name)=lower_ru(@n) ORDER BY id LIMIT 1";
            cmd.Parameters.AddWithValue("@t", table);
            cmd.Parameters.AddWithValue("@n", trimmed);
            object? o = cmd.ExecuteScalar();
            if (o != null && o != DBNull.Value)
            {
                existing = Convert.ToInt64(o, CultureInfo.InvariantCulture);
            }
        }
        long id = existing ?? 0;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            if (existing.HasValue)
            {
                cmd.CommandText = "UPDATE saved_views SET name=@n, json=@j, version=@v, created_at=@c WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
            }
            else
            {
                cmd.CommandText = "INSERT INTO saved_views(table_name, name, json, version, created_at) VALUES(@t,@n,@j,@v,@c); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@t", table);
            }
            cmd.Parameters.AddWithValue("@n", trimmed);
            cmd.Parameters.AddWithValue("@j", json);
            cmd.Parameters.AddWithValue("@v", (long)ViewState.CurrentVersion);
            cmd.Parameters.AddWithValue("@c", now);
            if (existing.HasValue)
            {
                cmd.ExecuteNonQuery();
            }
            else
            {
                id = Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
        tx.Commit();
        return id;
    }

    public void Delete(long id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM saved_views WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public ViewLoadResult Load(long id)
    {
        string json;
        int version;
        using (var conn = Db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT json, version FROM saved_views WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                return new ViewLoadResult(null, "Представление не найдено.");
            }
            json = r.GetString(0);
            version = (int)r.GetInt64(1);
        }
        if (version > ViewState.CurrentVersion)
        {
            return new ViewLoadResult(null, "Представление создано в более новой версии программы (версия " + version + ").");
        }
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ViewLoadResult(null, "Данные представления повреждены.");
            }
            var state = new ViewState();
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                switch (p.Name.ToLowerInvariant())
                {
                    case "search":
                        if (p.Value.ValueKind == JsonValueKind.String)
                        {
                            state.Search = p.Value.GetString() ?? "";
                        }
                        break;
                    case "orderby":
                        if (p.Value.ValueKind == JsonValueKind.String)
                        {
                            state.OrderBy = p.Value.GetString();
                        }
                        break;
                    case "desc":
                        if (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False)
                        {
                            state.Desc = p.Value.GetBoolean();
                        }
                        break;
                    case "filters":
                        if (p.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var f in p.Value.EnumerateObject())
                            {
                                if (f.Value.ValueKind == JsonValueKind.String)
                                {
                                    state.Filters[f.Name] = f.Value.GetString() ?? "";
                                }
                            }
                        }
                        break;
                    case "hiddencolumns":
                        if (p.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var c in p.Value.EnumerateArray())
                            {
                                if (c.ValueKind == JsonValueKind.String && c.GetString() is string s)
                                {
                                    state.HiddenColumns.Add(s);
                                }
                            }
                        }
                        break;
                }
            }
            return new ViewLoadResult(state, null);
        }
        catch (JsonException)
        {
            return new ViewLoadResult(null, "Данные представления повреждены и не могут быть прочитаны.");
        }
    }
}
