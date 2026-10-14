namespace SkfuAdmin.Data;

public enum ColumnKind
{
    Text,
    Int,
    Real,
    Date,
    Bool,
    Choice,
    Foreign
}

public sealed class ColumnMeta
{
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public ColumnKind Kind { get; init; }
    public bool Required { get; init; }
    public string[] Choices { get; init; } = Array.Empty<string>();
    public string? ForeignTable { get; init; }
    public string? ForeignDisplay { get; init; }
    public bool ReadOnly { get; init; }
    public bool ShowInGrid { get; init; } = true;
    public string LabelName => Name + "__label";
}

public sealed class TableMeta
{
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public string DisplayExpr { get; init; } = "id";
    public IReadOnlyList<ColumnMeta> Columns { get; init; } = Array.Empty<ColumnMeta>();
    public string[] SearchColumns { get; init; } = Array.Empty<string>();
    public string DefaultOrder { get; init; } = "id";
    public bool ReadOnly { get; init; }

    public ColumnMeta? Column(string name)
    {
        foreach (var c in Columns)
        {
            if (string.Equals(c.Name, name, StringComparison.Ordinal))
            {
                return c;
            }
        }
        return null;
    }
}

public static class TableCatalog
{
    private static readonly Dictionary<string, string> Displays = new()
    {
        ["institutes"] = "name",
        ["departments"] = "name",
        ["programs"] = "code||' '||name",
        ["groups"] = "name",
        ["students"] = "last_name||' '||first_name||CASE WHEN middle_name<>'' THEN ' '||middle_name ELSE '' END",
        ["teachers"] = "last_name||' '||first_name||CASE WHEN middle_name<>'' THEN ' '||middle_name ELSE '' END",
        ["buildings"] = "name",
        ["rooms"] = "number||' ('||COALESCE((SELECT b.name FROM buildings b WHERE b.id=building_id),'')||')'",
        ["subjects"] = "name",
        ["curriculum"] = "'Семестр '||semester||', '||control",
        ["time_slots"] = "number||' пара ('||start_time||'-'||end_time||')'",
        ["schedule"] = "'День '||weekday||', пара '||COALESCE((SELECT s.number FROM time_slots s WHERE s.id=slot_id),'?')||', '||COALESCE((SELECT g.name FROM groups g WHERE g.id=group_id),'')",
        ["grades"] = "CAST(id AS TEXT)",
        ["attendance"] = "date",
        ["users"] = "login",
        ["audit_log"] = "CAST(id AS TEXT)"
    };

    private static readonly string[] Degrees = { "бакалавриат", "магистратура", "специалитет" };
    private static readonly string[] Forms = { "очная", "заочная" };
    private static readonly string[] Genders = { "М", "Ж" };
    private static readonly string[] Statuses = { "учится", "академ", "отчислен" };
    private static readonly string[] RoomKinds = { "лекционная", "практика", "компьютерный класс", "лаборатория" };
    private static readonly string[] Controls = { "экзамен", "зачёт", "зачёт с оценкой" };
    private static readonly string[] LessonKinds = { "лекция", "практика", "лабораторная" };
    private static readonly string[] Parities = { "0", "1", "2" };
    private static readonly string[] Weekdays = { "1", "2", "3", "4", "5", "6" };
    private static readonly string[] Ops = { "insert", "update", "delete" };

    private static ColumnMeta Id() => new() { Name = "id", Title = "№", Kind = ColumnKind.Int, ReadOnly = true };

    private static ColumnMeta Text(string name, string title, bool required = false, bool grid = true) =>
        new() { Name = name, Title = title, Kind = ColumnKind.Text, Required = required, ShowInGrid = grid };

    private static ColumnMeta Int(string name, string title, bool required = false, bool grid = true) =>
        new() { Name = name, Title = title, Kind = ColumnKind.Int, Required = required, ShowInGrid = grid };

    private static ColumnMeta Date(string name, string title, bool required = false, bool grid = true) =>
        new() { Name = name, Title = title, Kind = ColumnKind.Date, Required = required, ShowInGrid = grid };

    private static ColumnMeta Bool(string name, string title) =>
        new() { Name = name, Title = title, Kind = ColumnKind.Bool, Required = true };

