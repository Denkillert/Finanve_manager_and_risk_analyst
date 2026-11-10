using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows.Input;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Services;
using Microsoft.Win32;

namespace FinanveManager.WpfClient.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propName);
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

public record DemoAccountInfo(
    string Login,
    string FullName,
    string RoleCode,
    string RoleName,
    string ClientType,
    bool RequiresTwoFactor,
    string PermissionsSummary)
{
    public string DisplayTitle => $"{RoleName} ({Login})";
}

public record AuthResult(bool Success, bool UsedOfflineFallback, string Message);

public record RequestRowDto(
    Guid Id,
    string Number,
    string ObjectName,
    string CostItemCode,
    decimal PayableAmount,
    string Currency,
    string Type,
    string Status,
    string PlannedPaymentDate,
    string Purpose,
    string? BlockReason,
    object? RowVersionToken)
{
    public string TypeDisplay => Type == "Advance" ? "Новый аванс" : "Текущий платёж";

    public string StatusDisplay => Status switch
    {
        "Draft" => "Черновик",
        "UnderReview" => "На проверке",
        "Returned" => "Возвращена",
        "Blocked" => "Заблокирована",
        "PendingApproval" => "На согласовании",
        "ApprovedByManagement" => "Согласована рук.",
        "Approved" => "Согласована к оплате",
        "Executed" => "Исполнена",
        "Closed" => "Закрыта",
        "Rejected" => "Отклонена",
        _ => Status
    };
}

public record ProjectForecastRow(
    string Code,
    string Name,
    decimal ContractValue,
    decimal Bac,
    decimal Pv,
    decimal Ev,
    decimal Ac,
    decimal? Cpi,
    decimal? Spi,
    decimal Etc,
    decimal Eac,
    decimal FinancialResultWithReserve);

public record FreeLimitRow(
    string ProjectCode,
    string ObjectName,
    string CostItemCode,
    string CostItemName,
    decimal ApprovedBudget,
    decimal GrossObligations,
    decimal PaidFact,
    decimal SubmittedRequests,
    decimal FreeLimit,
    decimal ObligationRemainder,
    string LimitStatus);

public record BudgetVersionRow(
    string ProjectCode,
    int VersionNumber,
    string Status,
    string ApprovedAt,
    string Comment);

public record CalendarDayRow(
    string Date,
    int DayIndex,
    decimal OpeningBalance,
    decimal Inflows,
    decimal Outflows,
    decimal ClosingBalance,
    decimal CashGap,
    string EventNote);

public record ContractRow(
    string Number,
    string Counterparty,
    string Unp,
    string Direction,
    decimal BaseAmount,
    decimal EffectiveAmount,
    string Currency,
    decimal RetentionPercent);

public record AdvanceRow(
    string ContractNumber,
    string ObjectName,
    decimal Amount,
    decimal UsedAmount,
    decimal Remainder,
    decimal UtilizationPercent,
    string DueDate,
    string Status);

public record RiskRow(
    string Code,
    string Event,
    decimal Probability,
    decimal MonetaryImpact,
    int IntegralScore,
    string Zone,
    decimal Emv,
    decimal ResidualEmv,
    string OwnerRole,
    bool TriggerFired,
    string Status);

public record SensitivityRow(
    string FactorName,
    string RangeLabel,
    decimal BaseAmount,
    decimal ImpactOnEac,
    string Comment);

public record NormRuleRow(
    string Code,
    string Name,
    string DocumentSource,
    string ValueText,
    int Revision,
    string ResponsibleRole);

public record AuditRow(
    string Timestamp,
    string UserLogin,
    string ObjectName,
    string Action,
    string OldValue,
    string NewValue,
    string Details);

public record NotificationRow(
    string Severity,
    string Category,
    string Title,
    string Message,
    string CreatedAt);

public record ReportTableRow(
    string Col1,
    string Col2,
    string Col3,
    string Col4,
    string Col5,
    string Col6);

public static class DemoSeedCatalog
{
    public static IReadOnlyList<DemoAccountInfo> Accounts { get; } =
    [
        new("director", "Ковалёв Андрей Сергеевич", "CEO", "Руководитель организации", "WPF / Веб", false,
            "Доступно: утверждение бюджетов, согласование/отклонение перерасхода (UC04), утверждение заявок, просмотр портфеля, рисков и аудита."),
        new("finmanager", "Петрова Елена Викторовна", "FinancialManager", "Финансовый менеджер", "Веб", false,
            "Доступно: создание версий бюджета, проверка лимитов Lдост, согласование заявок с ЭЦП, возврат/отклонение, платёжный календарь, отчёты."),
        new("accountant", "Сидорова Наталья Ивановна", "Accountant", "Бухгалтер", "WPF / Веб", false,
            "Доступно: исполнение согласованных заявок (Approved → Executed), банковские выписки MT940/CSV, учёт авансов, выгрузка 1С."),
        new("supply", "Новиков Дмитрий Олегович", "ProcurementSpecialist", "Специалист по снабжению", "WPF / Веб", false,
            "Доступно: договоры, спецификации, создание заявок на закупку материалов, контроль поставок и авансов."),
        new("pm", "Громов Алексей Николаевич", "ProjectManager", "Руководитель проекта", "Веб", false,
            "Доступно: создание платёжных заявок, акты С-2/С-3, прогноз EAC, регистрация рисков проекта."),
        new("risk", "Волкова Марина Петровна", "RiskSpecialist", "Специалист по рискам", "Веб", false,
            "Доступно: реестр рисков 5×5, расчёт EMV, σ и резерва P80, анализ чувствительности EAC («торнадо»)."),
        new("admin", "Титов Сергей Владимирович", "Admin", "Администратор системы", "WPF / Веб", true,
            "Ограничение НФТ-8: управление пользователями, 8 ролями, нормативными правилами и журналом аудита (обязательна 2FA: 246810). Финансовые операции заблокированы."),
        new("siteworker", "Орлов Максим Игоревич", "SiteWorker", "Работник на объекте", "Веб (360 px)", false,
            "Доступно: просмотр этапов строительства, подготовка актов С-2 и фотофиксация выполненных работ.")
    ];

    public static List<Risk> BuildChapter1Risks()
    {
        var specs = new (string Code, string Event, decimal P, decimal C, decimal ResP, string Owner, bool Trigger, string Status)[]
        {
            ("R-01", "Задержка финансирования", 0.30m, 600m, 0.15m, "Финансовый менеджер", false, "Treating"),
            ("R-02", "Рост цен на материалы", 0.46m, 450m, 0.21m, "Специалист по снабжению", false, "Treating"),
            ("R-03", "Срыв поставки", 0.25m, 400m, 0.15m, "Специалист по снабжению", false, "Treating"),
            ("R-04", "Просроченный аванс", 0.35m, 380m, 0.20m, "Финансовый менеджер", true, "Monitoring"),
            ("R-05", "Отставание от графика", 0.30m, 400m, 0.20m, "Руководитель проекта", false, "Treating"),
            ("R-06", "Ошибка проектного решения", 0.15m, 600m, 0.08m, "Руководитель проекта", false, "Treating"),
            ("R-07", "Изменение требований заказчика", 0.25m, 320m, 0.15m, "Руководитель проекта", false, "Monitoring"),
            ("R-08", "Неплатёж заказчика", 0.20m, 700m, 0.10m, "Финансовый менеджер", true, "Treating"),
            ("R-09", "Авария, форс-мажор", 0.10m, 800m, 0.05m, "Руководитель организации", false, "Monitoring"),
            ("R-10", "Ошибка классификации затрат", 0.30m, 150m, 0.15m, "Бухгалтер", false, "Monitoring"),
            ("R-11", "Утечка данных, компрометация учётной записи", 0.15m, 700m, 0.08m, "Администратор системы", false, "Treating"),
            ("R-12", "Отказ ИТ-инфраструктуры, потеря данных", 0.10m, 600m, 0.05m, "Администратор системы", false, "Monitoring")
        };

        return specs.Select(s =>
        {
            var r = new Risk
            {
                Code = s.Code,
                Event = s.Event,
                Probability = s.P,
                MonetaryImpact = s.C,
                ResidualProbability = s.ResP,
                OwnerRole = s.Owner,
                TriggerFired = s.Trigger
            };
            RiskCalculator.RecalculateScores(r);
            return r;
        }).ToList();
    }
}

