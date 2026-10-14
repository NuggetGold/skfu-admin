using System.Globalization;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public enum Severity
{
    Info,
    Warning,
    Error
}

public sealed record IntegrityRule(string Id, string Title, Severity Severity, string CountSql, string ListSql);

public sealed record IntegrityResult(string RuleId, string Title, Severity Severity, int Count);

public sealed record IntegrityIssue(string Table, long RowId, string Description);

public sealed class IntegrityService
{
    private const string FioExpr = "last_name||' '||first_name||CASE WHEN middle_name<>'' THEN ' '||middle_name ELSE '' END";

    private const string PairOverlap =
        "b.id>a.id AND b.weekday=a.weekday AND b.slot_id=a.slot_id AND (a.week_parity=0 OR b.week_parity=0 OR a.week_parity=b.week_parity)";

    private static string Pair(string col, string what) =>
        "FROM schedule a JOIN schedule b ON " + PairOverlap + " AND b." + col + "=a." + col;

    private static string PairList(string col, string what) =>
        "SELECT 'schedule', a.id, '" + what + ": занятия №'||a.id||' и №'||b.id||' (день '||a.weekday||', пара '||" +
        "(SELECT number FROM time_slots WHERE id=a.slot_id)||')' " + Pair(col, what) + " ORDER BY a.id";

    private static string PairCount(string col, string what) => "SELECT COUNT(*) " + Pair(col, what);

    private const string Rooms = @"FROM schedule s
JOIN groups g ON g.id=s.group_id
JOIN rooms r ON r.id=s.room_id
WHERE r.capacity < (SELECT COUNT(*) FROM students st WHERE st.group_id=s.group_id AND st.status='учится')";

