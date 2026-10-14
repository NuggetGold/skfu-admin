using System.Globalization;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public sealed record SearchHit(string Kind, string Table, long Id, string Title, string Subtitle);

public sealed class SearchService
{
    private const string Fio = "last_name||' '||first_name||CASE WHEN middle_name<>'' THEN ' '||middle_name ELSE '' END";

    public IReadOnlyList<SearchHit> Search(string text, int limit)
    {
        var hits = new List<SearchHit>();
        string q = (text ?? "").Trim();
        if (q.Length == 0 || limit <= 0)
        {
            return hits;
        }
        string pattern = "%" + Escape(q) + "%";
        using var conn = Db.Open();

        Run(conn, hits, limit, pattern, "Студент", "students",
            @"SELECT s.id, " + Fio + @", COALESCE(g.name,'без группы')||', зачётка '||s.record_book_no
FROM students s LEFT JOIN groups g ON g.id=s.group_id
WHERE lower_ru(" + Fio + @") LIKE lower_ru(@q) ESCAPE '\' OR lower_ru(s.record_book_no) LIKE lower_ru(@q) ESCAPE '\'
ORDER BY s.last_name, s.first_name, s.id LIMIT @lim");

        Run(conn, hits, limit, pattern, "Преподаватель", "teachers",
            @"SELECT t.id, " + Fio + @", COALESCE(d.name,'')
FROM teachers t LEFT JOIN departments d ON d.id=t.department_id
WHERE lower_ru(" + Fio + @") LIKE lower_ru(@q) ESCAPE '\'
ORDER BY t.last_name, t.first_name, t.id LIMIT @lim");

        Run(conn, hits, limit, pattern, "Группа", "groups",
            @"SELECT g.id, g.name, g.course||' курс, '||g.form
FROM groups g WHERE lower_ru(g.name) LIKE lower_ru(@q) ESCAPE '\'
ORDER BY g.name LIMIT @lim");

        Run(conn, hits, limit, pattern, "Аудитория", "rooms",
            @"SELECT r.id, COALESCE(b.name,'')||', ауд. '||r.number, r.kind||', мест '||r.capacity
FROM rooms r LEFT JOIN buildings b ON b.id=r.building_id
WHERE lower_ru(COALESCE(b.name,'')||' '||r.number) LIKE lower_ru(@q) ESCAPE '\'
   OR lower_ru(r.number) LIKE lower_ru(@q) ESCAPE '\'
ORDER BY b.name, r.number LIMIT @lim");

        Run(conn, hits, limit, pattern, "Дисциплина", "subjects",
            @"SELECT s.id, s.name, COALESCE(d.name,'')
FROM subjects s LEFT JOIN departments d ON d.id=s.department_id
WHERE lower_ru(s.name) LIKE lower_ru(@q) ESCAPE '\'
ORDER BY s.name LIMIT @lim");

        return hits;
    }

    private static void Run(SqliteConnection conn, List<SearchHit> hits, int limit, string pattern,
        string kind, string table, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@q", pattern);
        cmd.Parameters.AddWithValue("@lim", (long)limit);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            hits.Add(new SearchHit(kind, table, r.GetInt64(0),
                Convert.ToString(r.GetValue(1), CultureInfo.InvariantCulture) ?? "",
                r.IsDBNull(2) ? "" : Convert.ToString(r.GetValue(2), CultureInfo.InvariantCulture) ?? ""));
        }
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
