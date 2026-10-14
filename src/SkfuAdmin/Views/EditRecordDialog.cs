using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkfuAdmin.Data;

namespace SkfuAdmin.Views;

public sealed class EditRecordDialog : Window
{
    private readonly TableMeta _table;
    private readonly Dictionary<string, Control> _inputs = new();
    public Dictionary<string, object?> Values { get; } = new();
    public EditRecordDialog(TableMeta table, long? id, GenericRepository repo)
    {
        _table = table; Title = (id.HasValue ? "Изменение: " : "Добавление: ") + table.Title; Width = 520; Height = 680; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(18) }; var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Сохранить" }; save.Click += (_, _) => Save(); buttons.Children.Add(save); var cancel = new Button { Content = "Отмена" }; cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel); DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var panel = new StackPanel(); var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll); Content = root;
        var current = id.HasValue ? repo.GetById(table, id.Value) : null;
        foreach (var col in table.Columns.Where(c => c.Name != "id" && !c.ReadOnly))
        {
            panel.Children.Add(new TextBlock { Text = col.Title + (col.Required ? " *" : ""), Margin = new Thickness(0, 7, 0, 2) });
            Control control;
            string value = current != null && current.TryGetValue(col.Name, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
            if (col.Kind == ColumnKind.Choice) control = new ComboBox { ItemsSource = col.Choices, SelectedItem = value, MinWidth = 320 };
            else if (col.Kind == ColumnKind.Foreign && col.ForeignTable != null) { var choices = repo.Lookup(col.ForeignTable).Select(x => new Choice(x.Id, x.Label)).ToList(); var box = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Label", SelectedValuePath = "Id", SelectedValue = long.TryParse(value, out var fk) ? fk : null, MinWidth = 320 }; control = box; }
            else if (col.Kind == ColumnKind.Bool) control = new CheckBox { IsChecked = value == "1" || value == "True", Content = "Да", Margin = new Thickness(0, 4, 0, 4) };
            else control = new TextBox { Text = value, MinWidth = 320 };
            _inputs[col.Name] = control; panel.Children.Add(control);
        }
    }
    private void Save()
    {
        foreach (var col in _table.Columns.Where(c => c.Name != "id" && !c.ReadOnly))
        {
            var control = _inputs[col.Name]; object? value = control switch { TextBox t => t.Text.Trim(), ComboBox b => col.Kind == ColumnKind.Foreign ? b.SelectedValue : b.SelectedItem, CheckBox c => c.IsChecked == true, _ => null };
            if (col.Required && (value == null || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture)))) { MessageBox.Show(this, "Заполните поле «" + col.Title + "».", "Проверка данных"); return; }
            Values[col.Name] = value;
        }
        DialogResult = true;
    }
    private sealed record Choice(long Id, string Label);
}