    private static ColumnMeta Choice(string name, string title, string[] choices, bool required = true, bool grid = true) =>
        new() { Name = name, Title = title, Kind = ColumnKind.Choice, Required = required, Choices = choices, ShowInGrid = grid };

    private static ColumnMeta Foreign(string name, string title, string table, bool required = true, bool grid = true) =>
        new()
        {
            Name = name,
            Title = title,
            Kind = ColumnKind.Foreign,
            Required = required,
            ForeignTable = table,
            ForeignDisplay = Displays[table],
            ShowInGrid = grid
        };

    private static TableMeta Make(string name, string title, string[] search, string order, params ColumnMeta[] columns) =>
        new()
        {
            Name = name,
            Title = title,
            DisplayExpr = Displays[name],
            Columns = columns,
            SearchColumns = search,
            DefaultOrder = order
        };

    public static IReadOnlyList<TableMeta> All { get; } = Build();

    public static TableMeta Get(string name)
    {
        foreach (var t in All)
        {
            if (string.Equals(t.Name, name, StringComparison.Ordinal))
            {
                return t;
            }
        }
        throw new ArgumentException("Неизвестная таблица: " + name);
    }

    public static TableMeta? Find(string name)
    {
        foreach (var t in All)
        {
            if (string.Equals(t.Name, name, StringComparison.Ordinal))
            {
                return t;
            }
        }
        return null;
    }

