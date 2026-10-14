using System.Data;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SkfuAdmin.Data;
using SkfuAdmin.Services;

namespace SkfuAdmin.Views;

public partial class ScheduleView : UserControl
{
    private readonly ScheduleService _service = new();
    private readonly GenericRepository _repo = new();
    private List<ScheduleEntry> _entries = new();
    private long _selectedId;

    public ScheduleView()
    {
        InitializeComponent();
        Init();
        Loaded += (_, _) => Dispatcher.InvokeAsync(RefreshSchedule);
    }

    public void OpenEntry(long id)
    {
        var entry = _service.GetEntry(id);
        if (entry == null) return;
        ModeBox.SelectedIndex = 0;
        RefreshEntities();
        EntityBox.SelectedValue = entry.GroupId;
        ParityBox.SelectedValue = (long)entry.Parity;
        _selectedId = id;
        Dispatcher.InvokeAsync(RefreshSchedule);
    }

    private void Init()
    {
        ModeBox.ItemsSource = new[] { "Группы", "Преподаватели", "Аудитории" };
        ModeBox.SelectedIndex = 0;
        ParityBox.ItemsSource = Parities();
        ParityBox.DisplayMemberPath = "Label";
        ParityBox.SelectedValuePath = "Id";
        ParityBox.SelectedValue = 0L;
        FillCommon(FreeDay, FreeSlot, FreeBuilding, FreeKind, FreeParity);
        FillCommon(null, null, LoadBuilding, null, LoadParity);
        RefreshEntities();
    }

    private void FillCommon(ComboBox? day, ComboBox? slot, ComboBox? building, ComboBox? kind, ComboBox? parity)
    {
        if (day != null) { day.ItemsSource = Days(); day.DisplayMemberPath = "Label"; day.SelectedValuePath = "Id"; day.SelectedValue = 1L; }
        if (slot != null) { slot.ItemsSource = _service.TimeSlots().Select(x => new Item(x.Id, x.Label)).ToList(); slot.DisplayMemberPath = "Label"; slot.SelectedValuePath = "Id"; slot.SelectedIndex = 0; }
        if (building != null) { building.ItemsSource = new[] { new Item(0, "Все корпуса") }.Concat(_repo.Lookup("buildings").Select(x => new Item(x.Id, x.Label))).ToList(); building.DisplayMemberPath = "Label"; building.SelectedValuePath = "Id"; building.SelectedIndex = 0; }
        if (kind != null) { kind.ItemsSource = new[] { "Все типы", "лекционная", "семинарская", "компьютерный класс", "лаборатория" }; kind.SelectedIndex = 0; }
        if (parity != null) { parity.ItemsSource = Parities(); parity.DisplayMemberPath = "Label"; parity.SelectedValuePath = "Id"; parity.SelectedValue = 0L; }
    }

