using System.Data;
using System.Windows;
using System.Windows.Controls;
using SkfuAdmin.Data;
using SkfuAdmin.Services;

namespace SkfuAdmin.Views;
public partial class AuditView : UserControl
{
    private readonly AuditService _service = new();
    private int _page;
    private int _total;
    public Action<string, long>? OpenRecord;
    public AuditView()
    {
        InitializeComponent();
        TableBox.ItemsSource = new[] { "Все таблицы" }.Concat(TableCatalog.All.Where(x => x.Name != "audit_log").Select(x => x.Name)).ToList();
        TableBox.SelectedIndex = 0;
        OpBox.ItemsSource = new[] { "Все операции", "insert", "update", "delete" };
        OpBox.SelectedIndex = 0;
        Refresh();
    }
    private void Changed(object sender, EventArgs e) { if (IsLoaded) { _page = 0; Refresh(); } }
    private void RefreshClick(object sender, RoutedEventArgs e) => Refresh();
    private void Refresh()
    {
        string? table = TableBox.SelectedIndex == 0 ? null : TableBox.SelectedItem as string;
        string? operation = OpBox.SelectedIndex == 0 ? null : OpBox.SelectedItem as string;
        var rows = _service.QueryLog(table, operation, FromBox.SelectedDate, ToBox.SelectedDate, 50, _page * 50);
        Grid.ItemsSource = rows.DefaultView;
        _total = _service.CountLog(table, operation, FromBox.SelectedDate, ToBox.SelectedDate);
        PageText.Text = "Страница " + (_page + 1) + " · записей: " + _total;
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e) { if (Grid.SelectedItem is DataRowView row) Details.Text = "До:\r\n" + (row["before_json"] == DBNull.Value ? "—" : row["before_json"]) + "\r\n\r\nПосле:\r\n" + (row["after_json"] == DBNull.Value ? "—" : row["after_json"]); }
    private void UndoClick(object sender, RoutedEventArgs e) { if (Grid.SelectedItem is not DataRowView row) { MessageBox.Show("Выберите запись журнала."); return; } if (Convert.ToInt64(row["undone"]) != 0) { MessageBox.Show("Эта операция уже отменена."); return; } if (!ConfirmDialog.Ask(Window.GetWindow(this)!, "Отменить операцию?")) return; var result = _service.Undo(Convert.ToInt64(row["id"])); MessageBox.Show(result.Message, result.Success ? "Готово" : "Ошибка"); Refresh(); }
    private void OpenClick(object sender, RoutedEventArgs e) { if (Grid.SelectedItem is DataRowView row) OpenRecord?.Invoke(Convert.ToString(row["table_name"]) ?? "", Convert.ToInt64(row["row_id"])); }
    private void PreviousClick(object sender, RoutedEventArgs e) { if (_page > 0) { _page--; Refresh(); } }
    private void NextClick(object sender, RoutedEventArgs e) { if ((_page + 1) * 50 < _total) { _page++; Refresh(); } }
}