public class ApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, int> _offlineFailedCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _offlineLockouts = new(StringComparer.Ordinal);

    public string? JwtToken { get; private set; }
    public string CurrentLogin { get; private set; } = "director";
    public string CurrentRole { get; private set; } = "CEO";
    public string CurrentRoleName { get; private set; } = "Руководитель организации";
    public string CurrentUserName { get; private set; } = "Ковалёв Андрей Сергеевич";
    public bool IsOfflineMode { get; private set; } = true;
    public string ActiveBaseUrl { get; private set; } = "http://localhost:5050";

    private static Uri? BuildUri(string baseUrl, string relativePath)
    {
        if (!Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var root))
            return null;
        return new Uri(root, relativePath.TrimStart('/'));
    }

    public async Task<AuthResult> LoginAsync(string baseUrl, string login, string password, string? twoFactor = null)
    {
        var cleanLogin = login.Trim();
        if (string.IsNullOrWhiteSpace(cleanLogin) || string.IsNullOrEmpty(password))
            return new AuthResult(false, false, "Укажите логин и пароль.");

        var endpoint = BuildUri(baseUrl, "api/auth/login");
        if (endpoint == null)
            return new AuthResult(false, false, "Некорректный URL сервера API (пример: http://localhost:5050).");

        ActiveBaseUrl = baseUrl.Trim().TrimEnd('/');

        try
        {
            using var resp = await _http.PostAsJsonAsync(endpoint, new
            {
                login = cleanLogin,
                password,
                twoFactorCode = string.IsNullOrWhiteSpace(twoFactor) ? null : twoFactor.Trim()
            });

            var rawBody = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var errMsg = ExtractError(rawBody) ?? $"Ошибка входа ({(int)resp.StatusCode}).";
                return new AuthResult(false, false, errMsg);
            }

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            var token = GetString(root, "token");
            if (string.IsNullOrWhiteSpace(token))
                return new AuthResult(false, false, "Сервер не вернул JWT-токен.");

            var userEl = root.TryGetProperty("user", out var u) ? u : default;
            var roleCode = GetString(userEl, "role", "roleCode") ?? "CEO";
            var roleName = GetString(userEl, "roleName", "role_name") ?? roleCode;
            var fullName = GetString(userEl, "fullName", "full_name") ?? cleanLogin;
            var resolvedLogin = GetString(userEl, "login") ?? cleanLogin;

            JwtToken = token;
            CurrentLogin = resolvedLogin;
            CurrentRole = roleCode;
            CurrentRoleName = roleName;
            CurrentUserName = fullName;
            IsOfflineMode = false;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JwtToken);

            return new AuthResult(true, false, $"Вход выполнен через API ({ActiveBaseUrl}): {CurrentUserName} [{CurrentRoleName}].");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return TryOfflineLogin(cleanLogin, password, twoFactor);
        }
    }

    private AuthResult TryOfflineLogin(string login, string password, string? twoFactor)
    {
        if (_offlineLockouts.TryGetValue(login, out var until) && until > DateTime.UtcNow)
        {
            return new AuthResult(false, true,
                $"Учётная запись «{login}» временно заблокирована до {until.ToLocalTime():HH:mm:ss} (НФТ-7).");
        }

        var account = DemoSeedCatalog.Accounts.FirstOrDefault(a => a.Login == login);
        if (account == null)
            return new AuthResult(false, true, $"Неизвестный логин «{login}». Выберите одну из 8 демонстрационных учётных записей.");

        if (password != "StroyFin2026!")
        {
            var fails = _offlineFailedCounts.GetValueOrDefault(login) + 1;
            _offlineFailedCounts[login] = fails;
            if (fails >= 5)
            {
                _offlineLockouts[login] = DateTime.UtcNow.AddMinutes(15);
                return new AuthResult(false, true, "Превышено 5 неудачных попыток входа — учётная запись заблокирована на 15 минут (НФТ-7).");
            }
            return new AuthResult(false, true, $"Неверный пароль (попытка {fails} из 5). Для демонстрации используйте StroyFin2026!.");
        }

        if (account.RequiresTwoFactor && (twoFactor?.Trim() ?? string.Empty) != "246810")
        {
            return new AuthResult(false, true,
                "Для администратора требуется код 2FA (введите 246810).");
        }

        _offlineFailedCounts[login] = 0;
        _offlineLockouts.Remove(login);

        JwtToken = null;
        _http.DefaultRequestHeaders.Authorization = null;
        CurrentLogin = account.Login;
        CurrentRole = account.RoleCode;
        CurrentRoleName = account.RoleName;
        CurrentUserName = account.FullName;
        IsOfflineMode = true;

        return new AuthResult(true, true,
            $"Сервер {ActiveBaseUrl} недоступен — выполнен вход в локальном демо-режиме: {CurrentUserName} [{CurrentRoleName}].");
    }

    public async Task<(bool Ok, List<RequestRowDto> Items, string? Error)> LoadPaymentRequestsAsync()
    {
        if (IsOfflineMode)
            return (false, [], null);

        var uri = BuildUri(ActiveBaseUrl, "api/payment-requests");
        if (uri == null) return (false, [], "Некорректный URL.");

        try
        {
            using var resp = await _http.GetAsync(uri);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                return (false, [], ExtractError(body) ?? $"Ошибка загрузки заявок ({(int)resp.StatusCode}).");

            using var doc = JsonDocument.Parse(body);
            var list = new List<RequestRowDto>();
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    list.Add(ParseRequestRow(el));
                }
            }
            return (true, list, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, [], ex.Message);
        }
    }

    public async Task<(bool Ok, string Message)> PostJsonAsync(string relativePath, object payload)
    {
        if (IsOfflineMode)
            return (false, "Локальный режим");

        var uri = BuildUri(ActiveBaseUrl, relativePath);
        if (uri == null) return (false, "Некорректный URL.");

        try
        {
            using var resp = await _http.PostAsJsonAsync(uri, payload);
            var body = await resp.Content.ReadAsStringAsync();
            if (resp.StatusCode == HttpStatusCode.Conflict)
                return (false, ExtractError(body) ?? "Конфликт версий (RowVersion): данные обновлены другим пользователем.");
            if (!resp.IsSuccessStatusCode)
                return (false, ExtractError(body) ?? $"Ошибка операции ({(int)resp.StatusCode}).");
            return (true, "Операция выполнена на сервере.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (false, $"Сервер недоступен: {ex.Message}");
        }
    }

    private static RequestRowDto ParseRequestRow(JsonElement el)
    {
        var idStr = GetString(el, "id") ?? Guid.NewGuid().ToString();
        _ = Guid.TryParse(idStr, out var id);

        var number = GetString(el, "number") ?? "ЗП-000";
        var objectName = GetString(el, "objectName", "object_name") ?? "Жилой дом № 4";
        var costCode = GetString(el, "costItemCode", "cost_item_code") ?? "ST-01";
        var payable = GetDecimal(el, "payableAmount", "payable_amount");
        var currency = GetString(el, "currency") ?? "BYN";
        var type = ParseRequestType(el);
        var status = ParseRequestStatus(el);
        var planDate = GetString(el, "plannedPaymentDate", "planned_payment_date") ?? "2026-11-20";
        if (planDate.Length > 10) planDate = planDate[..10];
        var purpose = GetString(el, "purpose") ?? string.Empty;
        var blockReason = GetString(el, "blockReason", "block_reason", "returnOrRejectReason", "return_or_reject_reason");

        object? rowVersion = null;
        if (el.TryGetProperty("rowVersion", out var rvCamel) || el.TryGetProperty("row_version", out rvCamel))
        {
            rowVersion = rvCamel.ValueKind switch
            {
                JsonValueKind.Number => rvCamel.GetInt64(),
                JsonValueKind.String => rvCamel.GetString(),
                _ => null
            };
        }

        return new RequestRowDto(id, number, objectName, costCode, payable, currency, type, status, planDate, purpose, blockReason, rowVersion);
    }

    private static string ParseRequestType(JsonElement el)
    {
        if (!el.TryGetProperty("type", out var prop)) return "CurrentPayment";
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val))
            return val == 1 ? "Advance" : "CurrentPayment";
        return prop.GetString() ?? "CurrentPayment";
    }

    private static string ParseRequestStatus(JsonElement el)
    {
        if (!el.TryGetProperty("status", out var prop)) return "PendingApproval";
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out int val))
        {
            return val switch
            {
                0 => "Draft",
                1 => "UnderReview",
                2 => "Returned",
                3 => "Blocked",
                4 => "PendingApproval",
                5 => "ApprovedByManagement",
                6 => "Approved",
                7 => "Executed",
                8 => "Closed",
                9 => "Rejected",
                _ => "PendingApproval"
            };
        }
        return prop.GetString() ?? "PendingApproval";
    }

    private static string? GetString(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static decimal GetDecimal(JsonElement el, params string[] names)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0m;
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d))
                    return d;
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return parsed;
            }
        }
        return 0m;
    }

    private static string? ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return GetString(doc.RootElement, "error", "message", "title");
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// ViewModel реестра платёжных заявок (рис. 2.13: RequestViewModel).
/// </summary>
public class RequestViewModel : ViewModelBase
{
    private readonly ApiClient _api;
    private readonly Action<string, string, string, string, string> _recordAudit;
    private readonly Func<string, bool> _hasRole;
    private string _statusMessage = "Готово к работе";
    private string _selectedFilter = "Все";
    private RequestRowDto? _selectedRequest;

