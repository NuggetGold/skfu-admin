using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public sealed record ImportRow(int LineNo, IReadOnlyDictionary<string, object?> Values, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class ImportPreview
{
    public TableMeta Table { get; init; } = null!;
    public IReadOnlyList<ImportRow> Rows { get; init; } = Array.Empty<ImportRow>();
    public IReadOnlyList<string> FileErrors { get; init; } = Array.Empty<string>();
    public int ValidCount
    {
        get
        {
            int n = 0;
            foreach (var r in Rows)
            {
                if (r.IsValid)
                {
                    n++;
                }
            }
            return n;
        }
    }
    public int InvalidCount => Rows.Count - ValidCount;
}

public sealed record ImportResult(bool Success, int Inserted, string Message);

public sealed class CsvService
{
    private static readonly string[] ImportTables = { "students", "teachers", "rooms" };

    public static bool CanImport(string tableName) => Array.IndexOf(ImportTables, tableName) >= 0;

    public void Export(TableMeta t, string? search, IReadOnlyDictionary<string, string>? filters, string path)
    {
        var repo = new GenericRepository();
        DataTable data = repo.Query(t, search, filters, null, false, 0, 0);
        var sb = new StringBuilder();
        for (int i = 0; i < t.Columns.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(';');
            }
            sb.Append(Escape(t.Columns[i].Name));
        }
        sb.Append("\r\n");
        foreach (DataRow row in data.Rows)
        {
            for (int i = 0; i < t.Columns.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(';');
                }
                object v = row[t.Columns[i].Name];
                string s = v == DBNull.Value ? "" : Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
                sb.Append(Escape(s));
            }
            sb.Append("\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    private static string Escape(string s)
    {
        if (s.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0 || s.StartsWith(" ", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }

    public List<string[]> Parse(string path)
    {
        string text = File.ReadAllText(path, new UTF8Encoding(false));
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text.Substring(1);
        }
        return ParseText(text).Select(x => x.Fields).ToList();
    }

    private sealed class RawLine
    {
        public int LineNo { get; init; }
        public string[] Fields { get; init; } = Array.Empty<string>();
    }

    private static List<RawLine> ParseText(string text)
    {
        var rows = new List<RawLine>();
        var fields = new List<string>();
        var cur = new StringBuilder();
        bool inQuotes = false;
        bool hasContent = false;
        int line = 1;
        int startLine = 1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }
                    cur.Append(c);
                }
                continue;
            }
            if (c == '"')
            {
                inQuotes = true;
                hasContent = true;
            }
            else if (c == ';')
            {
                fields.Add(cur.ToString());
                cur.Clear();
                hasContent = true;
            }
            else if (c == '\r')
            {
                continue;
            }
            else if (c == '\n')
            {
                if (hasContent || cur.Length > 0)
                {
                    fields.Add(cur.ToString());
                    rows.Add(new RawLine { LineNo = startLine, Fields = fields.ToArray() });
                }
                fields.Clear();
                cur.Clear();
                hasContent = false;
                line++;
                startLine = line;
            }
            else
            {
                cur.Append(c);
                hasContent = true;
            }
        }
        if (hasContent || cur.Length > 0)
        {
            fields.Add(cur.ToString());
            rows.Add(new RawLine { LineNo = startLine, Fields = fields.ToArray() });
        }
        return rows;
    }

    public ImportPreview Preview(TableMeta t, string path)
    {
        var fileErrors = new List<string>();
        var result = new List<ImportRow>();
        if (!CanImport(t.Name))
        {
            fileErrors.Add("Импорт для таблицы «" + t.Title + "» не поддерживается.");
            return new ImportPreview { Table = t, FileErrors = fileErrors, Rows = result };
        }
        List<RawLine> lines;
        try
        {
            string text = File.ReadAllText(path, new UTF8Encoding(false));
            if (text.Length > 0 && text[0] == '\uFEFF')
            {
                text = text.Substring(1);
            }
            lines = ParseText(text);
        }
        catch (IOException ex)
        {
            fileErrors.Add("Не удалось прочитать файл: " + ex.Message);
            return new ImportPreview { Table = t, FileErrors = fileErrors, Rows = result };
        }
        catch (UnauthorizedAccessException ex)
        {
            fileErrors.Add("Нет доступа к файлу: " + ex.Message);
            return new ImportPreview { Table = t, FileErrors = fileErrors, Rows = result };
        }
        if (lines.Count == 0)
        {
            fileErrors.Add("Файл пуст.");
            return new ImportPreview { Table = t, FileErrors = fileErrors, Rows = result };
        }
        var header = lines[0].Fields;
        var map = new ColumnMeta?[header.Length];
        var mapped = new HashSet<string>();
        for (int i = 0; i < header.Length; i++)
        {
            string h = header[i].Trim();
            foreach (var c in t.Columns)
            {
                if (c.Name == "id")
                {
                    continue;
                }
                if (string.Equals(c.Name, h, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.Title, h, StringComparison.OrdinalIgnoreCase))
                {
                    map[i] = c;
                    mapped.Add(c.Name);
                    break;
                }
            }
        }
        foreach (var c in t.Columns)
        {
            if (c.Required && c.Name != "id" && !mapped.Contains(c.Name))
            {
                fileErrors.Add("В файле нет обязательной колонки «" + c.Title + "» (" + c.Name + ").");
            }
        }
        if (fileErrors.Count > 0)
        {
            return new ImportPreview { Table = t, FileErrors = fileErrors, Rows = result };
        }

        using var conn = Db.Open();
        var fkCache = new Dictionary<string, HashSet<long>>();
        var seen = new Dictionary<string, int>();
        for (int li = 1; li < lines.Count; li++)
        {
            var raw = lines[li];
            var errors = new List<string>();
            var values = new Dictionary<string, object?>();
            for (int i = 0; i < map.Length; i++)
            {
                var col = map[i];
                if (col == null)
                {
                    continue;
                }
                string cell = i < raw.Fields.Length ? raw.Fields[i].Trim() : "";
                values[col.Name] = ConvertCell(conn, col, cell, fkCache, errors);
            }
            foreach (var c in t.Columns)
            {
                if (c.Name != "id" && !values.ContainsKey(c.Name) && c.Kind == ColumnKind.Bool)
                {
                    values[c.Name] = 0L;
                }
            }
            if (raw.Fields.Length > map.Length)
            {
                errors.Add("Лишние значения в строке: ожидалось столбцов " + map.Length + ", найдено " + raw.Fields.Length + ".");
            }
            if (errors.Count == 0)
            {
                CheckUnique(conn, t, values, raw.LineNo, seen, errors);
            }
            result.Add(new ImportRow(raw.LineNo, values, errors));
        }
        return new ImportPreview { Table = t, FileErrors = fileErrors, Rows = result };
    }

    private static object? ConvertCell(SqliteConnection conn, ColumnMeta col, string cell,
        Dictionary<string, HashSet<long>> fkCache, List<string> errors)
    {
        if (cell.Length == 0)
        {
            if (col.Required)
            {
                errors.Add("Не заполнено обязательное поле «" + col.Title + "».");
            }
            return col.Kind == ColumnKind.Text || col.Kind == ColumnKind.Choice ? (object?)"" : null;
        }
        switch (col.Kind)
        {
            case ColumnKind.Int:
                if (long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                {
                    return l;
                }
                errors.Add("Поле «" + col.Title + "»: ожидается целое число, получено «" + cell + "».");
                return null;
            case ColumnKind.Real:
                if (double.TryParse(cell.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    return d;
                }
                errors.Add("Поле «" + col.Title + "»: ожидается число, получено «" + cell + "».");
                return null;
            case ColumnKind.Date:
                if (DateTime.TryParseExact(cell, new[] { "yyyy-MM-dd", "dd.MM.yyyy" }, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime dt))
                {
                    return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
                errors.Add("Поле «" + col.Title + "»: ожидается дата ГГГГ-ММ-ДД или ДД.ММ.ГГГГ, получено «" + cell + "».");
                return null;
            case ColumnKind.Bool:
                string x = cell.ToLowerInvariant();
                if (x == "1" || x == "true" || x == "да" || x == "yes")
                {
                    return 1L;
                }
                if (x == "0" || x == "false" || x == "нет" || x == "no")
                {
                    return 0L;
                }
                errors.Add("Поле «" + col.Title + "»: ожидается да/нет или 1/0, получено «" + cell + "».");
                return null;
            case ColumnKind.Choice:
                foreach (string ch in col.Choices)
                {
                    if (string.Equals(ch, cell, StringComparison.OrdinalIgnoreCase))
                    {
                        return ch;
                    }
                }
                errors.Add("Поле «" + col.Title + "»: недопустимое значение «" + cell + "». Допустимо: " + string.Join(", ", col.Choices) + ".");
                return null;
            case ColumnKind.Foreign:
                if (!long.TryParse(cell, NumberStyles.Integer, CultureInfo.InvariantCulture, out long fk))
                {
                    errors.Add("Поле «" + col.Title + "»: ожидается числовой идентификатор, получено «" + cell + "».");
                    return null;
                }
                string ft = col.ForeignTable ?? "";
                if (!fkCache.TryGetValue(ft, out var ids))
                {
                    ids = new HashSet<long>();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT id FROM " + ft;
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        ids.Add(r.GetInt64(0));
                    }
                    fkCache[ft] = ids;
                }
                if (!ids.Contains(fk))
                {
                    errors.Add("Поле «" + col.Title + "»: записи с номером " + fk + " не существует.");
                    return null;
                }
                return fk;
            default:
                return cell;
        }
    }

    private static void CheckUnique(SqliteConnection conn, TableMeta t, Dictionary<string, object?> values, int lineNo,
        Dictionary<string, int> seen, List<string> errors)
    {
        if (t.Name == "students")
        {
            string rb = Convert.ToString(values.GetValueOrDefault("record_book_no"), CultureInfo.InvariantCulture) ?? "";
            if (rb.Length == 0)
            {
                return;
            }
            string key = "students|" + rb.ToLowerInvariant();
            if (seen.TryGetValue(key, out int first))
            {
                errors.Add("Дубль внутри файла: № зачётной книжки «" + rb + "» уже встречается в строке " + first + ".");
            }
            else
            {
                seen[key] = lineNo;
            }
            if (Exists(conn, "SELECT 1 FROM students WHERE record_book_no=@v", rb))
            {
                errors.Add("Студент с № зачётной книжки «" + rb + "» уже есть в базе.");
            }
        }
        else if (t.Name == "rooms")
        {
            object? b = values.GetValueOrDefault("building_id");
            string num = Convert.ToString(values.GetValueOrDefault("number"), CultureInfo.InvariantCulture) ?? "";
            if (b == null || num.Length == 0)
            {
                return;
            }
            string key = "rooms|" + b + "|" + num.ToLowerInvariant();
            if (seen.TryGetValue(key, out int first))
            {
                errors.Add("Дубль внутри файла: аудитория «" + num + "» в этом корпусе уже встречается в строке " + first + ".");
            }
            else
            {
                seen[key] = lineNo;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM rooms WHERE building_id=@b AND number=@n";
            cmd.Parameters.AddWithValue("@b", b);
            cmd.Parameters.AddWithValue("@n", num);
            if (cmd.ExecuteScalar() != null)
            {
                errors.Add("Аудитория «" + num + "» уже есть в этом корпусе.");
            }
        }
        else if (t.Name == "teachers")
        {
            string ln = Convert.ToString(values.GetValueOrDefault("last_name"), CultureInfo.InvariantCulture) ?? "";
            string fn = Convert.ToString(values.GetValueOrDefault("first_name"), CultureInfo.InvariantCulture) ?? "";
            string mn = Convert.ToString(values.GetValueOrDefault("middle_name"), CultureInfo.InvariantCulture) ?? "";
            object? dep = values.GetValueOrDefault("department_id");
            string key = "teachers|" + dep + "|" + ln.ToLowerInvariant() + "|" + fn.ToLowerInvariant() + "|" + mn.ToLowerInvariant();
            if (seen.TryGetValue(key, out int first))
            {
                errors.Add("Дубль внутри файла: преподаватель уже встречается в строке " + first + ".");
            }
            else
            {
                seen[key] = lineNo;
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM teachers WHERE department_id=@d AND lower_ru(last_name)=lower_ru(@l) AND lower_ru(first_name)=lower_ru(@f) AND lower_ru(middle_name)=lower_ru(@m)";
            cmd.Parameters.AddWithValue("@d", dep ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@l", ln);
            cmd.Parameters.AddWithValue("@f", fn);
            cmd.Parameters.AddWithValue("@m", mn);
            if (cmd.ExecuteScalar() != null)
            {
                errors.Add("Такой преподаватель уже есть на этой кафедре.");
            }
        }
    }

    private static bool Exists(SqliteConnection conn, string sql, string value)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@v", value);
        return cmd.ExecuteScalar() != null;
    }

    public ImportResult Apply(ImportPreview preview)
    {
        if (preview.FileErrors.Count > 0)
        {
            return new ImportResult(false, 0, "Импорт невозможен: " + preview.FileErrors[0]);
        }
        var audit = new AuditService();
        int inserted = 0;
        try
        {
            using var conn = Db.Open();
            using var tx = conn.BeginTransaction();
            foreach (var row in preview.Rows)
            {
                if (!row.IsValid)
                {
                    continue;
                }
                var dict = new Dictionary<string, object?>(row.Values);
                audit.Insert(preview.Table, dict, tx);
                inserted++;
            }
            tx.Commit();
        }
        catch (AuditException ex)
        {
            return new ImportResult(false, 0, "Импорт отменён, изменения не сохранены: " + ex.Message);
        }
        catch (SqliteException ex)
        {
            return new ImportResult(false, 0, "Импорт отменён, изменения не сохранены: " + AuditService.Explain(ex));
        }
        return new ImportResult(true, inserted, "Импортировано записей: " + inserted + ".");
    }
}