    private static IReadOnlyList<TableMeta> Build()
    {
        var list = new List<TableMeta>
        {
            Make("institutes", "Институты", new[] { "name", "short_name", "dean", "city" }, "name",
                Id(),
                Text("name", "Название", true),
                Text("short_name", "Сокращение"),
                Text("dean", "Директор"),
                Text("city", "Город")),

            Make("departments", "Кафедры", new[] { "name" }, "name",
                Id(),
                Foreign("institute_id", "Институт", "institutes"),
                Text("name", "Название", true),
                Foreign("head_teacher_id", "Заведующий", "teachers", false)),

            Make("programs", "Направления подготовки", new[] { "code", "name" }, "code",
                Id(),
                Foreign("department_id", "Кафедра", "departments"),
                Text("code", "Код", true),
                Text("name", "Название", true),
                Choice("degree", "Уровень", Degrees),
                Int("years", "Срок обучения, лет", true)),

            Make("groups", "Группы", new[] { "name" }, "name",
                Id(),
                Foreign("program_id", "Направление", "programs"),
                Text("name", "Название", true),
                Int("course", "Курс", true),
                Int("year_admitted", "Год набора", true),
                Foreign("curator_id", "Куратор", "teachers", false),
                Choice("form", "Форма обучения", Forms),
                Int("capacity", "Мест", true)),

            Make("students", "Студенты", new[] { "last_name", "first_name", "middle_name", "record_book_no", "email", "phone" }, "last_name, first_name",
                Id(),
                Foreign("group_id", "Группа", "groups", false),
                Text("last_name", "Фамилия", true),
                Text("first_name", "Имя", true),
                Text("middle_name", "Отчество"),
                Date("birth_date", "Дата рождения"),
                Text("record_book_no", "№ зачётной книжки", true),
                Choice("gender", "Пол", Genders),
                Choice("status", "Статус", Statuses),
                Text("phone", "Телефон", false, false),
                Text("email", "Эл. почта", false, false),
                Date("enrolled_at", "Дата зачисления", false, false)),

            Make("teachers", "Преподаватели", new[] { "last_name", "first_name", "middle_name", "position", "email" }, "last_name, first_name",
                Id(),
                Foreign("department_id", "Кафедра", "departments"),
                Text("last_name", "Фамилия", true),
                Text("first_name", "Имя", true),
                Text("middle_name", "Отчество"),
                Text("position", "Должность"),
                Text("degree", "Учёная степень"),
                Text("email", "Эл. почта", false, false),
                Text("phone", "Телефон", false, false),
                Date("hire_date", "Дата приёма", false, false)),

            Make("buildings", "Корпуса", new[] { "name", "address", "city" }, "name",
                Id(),
                Text("name", "Название", true),
                Text("address", "Адрес"),
                Text("city", "Город")),

            Make("rooms", "Аудитории", new[] { "number" }, "building_id, number",
                Id(),
                Foreign("building_id", "Корпус", "buildings"),
                Text("number", "Номер", true),
                Int("floor", "Этаж", true),
                Int("capacity", "Вместимость", true),
                Choice("kind", "Тип", RoomKinds),
                Bool("has_projector", "Проектор")),

            Make("subjects", "Дисциплины", new[] { "name", "short_name" }, "name",
                Id(),
                Foreign("department_id", "Кафедра", "departments"),
                Text("name", "Название", true),
                Text("short_name", "Сокращение"),
                Int("hours_lecture", "Часы лекций", true),
                Int("hours_practice", "Часы практик", true),
                Int("hours_lab", "Часы лабораторных", true)),

            Make("curriculum", "Учебный план", Array.Empty<string>(), "program_id, semester",
                Id(),
                Foreign("program_id", "Направление", "programs"),
                Foreign("subject_id", "Дисциплина", "subjects"),
                Int("semester", "Семестр", true),
                Choice("control", "Контроль", Controls)),

            Make("time_slots", "Время пар", Array.Empty<string>(), "number",
                Id(),
                Int("number", "Номер пары", true),
                Text("start_time", "Начало", true),
                Text("end_time", "Конец", true)),

            Make("schedule", "Расписание", Array.Empty<string>(), "weekday, slot_id",
                Id(),
                Foreign("group_id", "Группа", "groups"),
                Foreign("subject_id", "Дисциплина", "subjects"),
                Foreign("teacher_id", "Преподаватель", "teachers"),
                Foreign("room_id", "Аудитория", "rooms"),
                Choice("weekday", "День недели", Weekdays),
                Foreign("slot_id", "Пара", "time_slots"),
                Choice("week_parity", "Неделя (0 все, 1 нечёт, 2 чёт)", Parities),
                Choice("kind", "Вид занятия", LessonKinds)),

            Make("grades", "Оценки", Array.Empty<string>(), "id",
                Id(),
                Foreign("student_id", "Студент", "students"),
                Foreign("subject_id", "Дисциплина", "subjects"),
                Int("semester", "Семестр", true),
                Int("value", "Оценка"),
                Text("kind", "Вид контроля", true),
                Date("graded_at", "Дата")),

            Make("attendance", "Посещаемость", Array.Empty<string>(), "date",
                Id(),
                Foreign("student_id", "Студент", "students"),
                Foreign("schedule_id", "Занятие", "schedule"),
                Date("date", "Дата", true),
                Bool("present", "Присутствовал")),

            Make("users", "Пользователи", new[] { "login", "full_name", "role" }, "login",
                Id(),
                Text("login", "Логин", true),
                Text("full_name", "ФИО"),
                Text("role", "Роль"),
                Date("last_login", "Последний вход")),

            new TableMeta
            {
                Name = "audit_log",
                Title = "Журнал изменений",
                DisplayExpr = Displays["audit_log"],
                SearchColumns = new[] { "user_login", "table_name", "op" },
                DefaultOrder = "id",
                ReadOnly = true,
                Columns = new[]
                {
                    Id(),
                    new ColumnMeta { Name = "at", Title = "Время", Kind = ColumnKind.Text, ReadOnly = true },
                    new ColumnMeta { Name = "user_login", Title = "Пользователь", Kind = ColumnKind.Text, ReadOnly = true },
                    new ColumnMeta { Name = "table_name", Title = "Таблица", Kind = ColumnKind.Text, ReadOnly = true },
                    new ColumnMeta { Name = "row_id", Title = "Запись", Kind = ColumnKind.Int, ReadOnly = true },
                    new ColumnMeta { Name = "op", Title = "Операция", Kind = ColumnKind.Choice, Choices = Ops, ReadOnly = true },
                    new ColumnMeta { Name = "before_json", Title = "До", Kind = ColumnKind.Text, ReadOnly = true, ShowInGrid = false },
                    new ColumnMeta { Name = "after_json", Title = "После", Kind = ColumnKind.Text, ReadOnly = true, ShowInGrid = false },
                    new ColumnMeta { Name = "undone", Title = "Отменено", Kind = ColumnKind.Bool, ReadOnly = true }
                }
            }
        };
        return list;
    }
}