    private string _newObjectName = "Жилой дом № 4 (Obj1)";
    private string _newCostItemCode = "ST-01";
    private string _newRequestType = "CurrentPayment";
    private decimal _newAmount = 500m;
    private string _newDocNumber = "С2-24/26";
    private string _newPurpose = "Оплата строительных материалов по графику";

    public ObservableCollection<RequestRowDto> AllRequests { get; } = [];
    public ObservableCollection<RequestRowDto> Requests { get; } = [];

    public IReadOnlyList<string> FilterOptions { get; } =
    [
        "Все", "Blocked", "PendingApproval", "Approved", "Executed", "Closed", "Returned", "Rejected"
    ];

    public IReadOnlyList<string> ObjectOptions { get; } =
    [
        "Жилой дом № 4 (Obj1)",
        "Здание ДДУ (Obj3)"
    ];

    public IReadOnlyList<string> CostItemOptions { get; } =
    [
        "ST-01", "ST-02", "ST-03", "ST-04"
    ];

    public IReadOnlyList<string> TypeOptions { get; } =
    [
        "CurrentPayment", "Advance"
    ];

    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    public string SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (SetField(ref _selectedFilter, value))
                ApplyFilter();
        }
    }

    public RequestRowDto? SelectedRequest
    {
        get => _selectedRequest;
        set
        {
            if (SetField(ref _selectedRequest, value))
                RaiseAllCanExecute();
        }
    }

    public string NewObjectName { get => _newObjectName; set => SetField(ref _newObjectName, value); }
    public string NewCostItemCode { get => _newCostItemCode; set => SetField(ref _newCostItemCode, value); }
    public string NewRequestType { get => _newRequestType; set => SetField(ref _newRequestType, value); }
    public decimal NewAmount { get => _newAmount; set => SetField(ref _newAmount, value); }
    public string NewDocNumber { get => _newDocNumber; set => SetField(ref _newDocNumber, value); }
    public string NewPurpose { get => _newPurpose; set => SetField(ref _newPurpose, value); }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand CreateRequestCommand { get; }
    public RelayCommand ApproveOverrunCommand { get; }
    public RelayCommand ApproveWithEdsCommand { get; }
    public RelayCommand ReturnRequestCommand { get; }
    public RelayCommand ExecutePaymentCommand { get; }

    public RequestViewModel(
        ApiClient api,
        Func<string, bool> hasRole,
        Action<string, string, string, string, string> recordAudit)
    {
        _api = api;
        _hasRole = hasRole;
        _recordAudit = recordAudit;

        RefreshCommand = new RelayCommand(async _ => await LoadAsync());

        CreateRequestCommand = new RelayCommand(
            async _ => await CreateRequestAsync(),
            _ => _hasRole("ProjectManager") || _hasRole("ProcurementSpecialist") || _hasRole("FinancialManager") || _hasRole("CEO"));

        ApproveOverrunCommand = new RelayCommand(
            async param => await ApproveOverrunAsync(param as RequestRowDto ?? SelectedRequest),
            _ => _hasRole("CEO"));

        ApproveWithEdsCommand = new RelayCommand(
            async param => await ApproveWithEdsAsync(param as RequestRowDto ?? SelectedRequest),
            _ => _hasRole("FinancialManager") || _hasRole("CEO"));

        ReturnRequestCommand = new RelayCommand(
            async param => await ReturnAsync(param as RequestRowDto ?? SelectedRequest),
            _ => _hasRole("FinancialManager") || _hasRole("CEO"));

        ExecutePaymentCommand = new RelayCommand(
            async param => await ExecuteAsync(param as RequestRowDto ?? SelectedRequest),
            _ => _hasRole("Accountant"));

        SeedDefaultRequests();
    }

    public void RaiseAllCanExecute()
    {
        CreateRequestCommand?.RaiseCanExecuteChanged();
        ApproveOverrunCommand?.RaiseCanExecuteChanged();
        ApproveWithEdsCommand?.RaiseCanExecuteChanged();
        ReturnRequestCommand?.RaiseCanExecuteChanged();
        ExecutePaymentCommand?.RaiseCanExecuteChanged();
    }

    private void SeedDefaultRequests()
    {
        AllRequests.Clear();
        AllRequests.Add(new RequestRowDto(
            Guid.Parse("95000000-0000-0000-0000-000000000001"),
            "ЗП-20261110-0001", "Жилой дом № 4 (Obj1)", "ST-01",
            1000m, "BYN", "CurrentPayment", "PendingApproval", "2026-11-18",
            "Оплата поставки плит перекрытия по накладной № 412", null, 1L));
        AllRequests.Add(new RequestRowDto(
            Guid.Parse("95000000-0000-0000-0000-000000000002"),
            "ЗП-20261110-0002", "Здание ДДУ (Obj3)", "ST-02",
            1500m, "BYN", "CurrentPayment", "Blocked", "2026-11-19",
            "Оплата монтажа кровли ДДУ (превышение лимита ST-02)",
            "Недостаточно свободного лимита: Lдост = -500.00 тыс. р. (Δ = +500.00)", 1L));
        AllRequests.Add(new RequestRowDto(
            Guid.Parse("95000000-0000-0000-0000-000000000003"),
            "ЗП-20261110-0003", "Жилой дом № 4 (Obj1)", "ST-04",
            850m, "BYN", "CurrentPayment", "Approved", "2026-11-16",
            "Оплата по акту С-2 № 18 за вычетом аванса 300 и удержания 50", null, 1L));
        ApplyFilter();
        SelectedRequest = Requests.FirstOrDefault();
    }

    private void ApplyFilter()
    {
        var selectedId = SelectedRequest?.Id;
        Requests.Clear();
        foreach (var item in AllRequests)
        {
            if (_selectedFilter == "Все" || string.Equals(item.Status, _selectedFilter, StringComparison.Ordinal))
                Requests.Add(item);
        }
        SelectedRequest = Requests.FirstOrDefault(r => r.Id == selectedId) ?? Requests.FirstOrDefault();
    }

    private void ReplaceRow(RequestRowDto updated)
    {
        for (int i = 0; i < AllRequests.Count; i++)
        {
            if (AllRequests[i].Id == updated.Id)
            {
                AllRequests[i] = updated;
                break;
            }
        }
        ApplyFilter();
        SelectedRequest = Requests.FirstOrDefault(r => r.Id == updated.Id);
    }

    private async Task CreateRequestAsync()
    {
        if (NewAmount <= 0 || string.IsNullOrWhiteSpace(NewDocNumber))
        {
            StatusMessage = "Укажите положительную сумму и номер документа-основания.";
            return;
        }

        if (!_api.IsOfflineMode)
        {
            bool isDdu = NewObjectName.Contains("Obj3", StringComparison.Ordinal);
            string projectId = isDdu ? "30000000-0000-0000-0000-000000000002" : "30000000-0000-0000-0000-000000000001";
            string objectId = isDdu ? "40000000-0000-0000-0000-000000000003" : "40000000-0000-0000-0000-000000000001";
            string contractId = isDdu ? "90000000-0000-0000-0000-000000000004" : "90000000-0000-0000-0000-000000000002";
            string costItemId = NewCostItemCode switch
            {
                "ST-02" => "50000000-0000-0000-0000-000000000002",
                "ST-03" => "50000000-0000-0000-0000-000000000003",
                "ST-04" => "50000000-0000-0000-0000-000000000004",
                _ => "50000000-0000-0000-0000-000000000001"
            };

            var (ok, msg) = await _api.PostJsonAsync("api/payment-requests", new
            {
                projectId,
                constructionObjectId = objectId,
                contractId,
                costItemId,
                amount = NewAmount,
                actAmount = NewAmount,
                advanceOffset = 0m,
                retentionAmount = 0m,
                vatIncluded = true,
                currency = "BYN",
                type = NewRequestType,
                plannedPaymentDate = "2026-11-20",
                purpose = string.IsNullOrWhiteSpace(NewPurpose) ? $"Оплата по {NewDocNumber}" : NewPurpose,
                documentType = "ActS2",
                documentNumber = NewDocNumber.Trim(),
                documentDate = "2026-11-10",
                submitImmediately = true
            });

            if (!ok)
            {
                StatusMessage = msg;
                return;
            }

            await LoadAsync();
            _recordAudit("PaymentRequest", "CREATE_REQUEST", "-", $"{NewDocNumber}:{NewAmount:N2}", NewPurpose);
            StatusMessage = $"Заявка по документу {NewDocNumber} создана на сервере и прошла автоматические проверки.";
            return;
        }

        // Локальный демо-режим: контроль по формуле (1.1) — Obj1 + ST-01 имеет Lдост = 4000; Obj3 + ST-02 имеет Lдост = -500
        bool overLimit = NewObjectName.Contains("Obj3", StringComparison.Ordinal) || NewAmount > 4000m;
        bool overdueAdvBlock = NewRequestType == "Advance" && NewObjectName.Contains("Obj3", StringComparison.Ordinal);

        string status = (overLimit || overdueAdvBlock) ? "Blocked" : "PendingApproval";
        string? blockReason = overdueAdvBlock
            ? "Блокировка: по договору имеется просроченный аванс (правило 10)."
            : overLimit
                ? "Недостаточно свободного лимита Lдост — заявка заблокирована до решения руководителя (UC04)."
                : null;

        var number = $"ЗП-20261110-{AllRequests.Count + 1:D4}";
        var created = new RequestRowDto(
            Guid.NewGuid(), number, NewObjectName, NewCostItemCode,
            NewAmount, "BYN", NewRequestType, status,
            DateTime.Today.AddDays(7).ToString("yyyy-MM-dd"),
            string.IsNullOrWhiteSpace(NewPurpose) ? $"Оплата по документу {NewDocNumber}" : NewPurpose,
            blockReason, 1L);

        AllRequests.Insert(0, created);
        ApplyFilter();
        SelectedRequest = created;
        _recordAudit("PaymentRequest", "CREATE_REQUEST", "-", $"{number}:{status}:{NewAmount:N2}", NewDocNumber);
        StatusMessage = status == "Blocked"
            ? $"Заявка {number} создана и ЗАБЛОКИРОВАНА: {blockReason}"
            : $"Заявка {number} создана ({NewAmount:N2} BYN) и направлена на согласование.";
    }

    private async Task ApproveOverrunAsync(RequestRowDto? row)
    {
        if (row == null)
        {
            StatusMessage = "Выберите заявку в таблице.";
            return;
        }
        if (row.Status != "Blocked")
        {
            StatusMessage = $"Согласование перерасхода (UC04) выполняется только для заблокированной заявки (текущий статус: {row.StatusDisplay}).";
            return;
        }

        if (!_api.IsOfflineMode)
        {
            var (ok, msg) = await _api.PostJsonAsync($"api/payment-requests/{row.Id}/approve-overrun",
                new { approve = true, reason = "Согласовано руководителем в WPF-клиенте", rowVersion = row.RowVersionToken });
            if (!ok)
            {
                StatusMessage = msg;
                return;
            }
            await LoadAsync();
        }
        else
        {
            ReplaceRow(row with { Status = "PendingApproval", BlockReason = null });
        }

        _recordAudit("PaymentRequest", "APPROVE_OVERRUN", "Blocked", "PendingApproval", $"Заявка {row.Number}");
        StatusMessage = $"Перерасход по заявке {row.Number} согласован руководителем (UC04) → На согласовании.";
    }

    private async Task ApproveWithEdsAsync(RequestRowDto? row)
    {
        if (row == null)
        {
            StatusMessage = "Выберите заявку в таблице.";
            return;
        }
        if (row.Status is not ("PendingApproval" or "ApprovedByManagement"))
        {
            StatusMessage = $"Нельзя согласовать к оплате заявку в статусе «{row.StatusDisplay}».";
            return;
        }

        var thumb = $"BY-UCA-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        if (!_api.IsOfflineMode)
        {
            var (ok, msg) = await _api.PostJsonAsync($"api/payment-requests/{row.Id}/approve",
                new { edsCertificateThumbprint = thumb, rowVersion = row.RowVersionToken });
            if (!ok)
            {
                StatusMessage = msg;
                return;
            }
            await LoadAsync();
        }
        else
        {
            ReplaceRow(row with { Status = "Approved" });
        }

        _recordAudit("PaymentRequest", "APPROVE_REQUEST", row.Status, "Approved", $"Заявка {row.Number}, ЭЦП {thumb}");
        StatusMessage = $"Заявка {row.Number} согласована к оплате с ЭЦП ({thumb}).";
    }

    private async Task ReturnAsync(RequestRowDto? row)
    {
        if (row == null)
        {
            StatusMessage = "Выберите заявку в таблице.";
            return;
        }
        if (row.Status is "Executed" or "Closed" or "Rejected")
        {
            StatusMessage = $"Нельзя вернуть заявку в статусе «{row.StatusDisplay}».";
            return;
        }

        if (!_api.IsOfflineMode)
        {
            var (ok, msg) = await _api.PostJsonAsync($"api/payment-requests/{row.Id}/return-or-reject",
                new { rejectPermanently = false, reason = "Возвращено на доработку из WPF-клиента", rowVersion = row.RowVersionToken });
            if (!ok)
            {
                StatusMessage = msg;
                return;
            }
            await LoadAsync();
        }
        else
        {
            ReplaceRow(row with { Status = "Returned", BlockReason = "Требуется уточнение документов" });
        }

        _recordAudit("PaymentRequest", "RETURN_REQUEST", row.Status, "Returned", $"Заявка {row.Number}");
        StatusMessage = $"Заявка {row.Number} возвращена на доработку.";
    }

    private async Task ExecuteAsync(RequestRowDto? row)
    {
        if (row == null)
        {
            StatusMessage = "Выберите заявку в таблице.";
            return;
        }
        if (row.Status != "Approved")
        {
            StatusMessage = $"Инвариант 3: исполнение платежа возможно только из статуса «Согласована к оплате» (сейчас: {row.StatusDisplay}).";
            return;
        }

        var orderNo = $"ПП-{DateTime.Now:HHmmss}";
        if (!_api.IsOfflineMode)
        {
            var (ok, msg) = await _api.PostJsonAsync($"api/payment-requests/{row.Id}/execute",
                new { paymentOrderNumber = orderNo, rowVersion = row.RowVersionToken });
            if (!ok)
            {
                StatusMessage = msg;
                return;
            }
            await LoadAsync();
        }
        else
        {
            ReplaceRow(row with { Status = "Executed" });
        }

        _recordAudit("PaymentRequest", "EXECUTE_PAYMENT", "Approved", "Executed", $"Заявка {row.Number}, {orderNo}");
        StatusMessage = $"Платёж по заявке {row.Number} исполнен бухгалтером ({orderNo}).";
    }

    public async Task LoadAsync()
    {
        if (_api.IsOfflineMode)
        {
            StatusMessage = $"Загружены локальные контрольные заявки: {AllRequests.Count}";
            return;
        }

        var (ok, items, error) = await _api.LoadPaymentRequestsAsync();
        if (ok)
        {
            AllRequests.Clear();
            foreach (var item in items) AllRequests.Add(item);
            ApplyFilter();
            StatusMessage = $"Загружено с сервера ({_api.ActiveBaseUrl}): {AllRequests.Count} заявок.";
        }
        else
        {
            StatusMessage = error ?? "Не удалось загрузить заявки с сервера; отображаются локальные контрольные данные.";
        }
    }
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
    public decimal EmvReduction => TotalEmv - ResidualEmv;
}

