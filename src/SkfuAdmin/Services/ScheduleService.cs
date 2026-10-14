using System.Globalization;
using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;

namespace SkfuAdmin.Services;

public enum ConflictKind
{
    Teacher,
    Room,
    Group,
    Capacity
}

public sealed record Conflict(ConflictKind Kind, string Message, long? EntryId = null, long? OtherEntryId = null);

public sealed record ScheduleEntry(
    long Id,
    long GroupId,
    string GroupName,
    long SubjectId,
    string SubjectName,
    long TeacherId,
    string TeacherName,
    long RoomId,
    string RoomLabel,
    int Weekday,
    long SlotId,
    int SlotNumber,
    string StartTime,
    string EndTime,
    int Parity,
    string Kind);

public sealed record RoomInfo(long Id, long BuildingId, string BuildingName, string Number, string Label,
    int Capacity, string Kind, bool HasProjector);

public sealed record RoomLoad(RoomInfo Room, bool[,] Busy);

public sealed record ScheduleDraft(
    long Id,
    long GroupId,
    long SubjectId,
    long TeacherId,
    long RoomId,
    int Weekday,
    long SlotId,
    int Parity,
    string Kind);

public sealed record ScheduleSaveResult(bool Saved, long Id, IReadOnlyList<Conflict> Conflicts, string Message);

public sealed class ScheduleService
{
    private const string TeacherExpr =
        "t.last_name||' '||substr(t.first_name,1,1)||'.'||CASE WHEN t.middle_name<>'' THEN substr(t.middle_name,1,1)||'.' ELSE '' END";

    private const string RoomExpr = "COALESCE(b.name,'')||', ауд. '||r.number";

    private const string EntrySelect = @"
SELECT s.id, s.group_id, g.name, s.subject_id, sub.name, s.teacher_id, " + TeacherExpr + @",
       s.room_id, " + RoomExpr + @", s.weekday, s.slot_id, ts.number, ts.start_time, ts.end_time,
       s.week_parity, s.kind
FROM schedule s
JOIN groups g ON g.id=s.group_id
JOIN subjects sub ON sub.id=s.subject_id
JOIN teachers t ON t.id=s.teacher_id
JOIN rooms r ON r.id=s.room_id
LEFT JOIN buildings b ON b.id=r.building_id
JOIN time_slots ts ON ts.id=s.slot_id";

    private const string Overlap = "(s.week_parity=0 OR @par=0 OR s.week_parity=@par)";

    private readonly AuditService _audit = new();

    public IReadOnlyList<ScheduleEntry> ForGroup(long id, int? parity) => ForColumn("s.group_id", id, parity);

    public IReadOnlyList<ScheduleEntry> ForTeacher(long id, int? parity) => ForColumn("s.teacher_id", id, parity);

    public IReadOnlyList<ScheduleEntry> ForRoom(long id, int? parity) => ForColumn("s.room_id", id, parity);