    public static IReadOnlyList<IntegrityRule> Rules { get; } = new List<IntegrityRule>
    {
        new IntegrityRule("student_no_group", "Студенты со статусом «учится» без группы", Severity.Warning,
            "SELECT COUNT(*) FROM students WHERE status='учится' AND group_id IS NULL",
            "SELECT 'students', id, " + FioExpr + " FROM students WHERE status='учится' AND group_id IS NULL ORDER BY id"),

        new IntegrityRule("group_no_curator", "Группы без куратора", Severity.Info,
            "SELECT COUNT(*) FROM groups WHERE curator_id IS NULL",
            "SELECT 'groups', id, 'Группа '||name FROM groups WHERE curator_id IS NULL ORDER BY id"),

        new IntegrityRule("dup_record_book", "Дубли номеров зачётных книжек", Severity.Error,
            "SELECT COUNT(*) FROM students WHERE record_book_no IN (SELECT record_book_no FROM students GROUP BY record_book_no HAVING COUNT(*)>1)",
            "SELECT 'students', id, " + FioExpr + "||': зачётка '||record_book_no FROM students WHERE record_book_no IN (SELECT record_book_no FROM students GROUP BY record_book_no HAVING COUNT(*)>1) ORDER BY record_book_no, id"),

        new IntegrityRule("dup_fio_birth", "Дубли ФИО и даты рождения", Severity.Warning,
            "SELECT COUNT(*) FROM (SELECT lower_ru(last_name), lower_ru(first_name), lower_ru(middle_name), birth_date FROM students WHERE birth_date IS NOT NULL GROUP BY lower_ru(last_name), lower_ru(first_name), lower_ru(middle_name), birth_date HAVING COUNT(*)>1)",
            "SELECT 'students', s.id, " + FioExpr + "||', '||s.birth_date FROM students s WHERE s.birth_date IS NOT NULL AND EXISTS (SELECT 1 FROM students o WHERE o.id<>s.id AND lower_ru(o.last_name)=lower_ru(s.last_name) AND lower_ru(o.first_name)=lower_ru(s.first_name) AND lower_ru(o.middle_name)=lower_ru(s.middle_name) AND o.birth_date=s.birth_date) ORDER BY s.last_name, s.id"),

        new IntegrityRule("room_too_small", "Занятия в аудитории вместимостью меньше численности группы", Severity.Error,
            "SELECT COUNT(*) " + Rooms,
            "SELECT 'schedule', s.id, 'Группа '||g.name||': в аудитории '||r.number||' мест '||r.capacity||', студентов '||(SELECT COUNT(*) FROM students st WHERE st.group_id=s.group_id AND st.status='учится') " + Rooms + " ORDER BY s.id"),

        new IntegrityRule("conflict_teacher", "Конфликты расписания: преподаватель", Severity.Error,
            PairCount("teacher_id", "Преподаватель"), PairList("teacher_id", "Преподаватель")),

        new IntegrityRule("conflict_room", "Конфликты расписания: аудитория", Severity.Error,
            PairCount("room_id", "Аудитория"), PairList("room_id", "Аудитория")),

        new IntegrityRule("conflict_group", "Конфликты расписания: группа", Severity.Error,
            PairCount("group_id", "Группа"), PairList("group_id", "Группа")),

        new IntegrityRule("teacher_no_department", "Преподаватели без кафедры", Severity.Error,
            "SELECT COUNT(*) FROM teachers WHERE department_id IS NULL OR department_id NOT IN (SELECT id FROM departments)",
            "SELECT 'teachers', id, " + FioExpr + " FROM teachers WHERE department_id IS NULL OR department_id NOT IN (SELECT id FROM departments) ORDER BY id"),

        new IntegrityRule("room_no_building", "Аудитории без корпуса", Severity.Error,
            "SELECT COUNT(*) FROM rooms WHERE building_id IS NULL OR building_id NOT IN (SELECT id FROM buildings)",
            "SELECT 'rooms', id, 'Аудитория '||number FROM rooms WHERE building_id IS NULL OR building_id NOT IN (SELECT id FROM buildings) ORDER BY id"),

        new IntegrityRule("department_no_head", "Кафедры без заведующего", Severity.Info,
            "SELECT COUNT(*) FROM departments WHERE head_teacher_id IS NULL",
            "SELECT 'departments', id, 'Кафедра '||name FROM departments WHERE head_teacher_id IS NULL ORDER BY id"),

        new IntegrityRule("grade_out_of_range", "Оценки вне диапазона 2..5", Severity.Error,
            "SELECT COUNT(*) FROM grades WHERE value IS NOT NULL AND (value<2 OR value>5)",
            "SELECT 'grades', id, 'Оценка '||value||' у студента №'||student_id FROM grades WHERE value IS NOT NULL AND (value<2 OR value>5) ORDER BY id"),

        new IntegrityRule("group_over_capacity", "Численность группы превышает её вместимость", Severity.Warning,
            "SELECT COUNT(*) FROM groups g WHERE (SELECT COUNT(*) FROM students st WHERE st.group_id=g.id AND st.status='учится') > g.capacity",
            "SELECT 'groups', g.id, 'Группа '||g.name||': студентов '||(SELECT COUNT(*) FROM students st WHERE st.group_id=g.id AND st.status='учится')||', мест '||g.capacity FROM groups g WHERE (SELECT COUNT(*) FROM students st WHERE st.group_id=g.id AND st.status='учится') > g.capacity ORDER BY g.id"),

        new IntegrityRule("group_no_schedule", "Группы без единого занятия в расписании", Severity.Info,
            "SELECT COUNT(*) FROM groups g WHERE NOT EXISTS (SELECT 1 FROM schedule s WHERE s.group_id=g.id)",
            "SELECT 'groups', g.id, 'Группа '||g.name FROM groups g WHERE NOT EXISTS (SELECT 1 FROM schedule s WHERE s.group_id=g.id) ORDER BY g.id")
    };

    public IReadOnlyList<IntegrityResult> RunAll()
    {
        var list = new List<IntegrityResult>();
        using var conn = Db.Open();
        foreach (var rule in Rules)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = rule.CountSql;
            int n = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            list.Add(new IntegrityResult(rule.Id, rule.Title, rule.Severity, n));
        }
        return list;
    }

    public IReadOnlyList<IntegrityIssue> Details(string ruleId, int limit)
    {
        IntegrityRule? rule = null;
        foreach (var r in Rules)
        {
            if (r.Id == ruleId)
            {
                rule = r;
                break;
            }
        }
        if (rule == null)
        {
            throw new ArgumentException("Неизвестное правило: " + ruleId);
        }
        var list = new List<IntegrityIssue>();
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = rule.ListSql + (limit > 0 ? " LIMIT @lim" : "");
        if (limit > 0)
        {
            cmd.Parameters.AddWithValue("@lim", (long)limit);
        }
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new IntegrityIssue(reader.GetString(0), reader.GetInt64(1),
                reader.IsDBNull(2) ? "" : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? ""));
        }
        return list;
    }

    public int TotalIssues()
    {
        int total = 0;
        foreach (var r in RunAll())
        {
            total += r.Count;
        }
        return total;
    }
}
