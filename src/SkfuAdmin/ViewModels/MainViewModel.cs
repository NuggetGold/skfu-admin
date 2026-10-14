using System.Collections;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SkfuAdmin.Data;
using SkfuAdmin.Infrastructure;
using SkfuAdmin.Services;
using SkfuAdmin.Views;

namespace SkfuAdmin.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly GenericRepository _repo = new();
    private readonly AuditService _audit = new();
    private readonly SearchService _search = new();
    private readonly SavedViewService _savedViews = new();
    private readonly System.Windows.Threading.DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _section = "Обзор", _subtitle = "Оперативная сводка по цифровой инфраструктуре", _searchText = "", _tableSearch = "", _toast = "";
    private bool _searchOpen, _descending;
    private int _page;
    private string? _order;
    private TableMeta _table = TableCatalog.Get("institutes");
    private DataTable _rows = new();
    private object? _selectedRow;
    private DashboardStats? _stats;
    private IntegrityIssue? _selectedIssue;
    private IntegrityResult? _selectedIntegrity;
    private long? _recordToFocusId;
    private readonly Dictionary<string, string> _filters = new();

    public MainViewModel()
    {
        NavigateCommand = new RelayCommand(x => Navigate(Convert.ToString(x) ?? "Обзор"));
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        RefreshCommand = new RelayCommand(_ => Refresh());
        SearchCommand = new RelayCommand(_ => SearchOpen = true);
        CloseSearchCommand = new RelayCommand(_ => SearchOpen = false);
        SelectSearchCommand = new RelayCommand(x => SelectSearch(x as SearchHit));
        AddCommand = new RelayCommand(_ => Edit()); EditCommand = new RelayCommand(_ => Edit(SelectedRow)); DeleteCommand = new RelayCommand(_ => DeleteSelected());
        ExportCommand = new RelayCommand(_ => Export()); ImportCommand = new RelayCommand(_ => Import());
        PreviousPageCommand = new RelayCommand(_ => { if (Page > 0) { Page--; LoadTable(); } });
        NextPageCommand = new RelayCommand(_ => { if ((Page + 1) * 50 < Total) { Page++; LoadTable(); } });
        CheckIntegrityCommand = new RelayCommand(_ => LoadIntegrity()); CreateBackupCommand = new RelayCommand(_ => CreateBackup()); OptimizeCommand = new RelayCommand(_ => Optimize());
        ResetFiltersCommand = new RelayCommand(_ => ResetFilters()); SaveViewCommand = new RelayCommand(_ => SaveView()); OpenViewsCommand = new RelayCommand(_ => OpenViews());
        MassUpdateCommand = new RelayCommand(_ => MassUpdate()); DeleteManyCommand = new RelayCommand(_ => DeleteMany());
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };
        Navigate("Обзор");
    }

    public ICommand NavigateCommand { get; } public ICommand ToggleThemeCommand { get; } public ICommand RefreshCommand { get; } public ICommand SearchCommand { get; } public ICommand CloseSearchCommand { get; } public ICommand SelectSearchCommand { get; }
    public ICommand AddCommand { get; } public ICommand EditCommand { get; } public ICommand DeleteCommand { get; } public ICommand ExportCommand { get; } public ICommand ImportCommand { get; } public ICommand PreviousPageCommand { get; } public ICommand NextPageCommand { get; } public ICommand CheckIntegrityCommand { get; } public ICommand CreateBackupCommand { get; } public ICommand OptimizeCommand { get; } public ICommand ResetFiltersCommand { get; } public ICommand SaveViewCommand { get; } public ICommand OpenViewsCommand { get; } public ICommand MassUpdateCommand { get; } public ICommand DeleteManyCommand { get; }
    public string Section { get => _section; set => Set(ref _section, value); } public string Subtitle { get => _subtitle; set => Set(ref _subtitle, value); } public bool SearchOpen { get => _searchOpen; set => Set(ref _searchOpen, value); }
    public string SearchText { get => _searchText; set { if (Set(ref _searchText, value)) { _searchTimer.Stop(); if (value.Trim().Length >= 2) _searchTimer.Start(); else SearchResults.Clear(); } } }
    public ObservableCollection<SearchHit> SearchResults { get; } = new();
    public string TableSearch { get => _tableSearch; set { if (Set(ref _tableSearch, value)) { Page = 0; LoadTable(); } } }
    public IReadOnlyList<TableMeta> Tables => TableCatalog.All.Where(x => !x.ReadOnly && x.Name != "schedule" && x.Name != "audit_log").ToList();
    public TableMeta Table { get => _table; set { if (Set(ref _table, value)) { Page = 0; Order = null; BuildFilters(); LoadTable(); Raise(nameof(CanImport)); Raise(nameof(CanBulkActions)); } } }
    public DataView Rows => _rows.DefaultView; public object? SelectedRow { get => _selectedRow; set => Set(ref _selectedRow, value); }
    public long? RecordToFocusId { get => _recordToFocusId; private set => Set(ref _recordToFocusId, value); }
    public int Page { get => _page; set { if (Set(ref _page, value)) Raise(nameof(PageText)); } } public int Total { get; private set; } public string PageText => $"Страница {Page + 1} · записей: {Total}";
    public string? Order { get => _order; set => Set(ref _order, value); } public bool Descending { get => _descending; set => Set(ref _descending, value); } public bool CanImport => CsvService.CanImport(Table.Name);
    public DashboardStats? Stats { get => _stats; set => Set(ref _stats, value); } public string Toast { get => _toast; set => Set(ref _toast, value); }
    public ObservableCollection<TableFilter> TableFilters { get; } = new(); public IReadOnlyList<long> SelectedIds { get; private set; } = Array.Empty<long>();
    public bool CanBulkActions => !Table.ReadOnly && Table.Name is not "users" and not "time_slots" and not "curriculum" and not "grades" and not "attendance"; public bool CanShowBulkActions => CanBulkActions && SelectedIds.Count > 0;
    public ObservableCollection<IntegrityResult> Integrity { get; } = new(); public ObservableCollection<IntegrityIssue> Issues { get; } = new();
    public IntegrityResult? SelectedIntegrity { get => _selectedIntegrity; set { if (Set(ref _selectedIntegrity, value)) ShowIssues(value); } } public IntegrityIssue? SelectedIssue { get => _selectedIssue; set => Set(ref _selectedIssue, value); }
    public ObservableCollection<BackupInfo> Backups { get; } = new(); public DbStats? DbStats { get; private set; }
    public Action<long>? ScheduleEntryRequested { get; set; }
    public bool IsDashboard => Section == "Обзор"; public bool IsTable => Section is "Структура" or "Контингент" or "Учебный процесс"; public bool IsIntegrity => Section == "Качество данных"; public bool IsSystem => Section == "Система"; public bool IsAudit => Section == "Журнал действий"; public bool IsSchedule => Section == "Расписание"; public string ThemeTitle => SettingsService.Theme == "dark" ? "Светлая тема" : "Тёмная тема";

    public void Navigate(string section) { Section = section; Subtitle = section; if (IsDashboard) Stats = new StatsService().Get(); else if (IsTable) Table = TableCatalog.Get(section == "Структура" ? "institutes" : section == "Контингент" ? "groups" : "subjects"); else if (IsIntegrity) LoadIntegrity(); else if (IsSystem) LoadSystem(); Raise(nameof(IsDashboard)); Raise(nameof(IsTable)); Raise(nameof(IsIntegrity)); Raise(nameof(IsSystem)); Raise(nameof(IsAudit)); Raise(nameof(IsSchedule)); }
    public void LoadTable() { try { _rows = _repo.Query(Table, TableSearch, _filters, Order, Descending, 50, Page * 50); Total = _repo.Count(Table, TableSearch, _filters); Raise(nameof(Rows)); Raise(nameof(PageText)); } catch (Exception ex) { Notify(ex.Message); } }
    public void Sort(string column) { if (Order == column) Descending = !Descending; else { Order = column; Descending = false; } LoadTable(); }
    public void SetSelectedRows(IList rows) { SelectedIds = rows.OfType<DataRowView>().Select(x => Convert.ToInt64(x["id"], CultureInfo.InvariantCulture)).Distinct().ToArray(); Raise(nameof(CanShowBulkActions)); }
    private void BuildFilters() { TableFilters.Clear(); _filters.Clear(); foreach (var c in Table.Columns.Where(x => x.Kind is ColumnKind.Foreign or ColumnKind.Choice)) { var values = new List<FilterValue> { new("", "Все значения") }; if (c.Kind == ColumnKind.Foreign && c.ForeignTable != null) values.AddRange(_repo.Lookup(c.ForeignTable).Select(x => new FilterValue(x.Id.ToString(CultureInfo.InvariantCulture), x.Label))); else values.AddRange(c.Choices.Select(x => new FilterValue(x, x))); TableFilters.Add(new TableFilter(c.Name, c.Title, values, ApplyFilter)); } }
    private void ApplyFilter(string name, string value) { if (value.Length == 0) _filters.Remove(name); else _filters[name] = value; Page = 0; LoadTable(); }
    private void ResetFilters() { foreach (var f in TableFilters) f.Reset(); _filters.Clear(); Page = 0; LoadTable(); }
    private void SaveView() { var d = new SavedViewDialog(_savedViews, Table.Name, new ViewState { Search = TableSearch, Filters = new Dictionary<string, string>(_filters), OrderBy = Order, Desc = Descending }) { Owner = Application.Current.MainWindow }; if (d.ShowDialog() == true) Notify("Представление сохранено."); }
    private void OpenViews() { var d = new SavedViewDialog(_savedViews, Table.Name, null) { Owner = Application.Current.MainWindow }; if (d.ShowDialog() == true && d.AppliedState != null) { TableSearch = d.AppliedState.Search; Order = Table.Column(d.AppliedState.OrderBy ?? "") == null ? null : d.AppliedState.OrderBy; Descending = d.AppliedState.Desc; foreach (var f in TableFilters) f.SetValue(d.AppliedState.Filters.TryGetValue(f.Name, out var v) ? v : ""); Page = 0; LoadTable(); } }
    private void MassUpdate() { if (!CanShowBulkActions) return; var d = new MassUpdateDialog(Table) { Owner = Application.Current.MainWindow }; if (d.ShowDialog() == true && d.Column != null) try { _audit.UpdateFieldMany(Table, SelectedIds, d.Column, d.Value); LoadTable(); Notify("Выбранные записи изменены."); } catch (Exception ex) { Notify(ex.Message); } }
    private void DeleteMany() { if (!CanShowBulkActions || !ConfirmDialog.Ask(Application.Current.MainWindow!, "Удалить выбранные записи?")) return; try { _audit.DeleteMany(Table, SelectedIds); LoadTable(); Notify("Выбранные записи удалены."); } catch (Exception ex) { Notify(ex.Message); } }
    private void Edit(object? row = null) { long? id = row is DataRowView rv ? Convert.ToInt64(rv["id"], CultureInfo.InvariantCulture) : null; var d = new EditRecordDialog(Table, id, _repo) { Owner = Application.Current.MainWindow }; if (d.ShowDialog() == true) try { if (id.HasValue) _audit.Update(Table, id.Value, d.Values); else _audit.Insert(Table, d.Values); LoadTable(); } catch (Exception ex) { Notify(ex.Message); } }
    private void DeleteSelected() { if (SelectedRow is not DataRowView row) return; if (ConfirmDialog.Ask(Application.Current.MainWindow!, "Удалить выбранную запись?")) try { _audit.Delete(Table, Convert.ToInt64(row["id"], CultureInfo.InvariantCulture)); LoadTable(); } catch (Exception ex) { Notify(ex.Message); } }
    private void Export() { var d = new SaveFileDialog { Filter = "CSV-файл|*.csv", FileName = Table.Name + ".csv" }; if (d.ShowDialog() == true) new CsvService().Export(Table, TableSearch, _filters, d.FileName); }
    private void Import() { if (!CanImport) return; var d = new OpenFileDialog { Filter = "CSV-файл|*.csv" }; if (d.ShowDialog() == true) { var p = new CsvService().Preview(Table, d.FileName); var dialog = new ImportPreviewDialog(p) { Owner = Application.Current.MainWindow }; if (dialog.ShowDialog() == true) { var result = new CsvService().Apply(p); Notify(result.Message); if (result.Success) LoadTable(); } } }
    private void RunSearch() { SearchResults.Clear(); foreach (var hit in _search.Search(SearchText, 30)) SearchResults.Add(hit); }
    private void SelectSearch(SearchHit? hit) { if (hit == null) return; Navigate(hit.Table is "groups" or "students" or "teachers" ? "Контингент" : hit.Table == "subjects" ? "Учебный процесс" : "Структура"); Table = TableCatalog.Get(hit.Table); }
    private void LoadIntegrity() { Integrity.Clear(); Issues.Clear(); foreach (var x in new IntegrityService().RunAll()) Integrity.Add(x); } public void ShowIssues(IntegrityResult? x) { Issues.Clear(); if (x != null) foreach (var i in new IntegrityService().Details(x.RuleId, 100)) Issues.Add(i); }
    public void OpenIssue(IntegrityIssue? issue) { if (issue != null) OpenRecord(issue.Table, issue.RowId); }
    public void OpenSchedule(long? scheduleId = null)
    {
        Navigate("Расписание");
        if (scheduleId.HasValue) ScheduleEntryRequested?.Invoke(scheduleId.Value);
    }
    public void OpenRecord(string table, long id)
    {
        if (table == "schedule") { OpenSchedule(id); return; }
        var meta = TableCatalog.Find(table);
        if (meta == null || meta.ReadOnly || _repo.GetById(meta, id) == null) return;
        Navigate(table is "students" or "teachers" or "groups" ? "Контингент" : table is "subjects" or "curriculum" ? "Учебный процесс" : "Структура");
        Table = meta;
        _filters.Clear();
        foreach (var filter in TableFilters) filter.Reset();
        TableSearch = "";
        Order = "id";
        Descending = false;
        Page = (int)(_repo.PositionOfId(meta, id, Order, Descending) / 50);
        RecordToFocusId = id;
        LoadTable();
    }
    private void LoadSystem() { DbStats = BackupService.GetStats(); Backups.Clear(); foreach (var x in BackupService.List()) Backups.Add(x); Raise(nameof(DbStats)); } private void CreateBackup() { try { Notify("Создана копия: " + BackupService.CreateBackup("Создано администратором").Name); LoadSystem(); } catch (Exception ex) { Notify(ex.Message); } } private void Optimize() { try { var r = BackupService.IntegrityCheck(); if (r == "ok") BackupService.Vacuum(); Notify(r); } catch (Exception ex) { Notify(ex.Message); } } private void ToggleTheme() { SettingsService.Theme = SettingsService.Theme == "dark" ? "light" : "dark"; ((MainWindow)Application.Current.MainWindow!).ApplyTheme(); Raise(nameof(ThemeTitle)); } private void Refresh() => Navigate(Section); private void Notify(string text) => Toast = text;
}

