using System.Windows;
using System.Windows.Controls;
using SkfuAdmin.Data;
using SkfuAdmin.Services;

namespace SkfuAdmin.Views;

public sealed class EditScheduleDialog : Window
{
    private readonly ScheduleDraft _initial;
    private readonly ComboBox _group = new(), _subject = new(), _teacher = new(), _room = new(), _weekday = new(), _slot = new(), _parity = new(), _kind = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    public ScheduleSaveResult? Result { get; private set; }
    public EditScheduleDialog(ScheduleDraft draft)
    {
        _initial = draft; Title = draft.Id == 0 ? "Добавление занятия" : "Изменение занятия"; Width = 510; Height = 580; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(18) };
        Add(panel, "Группа", _group, "groups", draft.GroupId); Add(panel, "Дисциплина", _subject, "subjects", draft.SubjectId); Add(panel, "Преподаватель", _teacher, "teachers", draft.TeacherId); Add(panel, "Аудитория", _room, "rooms", draft.RoomId);
        _weekday.ItemsSource = new[] { new Item(1, "Понедельник"), new Item(2, "Вторник"), new Item(3, "Среда"), new Item(4, "Четверг"), new Item(5, "Пятница"), new Item(6, "Суббота") }; _weekday.DisplayMemberPath = "Label"; _weekday.SelectedValuePath = "Id"; _weekday.SelectedValue = draft.Weekday; Add(panel, "День недели", _weekday);
        var service = new ScheduleService(); _slot.ItemsSource = service.TimeSlots().Select(x => new Item(x.Id, x.Label)).ToList(); _slot.DisplayMemberPath = "Label"; _slot.SelectedValuePath = "Id"; _slot.SelectedValue = draft.SlotId; Add(panel, "Пара", _slot);
        _parity.ItemsSource = new[] { new Item(0, "Каждая"), new Item(1, "Нечётная"), new Item(2, "Чётная") }; _parity.DisplayMemberPath = "Label"; _parity.SelectedValuePath = "Id"; _parity.SelectedValue = draft.Parity; Add(panel, "Неделя", _parity);
        _kind.ItemsSource = new[] { "лекция", "практика", "лабораторная" }; _kind.SelectedItem = draft.Kind; Add(panel, "Тип занятия", _kind); panel.Children.Add(_message);
        var save = new Button { Content = "Сохранить", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 110 }; save.Click += Save; panel.Children.Add(save); Content = new ScrollViewer { Content = panel };
    }
    private void Add(Panel panel, string label, ComboBox box, string? table = null, long selected = 0) { panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 5, 0, 2) }); if (table != null) { box.ItemsSource = new GenericRepository().Lookup(table).Select(x => new Item(x.Id, x.Label)).ToList(); box.DisplayMemberPath = "Label"; box.SelectedValuePath = "Id"; box.SelectedValue = selected; } panel.Children.Add(box); }
    private void Save(object sender, RoutedEventArgs e) { if (!TryId(_group, out long group) || !TryId(_subject, out long subject) || !TryId(_teacher, out long teacher) || !TryId(_room, out long room) || !TryId(_weekday, out long day) || !TryId(_slot, out long slot) || !TryId(_parity, out long parity) || _kind.SelectedItem is not string kind) { _message.Text = "Заполните все поля занятия."; return; } Result = new ScheduleService().SaveEntry(new ScheduleDraft(_initial.Id, group, subject, teacher, room, (int)day, slot, (int)parity, kind)); if (Result.Saved) { DialogResult = true; return; } _message.Text = Result.Conflicts.Count > 0 ? string.Join(Environment.NewLine, Result.Conflicts.Select(x => x.Message)) : Result.Message; }
    private static bool TryId(ComboBox box, out long value) { value = 0; return box.SelectedValue != null && long.TryParse(box.SelectedValue.ToString(), out value); }
    private sealed record Item(long Id, string Label);
}
