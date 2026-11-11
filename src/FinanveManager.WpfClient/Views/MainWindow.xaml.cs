using System.ComponentModel;
using System.IO;
using System.Windows;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.WpfClient.ViewModels;
using Microsoft.Win32;

namespace FinanveManager.WpfClient.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public bool LogoutRequested { get; private set; }

    public MainWindow(AppDatabaseFactory dbFactory, AuthenticatedSession session)
    {
        InitializeComponent();
        _vm = new MainViewModel(dbFactory, session);
        DataContext = _vm;
        Title = $"СтройФинРиск — {session.FullName} ({session.RoleName})";
        _vm.PropertyChanged += ViewModel_OnPropertyChanged;
        Loaded += async (_, _) =>
        {
            await _vm.LoadAllAsync();
            SyncReportHeaders();
        };
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.RCol1)
            or nameof(MainViewModel.RCol2)
            or nameof(MainViewModel.RCol3)
            or nameof(MainViewModel.RCol4)
            or nameof(MainViewModel.RCol5)
            or nameof(MainViewModel.RCol6))
        {
            SyncReportHeaders();
        }
    }

    private void SyncReportHeaders()
    {
        if (ReportGrid.Columns.Count < 6) return;
        ReportGrid.Columns[0].Header = _vm.RCol1;
        ReportGrid.Columns[1].Header = _vm.RCol2;
        ReportGrid.Columns[2].Header = _vm.RCol3;
        ReportGrid.Columns[3].Header = _vm.RCol4;
        ReportGrid.Columns[4].Header = _vm.RCol5;
        ReportGrid.Columns[5].Header = _vm.RCol6;
    }

    private void LogoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        LogoutRequested = true;
        Close();
    }

    private void ExportReportCsvButton_OnClick(object sender, RoutedEventArgs e)
    {
        var key = _vm.SelectedReportChoice?.Key ?? "report";
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (UTF-8)|*.csv",
            FileName = $"{key}-report.csv"
        };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllBytes(dialog.FileName, _vm.BuildCurrentReportCsvBytes());
            _vm.StatusMessage = $"Отчёт сохранён: {dialog.FileName}";
            _vm.IsStatusError = false;
        }
    }

    private async void Export1CButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (UTF-8)|*.csv",
            FileName = "1c-accounting-export.csv"
        };
        if (dialog.ShowDialog() == true)
        {
            var bytes = await _vm.Build1CExportCsvBytesAsync();
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            _vm.StatusMessage = $"Проводки для 1С сохранены: {dialog.FileName}";
            _vm.IsStatusError = false;
        }
    }
}