    public ScheduleEntry? GetEntry(long id)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = EntrySelect + " WHERE s.id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new ScheduleEntry(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetString(4),
            r.GetInt64(5), r.GetString(6), r.GetInt64(7), r.GetString(8), r.GetInt32(9), r.GetInt64(10), r.GetInt32(11),
            r.GetString(12), r.GetString(13), r.GetInt32(14), r.GetString(15)) : null;
    }

    public bool DeleteEntry(long id)
    {
        var table = TableCatalog.Get("schedule");
        if (new GenericRepository().GetById(table, id) == null)
        {
            throw new InvalidOperationException("Занятие не найдено.");
        }
        using (var conn = Db.Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM attendance WHERE schedule_id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            if (Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                throw new InvalidOperationException("Невозможно удалить занятие: для него есть записи посещаемости.");
            }
        }
        _audit.Delete(table, id);
        return true;
    }

    private static IReadOnlyList<ScheduleEntry> ForColumn(string column, long id, int? parity)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        string sql = EntrySelect + " WHERE " + column + "=@id";
        cmd.Parameters.AddWithValue("@id", id);
        if (parity.HasValue && parity.Value != 0)
        {
            sql += " AND (s.week_parity=0 OR s.week_parity=@par)";
            cmd.Parameters.AddWithValue("@par", (long)parity.Value);
        }
        cmd.CommandText = sql + " ORDER BY s.weekday, ts.number, s.week_parity";
        var list = new List<ScheduleEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ScheduleEntry(
                r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt64(3), r.GetString(4),
                r.GetInt64(5), r.GetString(6), r.GetInt64(7), r.GetString(8),
                r.GetInt32(9), r.GetInt64(10), r.GetInt32(11), r.GetString(12), r.GetString(13),
                r.GetInt32(14), r.GetString(15)));
        }
        return list;
    }

    public IReadOnlyList<(long Id, string Label)> TimeSlots()
    {
        using var conn = Db.Open();
        return TimeSlotsCore(conn);
    }

    private static List<(long Id, string Label)> TimeSlotsCore(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, number, start_time, end_time FROM time_slots ORDER BY number";
        var list = new List<(long, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add((r.GetInt64(0),
                r.GetInt64(1).ToString(CultureInfo.InvariantCulture) + " пара (" + r.GetString(2) + "-" + r.GetString(3) + ")"));
        }
        return list;
    }

    public IReadOnlyList<Conflict> CheckEntry(long groupId, long teacherId, long roomId, int weekday, long slotId,
        int parity, long? excludeId)
    {
        using var conn = Db.Open();
        return CheckCore(conn, null, groupId, teacherId, roomId, weekday, slotId, parity, excludeId);
    }

    private static string ParityName(int p) => p == 1 ? "нечётная неделя" : p == 2 ? "чётная неделя" : "каждая неделя";

    private static string DayName(int d)
    {
        string[] names = { "", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота" };
        return d >= 1 && d < names.Length ? names[d] : "день " + d;
    }

    private static List<Conflict> CheckCore(SqliteConnection conn, SqliteTransaction? tx, long groupId, long teacherId,
        long roomId, int weekday, long slotId, int parity, long? excludeId)
    {
        var result = new List<Conflict>();
        string[] kinds = { "s.teacher_id", "s.room_id", "s.group_id" };
        long[] values = { teacherId, roomId, groupId };
        ConflictKind[] ck = { ConflictKind.Teacher, ConflictKind.Room, ConflictKind.Group };
        string[] subjects = { "Преподаватель", "Аудитория", "Группа" };
        for (int i = 0; i < kinds.Length; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT s.id, g.name, " + TeacherExpr + ", " + RoomExpr + ", s.week_parity " +
                              "FROM schedule s JOIN groups g ON g.id=s.group_id " +
                              "JOIN teachers t ON t.id=s.teacher_id JOIN rooms r ON r.id=s.room_id " +
                              "LEFT JOIN buildings b ON b.id=r.building_id " +
                              "WHERE " + kinds[i] + "=@v AND s.weekday=@wd AND s.slot_id=@slot AND " + Overlap +
                              " AND (@ex IS NULL OR s.id<>@ex)";
            cmd.Parameters.AddWithValue("@v", values[i]);
            cmd.Parameters.AddWithValue("@wd", (long)weekday);
            cmd.Parameters.AddWithValue("@slot", slotId);
            cmd.Parameters.AddWithValue("@par", (long)parity);
            cmd.Parameters.AddWithValue("@ex", excludeId.HasValue ? excludeId.Value : DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long other = r.GetInt64(0);
                string msg = subjects[i] + " уже занят(а) в это время (" + DayName(weekday) + ", " +
                             ParityName(r.GetInt32(4)) + "): группа " + r.GetString(1) + ", " + r.GetString(2) +
                             ", " + r.GetString(3) + " (занятие №" + other + ").";
                result.Add(new Conflict(ck[i], msg, excludeId, other));
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT r.capacity, " + RoomExpr + @",
 (SELECT COUNT(*) FROM students st WHERE st.group_id=@g AND st.status='учится'),
 (SELECT name FROM groups WHERE id=@g)
FROM rooms r LEFT JOIN buildings b ON b.id=r.building_id WHERE r.id=@room";
            cmd.Parameters.AddWithValue("@g", groupId);
            cmd.Parameters.AddWithValue("@room", roomId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                long cap = r.GetInt64(0);
                long cnt = r.GetInt64(2);
                if (cap < cnt)
                {
                    result.Add(new Conflict(ConflictKind.Capacity,
                        "Вместимость аудитории (" + cap + ") меньше численности группы " + r.GetString(3) +
                        " (" + cnt + "): " + r.GetString(1) + ".", excludeId, null));
                }
            }
        }
        return result;
    }

    public IReadOnlyList<Conflict> FindAllConflicts()
    {
        var result = new List<Conflict>();
        using var conn = Db.Open();
        string[] cols = { "teacher_id", "room_id", "group_id" };
        ConflictKind[] ck = { ConflictKind.Teacher, ConflictKind.Room, ConflictKind.Group };
        string[] labels = { "Преподаватель", "Аудитория", "Группа" };
        for (int i = 0; i < cols.Length; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT a.id, b.id, a.weekday, ts.number, a.week_parity, b.week_parity, ga.name, gb.name
FROM schedule a
JOIN schedule b ON b.id>a.id AND b.weekday=a.weekday AND b.slot_id=a.slot_id AND b." + cols[i] + "=a." + cols[i] + @"
 AND (a.week_parity=0 OR b.week_parity=0 OR a.week_parity=b.week_parity)
JOIN time_slots ts ON ts.id=a.slot_id
JOIN groups ga ON ga.id=a.group_id
JOIN groups gb ON gb.id=b.group_id
ORDER BY a.weekday, ts.number, a.id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                string msg = labels[i] + ": пересечение занятий №" + r.GetInt64(0) + " (группа " + r.GetString(6) +
                             ") и №" + r.GetInt64(1) + " (группа " + r.GetString(7) + "), " + DayName(r.GetInt32(2)) +
                             ", " + r.GetInt64(3) + " пара.";
                result.Add(new Conflict(ck[i], msg, r.GetInt64(0), r.GetInt64(1)));
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT s.id, g.name, r.capacity, " + RoomExpr + @",
 (SELECT COUNT(*) FROM students st WHERE st.group_id=s.group_id AND st.status='учится') AS cnt
FROM schedule s
JOIN groups g ON g.id=s.group_id
JOIN rooms r ON r.id=s.room_id
LEFT JOIN buildings b ON b.id=r.building_id
WHERE r.capacity < (SELECT COUNT(*) FROM students st WHERE st.group_id=s.group_id AND st.status='учится')
ORDER BY s.id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(new Conflict(ConflictKind.Capacity,
                    "Занятие №" + r.GetInt64(0) + ": вместимость аудитории (" + r.GetInt64(2) +
                    ") меньше численности группы " + r.GetString(1) + " (" + r.GetInt64(4) + "), " + r.GetString(3) + ".",
                    r.GetInt64(0), null));
            }
        }
        return result;
    }

    public IReadOnlyList<RoomInfo> FindFreeRooms(int weekday, long slotId, int minCapacity, long? buildingId,
        string? kind, int parity)
    {
        using var conn = Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT r.id, r.building_id, COALESCE(b.name,''), r.number, r.capacity, r.kind, r.has_projector
FROM rooms r LEFT JOIN buildings b ON b.id=r.building_id
WHERE r.capacity>=@cap
 AND (@bid IS NULL OR r.building_id=@bid)
 AND (@kind IS NULL OR r.kind=@kind)
 AND NOT EXISTS (SELECT 1 FROM schedule s WHERE s.room_id=r.id AND s.weekday=@wd AND s.slot_id=@slot AND " + Overlap + @")
ORDER BY b.name, r.number";
        cmd.Parameters.AddWithValue("@cap", (long)minCapacity);
        cmd.Parameters.AddWithValue("@bid", buildingId.HasValue ? buildingId.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@kind", string.IsNullOrWhiteSpace(kind) ? DBNull.Value : kind!);
        cmd.Parameters.AddWithValue("@wd", (long)weekday);
        cmd.Parameters.AddWithValue("@slot", slotId);
        cmd.Parameters.AddWithValue("@par", (long)parity);
        return ReadRooms(cmd);
    }

    private static List<RoomInfo> ReadRooms(SqliteCommand cmd)
    {
        var list = new List<RoomInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string bname = r.GetString(2);
            string number = r.GetString(3);
            list.Add(new RoomInfo(r.GetInt64(0), r.GetInt64(1), bname, number, bname + ", ауд. " + number,
                (int)r.GetInt64(4), r.GetString(5), r.GetInt64(6) != 0));
        }
        return list;
    }

    public IReadOnlyList<RoomLoad> RoomLoadForBuilding(long buildingId, int parity)
    {
        using var conn = Db.Open();
        var slots = TimeSlotsCore(conn);
        var slotIndex = new Dictionary<long, int>();
        for (int i = 0; i < slots.Count; i++)
        {
            slotIndex[slots[i].Id] = i;
        }
        List<RoomInfo> rooms;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT r.id, r.building_id, COALESCE(b.name,''), r.number, r.capacity, r.kind, r.has_projector
FROM rooms r LEFT JOIN buildings b ON b.id=r.building_id WHERE r.building_id=@bid ORDER BY r.number";
            cmd.Parameters.AddWithValue("@bid", buildingId);
            rooms = ReadRooms(cmd);
        }
        var map = new Dictionary<long, bool[,]>();
        var result = new List<RoomLoad>();
        foreach (var room in rooms)
        {
            var busy = new bool[6, slots.Count];
            map[room.Id] = busy;
            result.Add(new RoomLoad(room, busy));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT s.room_id, s.weekday, s.slot_id FROM schedule s
JOIN rooms r ON r.id=s.room_id WHERE r.building_id=@bid AND " + Overlap;
            cmd.Parameters.AddWithValue("@bid", buildingId);
            cmd.Parameters.AddWithValue("@par", (long)parity);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int wd = r.GetInt32(1);
                if (wd < 1 || wd > 6 || !slotIndex.TryGetValue(r.GetInt64(2), out int si))
                {
                    continue;
                }
                if (map.TryGetValue(r.GetInt64(0), out var arr))
                {
                    arr[wd - 1, si] = true;
                }
            }
        }
        return result;
    }

    public ScheduleSaveResult SaveEntry(ScheduleDraft d)
    {
        var table = TableCatalog.Get("schedule");
        var values = new Dictionary<string, object?>
        {
            ["group_id"] = d.GroupId,
            ["subject_id"] = d.SubjectId,
            ["teacher_id"] = d.TeacherId,
            ["room_id"] = d.RoomId,
            ["weekday"] = (long)d.Weekday,
            ["slot_id"] = d.SlotId,
            ["week_parity"] = (long)d.Parity,
            ["kind"] = d.Kind
        };
        try
        {
            using var conn = Db.Open();
            using var tx = conn.BeginTransaction();
            long? exclude = d.Id > 0 ? d.Id : null;
            var conflicts = CheckCore(conn, tx, d.GroupId, d.TeacherId, d.RoomId, d.Weekday, d.SlotId, d.Parity, exclude);
            if (conflicts.Count > 0)
            {
                tx.Rollback();
                return new ScheduleSaveResult(false, d.Id, conflicts,
                    "Занятие не сохранено: обнаружены конфликты расписания (" + conflicts.Count + ").");
            }
            long id = d.Id;
            if (d.Id > 0)
            {
                _audit.Update(table, d.Id, values, tx);
            }
            else
            {
                id = _audit.Insert(table, values, tx);
            }
            tx.Commit();
            return new ScheduleSaveResult(true, id, Array.Empty<Conflict>(), "Занятие сохранено.");
        }
        catch (AuditException ex)
        {
            return new ScheduleSaveResult(false, d.Id, Array.Empty<Conflict>(), ex.Message);
        }
        catch (SqliteException ex)
        {
            return new ScheduleSaveResult(false, d.Id, Array.Empty<Conflict>(), AuditService.Explain(ex));
        }
    }
}
