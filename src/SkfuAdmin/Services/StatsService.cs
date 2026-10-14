using System.Globalization;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public sealed record LabelValue(string Label, double Value);

public sealed record RecentChange(long Id, string At, string User, string Table, long RowId, string Op);

public sealed class DashboardStats
{
    public int Students { get; init; }
    public int Groups { get; init; }
    public int Teachers { get; init; }
    public int Rooms { get; init; }
    public int ScheduleEntries { get; init; }
    public int IntegrityIssues { get; init; }
    public IReadOnlyList<LabelValue> StudentsByInstitute { get; init; } = Array.Empty<LabelValue>();
    public IReadOnlyList<LabelValue> AverageGradeByInstitute { get; init; } = Array.Empty<LabelValue>();
    public IReadOnlyList<LabelValue> AttendanceByInstitute { get; init; } = Array.Empty<LabelValue>();
    public IReadOnlyList<LabelValue> LoadByWeekday { get; init; } = Array.Empty<LabelValue>();
    public IReadOnlyList<RecentChange> RecentChanges { get; init; } = Array.Empty<RecentChange>();
}

public sealed class StatsService
{
    private static readonly string[] Days = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };

    public DashboardStats Get()
    {
        using var conn = Db.Open();
        int issues = new IntegrityService().TotalIssues();
        return new DashboardStats
        {
            Students = Scalar(conn, "SELECT COUNT(*) FROM students WHERE status='учится'"),
            Groups = Scalar(conn, "SELECT COUNT(*) FROM groups"),
            Teachers = Scalar(conn, "SELECT COUNT(*) FROM teachers"),
            Rooms = Scalar(conn, "SELECT COUNT(*) FROM rooms"),
            ScheduleEntries = Scalar(conn, "SELECT COUNT(*) FROM schedule"),
            IntegrityIssues = issues,
            StudentsByInstitute = Pairs(conn, @"SELECT i.name, COUNT(s.id)
FROM institutes i
LEFT JOIN departments d ON d.institute_id=i.id
LEFT JOIN programs p ON p.department_id=d.id
LEFT JOIN groups g ON g.program_id=p.id
LEFT JOIN students s ON s.group_id=g.id AND s.status='учится'
GROUP BY i.id ORDER BY i.name"),
            AverageGradeByInstitute = Pairs(conn, @"SELECT i.name, COALESCE(AVG(gr.value),0)
FROM institutes i
LEFT JOIN departments d ON d.institute_id=i.id
LEFT JOIN programs p ON p.department_id=d.id
LEFT JOIN groups g ON g.program_id=p.id
LEFT JOIN students s ON s.group_id=g.id
LEFT JOIN grades gr ON gr.student_id=s.id AND gr.value IS NOT NULL
GROUP BY i.id ORDER BY i.name"),
            AttendanceByInstitute = Pairs(conn, @"SELECT i.name, COALESCE(100.0*SUM(a.present)/NULLIF(COUNT(a.id),0),0)
FROM institutes i
LEFT JOIN departments d ON d.institute_id=i.id
LEFT JOIN programs p ON p.department_id=d.id
LEFT JOIN groups g ON g.program_id=p.id
LEFT JOIN students s ON s.group_id=g.id
LEFT JOIN attendance a ON a.student_id=s.id
GROUP BY i.id ORDER BY i.name"),
            LoadByWeekday = Weekdays(conn),
            RecentChanges = Recent(conn)
        };
    }

    private static int Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static List<LabelValue> Pairs(SqliteConnection conn, string sql)
    {
        var list = new List<LabelValue>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new LabelValue(r.GetString(0), r.IsDBNull(1) ? 0 : Convert.ToDouble(r.GetValue(1), CultureInfo.InvariantCulture)));
        }
        return list;
    }

    private static List<LabelValue> Weekdays(SqliteConnection conn)
    {
        var counts = new double[6];
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT weekday, COUNT(*) FROM schedule GROUP BY weekday";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int wd = r.GetInt32(0);
                if (wd >= 1 && wd <= 6)
                {
                    counts[wd - 1] = r.GetInt64(1);
                }
            }
        }
        var list = new List<LabelValue>();
        for (int i = 0; i < 6; i++)
        {
            list.Add(new LabelValue(Days[i], counts[i]));
        }
        return list;
    }

    private static List<RecentChange> Recent(SqliteConnection conn)
    {
        var list = new List<RecentChange>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, at, user_login, table_name, row_id, op FROM audit_log ORDER BY id DESC LIMIT 8";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new RecentChange(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4), r.GetString(5)));
        }
        return list;
    }
}
