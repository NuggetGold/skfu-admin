using System.Windows;
using System.Windows.Controls;
using SkfuAdmin.Services;

namespace SkfuAdmin.Views;

public sealed class SavedViewDialog : Window
{
    private readonly SavedViewService _service;
    private readonly string _table;
    private readonly ViewState? _state;
    private readonly ListBox _list = new();
    public string? ViewName { get; private set; }
    public ViewState? AppliedState { get; private set; }
    public SavedViewDialog(SavedViewService service, string table, ViewState? state)
    {
        _service = service; _table = table; _state = state; Title = state == null ? "Представления" : "Сохранить представление"; Width = 460; Height = state == null ? 360 : 180; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(16) };
        if (state != null) { var name = new TextBox(); var save = new Button { Content = "Сохранить", Margin = new Thickness(0, 8, 0, 0) }; save.Click += (_, _) => { ViewName = name.Text.Trim(); if (ViewName.Length == 0) return; _service.Save(_table, ViewName, _state!); DialogResult = true; }; panel.Children.Add(new TextBlock { Text = "Название" }); panel.Children.Add(name); panel.Children.Add(save); }
        else { _list.ItemsSource = _service.List(_table); _list.DisplayMemberPath = "Name"; panel.Children.Add(_list); var buttons = new StackPanel { Orientation = Orientation.Horizontal }; var apply = new Button { Content = "Применить" }; var delete = new Button { Content = "Удалить" }; var close = new Button { Content = "Закрыть" }; apply.Click += Apply; delete.Click += Delete; close.Click += (_, _) => Close(); buttons.Children.Add(apply); buttons.Children.Add(delete); buttons.Children.Add(close); panel.Children.Add(buttons); }
        Content = panel;
    }
    private void Apply(object sender, RoutedEventArgs e) { if (_list.SelectedItem is not SavedView view) return; var result = _service.Load(view.Id); if (result.State == null) { MessageBox.Show(result.Error ?? "Представление не удалось загрузить.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error); return; } AppliedState = result.State; DialogResult = true; }
    private void Delete(object sender, RoutedEventArgs e) { if (_list.SelectedItem is SavedView view) { _service.Delete(view.Id); _list.ItemsSource = _service.List(_table); } }
}
