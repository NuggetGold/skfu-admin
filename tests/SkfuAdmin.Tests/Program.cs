using Microsoft.Data.Sqlite;
using SkfuAdmin.Data;
using SkfuAdmin.Services;

Environment.SetEnvironmentVariable("SKFU_DB_PATH", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skfu-tests-" + Guid.NewGuid().ToString("N") + ".db"));
var files = new List<string> { Db.Path };
try
{
    Schema.EnsureCreated();
    using (var conn = Db.Open())
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "INSERT INTO institutes(name) VALUES('Институт'); INSERT INTO departments(institute_id,name) VALUES(1,'Кафедра'); INSERT INTO teachers(department_id,last_name,first_name) VALUES(1,'Иванов','Иван'),(1,'Петров','Пётр'); INSERT INTO programs(department_id,code,name,degree,years) VALUES(1,'01','Программа','бакалавриат',4); INSERT INTO groups(program_id,name,course,year_admitted,form,capacity) VALUES(1,'Группа 1',1,2026,'очная',30),(1,'Группа 2',1,2026,'очная',30),(1,'Группа 3',1,2026,'очная',30); INSERT INTO students(group_id,last_name,first_name,record_book_no,gender,status) VALUES(3,'Студент','Первый','001','М','учится'),(3,'Студент','Второй','002','Ж','учится'); INSERT INTO subjects(department_id,name) VALUES(1,'Математика'); INSERT INTO buildings(name) VALUES('Корпус'); INSERT INTO rooms(building_id,number,floor,capacity,kind) VALUES(1,'101',1,30,'лекционная'),(1,'102',1,1,'лекционная'); INSERT INTO time_slots(number,start_time,end_time) VALUES(1,'08:00','09:30'); INSERT INTO users(login,role) VALUES('admin','администратор');";
        cmd.ExecuteNonQuery();
    }
    var schedule = new ScheduleService();
    var first = schedule.SaveEntry(new ScheduleDraft(0, 1, 1, 1, 1, 1, 1, 0, "лекция"));
    Check(first.Saved, "первое занятие сохранено");
    var conflict = schedule.SaveEntry(new ScheduleDraft(0, 1, 1, 1, 1, 1, 1, 0, "лекция"));
    Check(!conflict.Saved && conflict.Conflicts.Any(x => x.Kind == ConflictKind.Teacher), "конфликт преподавателя отклонён");
    using (var conn = Db.Open()) using (var cmd = conn.CreateCommand()) { cmd.CommandText = "SELECT COUNT(*) FROM schedule"; Check(Convert.ToInt32(cmd.ExecuteScalar()) == 1, "конфликт не создал запись"); }
    var roomConflict = schedule.SaveEntry(new ScheduleDraft(0, 2, 1, 2, 1, 1, 1, 0, "лекция"));
    Check(!roomConflict.Saved && roomConflict.Conflicts.Any(x => x.Kind == ConflictKind.Room), "конфликт аудитории отклонён");
    var groupConflict = schedule.SaveEntry(new ScheduleDraft(0, 1, 1, 2, 2, 1, 1, 0, "лекция"));
    Check(!groupConflict.Saved && groupConflict.Conflicts.Any(x => x.Kind == ConflictKind.Group), "конфликт группы отклонён");
    var capacityConflict = schedule.SaveEntry(new ScheduleDraft(0, 3, 1, 2, 2, 2, 1, 0, "лекция"));
    Check(!capacityConflict.Saved && capacityConflict.Conflicts.Any(x => x.Kind == ConflictKind.Capacity), "конфликт вместимости отклонён");
    var parityConflict = schedule.SaveEntry(new ScheduleDraft(0, 2, 1, 1, 2, 1, 1, 1, "лекция"));
    Check(!parityConflict.Saved && parityConflict.Conflicts.Any(x => x.Kind == ConflictKind.Teacher), "конфликт каждой и нечётной недели отклонён");
    var evenEntry = schedule.SaveEntry(new ScheduleDraft(0, 2, 1, 1, 1, 3, 1, 2, "лекция"));
    Check(evenEntry.Saved, "занятие чётной недели сохранено");
    var oddEntry = schedule.SaveEntry(new ScheduleDraft(0, 3, 1, 1, 1, 3, 1, 1, "лекция"));
    Check(oddEntry.Saved, "занятие нечётной недели сохранено");
    Check(schedule.DeleteEntry(evenEntry.Id), "занятие удалено через сервис");
    Check(!Exists(TableCatalog.Get("schedule"), evenEntry.Id), "удалённое занятие отсутствует");
    var audit = new AuditService(); var meta = TableCatalog.Get("institutes"); var id = audit.Insert(meta, new Dictionary<string, object?> { ["name"] = "Аудит" }); Check(Exists(meta, id), "insert"); audit.Update(meta, id, new Dictionary<string, object?> { ["name"] = "Изменено" }); Check(ReadName(id) == "Изменено", "update"); var updateLog = LastAudit(id); Check(audit.Undo(updateLog).Success && ReadName(id) == "Аудит", "undo update"); audit.Delete(meta, id); Check(!Exists(meta, id), "delete"); var deleteLog = LastAudit(id); Check(audit.Undo(deleteLog).Success && Exists(meta, id), "undo delete");
    var service = new SavedViewService(); service.Save("institutes", "Проверка", new ViewState { Search = "Аудит", Desc = true }); var loaded = service.Load(service.List("institutes").Single(x => x.Name == "Проверка").Id); Check(loaded.State?.Search == "Аудит" && loaded.State.Desc, "saved view");
    var backup = BackupService.CreateBackup("тест"); var wrong = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skfu-wrong-" + Guid.NewGuid().ToString("N") + ".db"); files.Add(wrong); using (var conn = new SqliteConnection("Data Source=" + wrong)) { conn.Open(); using var cmd = conn.CreateCommand(); cmd.CommandText = "CREATE TABLE x(id INTEGER);"; cmd.ExecuteNonQuery(); } var before = ReadName(id); try { BackupService.Restore(wrong); throw new Exception("неверная копия принята"); } catch (BackupException) { } Check(ReadName(id) == before, "неверная копия не изменила базу"); BackupService.Restore(backup.Path); Check(Exists(meta, id), "восстановление корректной копии");
    Console.WriteLine("Тесты пройдены.");
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
finally { foreach (var file in files) foreach (var suffix in new[] { "", "-wal", "-shm" }) try { if (File.Exists(file + suffix)) File.Delete(file + suffix); } catch { } }

static void Check(bool condition, string name) { if (!condition) throw new Exception("Не пройден тест: " + name); Console.WriteLine("Пройден: " + name); }
static bool Exists(TableMeta table, long id) { using var conn = Db.Open(); using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM " + table.Name + " WHERE id=@id"; cmd.Parameters.AddWithValue("@id", id); return Convert.ToInt32(cmd.ExecuteScalar()) == 1; }
static string ReadName(long id) { using var conn = Db.Open(); using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT name FROM institutes WHERE id=@id"; cmd.Parameters.AddWithValue("@id", id); return Convert.ToString(cmd.ExecuteScalar()) ?? ""; }
static long LastAudit(long id) { using var conn = Db.Open(); using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT id FROM audit_log WHERE row_id=@id ORDER BY id DESC LIMIT 1"; cmd.Parameters.AddWithValue("@id", id); return Convert.ToInt64(cmd.ExecuteScalar()); }