public class MainViewModel : ViewModelBase
{
    private static readonly string[] SectionNames =
    [
        "Главная панель",
        "Проекты и бюджеты",
        "Платёжные заявки",
        "Платёжный календарь",
        "Договоры и закупки",
        "Риски (P80)",
        "Отчёты",
        "Администрирование",
        "Журнал аудита"
    ];

    private readonly ApiClient _api = new();
    private string _activeSection = "Главная панель";
    private int _selectedSectionIndex;
    private string _login = "director";
    private string _password = "StroyFin2026!";
    private string _twoFactorCode = string.Empty;
    private string _apiBaseUrl = "http://localhost:5050";
    private bool _isLoginPanelOpen = true;
    private string _authMessage = "Текущая учётная запись: Ковалёв Андрей Сергеевич (CEO). Выберите роль или смените учётную запись выше.";
    private bool _isAuthError;
    private DemoAccountInfo _selectedDemoAccount;
    private string _selectedReportType = "budget-plan-fact";

    public ObservableCollection<DemoAccountInfo> DemoAccounts { get; } = [];
    public IReadOnlyList<string> ServerOptions { get; } =
    [
        "http://localhost:5050",
        "https://localhost:5051",
        "https://localhost:5001",
        "http://localhost:5000"
    ];

    public IReadOnlyList<string> ReportTypes { get; } =
    [
        "budget-plan-fact",
        "project-evm-forecast",
        "risks-reserve-p80",
        "contracts-advances",
        "payment-requests-violations"
    ];

