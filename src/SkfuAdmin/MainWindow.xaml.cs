using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SkfuAdmin.Data;
using SkfuAdmin.Services;
using SkfuAdmin.ViewModels;

namespace SkfuAdmin;

public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); DataContext = new MainViewModel(); Vm.ScheduleEntryRequested = id => Dispatcher.InvokeAsync(() => ScheduleControl.OpenEntry(id)); AuditControl.OpenRecord = OpenAuditRecord; ApplyTheme(); DataContextChanged += (_, _) => ConfigureColumns(); ConfigureColumns(); }
    public void ApplyTheme()
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries.Clear();
        dictionaries.Add(new ResourceDictionary { Source = new Uri(SettingsService.Theme == "light" ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative) });
        BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(180)));
    }
    private MainViewModel Vm => (MainViewModel)DataContext;
    private void ConfigureColumns()
    {
        if (DataContext is not MainViewModel vm) return;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Table)) Dispatcher.InvokeAsync(ConfigureColumns);
            if (e.PropertyName == nameof(MainViewModel.RecordToFocusId) && vm.RecordToFocusId.HasValue)
                Dispatcher.InvokeAsync(() => FocusRecord(vm.RecordToFocusId.Value));
        };
        TableGrid.Columns.Clear();
        foreach (var column in vm.Table.Columns.Where(x => x.Name == "id" || x.ShowInGrid))
        {
            string path = column.Kind == ColumnKind.Foreign ? column.LabelName : column.Name;
            TableGrid.Columns.Add(new DataGridTextColumn { Header = column.Name == "id" ? "ID" : column.Title, Binding = new System.Windows.Data.Binding(path), SortMemberPath = column.Name });
        }
    }
    private void FocusRecord(long id)
    {
        var row = Vm.Rows.Cast<System.Data.DataRowView>().FirstOrDefault(x => Convert.ToInt64(x["id"]) == id);
        if (row == null) return;
        TableGrid.SelectedItem = row;
        TableGrid.ScrollIntoView(row);
    }
    private void OpenAuditRecord(string table, long id) => Vm.OpenRecord(table, id);
    private void TableGridDoubleClick(object sender, MouseButtonEventArgs e) => Vm.EditCommand.Execute(null);
    private void TableGridSorting(object sender, DataGridSortingEventArgs e) { e.Handled = true; if (e.Column.SortMemberPath is string name) Vm.Sort(name); }
    private void TableGridSelectionChanged(object sender, SelectionChangedEventArgs e) => Vm.SetSelectedRows(TableGrid.SelectedItems);
    private void IntegritySelectionChanged(object sender, SelectionChangedEventArgs e) => Vm.ShowIssues((sender as DataGrid)?.SelectedItem as IntegrityResult);
    private void OpenIssueClick(object sender, RoutedEventArgs e) => Vm.OpenIssue(((sender as Button)?.DataContext as IntegrityIssue));
    private void SearchResultDoubleClick(object sender, MouseButtonEventArgs e) => Vm.SelectSearchCommand.Execute((sender as ListBox)?.SelectedItem);
    private void RestoreClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "База данных|*.db;*.sqlite|Все файлы|*.*" };
        if (dialog.ShowDialog() == true && Views.ConfirmDialog.Ask(this, "Текущая база будет заменена. Продолжить?"))
        {
            try { BackupService.Restore(dialog.FileName); Vm.NavigateCommand.Execute("Система"); Vm.Toast = "База восстановлена из резервной копии."; }
            catch (Exception ex) { Vm.Toast = ex.Message; }
        }
    }
}

