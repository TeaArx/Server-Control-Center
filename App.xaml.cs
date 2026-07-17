using System.Windows;
using ServerControlCenter.Data;
using ServerControlCenter.Services;

namespace ServerControlCenter;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var migrationService = new DatabaseMigrationService(AppDbContextFactory.Shared);
            await migrationService.MigrateAsync();
        }
        catch (Exception ex)
        {
            ShowStartupError(
                $"Не удалось безопасно обновить локальную базу данных. " +
                $"Если резервная копия была создана, исходная база восстановлена.\n\n{ex.Message}");
            Shutdown(1);
            return;
        }

        try
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
            ShutdownMode = System.Windows.ShutdownMode.OnLastWindowClose;
        }
        catch (Exception ex)
        {
            ShowStartupError($"Не удалось загрузить интерфейс приложения.\n\n{ex.Message}");
            Shutdown(1);
        }
    }

    private static void ShowStartupError(string message)
    {
        MessageBox.Show(
            message,
            "Server Control Center — ошибка запуска",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
