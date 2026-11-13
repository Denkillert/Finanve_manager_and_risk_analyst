using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using FinanveManager.Data.Persistence;
using FinanveManager.WpfClient.Views;

namespace FinanveManager.WpfClient;

public partial class App : System.Windows.Application
{
    private const string DefaultConnectionString =
        "Server=.;Database=FinanveManagerDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private AppDatabaseFactory? _dbFactory;

    private static string ResolveConnectionString()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            return DefaultConnectionString;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("ConnectionStrings", out var csSection)
                && csSection.TryGetProperty("DefaultConnection", out var val)
                && !string.IsNullOrWhiteSpace(val.GetString()))
            {
                return val.GetString()!;
            }
        }
        catch
        {
            // Если appsettings.json повреждён, переходим к ошибке ниже или к DefaultConnectionString
        }

        return DefaultConnectionString;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Кодировка Windows-1251 для банковских выписок и файлов 1С.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            _dbFactory = new AppDatabaseFactory(ResolveConnectionString());
            await _dbFactory.InitializeAsync();
            ShowLoginFlow(_dbFactory);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка подключения к базе данных MS SQL Server:\n{ex.Message}",
                "ОАО «МАПИД» — Ошибка БД",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowLoginFlow(AppDatabaseFactory dbFactory)
    {
        while (true)
        {
            var loginWindow = new LoginWindow(dbFactory);
            bool? loggedIn = loginWindow.ShowDialog();
            if (loggedIn != true || loginWindow.Session == null)
            {
                Shutdown(0);
                return;
            }

            var mainWindow = new MainWindow(dbFactory, loginWindow.Session);
            MainWindow = mainWindow;
            mainWindow.ShowDialog();

            if (!mainWindow.LogoutRequested)
            {
                Shutdown(0);
                return;
            }
        }
    }
}
