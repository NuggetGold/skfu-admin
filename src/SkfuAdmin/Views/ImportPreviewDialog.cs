using System.Windows;
using System.Windows.Controls;
using SkfuAdmin.Services;

namespace SkfuAdmin.Views;

public sealed class ImportPreviewDialog : Window
{
    public ImportPreviewDialog(ImportPreview preview)
    {
        Title = "Предварительный просмотр импорта"; Width = 760; Height = 500; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel { Margin = new Thickness(16) }; var apply = new Button { Content = "Импортировать допустимые строки", IsEnabled = preview.FileErrors.Count == 0 && preview.ValidCount > 0 }; apply.Click += (_, _) => DialogResult = true; DockPanel.SetDock(apply, Dock.Bottom); panel.Children.Add(apply);
        panel.Children.Add(new DataGrid { ItemsSource = preview.Rows.Select(x => new { x.LineNo, Значения = string.Join("; ", x.Values.Select(v => v.Key + "=" + v.Value)), Ошибки = string.Join(" ", x.Errors) }), AutoGenerateColumns = true, IsReadOnly = true }); Content = panel;
    }
}
