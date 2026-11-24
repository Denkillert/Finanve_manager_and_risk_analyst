using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
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
        Title = $"ОАО «МАПИД» — {session.FullName} ({session.RoleName})";
        _vm.PropertyChanged += ViewModel_OnPropertyChanged;
        Loaded += async (_, _) =>
        {
            await _vm.LoadAllAsync();
            SyncReportHeaders();
            AnimateHub();
        };
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsHubVisible))
        {
            if (_vm.IsHubVisible) AnimateHub();
            else AnimateSection();
        }

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

    /// <summary>Мягкое появление главного меню: шапка и плитки «лесенкой».</summary>
    private void AnimateHub()
    {
        ContentScroll.ScrollToTop();
        if (!SystemParameters.ClientAreaAnimation) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        HubPanel.Opacity = 1;
        if (HubPanel.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            HubPanel.RenderTransform = shift;
        }

        HubPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease });

        HubTiles.UpdateLayout();
        for (int i = 0; i < _vm.NavigationItems.Count; i++)
        {
            if (HubTiles.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement tile) continue;

            var delay = TimeSpan.FromMilliseconds(Math.Min(i, 8) * 45);
            tile.Opacity = 1;

            var move = new TranslateTransform(0, 14);
            var scale = new ScaleTransform(0.985, 0.985);
            var group = new TransformGroup();
            group.Children.Add(scale);
            group.Children.Add(move);
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.RenderTransform = group;

            tile.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
                {
                    BeginTime = delay,
                    EasingFunction = ease
                });
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(340))
                {
                    BeginTime = delay,
                    EasingFunction = ease
                });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(340))
                {
                    BeginTime = delay,
                    EasingFunction = ease
                });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.985, 1, TimeSpan.FromMilliseconds(340))
                {
                    BeginTime = delay,
                    EasingFunction = ease
                });
        }
    }

    /// <summary>Раздел появляется мягко: лёгкий подъём и проявление без «вспышек».</summary>
    private void AnimateSection()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            SectionsHost.Opacity = 1;
            return;
        }

        Dispatcher.BeginInvoke(new Action(PlaySectionReveal), DispatcherPriority.Loaded);
    }

    private void PlaySectionReveal()
    {
        if (_vm.IsHubVisible) return;
        SectionsHost.UpdateLayout();
        ContentScroll.ScrollToTop();

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        SectionsHost.Opacity = 1;
        if (SectionsHost.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            SectionsHost.RenderTransform = shift;
        }

        SectionsHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_vm.IsHubVisible)
        {
            _vm.GoHomeCommand.Execute(null);
            e.Handled = true;
        }
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
            ReportSaved(dialog.FileName);
        }
    }

    private async void Export1CButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Формат 1С (1CClientBankExchange)|*.txt",
            FileName = $"1c-client-bank-exchange-{DateTime.Now:yyyyMMdd}.txt"
        };
        if (dialog.ShowDialog() != true) return;

        var bytes = await _vm.Build1CClientBankExchangeBytesAsync();
        await File.WriteAllBytesAsync(dialog.FileName, bytes);
        ReportSaved(dialog.FileName);
    }

    private async void ExportAccountingButton_OnClick(object sender, RoutedEventArgs e)
    {
        string report = (sender as FrameworkElement)?.Tag as string ?? "balance";
        string fileName = report switch
        {
            "pnl" => "forma-2.xlsx",
            "vat" => "reestr-nds.xlsx",
            "documents" => "reestr-pervichnyh-dokumentov.xlsx",
            "debts" => "debitorka-kreditorka.xlsx",
            "payments" => "reestr-platezhey.xlsx",
            _ => "buhgalterskiy-balans.xlsx"
        };

        var dialog = new SaveFileDialog
        {
            Filter = "Книга Excel|*.xlsx",
            FileName = fileName
        };
        if (dialog.ShowDialog() != true) return;

        var bytes = await _vm.BuildAccountingReportBytesAsync(report);
        await File.WriteAllBytesAsync(dialog.FileName, bytes);
        ReportSaved(dialog.FileName);
    }

    private void ImportStatementFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Банковская выписка (*.csv;*.txt;*.sta)|*.csv;*.txt;*.sta|Все файлы|*.*",
            Title = "Выберите файл банковской выписки (CSV или MT940)"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            Encoding encoding = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
                ? new UTF8Encoding(true)
                : DetectEncoding(bytes);

            _vm.StatementContent = encoding.GetString(bytes);
            _vm.StatusMessage = $"Файл «{Path.GetFileName(dialog.FileName)}» загружен. Нажмите «Загрузить и сверить выписку».";
            _vm.IsStatusError = false;
        }
        catch (Exception ex)
        {
            _vm.StatusMessage = $"Не удалось прочитать файл выписки: {ex.Message}";
            _vm.IsStatusError = true;
        }
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).Length > 0
                ? new UTF8Encoding(false)
                : Encoding.GetEncoding(1251);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251);
        }
    }

    private void ReportSaved(string path)
    {
        _vm.StatusMessage = $"Файл сохранён: {path}";
        _vm.IsStatusError = false;
    }
}
