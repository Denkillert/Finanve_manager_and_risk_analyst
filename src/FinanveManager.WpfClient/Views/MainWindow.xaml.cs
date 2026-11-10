using System.Windows;
using System.Windows.Controls;
using FinanveManager.WpfClient.ViewModels;

namespace FinanveManager.WpfClient.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        if (DataContext is MainViewModel vm)
        {
            LoginPasswordBox.Password = vm.Password;
        }
    }

    private void LoginPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is PasswordBox box)
        {
            vm.Password = box.Password;
        }
    }
}
