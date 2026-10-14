using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public sealed record UndoResult(bool Success, string Message);

public sealed class AuditException : Exception
{
    public AuditException(string message) : base(message)
    {
    }

    public AuditException(string message, Exception inner) : base(message, inner)
    {
    }
}

public sealed class AuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object UserGate = new();
    private static string? _currentUser;

    private readonly GenericRepository _repo = new();

    public static string CurrentUser
    {
        get
        {
            lock (UserGate)
            {
                if (!string.IsNullOrWhiteSpace(_currentUser))
                {
                    return _currentUser!;
                }
            }
            string? found = ResolveDefaultUser();
            if (found == null)
            {
                return "admin";
            }
            lock (UserGate)
            {
                if (string.IsNullOrWhiteSpace(_currentUser))
                {
                    _currentUser = found;
                }
                return _currentUser!;
            }
        }
        set
        {
            lock (UserGate)
            {
                _currentUser = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
    }

    private static string? ResolveDefaultUser()
    {
        try
        {
            using var conn = Db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT login FROM users
WHERE lower_ru(role) LIKE '%админ%' OR lower_ru(role) LIKE '%admin%'
ORDER BY id LIMIT 1";
            object? r = cmd.ExecuteScalar();
            if (r != null && r != DBNull.Value)
            {
                return Convert.ToString(r, CultureInfo.InvariantCulture);
            }
            return null;
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    public long Insert(TableMeta t, IDictionary<string, object?> values)
    {
        return InTransaction((tx, user) => InsertCore(t, values, tx, user));
    }

    public long Insert(TableMeta t, IDictionary<string, object?> values, SqliteTransaction tx)
    {
        string user = CurrentUser;
        return Guard(() => InsertCore(t, values, tx, user));
    }

    public void Update(TableMeta t, long id, IDictionary<string, object?> values)
    {
        InTransaction((tx, user) =>
        {
            UpdateCore(t, id, values, tx, user);
            return 0;
        });
    }

    public void Update(TableMeta t, long id, IDictionary<string, object?> values, SqliteTransaction tx)
    {
        string user = CurrentUser;
        Guard(() =>
        {
            UpdateCore(t, id, values, tx, user);
            return 0;
        });
    }

    public void Delete(TableMeta t, long id)
    {
        InTransaction((tx, user) =>
        {
            DeleteCore(t, id, tx, user);
            return 0;
        });
    }

    public int DeleteMany(TableMeta t, IReadOnlyList<long> ids)
    {
        return InTransaction((tx, user) => DeleteManyCore(t, ids, tx, user));
    }

    public int UpdateFieldMany(TableMeta t, IReadOnlyList<long> ids, string column, object? value)
    {
        return InTransaction((tx, user) => UpdateFieldManyCore(t, ids, column, value, tx, user));
    }

    private long InsertCore(TableMeta t, IDictionary<string, object?> values, SqliteTransaction tx, string user)
    {
        long id = _repo.Insert(t, values, tx);
        var after = _repo.GetById(t, id, tx);
        WriteLog(tx, user, t.Name, id, "insert", null, ToJson(after));
        return id;
    }

    private void UpdateCore(TableMeta t, long id, IDictionary<string, object?> values, SqliteTransaction tx, string user)
    {
        var before = _repo.GetById(t, id, tx);
        if (before == null)
        {
            throw new AuditException("Запись не найдена: " + t.Title + " №" + id + ".");
        }
        _repo.Update(t, id, values, tx);
        var after = _repo.GetById(t, id, tx);
        WriteLog(tx, user, t.Name, id, "update", ToJson(before), ToJson(after));
    }

    private void DeleteCore(TableMeta t, long id, SqliteTransaction tx, string user)
    {
        var before = _repo.GetById(t, id, tx);
        if (before == null)
        {
            throw new AuditException("Запись не найдена: " + t.Title + " №" + id + ".");
        }
        _repo.Delete(t, id, tx);
        WriteLog(tx, user, t.Name, id, "delete", ToJson(before), null);
    }

    private int DeleteManyCore(TableMeta t, IReadOnlyList<long> ids, SqliteTransaction tx, string user)
    {
        var existing = new List<long>();
        var befores = new List<string?>();
        var seen = new HashSet<long>();
        foreach (long id in ids)
        {
            if (!seen.Add(id))
            {
                continue;
            }
            var before = _repo.GetById(t, id, tx);
            if (before != null)
            {
                existing.Add(id);
                befores.Add(ToJson(before));
            }
        }
        if (existing.Count == 0)
        {
            return 0;
        }
        int deleted = _repo.DeleteMany(t, existing, tx);
        for (int i = 0; i < existing.Count; i++)
        {
            WriteLog(tx, user, t.Name, existing[i], "delete", befores[i], null);
        }
        return deleted;
    }

    private int UpdateFieldManyCore(TableMeta t, IReadOnlyList<long> ids, string column, object? value,
        SqliteTransaction tx, string user)
    {
        var existing = new List<long>();
        var befores = new List<string?>();
        var seen = new HashSet<long>();
        foreach (long id in ids)
        {
            if (!seen.Add(id))
            {
                continue;
            }
            var before = _repo.GetById(t, id, tx);
            if (before != null)
            {
                existing.Add(id);
                befores.Add(ToJson(before));
            }
        }
        if (existing.Count == 0)
        {
            return 0;
        }
        int updated = _repo.UpdateFieldMany(t, existing, column, value, tx);
        for (int i = 0; i < existing.Count; i++)
        {
            var after = _repo.GetById(t, existing[i], tx);
            WriteLog(tx, user, t.Name, existing[i], "update", befores[i], ToJson(after));
        }
        return updated;
    }

    private static void WriteLog(SqliteTransaction tx, string user, string table, long rowId, string op,
        string? before, string? after)
    {
        using var cmd = tx.Connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO audit_log(at, user_login, table_name, row_id, op, before_json, after_json) " +
                          "VALUES(@at, @user, @table, @row, @op, @before, @after)";
        cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@user", user);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@row", rowId);
        cmd.Parameters.AddWithValue("@op", op);
        cmd.Parameters.AddWithValue("@before", (object?)before ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@after", (object?)after ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static T InTransaction<T>(Func<SqliteTransaction, string, T> work)
    {
        string user = CurrentUser;
        return Guard(() =>
        {
            using var conn = Db.Open();
            using var tx = conn.BeginTransaction();
            T result = work(tx, user);
            tx.Commit();
            return result;
        });
    }

    private static T Guard<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (SqliteException ex)
        {
            throw new AuditException(Explain(ex), ex);
        }
        catch (FormatException ex)
        {
            throw new AuditException("Некорректное значение в одном из полей: проверьте числа и даты.", ex);
        }
        catch (OverflowException ex)
        {
            throw new AuditException("Слишком большое числовое значение в одном из полей.", ex);
        }
    }

    public static string Explain(SqliteException ex)
    {
        string msg = ex.Message ?? "";
        if (msg.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
        {
            return "Операция нарушает связи между таблицами: запись используется в других таблицах " +
                   "или ссылается на несуществующую запись.";
        }
        if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase))
        {
            string fields = DescribeFields(msg);
            return fields.Length > 0
                ? "Запись с таким значением уже существует: " + fields + "."
                : "Запись с такими значениями уже существует.";
        }
        if (msg.Contains("NOT NULL constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            string fields = DescribeFields(msg);
            return fields.Length > 0
                ? "Не заполнено обязательное поле: " + fields + "."
                : "Не заполнено обязательное поле.";
        }
        if (msg.Contains("CHECK constraint failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Недопустимое значение: нарушено ограничение поля (" + AfterColon(msg) + ").";
        }
        if (msg.Contains("locked", StringComparison.OrdinalIgnoreCase) ||
            msg.Contains("busy", StringComparison.OrdinalIgnoreCase))
        {
            return "База данных занята другим процессом. Повторите попытку позже.";
        }
        return "Ошибка базы данных: " + msg;
    }

    private static string AfterColon(string msg)
    {
        int i = msg.IndexOf(':');
        return i >= 0 ? msg.Substring(i + 1).Trim() : msg;
    }

    private static string DescribeFields(string msg)
    {
        string tail = AfterColon(msg).Trim().TrimEnd('\'', '.', ' ');
        int paren = tail.IndexOf('(');
        if (paren > 0)
        {
            tail = tail.Substring(0, paren).Trim();
        }
        var titles = new List<string>();
        foreach (string raw in tail.Split(','))
        {
            string part = raw.Trim();
            int dot = part.IndexOf('.');
            if (dot <= 0)
            {
                continue;
            }
            string table = part.Substring(0, dot);
            string col = part.Substring(dot + 1);
            var meta = TableCatalog.Find(table)?.Column(col);
            titles.Add("«" + (meta?.Title ?? col) + "»");
        }
        return string.Join(", ", titles);
    }

    private static string? ToJson(Dictionary<string, object?>? row)
    {
        if (row == null)
        {
            return null;
        }
        return JsonSerializer.Serialize(row, JsonOptions);
    }

    private static Dictionary<string, object?>? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        using var doc = JsonDocument.Parse(json!);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var result = new Dictionary<string, object?>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            switch (p.Value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (p.Value.TryGetInt64(out long l))
                    {
                        result[p.Name] = l;
                    }
                    else
                    {
                        result[p.Name] = p.Value.GetDouble();
                    }
                    break;
                case JsonValueKind.String:
                    result[p.Name] = p.Value.GetString();
                    break;
                case JsonValueKind.True:
                    result[p.Name] = 1L;
                    break;
                case JsonValueKind.False:
                    result[p.Name] = 0L;
                    break;
                default:
                    result[p.Name] = null;
                    break;
            }
        }
        return result;
    }

    public UndoResult Undo(long auditId)
    {
        try
        {
            using var conn = Db.Open();
            using var tx = conn.BeginTransaction();
            string table;
            long rowId;
            string op;
            string? beforeJson;
            bool undone;
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT table_name, row_id, op, before_json, undone FROM audit_log WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", auditId);
                using var r = cmd.ExecuteReader();
                if (!r.Read())
                {
                    return new UndoResult(false, "Запись журнала не найдена.");
                }
                table = r.GetString(0);
                rowId = r.GetInt64(1);
                op = r.GetString(2);
                beforeJson = r.IsDBNull(3) ? null : r.GetString(3);
                undone = r.GetInt64(4) != 0;
            }
            if (undone)
            {
                return new UndoResult(false, "Эта операция уже отменена.");
            }
            var t = TableCatalog.Find(table);
            if (t == null)
            {
                return new UndoResult(false, "Таблица «" + table + "» не найдена, откат невозможен.");
            }
            var current = _repo.GetById(t, rowId, tx);
            string done;
            switch (op)
            {
                case "insert":
                    if (current == null)
                    {
                        return new UndoResult(false, "Добавленная запись уже удалена, откат невозможен.");
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = "DELETE FROM " + t.Name + " WHERE id=@id";
                        cmd.Parameters.AddWithValue("@id", rowId);
                        cmd.ExecuteNonQuery();
                    }
                    done = "Добавление отменено: запись №" + rowId + " (" + t.Title + ") удалена.";
                    break;
                case "update":
                    {
                        if (current == null)
                        {
                            return new UndoResult(false, "Изменённая запись больше не существует, откат невозможен.");
                        }
                        var before = FromJson(beforeJson);
                        if (before == null)
                        {
                            return new UndoResult(false, "В журнале нет данных для отката.");
                        }
                        WriteRow(conn, tx, t, rowId, before, false);
                        done = "Изменение отменено: запись №" + rowId + " (" + t.Title + ") возвращена к прежним значениям.";
                        break;
                    }
                case "delete":
                    {
                        if (current != null)
                        {
                            return new UndoResult(false, "Запись с номером " + rowId + " уже существует, восстановление невозможно.");
                        }
                        var before = FromJson(beforeJson);
                        if (before == null)
                        {
                            return new UndoResult(false, "В журнале нет данных для восстановления.");
                        }
                        WriteRow(conn, tx, t, rowId, before, true);
                        done = "Удаление отменено: запись №" + rowId + " (" + t.Title + ") восстановлена.";
                        break;
                    }
                default:
                    return new UndoResult(false, "Неизвестный тип операции: " + op + ".");
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE audit_log SET undone=1 WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", auditId);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return new UndoResult(true, done);
        }
        catch (SqliteException ex)
        {
            return new UndoResult(false, Explain(ex));
        }
        catch (JsonException)
        {
            return new UndoResult(false, "Данные журнала повреждены, откат невозможен.");
        }
    }

    private static void WriteRow(SqliteConnection conn, SqliteTransaction tx, TableMeta t, long id,
        Dictionary<string, object?> values, bool insert)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        var cols = new List<string>();
        var pars = new List<string>();
        int n = 0;
        foreach (var c in t.Columns)
        {
            if (c.Name == "id" || !values.TryGetValue(c.Name, out object? v))
            {
                continue;
            }
            string p = "@p" + n++;
            cols.Add(c.Name);
            pars.Add(p);
            cmd.Parameters.AddWithValue(p, v ?? DBNull.Value);
        }
        cmd.Parameters.AddWithValue("@id", id);
        var sb = new StringBuilder();
        if (insert)
        {
            cols.Insert(0, "id");
            pars.Insert(0, "@id");
            sb.Append("INSERT INTO ").Append(t.Name).Append(" (").Append(string.Join(", ", cols))
              .Append(") VALUES (").Append(string.Join(", ", pars)).Append(')');
        }
        else
        {
            if (cols.Count == 0)
            {
                return;
            }
            var sets = new List<string>();
            for (int i = 0; i < cols.Count; i++)
            {
                sets.Add(cols[i] + "=" + pars[i]);
            }
            sb.Append("UPDATE ").Append(t.Name).Append(" SET ").Append(string.Join(", ", sets)).Append(" WHERE id=@id");
        }
        cmd.CommandText = sb.ToString();
        cmd.ExecuteNonQuery();
    }

    public DataTable QueryLog(string? table, string? op, DateTime? from, DateTime? to, int limit, int offset)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        var sb = new StringBuilder();
        sb.Append("SELECT id, at, user_login, table_name, row_id, op, before_json, after_json, undone FROM audit_log");
        AppendLogWhere(sb, cmd, table, op, from, to);
        sb.Append(" ORDER BY id DESC");
        if (limit > 0)
        {
            sb.Append(" LIMIT @lim OFFSET @off");
            cmd.Parameters.AddWithValue("@lim", (long)limit);
            cmd.Parameters.AddWithValue("@off", (long)Math.Max(0, offset));
        }
        cmd.CommandText = sb.ToString();
        var result = new DataTable("audit_log");
        using var reader = cmd.ExecuteReader();
        result.Load(reader);
        return result;
    }

    public int CountLog(string? table, string? op, DateTime? from, DateTime? to)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        var sb = new StringBuilder("SELECT COUNT(*) FROM audit_log");
        AppendLogWhere(sb, cmd, table, op, from, to);
        cmd.CommandText = sb.ToString();
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void AppendLogWhere(StringBuilder sb, SqliteCommand cmd, string? table, string? op,
        DateTime? from, DateTime? to)
    {
        var conds = new List<string>();
        if (!string.IsNullOrWhiteSpace(table))
        {
            conds.Add("table_name=@table");
            cmd.Parameters.AddWithValue("@table", table!.Trim());
        }
        if (!string.IsNullOrWhiteSpace(op))
        {
            conds.Add("op=@op");
            cmd.Parameters.AddWithValue("@op", op!.Trim());
        }
        if (from.HasValue)
        {
            conds.Add("at>=@from");
            cmd.Parameters.AddWithValue("@from", from.Value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }
        if (to.HasValue)
        {
            DateTime end = to.Value.TimeOfDay == TimeSpan.Zero ? to.Value.Date.AddDays(1).AddSeconds(-1) : to.Value;
            conds.Add("at<=@to");
            cmd.Parameters.AddWithValue("@to", end.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
        }
        if (conds.Count > 0)
        {
            sb.Append(" WHERE ").Append(string.Join(" AND ", conds));
        }
    }
}