    private static IEnumerable<Item> Days() => new[] { new Item(1, "Понедельник"), new Item(2, "Вторник"), new Item(3, "Среда"), new Item(4, "Четверг"), new Item(5, "Пятница"), new Item(6, "Суббота") };
    private static IEnumerable<Item> Parities() => new[] { new Item(0, "Каждая неделя"), new Item(1, "Нечётная"), new Item(2, "Чётная") };

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (sender == ModeBox) RefreshEntities();
        RefreshSchedule();
    }

    private void RefreshEntities()
    {
        string table = ModeBox.SelectedIndex == 1 ? "teachers" : ModeBox.SelectedIndex == 2 ? "rooms" : "groups";
        EntityBox.ItemsSource = _repo.Lookup(table).Select(x => new Item(x.Id, x.Label)).ToList();
        EntityBox.DisplayMemberPath = "Label";
        EntityBox.SelectedValuePath = "Id";
        if (EntityBox.Items.Count > 0) EntityBox.SelectedIndex = 0;
    }

    private void RefreshClick(object sender, RoutedEventArgs e) => RefreshSchedule();

    private void RefreshSchedule()
    {
        if (!TryId(EntityBox, out long id)) { WeekGrid.Children.Clear(); DeleteButton.IsEnabled = false; return; }
        int parity = Value(ParityBox);
        _entries = (ModeBox.SelectedIndex == 1 ? _service.ForTeacher(id, parity) : ModeBox.SelectedIndex == 2 ? _service.ForRoom(id, parity) : _service.ForGroup(id, parity)).ToList();
        if (_selectedId != 0 && !_entries.Any(x => x.Id == _selectedId)) _selectedId = 0;
        DeleteButton.IsEnabled = _selectedId != 0;
        WeekGrid.Children.Clear(); WeekGrid.RowDefinitions.Clear(); WeekGrid.ColumnDefinitions.Clear();
        WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        for (int d = 0; d < 6; d++) WeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });
        WeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int s = 0; s < 7; s++) WeekGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(95) });
        AddCell(new TextBlock { Text = "Пара", FontWeight = FontWeights.Bold }, 0, 0);
        string[] names = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };
        for (int d = 0; d < 6; d++) AddCell(new TextBlock { Text = names[d], FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center }, 0, d + 1);
        var slots = _service.TimeSlots();
        for (int s = 0; s < 7; s++)
        {
            string text = s < slots.Count ? slots[s].Label : (s + 1) + " пара";
            AddCell(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, s + 1, 0);
            for (int d = 0; d < 6; d++)
            {
                var panel = new StackPanel(); long slot = s < slots.Count ? slots[s].Id : -1;
                foreach (var entry in _entries.Where(x => x.Weekday == d + 1 && x.SlotId == slot)) AddCard(panel, entry);
                AddCell(panel, s + 1, d + 1);
            }
        }
    }

    private void AddCard(Panel panel, ScheduleEntry entry)
    {
        bool selected = entry.Id == _selectedId;
        var border = new Border { BorderThickness = new Thickness(selected ? 3 : 1), BorderBrush = selected ? Brushes.OrangeRed : Brushes.SteelBlue, Background = selected ? Brushes.LightGoldenrodYellow : Brushes.AliceBlue, Padding = new Thickness(5), Margin = new Thickness(2), Tag = entry };
        border.Child = new TextBlock { Text = entry.SubjectName + "\n" + (ModeBox.SelectedIndex == 0 ? entry.TeacherName + ", " + entry.RoomLabel : entry.GroupName) + "\n" + entry.Kind, TextWrapping = TextWrapping.Wrap };
        border.MouseLeftButtonDown += CardClick;
        panel.Children.Add(border);
    }

    private void AddCell(UIElement element, int row, int col)
    {
        var border = new Border { BorderThickness = new Thickness(0.5), BorderBrush = Brushes.LightGray, Padding = new Thickness(3), Child = element };
        Grid.SetRow(border, row); Grid.SetColumn(border, col); WeekGrid.Children.Add(border);
    }

    private void CardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: ScheduleEntry entry }) return;
        _selectedId = entry.Id;
        if (e.ClickCount == 2) Edit(entry); else RefreshSchedule();
    }

    private void AddClick(object sender, RoutedEventArgs e) => Edit(new ScheduleEntry(0, 0, "", 0, "", 0, "", 0, "", 1, 0, 1, "", "", 0, "лекция"));
    private void Edit(ScheduleEntry entry) { var dialog = new EditScheduleDialog(new ScheduleDraft(entry.Id, entry.GroupId, entry.SubjectId, entry.TeacherId, entry.RoomId, entry.Weekday, entry.SlotId, entry.Parity, entry.Kind)) { Owner = Window.GetWindow(this) }; if (dialog.ShowDialog() == true) { _selectedId = dialog.Result?.Id ?? 0; RefreshSchedule(); } }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (_selectedId == 0 || !ConfirmDialog.Ask(Window.GetWindow(this)!, "Удалить выбранное занятие?")) return;
        try { _service.DeleteEntry(_selectedId); _selectedId = 0; RefreshSchedule(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Удаление занятия", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void FreeClick(object sender, RoutedEventArgs e) { if (!int.TryParse(FreeCapacity.Text, out int cap)) cap = 0; long buildingId = Value(FreeBuilding); FreeGrid.ItemsSource = _service.FindFreeRooms(Value(FreeDay), Id(FreeSlot), cap, buildingId == 0 ? null : buildingId, FreeKind.SelectedIndex == 0 ? null : FreeKind.SelectedItem as string, Value(FreeParity)).Select(x => new { Room = x.Label, Capacity = x.Capacity, Kind = x.Kind, Projector = x.HasProjector }).ToList(); }
    private void LoadClick(object sender, RoutedEventArgs e) { long buildingId = Value(LoadBuilding); if (buildingId == 0) { LoadGrid.ItemsSource = null; return; } var table = new DataTable(); table.Columns.Add("Аудитория"); foreach (var day in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" }) for (int slot = 1; slot <= 7; slot++) table.Columns.Add(day + " " + slot); foreach (var room in _service.RoomLoadForBuilding(buildingId, Value(LoadParity))) { var row = table.NewRow(); row[0] = room.Room.Label; int column = 1; for (int day = 0; day < 6; day++) for (int slot = 0; slot < 7; slot++) row[column++] = slot < room.Busy.GetLength(1) && room.Busy[day, slot] ? "Занято" : "Свободно"; table.Rows.Add(row); } LoadGrid.ItemsSource = table.DefaultView; }
    private void ExportClick(object sender, RoutedEventArgs e) { var dialog = new SaveFileDialog { Filter = "CSV-файл|*.csv", FileName = "расписание.csv" }; if (dialog.ShowDialog() != true) return; var text = new StringBuilder("День;Пара;Дисциплина;Группа;Преподаватель;Аудитория;Тип\r\n"); foreach (var entry in _entries) text.AppendLine(string.Join(";", entry.Weekday, entry.SlotNumber, Csv(entry.SubjectName), Csv(entry.GroupName), Csv(entry.TeacherName), Csv(entry.RoomLabel), Csv(entry.Kind))); File.WriteAllText(dialog.FileName, text.ToString(), new UTF8Encoding(true)); }
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static bool TryId(ComboBox box, out long id) { id = 0; return box.SelectedValue != null && long.TryParse(box.SelectedValue.ToString(), out id); }
    private static long Id(ComboBox box) { TryId(box, out long id); return id; }
    private static int Value(ComboBox box) => (int)Id(box);
    private sealed record Item(long Id, string Label);
}