    public ObservableCollection<ProjectForecastRow> ProjectForecasts { get; } = [];
    public ObservableCollection<FreeLimitRow> FreeLimits { get; } = [];
    public ObservableCollection<BudgetVersionRow> BudgetVersions { get; } = [];
    public ObservableCollection<CalendarDayRow> CalendarDays { get; } = [];
    public ObservableCollection<ContractRow> Contracts { get; } = [];
    public ObservableCollection<AdvanceRow> Advances { get; } = [];
    public ObservableCollection<RiskRow> Risks { get; } = [];
    public ObservableCollection<SensitivityRow> SensitivityFactors { get; } = [];
    public ObservableCollection<NormRuleRow> NormRules { get; } = [];
    public ObservableCollection<AuditRow> AuditLogs { get; } = [];
    public ObservableCollection<NotificationRow> Notifications { get; } = [];
    public ObservableCollection<ReportTableRow> ReportRows { get; } = [];

    private string _reportCol1Header = "Код";
    private string _reportCol2Header = "Наименование";
    private string _reportCol3Header = "План / Бюджет";
    private string _reportCol4Header = "Обязательства / Факт";
    private string _reportCol5Header = "Отклонение / Резерв";
    private string _reportCol6Header = "Статус";
    private string _reportStatusMessage = "Выберите тип отчёта и нажмите «Сформировать предпросмотр».";

    public string ReportCol1Header { get => _reportCol1Header; set => SetField(ref _reportCol1Header, value); }
    public string ReportCol2Header { get => _reportCol2Header; set => SetField(ref _reportCol2Header, value); }
    public string ReportCol3Header { get => _reportCol3Header; set => SetField(ref _reportCol3Header, value); }
    public string ReportCol4Header { get => _reportCol4Header; set => SetField(ref _reportCol4Header, value); }
    public string ReportCol5Header { get => _reportCol5Header; set => SetField(ref _reportCol5Header, value); }
    public string ReportCol6Header { get => _reportCol6Header; set => SetField(ref _reportCol6Header, value); }
    public string ReportStatusMessage { get => _reportStatusMessage; set => SetField(ref _reportStatusMessage, value); }

    public string SelectedReportType
    {
        get => _selectedReportType;
        set
        {
            if (SetField(ref _selectedReportType, value))
                BuildReportPreview();
        }
    }

