using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FinanveManager.WpfClient.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        return true;
    }
}

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public class ApiClient
{
    private readonly HttpClient _http = new() { BaseAddress = new Uri("https://localhost:5001/") };

    public string? JwtToken { get; private set; }
    public string CurrentRole { get; private set; } = "CEO";
    public string CurrentUserName { get; private set; } = "Руководитель организации";

    public async Task<bool> LoginAsync(string login, string password, string? twoFactor = null)
    {
        var resp = await _http.PostAsJsonAsync("api/auth/login", new { login, password, twoFactorCode = twoFactor });
        if (!resp.IsSuccessStatusCode) return false;

        var payload = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        if (payload?.Token == null) return false;

        JwtToken = payload.Token;
        CurrentRole = payload.User?.Role ?? "CEO";
        CurrentUserName = payload.User?.FullName ?? login;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtToken);
        return true;
    }

    public async Task<T?> GetAsync<T>(string url) => await _http.GetFromJsonAsync<T>(url);

    public async Task<HttpResponseMessage> PostAsync<T>(string url, T body) => await _http.PostAsJsonAsync(url, body);

    public record LoginResponse(string Token, UserDto? User);
    public record UserDto(Guid Id, string Login, string FullName, string Role, string RoleName);
}

/// <summary>
/// ViewModel реестра платёжных заявок (рис. 2.13: RequestViewModel).
/// </summary>
public class RequestViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private string _statusMessage = "Готово к работе";
    private string _selectedFilter = "Все";

    public ObservableCollection<RequestRowDto> Requests { get; } = new();
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
    public string SelectedFilter { get => _selectedFilter; set => SetField(ref _selectedFilter, value); }

    public ICommand RefreshCommand { get; }
    public ICommand ApproveOverrunCommand { get; }
    public ICommand ExecutePaymentCommand { get; }

    public RequestViewModel(ApiClient api)
    {
        _api = api;
        RefreshCommand = new RelayCommand(async _ => await LoadAsync());
        ApproveOverrunCommand = new RelayCommand(async param =>
        {
            if (param is RequestRowDto row)
            {
                var resp = await _api.PostAsync($"api/payment-requests/{row.Id}/approve-overrun",
                    new { approve = true, reason = "Согласовано руководством в WPF-клиенте", rowVersion = row.RowVersion });
                StatusMessage = resp.StatusCode == System.Net.HttpStatusCode.Conflict
                    ? "Конфликт версий: данные обновлены другим пользователем. Перечитайте список."
                    : resp.IsSuccessStatusCode ? $"Перерасход по заявке {row.Number} согласован." : "Ошибка согласования.";
                await LoadAsync();
            }
        });
        ExecutePaymentCommand = new RelayCommand(async param =>
        {
            if (param is RequestRowDto row)
            {
                var resp = await _api.PostAsync($"api/payment-requests/{row.Id}/execute",
                    new { paymentOrderNumber = $"ПП-{DateTime.Now:HHmmss}", rowVersion = row.RowVersion });
                StatusMessage = resp.IsSuccessStatusCode ? $"Платёж по заявке {row.Number} исполнен." : "Ошибка исполнения платежа.";
                await LoadAsync();
            }
        });
    }

    public async Task LoadAsync()
    {
        try
        {
            var items = await _api.GetAsync<List<RequestRowDto>>("api/payment-requests");
            Requests.Clear();
            if (items != null)
            {
                foreach (var item in items) Requests.Add(item);
            }
            StatusMessage = $"Загружено заявок: {Requests.Count}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Сервер недоступен ({ex.Message}). Загрузите контрольные данные или запустите API.";
        }
    }

    public record RequestRowDto(Guid Id, string Number, decimal PayableAmount, string Currency, string Type, string Status, byte[] RowVersion);
}

/// <summary>
/// ViewModel бюджета и лимитов (рис. 2.13: BudgetViewModel).
/// </summary>
public class BudgetViewModel : ViewModelBase
{
    public decimal ApprovedBudget { get; set; } = 10000m;
    public decimal GrossObligations { get; set; } = 6000m;
    public decimal PaidFact { get; set; } = 3000m;
    public decimal FreeLimit => ApprovedBudget - GrossObligations;
    public decimal ObligationRemainder => GrossObligations - PaidFact;
}

/// <summary>
/// ViewModel управления рисками и резервом P80 (рис. 2.13: RiskViewModel).
/// </summary>
public class RiskViewModel : ViewModelBase
{
    public decimal TotalEmv { get; set; } = 1340m;
    public decimal Sigma { get; set; } = 723.22m;
    public decimal ReserveP80 { get; set; } = 1948m;
    public decimal ResidualEmv { get; set; } = 715m;
}

public class MainViewModel : ViewModelBase
{
    private readonly ApiClient _api = new();
    private string _activeSection = "Главная панель";
    private string _login = "director";
    private string _password = string.Empty;

    public string ActiveSection { get => _activeSection; set => SetField(ref _activeSection, value); }
    public string Login { get => _login; set => SetField(ref _login, value); }
    public string Password { get => _password; set => SetField(ref _password, value); }
    public string CurrentRole => _api.CurrentRole;

    public RequestViewModel RequestVm { get; }
    public BudgetViewModel BudgetVm { get; } = new();
    public RiskViewModel RiskVm { get; } = new();

    public ICommand NavigateCommand { get; }
    public ICommand LoginCommand { get; }

    public MainViewModel()
    {
        RequestVm = new RequestViewModel(_api);
        NavigateCommand = new RelayCommand(p => ActiveSection = p?.ToString() ?? "Главная панель");
        LoginCommand = new RelayCommand(async _ => await _api.LoginAsync(Login, Password));
    }
}
