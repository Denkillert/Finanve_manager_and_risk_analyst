using System.Windows;
using FinanveManager.Data.Persistence;
using FinanveManager.WpfClient.Views;

namespace FinanveManager.WpfClient;

public partial class App : System.Windows.Application
{
    private const string DefaultConnectionString =
        "Server=.;Database=FinanveManagerDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private readonly AppDatabaseFactory _dbFactory = new(DefaultConnectionString, "FinanveManagerDesktopDb");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            await _dbFactory.InitializeAsync();
            ShowLoginFlow();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка инициализации базы данных:\n{ex.Message}",
                "СтройФинРиск",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void ShowLoginFlow()
    {
        while (true)
        {
            var loginWindow = new LoginWindow(_dbFactory);
            bool? loggedIn = loginWindow.ShowDialog();
            if (loggedIn != true || loginWindow.Session == null)
            {
                Shutdown(0);
                return;
            }

            var mainWindow = new MainWindow(_dbFactory, loginWindow.Session);
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