    public int SelectedSectionIndex
    {
        get => _selectedSectionIndex;
        set
        {
            var normalized = value is >= 0 and < 9 ? value : 0;
            if (SetField(ref _selectedSectionIndex, normalized))
            {
                var name = SectionNames[normalized];
                if (_activeSection != name)
                {
                    _activeSection = name;
                    OnPropertyChanged(nameof(ActiveSection));
                }
            }
        }
    }

    public string ActiveSection
    {
        get => _activeSection;
        set
        {
            var target = string.IsNullOrWhiteSpace(value) ? SectionNames[0] : value;
            if (SetField(ref _activeSection, target))
            {
                var idx = Array.IndexOf(SectionNames, target);
                if (idx >= 0 && _selectedSectionIndex != idx)
                {
                    _selectedSectionIndex = idx;
                    OnPropertyChanged(nameof(SelectedSectionIndex));
                }
            }
        }
    }

    public string Login { get => _login; set => SetField(ref _login, value); }
    public string Password { get => _password; set => SetField(ref _password, value); }
    public string TwoFactorCode { get => _twoFactorCode; set => SetField(ref _twoFactorCode, value); }
    public string ApiBaseUrl { get => _apiBaseUrl; set => SetField(ref _apiBaseUrl, value); }

    public bool IsLoginPanelOpen { get => _isLoginPanelOpen; set => SetField(ref _isLoginPanelOpen, value); }
    public string AuthMessage { get => _authMessage; set => SetField(ref _authMessage, value); }
    public bool IsAuthError { get => _isAuthError; set => SetField(ref _isAuthError, value); }

    public DemoAccountInfo SelectedDemoAccount
    {
        get => _selectedDemoAccount;
        set
        {
            if (SetField(ref _selectedDemoAccount, value) && value != null)
            {
                Login = value.Login;
                TwoFactorCode = value.RequiresTwoFactor ? "246810" : string.Empty;
            }
        }
    }

    public string CurrentRole => _api.CurrentRole;
    public string CurrentRoleName => _api.CurrentRoleName;
    public string CurrentUserName => _api.CurrentUserName;
    public string CurrentLogin => _api.CurrentLogin;
    public string CurrentUserDisplay => $"{CurrentUserName} — {CurrentRoleName} ({CurrentRole})";
    public string ConnectionModeText => _api.IsOfflineMode
        ? $"Локальные контрольные данные (API {_api.ActiveBaseUrl} не подключён)"
        : $"Подключено к API: {_api.ActiveBaseUrl}";

    public string RoleCapabilitiesText =>
        DemoAccounts.FirstOrDefault(a => a.RoleCode == CurrentRole)?.PermissionsSummary
        ?? "Просмотр данных в пределах назначенной роли RBAC.";

    public bool CanCreateRequest => CurrentRole is "ProjectManager" or "ProcurementSpecialist" or "FinancialManager" or "CEO";
    public bool CanApproveOverrun => CurrentRole == "CEO";
    public bool CanApproveRequest => CurrentRole is "FinancialManager" or "CEO";
    public bool CanExecutePayment => CurrentRole == "Accountant";
    public bool CanManageBudgets => CurrentRole is "FinancialManager" or "CEO";
    public bool CanAdminister => CurrentRole == "Admin";
    public bool CanViewAudit => CurrentRole is "Admin" or "CEO" or "FinancialManager";
    public bool IsAdminRestrictedFromFinance => CurrentRole == "Admin";

    public int CalendarGapDaysCount => CalendarDays.Count(d => d.CashGap > 0m);
    public decimal CalendarMaxCashGap => CalendarDays.Count == 0 ? 0m : CalendarDays.Max(d => d.CashGap);

    public RequestViewModel RequestVm { get; }
    public BudgetViewModel BudgetVm { get; } = new();
    public RiskViewModel RiskVm { get; } = new();

    public ICommand NavigateCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand SwitchSelectedAccountCommand { get; }
    public ICommand ToggleLoginPanelCommand { get; }
    public ICommand RefreshAllCommand { get; }
    public RelayCommand CreateDraftBudgetVersionCommand { get; }
    public ICommand GenerateReportCommand { get; }
    public ICommand ExportReportCsvCommand { get; }

    public MainViewModel()
    {
        foreach (var acc in DemoSeedCatalog.Accounts)
            DemoAccounts.Add(acc);

        _selectedDemoAccount = DemoAccounts[0];

        RequestVm = new RequestViewModel(
            _api,
            role => string.Equals(CurrentRole, role, StringComparison.Ordinal),
            RecordAudit);

        NavigateCommand = new RelayCommand(p => ActiveSection = p?.ToString() ?? SectionNames[0]);
        ToggleLoginPanelCommand = new RelayCommand(_ => IsLoginPanelOpen = !IsLoginPanelOpen);
        LoginCommand = new RelayCommand(async _ => await PerformLoginAsync(Login, Password, TwoFactorCode));
        SwitchSelectedAccountCommand = new RelayCommand(async _ =>
        {
            if (SelectedDemoAccount != null)
            {
                var twoFa = SelectedDemoAccount.RequiresTwoFactor ? "246810" : null;
                Login = SelectedDemoAccount.Login;
                TwoFactorCode = twoFa ?? string.Empty;
                await PerformLoginAsync(SelectedDemoAccount.Login, Password, twoFa);
            }
        });
        RefreshAllCommand = new RelayCommand(async _ => await RequestVm.LoadAsync());
        CreateDraftBudgetVersionCommand = new RelayCommand(
            _ => CreateDraftBudgetVersion(),
            _ => CanManageBudgets);
        GenerateReportCommand = new RelayCommand(_ => BuildReportPreview());
        ExportReportCsvCommand = new RelayCommand(_ => ExportCurrentReportCsv());

        SeedWorkspaceCollections();
        BuildReportPreview();
    }

    private async Task PerformLoginAsync(string login, string password, string? twoFactor)
    {
        var previousRole = CurrentRole;
        var res = await _api.LoginAsync(ApiBaseUrl, login, password, twoFactor);
        IsAuthError = !res.Success;
        AuthMessage = res.Message;

        if (!res.Success)
        {
            RecordAudit("Auth", "LOGIN_FAILED", previousRole, login, res.Message);
            return;
        }

        var matched = DemoAccounts.FirstOrDefault(a => a.Login == _api.CurrentLogin);
        if (matched != null && _selectedDemoAccount != matched)
        {
            _selectedDemoAccount = matched;
            OnPropertyChanged(nameof(SelectedDemoAccount));
        }

        RecordAudit("Auth", "SWITCH_ACCOUNT", previousRole, _api.CurrentRole, $"{_api.CurrentLogin} ({_api.CurrentUserName})");
        NotifyRoleAndUserChanged();
        await RequestVm.LoadAsync();
    }

    private void NotifyRoleAndUserChanged()
    {
        OnPropertyChanged(nameof(CurrentRole));
        OnPropertyChanged(nameof(CurrentRoleName));
        OnPropertyChanged(nameof(CurrentUserName));
        OnPropertyChanged(nameof(CurrentLogin));
        OnPropertyChanged(nameof(CurrentUserDisplay));
        OnPropertyChanged(nameof(ConnectionModeText));
        OnPropertyChanged(nameof(RoleCapabilitiesText));
        OnPropertyChanged(nameof(CanCreateRequest));
        OnPropertyChanged(nameof(CanApproveOverrun));
        OnPropertyChanged(nameof(CanApproveRequest));
        OnPropertyChanged(nameof(CanExecutePayment));
        OnPropertyChanged(nameof(CanManageBudgets));
        OnPropertyChanged(nameof(CanAdminister));
        OnPropertyChanged(nameof(CanViewAudit));
        OnPropertyChanged(nameof(IsAdminRestrictedFromFinance));
        RequestVm.RaiseAllCanExecute();
        CreateDraftBudgetVersionCommand.RaiseCanExecuteChanged();
    }

