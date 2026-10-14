using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SkfuAdmin.Data;

public sealed class GenericRepository
{
    private sealed class Scope : IDisposable
    {
        private readonly bool _owns;
        public SqliteConnection Conn { get; }

        public Scope(SqliteConnection? conn)
        {
            _owns = conn == null;
            Conn = conn ?? Db.Open();
        }

        public void Dispose()
        {
            if (_owns)
            {
                Conn.Dispose();
            }
        }
    }

    public DataTable Query(TableMeta t, string? search, IReadOnlyDictionary<string, string>? filters,
        string? orderBy, bool desc, int limit, int offset,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        using var scope = new Scope(conn ?? tx?.Connection);
        using var cmd = scope.Conn.CreateCommand();
        cmd.Transaction = tx;
        var sb = new StringBuilder();
        sb.Append("SELECT ");
        sb.Append(SelectList(t));
        sb.Append(" FROM ").Append(t.Name).Append(" t");
        AppendJoins(sb, t);
        AppendWhere(sb, cmd, t, search, filters);
        sb.Append(" ORDER BY ").Append(OrderClause(t, orderBy, desc));
        if (limit > 0)
        {
            sb.Append(" LIMIT @lim OFFSET @off");
            cmd.Parameters.AddWithValue("@lim", (long)limit);
            cmd.Parameters.AddWithValue("@off", (long)Math.Max(0, offset));
        }
        cmd.CommandText = sb.ToString();
        var table = new DataTable(t.Name);
        using var reader = cmd.ExecuteReader();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var dc = table.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
            dc.AllowDBNull = true;
        }
        table.Load(reader);
        return table;
    }

    public int Count(TableMeta t, string? search, IReadOnlyDictionary<string, string>? filters,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        using var scope = new Scope(conn ?? tx?.Connection);
        using var cmd = scope.Conn.CreateCommand();
        cmd.Transaction = tx;
        var sb = new StringBuilder();
        sb.Append("SELECT COUNT(*) FROM ").Append(t.Name).Append(" t");
        AppendWhere(sb, cmd, t, search, filters);
        cmd.CommandText = sb.ToString();
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public Dictionary<string, object?>? GetById(TableMeta t, long id,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        using var scope = new Scope(conn ?? tx?.Connection);
        using var cmd = scope.Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT " + string.Join(", ", t.Columns.Select(c => c.Name)) +
                          " FROM " + t.Name + " WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }
        var result = new Dictionary<string, object?>();
        for (int i = 0; i < r.FieldCount; i++)
        {
            result[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
        }
        return result;
    }

    public long PositionOfId(TableMeta t, long id, string? orderBy, bool desc)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM " + t.Name + " WHERE id < @id";
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public long Insert(TableMeta t, IDictionary<string, object?> values,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        EnsureWritable(t);
        using var scope = new Scope(conn ?? tx?.Connection);
        using var cmd = scope.Conn.CreateCommand();
        cmd.Transaction = tx;
        var cols = new List<string>();
        var pars = new List<string>();
        int n = 0;
        foreach (var kv in values)
        {
            var col = WritableColumn(t, kv.Key);
            if (col == null)
            {
                continue;
            }
            string p = "@p" + n++;
            cols.Add(col.Name);
            pars.Add(p);
            cmd.Parameters.AddWithValue(p, ToDb(col, kv.Value));
        }
        if (cols.Count == 0)
        {
            cmd.CommandText = "INSERT INTO " + t.Name + " DEFAULT VALUES; SELECT last_insert_rowid();";
        }
        else
        {
            cmd.CommandText = "INSERT INTO " + t.Name + " (" + string.Join(", ", cols) + ") VALUES (" +
                              string.Join(", ", pars) + "); SELECT last_insert_rowid();";
        }
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void Update(TableMeta t, long id, IDictionary<string, object?> values,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        EnsureWritable(t);
        using var scope = new Scope(conn ?? tx?.Connection);
        using var cmd = scope.Conn.CreateCommand();
        cmd.Transaction = tx;
        var sets = new List<string>();
        int n = 0;
        foreach (var kv in values)
        {
            var col = WritableColumn(t, kv.Key);
            if (col == null)
            {
                continue;
            }
            string p = "@p" + n++;
            sets.Add(col.Name + "=" + p);
            cmd.Parameters.AddWithValue(p, ToDb(col, kv.Value));
        }
        if (sets.Count == 0)
        {
            return;
        }
        cmd.Parameters.AddWithValue("@id", id);
        cmd.CommandText = "UPDATE " + t.Name + " SET " + string.Join(", ", sets) + " WHERE id=@id";
        cmd.ExecuteNonQuery();
    }

    public void Delete(TableMeta t, long id,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        EnsureWritable(t);
        using var scope = new Scope(conn ?? tx?.Connection);
        using var cmd = scope.Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM " + t.Name + " WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    public int DeleteMany(TableMeta t, IReadOnlyList<long> ids,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        EnsureWritable(t);
        if (ids.Count == 0)
        {
            return 0;
        }
        using var scope = new Scope(conn ?? tx?.Connection);
        int total = 0;
        foreach (var chunk in Chunks(ids, 500))
        {
            using var cmd = scope.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM " + t.Name + " WHERE id IN (" + InList(cmd, chunk) + ")";
            total += cmd.ExecuteNonQuery();
        }
        return total;
    }

    public int UpdateFieldMany(TableMeta t, IReadOnlyList<long> ids, string column, object? value,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        EnsureWritable(t);
        var col = WritableColumn(t, column) ?? throw new ArgumentException("Недопустимая колонка: " + column);
        if (ids.Count == 0)
        {
            return 0;
        }
        using var scope = new Scope(conn ?? tx?.Connection);
        int total = 0;
        foreach (var chunk in Chunks(ids, 500))
        {
            using var cmd = scope.Conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.Parameters.AddWithValue("@val", ToDb(col, value));
            cmd.CommandText = "UPDATE " + t.Name + " SET " + col.Name + "=@val WHERE id IN (" + InList(cmd, chunk) + ")";
            total += cmd.ExecuteNonQuery();
        }
        return total;
    }

    public IReadOnlyList<(long Id, string Label)> Lookup(string table,
        SqliteTransaction? tx = null, SqliteConnection? conn = null)
    {
        var t = TableCatalog.Get(table);
        using var scope = new Scope(conn ?? tx?.Connection);
        using var cmd = scope.Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, (" + t.DisplayExpr + ") AS label FROM " + t.Name + " ORDER BY label COLLATE NOCASE, id";
        var list = new List<(long, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add((r.GetInt64(0), r.IsDBNull(1) ? "" : Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? ""));
        }
        return list;
    }

    private static void EnsureWritable(TableMeta t)
    {
        if (t.ReadOnly)
        {
            throw new InvalidOperationException("Таблица доступна только для чтения: " + t.Name);
        }
    }

    private static ColumnMeta? WritableColumn(TableMeta t, string name)
    {
        var col = t.Column(name);
        if (col == null)
        {
            throw new ArgumentException("Неизвестная колонка: " + name);
        }
        if (col.ReadOnly || col.Name == "id")
        {
            return null;
        }
        return col;
    }

    private static string SelectList(TableMeta t)
    {
        var parts = new List<string>();
        int j = 0;
        foreach (var c in t.Columns)
        {
            parts.Add("t." + c.Name + " AS " + c.Name);
            if (c.Kind == ColumnKind.Foreign && c.ForeignTable != null)
            {
                parts.Add("j" + j + ".label AS " + c.LabelName);
                j++;
            }
        }
        return string.Join(", ", parts);
    }

    private static void AppendJoins(StringBuilder sb, TableMeta t)
    {
        int j = 0;
        foreach (var c in t.Columns)
        {
            if (c.Kind == ColumnKind.Foreign && c.ForeignTable != null)
            {
                var ft = TableCatalog.Get(c.ForeignTable);
                sb.Append(" LEFT JOIN (SELECT id, (").Append(ft.DisplayExpr).Append(") AS label FROM ")
                  .Append(ft.Name).Append(") j").Append(j).Append(" ON j").Append(j)
                  .Append(".id=t.").Append(c.Name);
                j++;
            }
        }
    }

    private static void AppendWhere(StringBuilder sb, SqliteCommand cmd, TableMeta t, string? search,
        IReadOnlyDictionary<string, string>? filters)
    {
        var conds = new List<string>();
        if (!string.IsNullOrWhiteSpace(search) && t.SearchColumns.Length > 0)
        {
            var ors = new List<string>();
            foreach (var name in t.SearchColumns)
            {
                var col = t.Column(name);
                if (col != null)
                {
                    ors.Add("lower_ru(t." + col.Name + ") LIKE lower_ru(@search) ESCAPE '\\'");
                }
            }
            if (ors.Count > 0)
            {
                conds.Add("(" + string.Join(" OR ", ors) + ")");
                cmd.Parameters.AddWithValue("@search", "%" + EscapeLike(search!.Trim()) + "%");
            }
        }
        if (filters != null)
        {
            int n = 0;
            foreach (var kv in filters)
            {
                var col = t.Column(kv.Key);
                if (col == null || string.IsNullOrEmpty(kv.Value))
                {
                    continue;
                }
                string p = "@f" + n++;
                if (col.Kind == ColumnKind.Text || col.Kind == ColumnKind.Date)
                {
                    conds.Add("lower_ru(t." + col.Name + ") LIKE lower_ru(" + p + ") ESCAPE '\\'");
                    cmd.Parameters.AddWithValue(p, "%" + EscapeLike(kv.Value) + "%");
                }
                else
                {
                    conds.Add("t." + col.Name + " = " + p);
                    cmd.Parameters.AddWithValue(p, ToDb(col, kv.Value));
                }
            }
        }
        if (conds.Count > 0)
        {
            sb.Append(" WHERE ").Append(string.Join(" AND ", conds));
        }
    }

    private static string OrderClause(TableMeta t, string? orderBy, bool desc)
    {
        string dir = desc ? " DESC" : " ASC";
        if (!string.IsNullOrEmpty(orderBy))
        {
            var col = t.Column(orderBy!);
            if (col != null)
            {
                if (col.Kind == ColumnKind.Foreign && col.ForeignTable != null)
                {
                    int j = 0;
                    foreach (var c in t.Columns)
                    {
                        if (c.Kind == ColumnKind.Foreign && c.ForeignTable != null)
                        {
                            if (c.Name == col.Name)
                            {
                                return "j" + j + ".label COLLATE NOCASE" + dir + ", t.id";
                            }
                            j++;
                        }
                    }
                }
                return "t." + col.Name + dir + ", t.id";
            }
        }
        var parts = new List<string>();
        foreach (var raw in t.DefaultOrder.Split(','))
        {
            string name = raw.Trim();
            if (t.Column(name) != null)
            {
                parts.Add("t." + name + dir);
            }
        }
        if (parts.Count == 0)
        {
            parts.Add("t.id" + dir);
        }
        return string.Join(", ", parts);
    }

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static object ToDb(ColumnMeta col, object? v)
    {
        if (v == null || v is DBNull)
        {
            return DBNull.Value;
        }
        if (v is string s)
        {
            if (s.Length == 0 && col.Kind != ColumnKind.Text && col.Kind != ColumnKind.Choice)
            {
                return DBNull.Value;
            }
            if (s.Length == 0 && !col.Required && col.Kind != ColumnKind.Text)
            {
                return DBNull.Value;
            }
            switch (col.Kind)
            {
                case ColumnKind.Int:
                case ColumnKind.Foreign:
                    return long.Parse(s.Trim(), CultureInfo.InvariantCulture);
                case ColumnKind.Real:
                    return double.Parse(s.Trim().Replace(',', '.'), CultureInfo.InvariantCulture);
                case ColumnKind.Bool:
                    return ParseBool(s) ? 1L : 0L;
                case ColumnKind.Choice:
                    if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv) &&
                        col.Choices.Length > 0 && col.Choices[0].Length > 0 && char.IsDigit(col.Choices[0][0]))
                    {
                        return lv;
                    }
                    return s;
                default:
                    return s;
            }
        }
        if (v is bool b)
        {
            return b ? 1L : 0L;
        }
        if (v is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (v is int i)
        {
            return (long)i;
        }
        return v;
    }

    private static bool ParseBool(string s)
    {
        string x = s.Trim().ToLowerInvariant();
        return x == "1" || x == "true" || x == "да" || x == "yes";
    }

    private static IEnumerable<IReadOnlyList<long>> Chunks(IReadOnlyList<long> ids, int size)
    {
        for (int i = 0; i < ids.Count; i += size)
        {
            var part = new List<long>();
            for (int k = i; k < Math.Min(i + size, ids.Count); k++)
            {
                part.Add(ids[k]);
            }
            yield return part;
        }
    }

    private static string InList(SqliteCommand cmd, IReadOnlyList<long> ids)
    {
        var names = new string[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            names[i] = "@i" + i;
            cmd.Parameters.AddWithValue(names[i], ids[i]);
        }
        return string.Join(",", names);
    }
}
