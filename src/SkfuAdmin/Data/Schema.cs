using Microsoft.Data.Sqlite;

namespace SkfuAdmin.Data;

public static class Schema
{
    public const int Version = 1;

    private const string Sql = @"
CREATE TABLE IF NOT EXISTS institutes(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    short_name TEXT NOT NULL DEFAULT '',
    dean TEXT NOT NULL DEFAULT '',
    city TEXT NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS departments(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    institute_id INTEGER NOT NULL REFERENCES institutes(id),
    name TEXT NOT NULL,
    head_teacher_id INTEGER NULL REFERENCES teachers(id) ON DELETE SET NULL
);
CREATE TABLE IF NOT EXISTS programs(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    department_id INTEGER NOT NULL REFERENCES departments(id),
    code TEXT NOT NULL,
    name TEXT NOT NULL,
    degree TEXT NOT NULL CHECK(degree IN ('бакалавриат','магистратура','специалитет')),
    years INTEGER NOT NULL DEFAULT 4 CHECK(years BETWEEN 1 AND 8)
);
CREATE TABLE IF NOT EXISTS teachers(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    department_id INTEGER NOT NULL REFERENCES departments(id),
    last_name TEXT NOT NULL,
    first_name TEXT NOT NULL,
    middle_name TEXT NOT NULL DEFAULT '',
    position TEXT NOT NULL DEFAULT '',
    degree TEXT NOT NULL DEFAULT '',
    email TEXT NOT NULL DEFAULT '',
    phone TEXT NOT NULL DEFAULT '',
    hire_date TEXT NULL
);
CREATE TABLE IF NOT EXISTS groups(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    program_id INTEGER NOT NULL REFERENCES programs(id),
    name TEXT NOT NULL UNIQUE,
    course INTEGER NOT NULL CHECK(course BETWEEN 1 AND 6),
    year_admitted INTEGER NOT NULL,
    curator_id INTEGER NULL REFERENCES teachers(id) ON DELETE SET NULL,
    form TEXT NOT NULL DEFAULT 'очная' CHECK(form IN ('очная','заочная')),
    capacity INTEGER NOT NULL DEFAULT 25 CHECK(capacity > 0)
);
CREATE TABLE IF NOT EXISTS students(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    group_id INTEGER NULL REFERENCES groups(id) ON DELETE SET NULL,
    last_name TEXT NOT NULL,
    first_name TEXT NOT NULL,
    middle_name TEXT NOT NULL DEFAULT '',
    birth_date TEXT NULL,
    record_book_no TEXT NOT NULL UNIQUE,
    gender TEXT NOT NULL CHECK(gender IN ('М','Ж')),
    status TEXT NOT NULL DEFAULT 'учится' CHECK(status IN ('учится','академ','отчислен')),
    phone TEXT NOT NULL DEFAULT '',
    email TEXT NOT NULL DEFAULT '',
    enrolled_at TEXT NULL
);
CREATE TABLE IF NOT EXISTS buildings(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    address TEXT NOT NULL DEFAULT '',
    city TEXT NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS rooms(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    building_id INTEGER NOT NULL REFERENCES buildings(id),
    number TEXT NOT NULL,
    floor INTEGER NOT NULL DEFAULT 1,
    capacity INTEGER NOT NULL DEFAULT 30 CHECK(capacity > 0),
    kind TEXT NOT NULL CHECK(kind IN ('лекционная','практика','компьютерный класс','лаборатория')),
    has_projector INTEGER NOT NULL DEFAULT 0 CHECK(has_projector IN (0,1)),
    UNIQUE(building_id, number)
);
CREATE TABLE IF NOT EXISTS subjects(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    department_id INTEGER NOT NULL REFERENCES departments(id),
    name TEXT NOT NULL,
    short_name TEXT NOT NULL DEFAULT '',
    hours_lecture INTEGER NOT NULL DEFAULT 0 CHECK(hours_lecture >= 0),
    hours_practice INTEGER NOT NULL DEFAULT 0 CHECK(hours_practice >= 0),
    hours_lab INTEGER NOT NULL DEFAULT 0 CHECK(hours_lab >= 0)
);
CREATE TABLE IF NOT EXISTS curriculum(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    program_id INTEGER NOT NULL REFERENCES programs(id),
    subject_id INTEGER NOT NULL REFERENCES subjects(id),
    semester INTEGER NOT NULL CHECK(semester BETWEEN 1 AND 12),
    control TEXT NOT NULL CHECK(control IN ('экзамен','зачёт','зачёт с оценкой')),
    UNIQUE(program_id, subject_id, semester)
);
CREATE TABLE IF NOT EXISTS time_slots(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    number INTEGER NOT NULL UNIQUE CHECK(number BETWEEN 1 AND 7),
    start_time TEXT NOT NULL,
    end_time TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS schedule(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    group_id INTEGER NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    subject_id INTEGER NOT NULL REFERENCES subjects(id),
    teacher_id INTEGER NOT NULL REFERENCES teachers(id),
    room_id INTEGER NOT NULL REFERENCES rooms(id),
    weekday INTEGER NOT NULL CHECK(weekday BETWEEN 1 AND 6),
    slot_id INTEGER NOT NULL REFERENCES time_slots(id),
    week_parity INTEGER NOT NULL DEFAULT 0 CHECK(week_parity IN (0,1,2)),
    kind TEXT NOT NULL CHECK(kind IN ('лекция','практика','лабораторная'))
);
CREATE TABLE IF NOT EXISTS grades(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    student_id INTEGER NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    subject_id INTEGER NOT NULL REFERENCES subjects(id),
    semester INTEGER NOT NULL CHECK(semester BETWEEN 1 AND 12),
    value INTEGER NULL CHECK(value IS NULL OR value BETWEEN 2 AND 5),
    kind TEXT NOT NULL DEFAULT 'экзамен',
    graded_at TEXT NULL
);
CREATE TABLE IF NOT EXISTS attendance(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    student_id INTEGER NOT NULL REFERENCES students(id) ON DELETE CASCADE,
    schedule_id INTEGER NOT NULL REFERENCES schedule(id) ON DELETE CASCADE,
    date TEXT NOT NULL,
    present INTEGER NOT NULL DEFAULT 1 CHECK(present IN (0,1)),
    UNIQUE(student_id, schedule_id, date)
);
CREATE TABLE IF NOT EXISTS users(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    login TEXT NOT NULL UNIQUE,
    full_name TEXT NOT NULL DEFAULT '',
    role TEXT NOT NULL DEFAULT '',
    last_login TEXT NULL
);
CREATE TABLE IF NOT EXISTS audit_log(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    at TEXT NOT NULL,
    user_login TEXT NOT NULL DEFAULT '',
    table_name TEXT NOT NULL,
    row_id INTEGER NOT NULL,
    op TEXT NOT NULL CHECK(op IN ('insert','update','delete')),
    before_json TEXT NULL,
    after_json TEXT NULL,
    undone INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS saved_views(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    table_name TEXT NOT NULL,
    name TEXT NOT NULL,
    json TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS settings(
    key TEXT PRIMARY KEY,
    value TEXT NULL
);
CREATE INDEX IF NOT EXISTS ix_departments_institute ON departments(institute_id);
CREATE INDEX IF NOT EXISTS ix_departments_head ON departments(head_teacher_id);
CREATE INDEX IF NOT EXISTS ix_programs_department ON programs(department_id);
CREATE INDEX IF NOT EXISTS ix_groups_program ON groups(program_id);
CREATE INDEX IF NOT EXISTS ix_groups_curator ON groups(curator_id);
CREATE INDEX IF NOT EXISTS ix_students_group ON students(group_id);
CREATE INDEX IF NOT EXISTS ix_students_last_name ON students(last_name);
CREATE INDEX IF NOT EXISTS ix_teachers_department ON teachers(department_id);
CREATE INDEX IF NOT EXISTS ix_rooms_building ON rooms(building_id);
CREATE INDEX IF NOT EXISTS ix_subjects_department ON subjects(department_id);
CREATE INDEX IF NOT EXISTS ix_curriculum_program ON curriculum(program_id);
CREATE INDEX IF NOT EXISTS ix_curriculum_subject ON curriculum(subject_id);
CREATE INDEX IF NOT EXISTS ix_schedule_group ON schedule(group_id);
CREATE INDEX IF NOT EXISTS ix_schedule_subject ON schedule(subject_id);
CREATE INDEX IF NOT EXISTS ix_schedule_teacher ON schedule(teacher_id);
CREATE INDEX IF NOT EXISTS ix_schedule_room ON schedule(room_id);
CREATE INDEX IF NOT EXISTS ix_schedule_slot ON schedule(slot_id);
CREATE INDEX IF NOT EXISTS ix_schedule_wd_slot_room ON schedule(weekday, slot_id, room_id);
CREATE INDEX IF NOT EXISTS ix_schedule_wd_slot_teacher ON schedule(weekday, slot_id, teacher_id);
CREATE INDEX IF NOT EXISTS ix_schedule_wd_slot_group ON schedule(weekday, slot_id, group_id);
CREATE INDEX IF NOT EXISTS ix_grades_student ON grades(student_id);
CREATE INDEX IF NOT EXISTS ix_grades_subject ON grades(subject_id);
CREATE INDEX IF NOT EXISTS ix_attendance_student ON attendance(student_id);
CREATE INDEX IF NOT EXISTS ix_attendance_schedule ON attendance(schedule_id);
CREATE INDEX IF NOT EXISTS ix_audit_table_row ON audit_log(table_name, row_id);
CREATE INDEX IF NOT EXISTS ix_saved_views_table ON saved_views(table_name);
";

    public static void EnsureCreated()
    {
        using var conn = Db.Open();
        Ensure(conn);
    }

    public static void Ensure(SqliteConnection conn)
    {
        int current;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA user_version;";
            current = Convert.ToInt32(cmd.ExecuteScalar());
        }
        if (current >= Version)
        {
            return;
        }
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = Sql;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "PRAGMA user_version=" + Version + ";";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