    private void RecordAudit(string objectName, string action, string oldVal, string newVal, string details)
    {
        AuditLogs.Insert(0, new AuditRow(
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            CurrentLogin,
            objectName,
            action,
            oldVal,
            newVal,
            details));
    }

    private void CreateDraftBudgetVersion()
    {
        int nextVer = BudgetVersions.Where(v => v.ProjectCode == "PRJ-2026-01").Max(v => v.VersionNumber) + 1;
        BudgetVersions.Insert(0, new BudgetVersionRow(
            "PRJ-2026-01",
            nextVer,
            "Draft",
            "—",
            $"Черновик корректировки бюджета v{nextVer} (создал {CurrentUserName})"));
        RecordAudit("BudgetVersion", "CREATE_BUDGET_VERSION", "v1", $"v{nextVer}", "Корректировка бюджета PRJ-2026-01");
        RequestVm.StatusMessage = $"Создана новая черновая версия бюджета v{nextVer} (утверждённая v1 неизменяема по правилу 12).";
    }

    private void SeedWorkspaceCollections()
    {
        var domainRisks = DemoSeedCatalog.BuildChapter1Risks();
        var riskSummary = RiskCalculator.CalculatePortfolioSummary(domainRisks);
        RiskVm.TotalEmv = riskSummary.TotalEmv;
        RiskVm.Sigma = riskSummary.Sigma;
        RiskVm.ReserveP80 = riskSummary.ReserveP80;
        RiskVm.ResidualEmv = riskSummary.TotalResidualEmv;

        foreach (var r in domainRisks)
        {
            Risks.Add(new RiskRow(
                r.Code, r.Event, r.Probability, r.MonetaryImpact, r.IntegralScore,
                RiskCalculator.GetRiskZone(r.IntegralScore),
                Math.Round(r.Emv, 2),
                Math.Round(r.ResidualEmv ?? r.Emv, 2),
                r.OwnerRole, r.TriggerFired, r.IntegralScore >= 10 ? "Treating" : "Monitoring"));
        }

        foreach (var s in RiskCalculator.CalculateEacSensitivity())
        {
            SensitivityFactors.Add(new SensitivityRow(s.FactorName, s.RangeLabel, s.BaseAmount, s.ImpactOnEac, s.Comment));
        }

        var evm1 = EarnedValueCalculator.Calculate(11000m, 3400m, 3000m, 3300m, 14500m, Math.Round(riskSummary.ReserveP80, 0));
        var evm2 = EarnedValueCalculator.Calculate(10000m, 2100m, 2000m, 1950m, 8900m, 0m);
        ProjectForecasts.Add(new ProjectForecastRow(
            "PRJ-2026-01", "ЖК «Лошицкий квартал», дома № 4 и № 5",
            evm1.ContractValue, evm1.Bac, evm1.Pv, evm1.Ev, evm1.Ac, evm1.Cpi, evm1.Spi, evm1.Etc, evm1.Eac, evm1.FinancialResultWithReserve));
        ProjectForecasts.Add(new ProjectForecastRow(
            "PRJ-2026-02", "Детский сад на 230 мест в м-не Каменная Горка",
            evm2.ContractValue, evm2.Bac, evm2.Pv, evm2.Ev, evm2.Ac, evm2.Cpi, evm2.Spi, evm2.Etc, evm2.Eac, evm2.FinancialResultWithReserve));

        var lim1 = FinancialCalculator.CalculateFreeLimit(10000m, 6000m, 3000m, 1000m);
        var lim2 = FinancialCalculator.CalculateFreeLimit(1000m, 0m, 0m, 850m);
        var lim3 = FinancialCalculator.CalculateFreeLimit(10000m, 10500m, 7000m, 1500m);
        FreeLimits.Add(new FreeLimitRow("PRJ-2026-01", "Жилой дом № 4 (Obj1)", "ST-01", "Строительные материалы", lim1.ApprovedBudget, lim1.GrossObligations, lim1.PaidFact, lim1.SubmittedRequests, lim1.FreeLimit, lim1.ObligationRemainder, "В пределах лимита"));
        FreeLimits.Add(new FreeLimitRow("PRJ-2026-01", "Жилой дом № 4 (Obj1)", "ST-04", "Накладные расходы", lim2.ApprovedBudget, lim2.GrossObligations, lim2.PaidFact, lim2.SubmittedRequests, lim2.FreeLimit, lim2.ObligationRemainder, "В пределах лимита"));
        FreeLimits.Add(new FreeLimitRow("PRJ-2026-02", "Здание ДДУ (Obj3)", "ST-02", "Субподрядные работы", lim3.ApprovedBudget, lim3.GrossObligations, lim3.PaidFact, lim3.SubmittedRequests, lim3.FreeLimit, lim3.ObligationRemainder, "ПЕРЕРАСХОД (-500)"));

        BudgetVersions.Add(new BudgetVersionRow("PRJ-2026-01", 1, "Approved", "2026-01-10", "Утверждённый бюджет проекта (BAC = 11 000 тыс. р.)"));
        BudgetVersions.Add(new BudgetVersionRow("PRJ-2026-02", 1, "Approved", "2026-03-01", "Утверждённый бюджет ДДУ (ST-02 = 10 000 тыс. р.)"));

        var startDate = new DateTime(2026, 11, 10);
        var flows = new Dictionary<DateTime, (decimal, decimal)>
        {
            [startDate.AddDays(1)] = (0m, 300m),
            [startDate.AddDays(2)] = (1000m, 400m),
            [startDate.AddDays(3)] = (0m, 1800m),
            [startDate.AddDays(4)] = (600m, 200m),
            [startDate.AddDays(5)] = (0m, 100m),
            [startDate.AddDays(10)] = (2200m, 0m)
        };
        var notes = new Dictionary<int, string>
        {
            [0] = "Начальный остаток Cash₀ = 2 000 тыс. р.",
            [1] = "Поставка цемента (-300)",
            [2] = "Поступление от заказчика (+1 000) / арматура (-400)",
            [3] = "Субподряд монтажа (-1 800) → КАССОВЫЙ РАЗРЫВ!",
            [4] = "Аванс заказчика (+600) / бетон (-200) → РАЗРЫВ",
            [5] = "Прочие материалы (-100) → РАЗРЫВ",
            [10] = "Оплата заказчиком по акту С-2 № 11 (+2 200)"
        };

        foreach (var day in FinancialCalculator.CalculateCashCalendar(2000m, 1000m, startDate, 90, flows))
        {
            if (day.DayIndex <= 14 || day.CashGap > 0m || day.Inflows > 0m || day.Outflows > 0m)
            {
                CalendarDays.Add(new CalendarDayRow(
                    day.Date.ToString("yyyy-MM-dd"),
                    day.DayIndex,
                    day.OpeningBalance,
                    day.Inflows,
                    day.Outflows,
                    day.ClosingBalance,
                    day.CashGap,
                    notes.GetValueOrDefault(day.DayIndex, "Плановый день без движений")));
            }
        }

        Contracts.Add(new ContractRow("ГП-2026/01", "УКС Мингорисполкома", "100234567", "Доходный (Заказчик)", 14500m, 14500m, "BYN", 5m));
        Contracts.Add(new ContractRow("П-2026/14", "ОАО «Белорусский цементный завод»", "700012345", "Расходный (Поставщик)", 9500m, 10000m, "BYN", 0m));
        Contracts.Add(new ContractRow("СП-2026/09", "ООО «СпецМонтажСтрой»", "191234567", "Расходный (Субподряд)", 3500m, 3500m, "BYN", 5m));
        Contracts.Add(new ContractRow("СП-2026/22", "ОАО «СтройТрест № 7»", "100998877", "Расходный (Субподряд)", 12000m, 12000m, "BYN", 5m));

        Advances.Add(new AdvanceRow("П-2026/14", "Жилой дом № 4", 1200m, 900m, 300m, 75m, "2026-12-20", "Active"));
        Advances.Add(new AdvanceRow("СП-2026/09", "Жилой дом № 5", 1500m, 1000m, 500m, 66.7m, "2026-10-15", "Overdue"));

        NormRules.Add(new NormRuleRow("VAT_RATE", "Базовая ставка НДС", "Налоговый кодекс РБ", "20 %", 1, "Бухгалтер"));
        NormRules.Add(new NormRuleRow("RETENTION_DEFAULT", "Гарантийное удержание по акту", "Кодекс № 289-З", "5 % суммы акта", 1, "Финансовый менеджер"));
        NormRules.Add(new NormRuleRow("PENALTY_DAILY", "Пеня за просрочку обязательства", "Условия договора", "0,1 %/день, макс. 10 %", 1, "Финансовый менеджер"));
        NormRules.Add(new NormRuleRow("RESERVE_Z_P80", "Обеспеченность резерва P80", "СТБ ISO 31000-2020", "z = 0,84 (80 %)", 1, "Специалист по рискам"));
        NormRules.Add(new NormRuleRow("RISK_THRESHOLD", "Порог приемлемого риска", "Политика рисков", "R ≤ 9 (свыше — обработка)", 1, "Специалист по рискам"));
        NormRules.Add(new NormRuleRow("ADVANCE_EXPIRY", "Автоотклонение Blocked заявок", "Регламент UC04", "30 календарных дней", 1, "Администратор системы"));

        Notifications.Add(new NotificationRow("Critical", "ApprovalDue", "Перерасход по заявке ЗП-20261110-0002", "Свободный лимит по статье ST-02 исчерпан (Lдост = -500 тыс. р.). Требуется решение руководителя.", "2026-11-10 09:00"));
        Notifications.Add(new NotificationRow("Critical", "CashGap", "Прогноз кассового разрыва (день 3–5)", "На 13.11.2026 прогнозируется кассовый разрыв 500 тыс. р. по проекту ЖК «Лошицкий квартал».", "2026-11-10 09:00"));
        Notifications.Add(new NotificationRow("Warning", "RiskTrigger", "Сработали триггеры рисков R-04 и R-08", "Просрочен аванс по договору СП-2026/09 и оплата заказчика свыше 10 дней.", "2026-11-10 09:00"));

        AuditLogs.Add(new AuditRow("2026-11-10 09:00:00", "admin", "System", "SEED_DATABASE", "-", "Completed", "Загружены контрольные примеры главы 1 и реестр 12 рисков"));
    }

