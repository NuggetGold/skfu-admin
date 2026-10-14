using System.Windows;
using SkfuAdmin.Data;
using SkfuAdmin.Services;

namespace SkfuAdmin;

public partial class App : Application
{
    private Mutex? _mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "SkfuAdmin.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("Приложение уже запущено.", "СКФУ", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        try
        {
            base.OnStartup(e);
            Schema.EnsureCreated();
            Seeder.SeedIfEmpty();
            BackupService.AutoBackup();
            new MainWindow().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось запустить приложение: " + ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
    protected override void OnExit(ExitEventArgs e) { _mutex?.ReleaseMutex(); _mutex?.Dispose(); base.OnExit(e); }
}
