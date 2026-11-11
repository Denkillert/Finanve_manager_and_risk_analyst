using System.Windows;
using System.Windows.Controls;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.WpfClient.Views;

public sealed record DemoAccountChoice(string Login, string RoleCode, string RoleName, bool RequiresTwoFactor)
{
    public string Display => RequiresTwoFactor ? $"{RoleName} ({Login}, 2FA)" : $"{RoleName} ({Login})";

    public override string ToString() => Display;
}

public partial class LoginWindow : Window
{
    private readonly AppDatabaseFactory _dbFactory;

    public AuthenticatedSession? Session { get; private set; }

    public LoginWindow(AppDatabaseFactory dbFactory)
    {
        _dbFactory = dbFactory;
        InitializeComponent();
        Loaded += LoginWindow_OnLoaded;
    }

    private async void LoginWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await ReloadUsersAsync();
    }

    private async Task ReloadUsersAsync()
    {
        try
        {
            using var scope = _dbFactory.CreateScope();
            bool hasAnyUser = await scope.Db.Users.AnyAsync(u => !u.IsDeleted);
            EmptyDbBanner.Visibility = hasAnyUser ? Visibility.Collapsed : Visibility.Visible;

            var allUsers = await scope.Db.Users.Include(u => u.Role)
                .Where(u => !u.IsDeleted && u.IsActive)
                .OrderBy(u => u.Login)
                .Select(u => new DemoAccountChoice(
                    u.Login,
                    u.Role != null ? u.Role.Code : "Admin",
                    u.Role != null ? u.Role.Name : u.Login,
                    u.TwoFactorEnabled))
                .ToListAsync();

            var users = allUsers.Where(u => RolePermissions.IsClientAllowed(u.RoleCode, ClientApp.Wpf)).ToList();
            DemoUsersCombo.ItemsSource = users;
            var defaultChoice = users.FirstOrDefault(u => u.Login == "director") ?? users.FirstOrDefault();
            if (defaultChoice != null)
            {
                DemoUsersCombo.SelectedItem = defaultChoice;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void InitRolesAdminButton_OnClick(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        try
        {
            using var scope = _dbFactory.CreateScope();
            await DbInitializer.SeedRolesAndAdminOnlyAsync(scope.Db);
            await ReloadUsersAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void InitDemoDataButton_OnClick(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        try
        {
            using var scope = _dbFactory.CreateScope();
            await DbInitializer.SeedIfEmptyAsync(scope.Db);
            await ReloadUsersAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void DemoUsersCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DemoUsersCombo.SelectedItem is not DemoAccountChoice choice)
            return;

        LoginTextBox.Text = choice.Login;
        PasswordBox.Password = "StroyFin2026!";
        TwoFactorTextBox.Text = choice.RequiresTwoFactor ? "246810" : string.Empty;
        ErrorBanner.Visibility = Visibility.Collapsed;
    }

    private async void LoginButton_OnClick(object sender, RoutedEventArgs e)
    {
        ErrorBanner.Visibility = Visibility.Collapsed;
        LoginButton.IsEnabled = false;
        try
        {
            using var scope = _dbFactory.CreateScope();
            var result = await scope.Auth.LoginAsync(
                LoginTextBox.Text,
                PasswordBox.Password,
                TwoFactorTextBox.Text,
                "WPF Desktop",
                ClientApp.Wpf);

            if (!result.Success || result.Session == null)
            {
                ShowError(result.ErrorMessage ?? "Ошибка авторизации.");
                return;
            }

            Session = result.Session;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }
}