    private void BuildReportPreview()
    {
        ReportRows.Clear();
        switch (_selectedReportType)
        {
            case "budget-plan-fact":
                ReportCol1Header = "Проект";
                ReportCol2Header = "Объект / Статья";
                ReportCol3Header = "Бюджет B";
                ReportCol4Header = "Обязательства O";
                ReportCol5Header = "Свободный лимит Lдост";
                ReportCol6Header = "Статус";
                foreach (var fl in FreeLimits)
                {
                    ReportRows.Add(new ReportTableRow(
                        fl.ProjectCode, $"{fl.ObjectName} / {fl.CostItemCode}",
                        fl.ApprovedBudget.ToString("N2"), fl.GrossObligations.ToString("N2"),
                        fl.FreeLimit.ToString("N2"), fl.LimitStatus));
                }
                break;

            case "project-evm-forecast":
                ReportCol1Header = "Код";
                ReportCol2Header = "Проект";
                ReportCol3Header = "BAC / Цена V";
                ReportCol4Header = "CPI / SPI";
                ReportCol5Header = "Прогноз EAC";
                ReportCol6Header = "Фин. результат (с P80)";
                foreach (var p in ProjectForecasts)
                {
                    ReportRows.Add(new ReportTableRow(
                        p.Code, p.Name, $"{p.Bac:N0} / {p.ContractValue:N0}",
                        $"{p.Cpi:N3} / {p.Spi:N3}", p.Eac.ToString("N2"), p.FinancialResultWithReserve.ToString("N2")));
                }
                break;

            case "risks-reserve-p80":
                ReportCol1Header = "Код";
                ReportCol2Header = "Риск";
                ReportCol3Header = "p / Ущерб C";
                ReportCol4Header = "Интеграл R";
                ReportCol5Header = "EMV / Остат. EMV";
                ReportCol6Header = "Владелец";
                foreach (var r in Risks)
                {
                    ReportRows.Add(new ReportTableRow(
                        r.Code, r.Event, $"{r.Probability:P0} / {r.MonetaryImpact:N0}",
                        $"{r.IntegralScore} ({r.Zone})", $"{r.Emv:N2} / {r.ResidualEmv:N2}", r.OwnerRole));
                }
                break;

            case "contracts-advances":
                ReportCol1Header = "Договор";
                ReportCol2Header = "Контрагент / Объект";
                ReportCol3Header = "Сумма";
                ReportCol4Header = "Освоено / Эффект.";
                ReportCol5Header = "Остаток / Удерж.";
                ReportCol6Header = "Статус";
                foreach (var a in Advances)
                {
                    ReportRows.Add(new ReportTableRow(
                        a.ContractNumber, a.ObjectName, a.Amount.ToString("N2"),
                        $"{a.UsedAmount:N2} ({a.UtilizationPercent:N1}%)", a.Remainder.ToString("N2"), a.Status));
                }
                break;

            default:
                ReportCol1Header = "Номер";
                ReportCol2Header = "Объект / Статья";
                ReportCol3Header = "Тип";
                ReportCol4Header = "К оплате";
                ReportCol5Header = "План. дата";
                ReportCol6Header = "Статус";
                foreach (var r in RequestVm.AllRequests)
                {
                    ReportRows.Add(new ReportTableRow(
                        r.Number, $"{r.ObjectName} ({r.CostItemCode})", r.TypeDisplay,
                        $"{r.PayableAmount:N2} {r.Currency}", r.PlannedPaymentDate, r.StatusDisplay));
                }
                break;
        }
        ReportStatusMessage = $"Отчёт «{_selectedReportType}» сформирован: {ReportRows.Count} строк.";
    }

    private void ExportCurrentReportCsv()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV (разделитель — точка с запятой)|*.csv",
            FileName = $"report_{SelectedReportType}_{DateTime.Now:yyyyMMdd}.csv"
        };
        if (dlg.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', ReportCol1Header, ReportCol2Header, ReportCol3Header, ReportCol4Header, ReportCol5Header, ReportCol6Header));
        foreach (var row in ReportRows)
            sb.AppendLine(string.Join(';', row.Col1, row.Col2, row.Col3, row.Col4, row.Col5, row.Col6));

        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
        RecordAudit("Report", "EXPORT_CSV", SelectedReportType, dlg.FileName, $"{ReportRows.Count} строк");
        ReportStatusMessage = $"Отчёт сохранён: {dlg.FileName}";
    }
}
