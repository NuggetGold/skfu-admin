using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SkfuAdmin.Data;

namespace SkfuAdmin.Views;

public sealed class MassUpdateDialog : Window
{
    private readonly GenericRepository _repo = new();
    private readonly ComboBox _columns;
    private readonly ContentControl _valueHost;
    private ColumnMeta? _selectedColumn;
    public string? Column { get; private set; }
    public object? Value { get; private set; }

    public MassUpdateDialog(TableMeta table)
    {
        Title = "Массовое изменение"; Width = 420; Height = 230; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        _columns = new ComboBox { ItemsSource = table.Columns.Where(x => !x.ReadOnly && x.Name != "id").ToList(), DisplayMemberPath = "Title", Margin = new Thickness(0, 0, 0, 10) };
        _columns.SelectionChanged += (_, _) => ShowValueEditor(_columns.SelectedItem as ColumnMeta);
        _valueHost = new ContentControl { Margin = new Thickness(0, 0, 0, 12) };
        var save = new Button { Content = "Применить", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 100 };
        save.Click += (_, _) => Save();
        panel.Children.Add(_columns); panel.Children.Add(_valueHost); panel.Children.Add(save); Content = panel;
    }

    private void ShowValueEditor(ColumnMeta? column)
    {
        _selectedColumn = column;
        if (column == null) { _valueHost.Content = null; return; }
        if (column.Kind == ColumnKind.Foreign && column.ForeignTable != null)
        {
            _valueHost.Content = new ComboBox { ItemsSource = _repo.Lookup(column.ForeignTable).Select(x => new LookupItem(x.Id, x.Label)).ToList(), DisplayMemberPath = "Label", SelectedValuePath = "Id" };
            return;
        }
        if (column.Kind == ColumnKind.Choice)
        {
            _valueHost.Content = new ComboBox { ItemsSource = column.Choices, SelectedIndex = column.Choices.Length > 0 ? 0 : -1 };
            return;
        }
        if (column.Kind == ColumnKind.Bool) { _valueHost.Content = new CheckBox { Content = "Да" }; return; }
        if (column.Kind == ColumnKind.Date) { _valueHost.Content = new DatePicker { SelectedDate = DateTime.Today }; return; }
        _valueHost.Content = new TextBox();
    }

    private void Save()
    {
        if (_selectedColumn == null || _valueHost.Content == null) return;
        object? value;
        try
        {
            value = _valueHost.Content switch
            {
                ComboBox box when _selectedColumn.Kind == ColumnKind.Foreign => box.SelectedValue ?? throw new InvalidOperationException("Выберите значение."),
                ComboBox box when _selectedColumn.Kind == ColumnKind.Choice => box.SelectedItem ?? throw new InvalidOperationException("Выберите значение."),
                CheckBox box => box.IsChecked == true,
                DatePicker picker => picker.SelectedDate ?? throw new InvalidOperationException("Выберите дату."),
                TextBox box => ParseText(box.Text, _selectedColumn),
                _ => throw new InvalidOperationException("Не удалось получить значение.")
            };
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Массовое изменение", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        Column = _selectedColumn.Name; Value = value; DialogResult = true;
    }

    private static object ParseText(string text, ColumnMeta column)
    {
        if (column.Kind == ColumnKind.Int)
        {
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer)) throw new InvalidOperationException("Введите целое число.");
            return integer;
        }
        if (column.Kind == ColumnKind.Real)
        {
            if (!double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) throw new InvalidOperationException("Введите число.");
            return number;
        }
        return text;
    }

    private sealed record LookupItem(long Id, string Label);
}
