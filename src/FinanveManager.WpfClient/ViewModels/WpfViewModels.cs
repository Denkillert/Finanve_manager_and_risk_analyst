using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.WpfClient.ViewModels;

public sealed class NavItemViewModel : ViewModelBase
{
    private bool _isSelected;
    public AppSection Section { get; }
    public string Title { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public NavItemViewModel(AppSection section, string title)
    {
        Section = section;
        Title = title;
    }
}

public sealed record LookupItem(Guid Id, string Display);
public sealed record ReportChoice(string Key, string Title);
public sealed record StatusFilterChoice(string? Key, string Title);
public sealed record RequestTypeChoice(RequestType Type, string Title);

public sealed record ForecastGridRow(
    string Code,
    string Name,
    decimal ContractValue,
    decimal Bac,
    decimal Pv,
    decimal Ev,
    decimal Ac,
    string CpiText,
    string SpiText,
    decimal Eac,
    decimal ReserveP80,
    decimal FinancialResult);

public sealed record NotificationGridRow(
    string SeverityText,
    string Title,
    string Message);

public sealed record CashDayGridRow(
    DateTime Date,
    int DayIndex,
    decimal OpeningBalance,
    decimal Inflows,
    decimal Outflows,
    decimal ClosingBalance,
    decimal CashGap)
{
    public bool HasCashGap => CashGap > 0m;
}

public sealed record FreeLimitGridRow(
    string ProjectCode,
    string ObjectName,
    string CostItemCode,
    string CostItemName,
    decimal ApprovedBudget,
    decimal GrossObligations,
    decimal PaidFact,
    decimal SubmittedRequests,
    decimal FreeLimit,
    decimal ObligationRemainder);

public sealed record BudgetVersionGridRow(
    Guid Id,
    string ProjectCode,
    int VersionNumber,
    BudgetVersionStatus Status,
    string StatusText,
    string Comment,
    byte[] RowVersion);

public sealed record BudgetLimitGridRow(
    Guid Id,
    int VersionNumber,
    BudgetVersionStatus VersionStatus,
    string VersionStatusText,
    string ObjectName,
    string CostItemCode,
    decimal Amount,
    byte[] RowVersion);

public sealed record StageGridRow(
    string ObjectName,
    string StageName,
    string PeriodText,
    decimal Pv,
    decimal Ev,
    decimal Ac);

public sealed record PaymentRequestGridRow(
    Guid Id,
    string Number,
    string TypeText,
    string ContractNumber,
    string CostItemCode,
    decimal PayableAmount,
    string Currency,
    DateTime PlannedDate,
    RequestStatus Status,
    string StatusText,
    string NoteText,
    byte[] RowVersion);

public sealed record ContractGridRow(
    string Number,
    string Counterparty,
    string Unp,
    string DirectionText,
    decimal EffectiveAmount,
    string Currency,
    decimal RetentionPercent,
    decimal GrossObligations,
    decimal PaidAmount,
    decimal RemainderAmount);

public sealed record AdvanceGridRow(
    Guid Id,
    string ContractNumber,
    string FundingSource,
    decimal Amount,
    decimal UsedAmount,
    decimal Remainder,
    decimal UtilizationPercent,
    DateTime DueDate,
    string StatusText);

public sealed record RiskGridRow(
    Guid Id,
    string Code,
    string Event,
    string OwnerRole,
    decimal Probability,
    decimal MonetaryImpact,
    int IntegralScore,
    string Zone,
    decimal Emv,
    decimal ResidualEmv,
    string StatusText,
    int Version,
    string EarlyIndicator,
    string Mitigation,
    byte[] RowVersion);

public sealed record RiskMatrixCellView(
    string HeaderText,
    string CodesText,
    string BackgroundHex,
    string ForegroundHex);

public sealed record ReportRowView(string Col1, string Col2, string Col3, string Col4, string Col5, string Col6);

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppDatabaseFactory _dbFactory;
    private readonly AuthenticatedSession _session;
    private readonly HashSet<AppSection> _allowedSections;

    public MainViewModel(AppDatabaseFactory dbFactory, AuthenticatedSession session)
    {
        _dbFactory = dbFactory;
        _session = session;

        var definitions = RolePermissions.GetSectionsForRole(session.RoleCode);
        _allowedSections = definitions.Select(d => d.Section).ToHashSet();
        foreach (var def in definitions)
            NavigationItems.Add(new NavItemViewModel(def.Section, def.Title));

        ReportChoices = session.RoleCode == "RiskSpecialist"
            ? new List<ReportChoice>
            {
                new("risks-reserve-p80", "1. Реестр рисков и резерв P80"),
                new("project-evm-forecast", "2. Прогноз стоимости проектов (EVM + P80)")
            }
            : new List<ReportChoice>
            {
                new("budget-plan-fact", "1. План-факт исполнения бюджета (vFreeLimit)"),
                new("project-evm-forecast", "2. Прогноз стоимости проектов (EVM + P80)"),
                new("risks-reserve-p80", "3. Реестр рисков и резерв P80"),
                new("contracts-advances", "4. Договоры и контроль авансов"),
                new("payment-requests-violations", "5. Реестр платёжных заявок и нарушений")
            };
        _selectedReportChoice = ReportChoices.FirstOrDefault();

        SelectSectionCommand = new RelayCommand<NavItemViewModel>(async item =>
        {
            if (item != null) await SelectSectionAsync(item.Section);
        });
        RefreshCommand = new RelayCommand(async () => await LoadAllAsync());

        // Budgets
        CreateBudgetVersionCommand = new RelayCommand(async () => await CreateBudgetVersionAsync());
        UpdateLimitCommand = new RelayCommand(async () => await UpdateSelectedLimitAsync());
        ApproveVersionCommand = new RelayCommand(async () => await ApproveSelectedVersionAsync());

        // Requests & Acts
        CreateRequestCommand = new RelayCommand(async () => await CreatePaymentRequestAsync());
        ApproveOverrunCommand = new RelayCommand(async () => await ApproveOverrunAsync(true));
        RejectOverrunCommand = new RelayCommand(async () => await ApproveOverrunAsync(false));
        ApproveRequestCommand = new RelayCommand(async () => await ApproveSelectedRequestAsync());
        ReturnRequestCommand = new RelayCommand(async () => await ReturnSelectedRequestAsync());
        ExecutePaymentCommand = new RelayCommand(async () => await ExecuteSelectedPaymentAsync());
        CreateActCommand = new RelayCommand(async () => await CreateActAsync());

        // Calendar
        RefreshCalendarCommand = new RelayCommand(async () => await RefreshCalendarAsync());
        RescheduleScheduleItemCommand = new RelayCommand(async () => await RescheduleSelectedItemAsync());

        // Contracts & Integrations
        CreateContractCommand = new RelayCommand(async () => await CreateContractAsync());
        OffsetAdvanceCommand = new RelayCommand(async () => await OffsetSelectedAdvanceAsync());
        ImportStatementCommand = new RelayCommand(async () => await ImportStatementAsync());
        ImportEstimateCommand = new RelayCommand(async () => await ImportEstimateAsync());

        // Risks
        RefreshRisksCommand = new RelayCommand(async () => await RefreshRisksAsync());
        UpdateRiskCommand = new RelayCommand(async () => await UpdateSelectedRiskAsync());
        CreateRiskCommand = new RelayCommand(async () => await CreateRiskAsync());

        // Reports
        LoadReportCommand = new RelayCommand(async () => await LoadReportAsync());

        // Admin
        CreateUserCommand = new RelayCommand(async () => await CreateUserAsync());
        ToggleUserActiveCommand = new RelayCommand(async () => await ToggleSelectedUserAsync());
        AddNormRuleCommand = new RelayCommand(async () => await AddNormRuleAsync());

        // Default landing section for role
        var first = NavigationItems.FirstOrDefault();
        if (first != null)
        {
            _selectedSection = first.Section;
            first.IsSelected = true;
            _selectedSectionTitle = first.Title;
        }
    }

    // Session & Role Info
    public string UserFullName => _session.FullName;
    public string UserLogin => _session.Login;
    public string RoleName => _session.RoleName;
    public string RoleCode => _session.RoleCode;
    public string DatabaseModeLabel => _dbFactory.ProviderLabel;

    // Role Permissions (UI Control Visibility)
    public bool IsCeoDashboard => RoleCode == "CEO";
    public bool CanManageBudgets => RoleCode is "FinancialManager" or "CEO";
    public bool CanApproveVersion => RoleCode == "CEO";
    public bool CanCreateRequest => RoleCode is "ProjectManager" or "ProcurementSpecialist" or "FinancialManager" or "CEO";
    public bool CanApproveOverrun => RoleCode == "CEO";
    public bool CanApproveRequest => RoleCode is "FinancialManager" or "CEO";
    public bool CanExecutePayment => RoleCode == "Accountant";
    public bool CanCreateAct => RoleCode is "SiteWorker" or "ProjectManager" or "Accountant" or "ProcurementSpecialist";
    public bool CanRescheduleCalendar => RoleCode is "FinancialManager" or "CEO";
    public bool CanCreateContract => RoleCode is "ProcurementSpecialist" or "FinancialManager";
    public bool CanOffsetAdvance => RoleCode is "Accountant" or "FinancialManager";
    public bool CanImportStatement => RoleCode == "Accountant";
    public bool CanImportEstimate => RoleCode is "FinancialManager" or "ProjectManager" or "ProcurementSpecialist";
    public bool CanEditRisks => RoleCode is "RiskSpecialist" or "ProjectManager" or "CEO";
    public bool IsAdminRole => RoleCode == "Admin";

    // Navigation State
    public ObservableCollection<NavItemViewModel> NavigationItems { get; } = new();

    private AppSection _selectedSection;
    public AppSection SelectedSection
    {
        get => _selectedSection;
        private set
        {
            if (SetField(ref _selectedSection, value))
            {
                OnPropertyChanged(nameof(IsDashboardSection));
                OnPropertyChanged(nameof(IsProjectsSection));
                OnPropertyChanged(nameof(IsRequestsSection));
                OnPropertyChanged(nameof(IsSiteActsSection));
                OnPropertyChanged(nameof(IsCalendarSection));
                OnPropertyChanged(nameof(IsContractsSection));
                OnPropertyChanged(nameof(IsRisksSection));
                OnPropertyChanged(nameof(IsReportsSection));
                OnPropertyChanged(nameof(IsAdminSection));
                OnPropertyChanged(nameof(IsAuditSection));
            }
        }
    }

    private string _selectedSectionTitle = string.Empty;
    public string SelectedSectionTitle
    {
        get => _selectedSectionTitle;
        private set => SetField(ref _selectedSectionTitle, value);
    }

    public bool IsDashboardSection => SelectedSection == AppSection.Dashboard && _allowedSections.Contains(AppSection.Dashboard);
    public bool IsProjectsSection => SelectedSection == AppSection.ProjectsAndBudgets && _allowedSections.Contains(AppSection.ProjectsAndBudgets);
    public bool IsRequestsSection => SelectedSection == AppSection.PaymentRequests && _allowedSections.Contains(AppSection.PaymentRequests);
    public bool IsSiteActsSection => SelectedSection == AppSection.SiteActs && _allowedSections.Contains(AppSection.SiteActs);
    public bool IsCalendarSection => SelectedSection == AppSection.PaymentCalendar && _allowedSections.Contains(AppSection.PaymentCalendar);
    public bool IsContractsSection => SelectedSection == AppSection.ContractsAndStatements && _allowedSections.Contains(AppSection.ContractsAndStatements);
    public bool IsRisksSection => SelectedSection == AppSection.RisksAndP80 && _allowedSections.Contains(AppSection.RisksAndP80);
    public bool IsReportsSection => SelectedSection == AppSection.Reports && _allowedSections.Contains(AppSection.Reports);
    public bool IsAdminSection => SelectedSection == AppSection.Administration && _allowedSections.Contains(AppSection.Administration);
    public bool IsAuditSection => SelectedSection == AppSection.AuditLog && _allowedSections.Contains(AppSection.AuditLog);

    private string _statusMessage = "Готово";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    private bool _isStatusError;
    public bool IsStatusError
    {
        get => _isStatusError;
        set => SetField(ref _isStatusError, value);
    }

    // Dashboard KPIs
    private string _kpiContractTotal = "0 тыс. р.";
    public string KpiContractTotal { get => _kpiContractTotal; set => SetField(ref _kpiContractTotal, value); }
    private string _kpiApprovedBudget = "0 тыс. р.";
    public string KpiApprovedBudget { get => _kpiApprovedBudget; set => SetField(ref _kpiApprovedBudget, value); }
    private string _kpiFreeLimit = "0 тыс. р.";
    public string KpiFreeLimit { get => _kpiFreeLimit; set => SetField(ref _kpiFreeLimit, value); }
    private string _kpiReserveP80 = "0 тыс. р.";
    public string KpiReserveP80 { get => _kpiReserveP80; set => SetField(ref _kpiReserveP80, value); }
    private string _kpiCashGap = "Нет";
    public string KpiCashGap { get => _kpiCashGap; set => SetField(ref _kpiCashGap, value); }

    // Lookups & Collections
    public ObservableCollection<LookupItem> ProjectLookups { get; } = new();
    public ObservableCollection<LookupItem> CalendarProjectLookups { get; } = new();
    public ObservableCollection<LookupItem> ObjectLookups { get; } = new();
    public ObservableCollection<LookupItem> OutgoingContractLookups { get; } = new();
    public ObservableCollection<LookupItem> CostItemLookups { get; } = new();
    public ObservableCollection<LookupItem> RoleLookups { get; } = new();

    public ObservableCollection<ForecastGridRow> Forecasts { get; } = new();
    public ObservableCollection<PaymentRequestGridRow> DashboardPendingRequests { get; } = new();
    public ObservableCollection<CashDayGridRow> DashboardCashDays { get; } = new();
    public ObservableCollection<NotificationGridRow> Notifications { get; } = new();
    public ObservableCollection<FreeLimitGridRow> FreeLimits { get; } = new();
    public ObservableCollection<BudgetVersionGridRow> BudgetVersions { get; } = new();
    public ObservableCollection<BudgetLimitGridRow> BudgetLimits { get; } = new();
    public ObservableCollection<StageGridRow> WorkStages { get; } = new();
    public ObservableCollection<PaymentRequestGridRow> PaymentRequests { get; } = new();
    public ObservableCollection<Document> Acts { get; } = new();
    public ObservableCollection<CashDayGridRow> CalendarDays { get; } = new();
    public ObservableCollection<ContractScheduleItem> ScheduleItems { get; } = new();
    public ObservableCollection<ContractGridRow> Contracts { get; } = new();
    public ObservableCollection<AdvanceGridRow> Advances { get; } = new();
    public ObservableCollection<RiskGridRow> Risks { get; } = new();
    public ObservableCollection<RiskMatrixCellView> RiskMatrix { get; } = new();
    public ObservableCollection<SensitivityFactorResult> Sensitivity { get; } = new();
    public ObservableCollection<User> Users { get; } = new();
    public ObservableCollection<NormRule> NormRules { get; } = new();
    public ObservableCollection<CurrencyRate> CurrencyRates { get; } = new();
    public ObservableCollection<AuditLog> AuditLogs { get; } = new();
    public ObservableCollection<ReportRowView> ReportRows { get; } = new();

    // Budgets Form Inputs
    private LookupItem? _budgetProject;
    public LookupItem? BudgetProject { get => _budgetProject; set => SetField(ref _budgetProject, value); }
    private string _budgetComment = "Плановая корректировка лимитов";
    public string BudgetComment { get => _budgetComment; set => SetField(ref _budgetComment, value); }
    private BudgetVersionGridRow? _selectedBudgetVersion;
    public BudgetVersionGridRow? SelectedBudgetVersion { get => _selectedBudgetVersion; set => SetField(ref _selectedBudgetVersion, value); }
    private BudgetLimitGridRow? _selectedBudgetLimit;
    public BudgetLimitGridRow? SelectedBudgetLimit
    {
        get => _selectedBudgetLimit;
        set
        {
            if (SetField(ref _selectedBudgetLimit, value) && value != null)
                LimitAmountInput = value.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
    private string _limitAmountInput = "0.00";
    public string LimitAmountInput { get => _limitAmountInput; set => SetField(ref _limitAmountInput, value); }

    // Requests & Acts Form Inputs
    public List<StatusFilterChoice> RequestStatusFilters { get; } = new()
    {
        new(null, "Все заявки"),
        new("Blocked", "Заблокирована"),
        new("PendingApproval", "На согласовании"),
        new("Approved", "Согласована"),
        new("Executed", "Исполнена"),
        new("Returned", "Возвращена"),
        new("Rejected", "Отклонена")
    };
    private StatusFilterChoice _selectedRequestStatusFilter = new(null, "Все заявки");
    public StatusFilterChoice SelectedRequestStatusFilter
    {
        get => _selectedRequestStatusFilter;
        set
        {
            if (SetField(ref _selectedRequestStatusFilter, value))
                _ = LoadRequestsAsync();
        }
    }

    private PaymentRequestGridRow? _selectedRequest;
    public PaymentRequestGridRow? SelectedRequest
    {
        get => _selectedRequest;
        set
        {
            if (SetField(ref _selectedRequest, value))
                _ = LoadSelectedRequestCardAsync(value);
        }
    }
    private string _selectedRequestCardSummary = "Выберите заявку в таблице выше для просмотра расчёта Lдост, Oост, документов-оснований и истории переходов.";
    public string SelectedRequestCardSummary { get => _selectedRequestCardSummary; set => SetField(ref _selectedRequestCardSummary, value); }
    private string _selectedRequestHistoryText = string.Empty;
    public string SelectedRequestHistoryText { get => _selectedRequestHistoryText; set => SetField(ref _selectedRequestHistoryText, value); }

    private string _actionReasonInput = "Согласовано";
    public string ActionReasonInput { get => _actionReasonInput; set => SetField(ref _actionReasonInput, value); }
    private string _paymentOrderInput = "ПП-205";
    public string PaymentOrderInput { get => _paymentOrderInput; set => SetField(ref _paymentOrderInput, value); }

    public List<RequestTypeChoice> RequestTypes { get; } = new()
    {
        new(RequestType.CurrentPayment, "Текущий платёж"),
        new(RequestType.Advance, "Новый аванс")
    };
    private RequestTypeChoice _newReqType = new(RequestType.CurrentPayment, "Текущий платёж");
    public RequestTypeChoice NewReqType { get => _newReqType; set => SetField(ref _newReqType, value); }
    private LookupItem? _newReqObject;
    public LookupItem? NewReqObject { get => _newReqObject; set => SetField(ref _newReqObject, value); }
    private LookupItem? _newReqContract;
    public LookupItem? NewReqContract { get => _newReqContract; set => SetField(ref _newReqContract, value); }
    private LookupItem? _newReqCostItem;
    public LookupItem? NewReqCostItem { get => _newReqCostItem; set => SetField(ref _newReqCostItem, value); }
    public List<string> Currencies { get; } = new() { "BYN", "USD", "EUR", "RUB" };
    private string _newReqCurrency = "BYN";
    public string NewReqCurrency { get => _newReqCurrency; set => SetField(ref _newReqCurrency, value); }
    private string _newReqAmount = "320.00";
    public string NewReqAmount { get => _newReqAmount; set => SetField(ref _newReqAmount, value); }
    private string _newReqDate = "2026-11-15";
    public string NewReqDate { get => _newReqDate; set => SetField(ref _newReqDate, value); }
    private string _newReqDocNo = "С-2/341";
    public string NewReqDocNo { get => _newReqDocNo; set => SetField(ref _newReqDocNo, value); }
    private string _newReqPurpose = "Оплата монолитных работ";
    public string NewReqPurpose { get => _newReqPurpose; set => SetField(ref _newReqPurpose, value); }

    // Site Act Inputs
    private LookupItem? _newActObject;
    public LookupItem? NewActObject { get => _newActObject; set => SetField(ref _newActObject, value); }
    private LookupItem? _newActContract;
    public LookupItem? NewActContract { get => _newActContract; set => SetField(ref _newActContract, value); }
    private string _newActNumber = "С-2/204";
    public string NewActNumber { get => _newActNumber; set => SetField(ref _newActNumber, value); }
    private string _newActGross = "350.00";
    public string NewActGross { get => _newActGross; set => SetField(ref _newActGross, value); }
    private string _newActOffset = "50.00";
    public string NewActOffset { get => _newActOffset; set => SetField(ref _newActOffset, value); }
    private string _newActRetention = "17.50";
    public string NewActRetention { get => _newActRetention; set => SetField(ref _newActRetention, value); }
    private string _newActDesc = "Монтаж ограждающих конструкций и устройство стяжки";
    public string NewActDesc { get => _newActDesc; set => SetField(ref _newActDesc, value); }

    // Calendar Inputs
    private LookupItem? _calendarProject;
    public LookupItem? CalendarProject { get => _calendarProject; set => SetField(ref _calendarProject, value); }
    public List<int> HorizonOptions { get; } = new() { 5, 30, 90 };
    private int _calendarHorizonDays = 90;
    public int CalendarHorizonDays { get => _calendarHorizonDays; set => SetField(ref _calendarHorizonDays, value); }
    private string _calendarSummaryText = string.Empty;
    public string CalendarSummaryText { get => _calendarSummaryText; set => SetField(ref _calendarSummaryText, value); }
    private ContractScheduleItem? _selectedScheduleItem;
    public ContractScheduleItem? SelectedScheduleItem
    {
        get => _selectedScheduleItem;
        set
        {
            if (SetField(ref _selectedScheduleItem, value) && value != null)
                RescheduleDateInput = value.PlannedDate.ToString("yyyy-MM-dd");
        }
    }
    private string _rescheduleDateInput = "2026-11-18";
    public string RescheduleDateInput { get => _rescheduleDateInput; set => SetField(ref _rescheduleDateInput, value); }

    // Contracts & Statements Inputs
    private LookupItem? _newContractProject;
    public LookupItem? NewContractProject { get => _newContractProject; set => SetField(ref _newContractProject, value); }
    private string _newContractNumber = "ПД-2026/19";
    public string NewContractNumber { get => _newContractNumber; set => SetField(ref _newContractNumber, value); }
    private string _newContractCounterparty = "ООО БелСтройПоставка";
    public string NewContractCounterparty { get => _newContractCounterparty; set => SetField(ref _newContractCounterparty, value); }
    private string _newContractUnp = "192345678";
    public string NewContractUnp { get => _newContractUnp; set => SetField(ref _newContractUnp, value); }
    private string _newContractAmount = "1500.00";
    public string NewContractAmount { get => _newContractAmount; set => SetField(ref _newContractAmount, value); }
    private string _newContractSpec = "Поставка арматуры и ЖБИ по графику";
    public string NewContractSpec { get => _newContractSpec; set => SetField(ref _newContractSpec, value); }

    private AdvanceGridRow? _selectedAdvance;
    public AdvanceGridRow? SelectedAdvance { get => _selectedAdvance; set => SetField(ref _selectedAdvance, value); }
    private string _advanceOffsetInput = "50.00";
    public string AdvanceOffsetInput { get => _advanceOffsetInput; set => SetField(ref _advanceOffsetInput, value); }
    private string _statementContent =
        "BankOperationId;Date;Amount;Currency;Counterparty;RequestNumber;Purpose;OrderNo\n" +
        "OP-2026-9001;2026-11-10;850.00;BYN;ОАО Белорусский цементный завод;ЗП-20261110-0003;Оплата по акту С-2 № 18;ПП-103";
    public string StatementContent { get => _statementContent; set => SetField(ref _statementContent, value); }
    private string _estimateContent =
        "Code;Name;Amount\n" +
        "ST-01;Строительные материалы, бетон и ЖБИ;6000.00\n" +
        "ST-02;Субподрядные монтажные работы;3500.00\n" +
        "ST-03;Инженерное оборудование;520.00";
    public string EstimateContent { get => _estimateContent; set => SetField(ref _estimateContent, value); }

    // Risks Inputs
    private LookupItem? _riskProject;
    public LookupItem? RiskProject { get => _riskProject; set => SetField(ref _riskProject, value); }
    private string _riskSummaryEmv = "0.00";
    public string RiskSummaryEmv { get => _riskSummaryEmv; set => SetField(ref _riskSummaryEmv, value); }
    private string _riskSummarySigma = "0.00";
    public string RiskSummarySigma { get => _riskSummarySigma; set => SetField(ref _riskSummarySigma, value); }
    private string _riskSummaryP80 = "0.00";
    public string RiskSummaryP80 { get => _riskSummaryP80; set => SetField(ref _riskSummaryP80, value); }
    private string _riskSummaryResidual = "0.00";
    public string RiskSummaryResidual { get => _riskSummaryResidual; set => SetField(ref _riskSummaryResidual, value); }

    private RiskGridRow? _selectedRisk;
    public RiskGridRow? SelectedRisk
    {
        get => _selectedRisk;
        set
        {
            if (SetField(ref _selectedRisk, value) && value != null)
            {
                EditRiskProb = value.Probability.ToString("0.00", CultureInfo.InvariantCulture);
                EditRiskImpact = value.MonetaryImpact.ToString("0", CultureInfo.InvariantCulture);
            }
        }
    }
    private string _editRiskProb = "0.35";
    public string EditRiskProb { get => _editRiskProb; set => SetField(ref _editRiskProb, value); }
    private string _editRiskImpact = "400";
    public string EditRiskImpact { get => _editRiskImpact; set => SetField(ref _editRiskImpact, value); }
    private string _newRiskCode = "R-13";
    public string NewRiskCode { get => _newRiskCode; set => SetField(ref _newRiskCode, value); }
    private string _newRiskEvent = "Задержка поставки электрощитового оборудования";
    public string NewRiskEvent { get => _newRiskEvent; set => SetField(ref _newRiskEvent, value); }
    private string _newRiskProb = "0.25";
    public string NewRiskProb { get => _newRiskProb; set => SetField(ref _newRiskProb, value); }
    private string _newRiskImpact = "280";
    public string NewRiskImpact { get => _newRiskImpact; set => SetField(ref _newRiskImpact, value); }

    // Reports
    public List<ReportChoice> ReportChoices { get; }
    private ReportChoice? _selectedReportChoice;
    public ReportChoice? SelectedReportChoice
    {
        get => _selectedReportChoice;
        set
        {
            if (SetField(ref _selectedReportChoice, value))
                _ = LoadReportAsync();
        }
    }
    private string _reportSubtitle = string.Empty;
    public string ReportSubtitle { get => _reportSubtitle; set => SetField(ref _reportSubtitle, value); }
    private string _rCol1 = "Колонка 1"; public string RCol1 { get => _rCol1; set => SetField(ref _rCol1, value); }
    private string _rCol2 = "Колонка 2"; public string RCol2 { get => _rCol2; set => SetField(ref _rCol2, value); }
    private string _rCol3 = "Колонка 3"; public string RCol3 { get => _rCol3; set => SetField(ref _rCol3, value); }
    private string _rCol4 = "Колонка 4"; public string RCol4 { get => _rCol4; set => SetField(ref _rCol4, value); }
    private string _rCol5 = "Колонка 5"; public string RCol5 { get => _rCol5; set => SetField(ref _rCol5, value); }
    private string _rCol6 = "Колонка 6"; public string RCol6 { get => _rCol6; set => SetField(ref _rCol6, value); }

    // Admin Inputs
    private User? _selectedUser;
    public User? SelectedUser { get => _selectedUser; set => SetField(ref _selectedUser, value); }
    private string _newUserLogin = "economist1";
    public string NewUserLogin { get => _newUserLogin; set => SetField(ref _newUserLogin, value); }
    private string _newUserFullName = "Иванов И.И.";
    public string NewUserFullName { get => _newUserFullName; set => SetField(ref _newUserFullName, value); }
    private string _newUserPassword = "StroyFin2026!";
    public string NewUserPassword { get => _newUserPassword; set => SetField(ref _newUserPassword, value); }
    private LookupItem? _newUserRole;
    public LookupItem? NewUserRole { get => _newUserRole; set => SetField(ref _newUserRole, value); }
    private string _newRuleCode = "NORM-05";
    public string NewRuleCode { get => _newRuleCode; set => SetField(ref _newRuleCode, value); }
    private string _newRuleName = "Макс. аванс субподрядчику";
    public string NewRuleName { get => _newRuleName; set => SetField(ref _newRuleName, value); }
    private string _newRuleValue = "До 50 % стоимости этапа";
    public string NewRuleValue { get => _newRuleValue; set => SetField(ref _newRuleValue, value); }

    // Commands
    public RelayCommand<NavItemViewModel> SelectSectionCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand CreateBudgetVersionCommand { get; }
    public RelayCommand UpdateLimitCommand { get; }
    public RelayCommand ApproveVersionCommand { get; }
    public RelayCommand CreateRequestCommand { get; }
    public RelayCommand ApproveOverrunCommand { get; }
    public RelayCommand RejectOverrunCommand { get; }
    public RelayCommand ApproveRequestCommand { get; }
    public RelayCommand ReturnRequestCommand { get; }
    public RelayCommand ExecutePaymentCommand { get; }
    public RelayCommand CreateActCommand { get; }
    public RelayCommand RefreshCalendarCommand { get; }
    public RelayCommand RescheduleScheduleItemCommand { get; }
    public RelayCommand CreateContractCommand { get; }
    public RelayCommand OffsetAdvanceCommand { get; }
    public RelayCommand ImportStatementCommand { get; }
    public RelayCommand ImportEstimateCommand { get; }
    public RelayCommand RefreshRisksCommand { get; }
    public RelayCommand UpdateRiskCommand { get; }
    public RelayCommand CreateRiskCommand { get; }
    public RelayCommand LoadReportCommand { get; }
    public RelayCommand CreateUserCommand { get; }
    public RelayCommand ToggleUserActiveCommand { get; }
    public RelayCommand AddNormRuleCommand { get; }

    public async Task SelectSectionAsync(AppSection section)
    {
        if (!_allowedSections.Contains(section)) return;
        foreach (var item in NavigationItems)
            item.IsSelected = item.Section == section;

        SelectedSectionTitle = NavigationItems.FirstOrDefault(i => i.Section == section)?.Title ?? string.Empty;
        SelectedSection = section;
        await LoadCurrentSectionAsync();
    }

    public async Task LoadAllAsync()
    {
        await RunSafeAsync(async () =>
        {
            await LoadLookupsAsync();
            await LoadCurrentSectionAsync();
            SetStatus($"Данные обновлены ({DatabaseModeLabel}).", false);
        });
    }

    private async Task LoadCurrentSectionAsync()
    {
        switch (SelectedSection)
        {
            case AppSection.Dashboard: await LoadDashboardAsync(); break;
            case AppSection.ProjectsAndBudgets: await LoadBudgetsAsync(); break;
            case AppSection.PaymentRequests: await LoadRequestsAsync(); break;
            case AppSection.SiteActs: await LoadSiteActsAsync(); break;
            case AppSection.PaymentCalendar: await RefreshCalendarAsync(); break;
            case AppSection.ContractsAndStatements: await LoadContractsAsync(); break;
            case AppSection.RisksAndP80: await RefreshRisksAsync(); break;
            case AppSection.Reports: await LoadReportAsync(); break;
            case AppSection.Administration: await LoadAdminAsync(); break;
            case AppSection.AuditLog: await LoadAuditLogsAsync(); break;
        }
    }

    private async Task LoadLookupsAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var projects = await scope.Db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync();
        var objects = await scope.Db.ConstructionObjects.Where(o => !o.IsDeleted).OrderBy(o => o.Name).ToListAsync();
        var contracts = await scope.Db.Contracts.Where(c => !c.IsDeleted && c.Direction == ContractDirection.SupplierSubcontractorOutgoing).OrderBy(c => c.Number).ToListAsync();
        var costItems = await scope.Db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync();

        var prevBudgetPrj = BudgetProject?.Id;
        var prevRiskPrj = RiskProject?.Id;
        var prevCalPrj = CalendarProject?.Id;

        ProjectLookups.Clear();
        CalendarProjectLookups.Clear();
        CalendarProjectLookups.Add(new LookupItem(Guid.Empty, "Все проекты (портфель)"));
        foreach (var p in projects)
        {
            var item = new LookupItem(p.Id, $"{p.Code} — {p.Name}");
            ProjectLookups.Add(item);
            CalendarProjectLookups.Add(item);
        }
        BudgetProject = ProjectLookups.FirstOrDefault(x => x.Id == prevBudgetPrj) ?? ProjectLookups.FirstOrDefault();
        RiskProject = ProjectLookups.FirstOrDefault(x => x.Id == prevRiskPrj) ?? ProjectLookups.FirstOrDefault();
        CalendarProject = CalendarProjectLookups.FirstOrDefault(x => x.Id == prevCalPrj) ?? CalendarProjectLookups.FirstOrDefault();
        NewContractProject ??= ProjectLookups.FirstOrDefault();

        ObjectLookups.Clear();
        foreach (var o in objects) ObjectLookups.Add(new LookupItem(o.Id, o.Name));
        NewReqObject ??= ObjectLookups.FirstOrDefault();
        NewActObject ??= ObjectLookups.FirstOrDefault();

        OutgoingContractLookups.Clear();
        foreach (var c in contracts) OutgoingContractLookups.Add(new LookupItem(c.Id, $"{c.Number} — {c.Counterparty}"));
        NewReqContract ??= OutgoingContractLookups.FirstOrDefault();
        NewActContract ??= OutgoingContractLookups.FirstOrDefault();

        CostItemLookups.Clear();
        foreach (var ci in costItems) CostItemLookups.Add(new LookupItem(ci.Id, $"{ci.Code} — {ci.Name}"));
        NewReqCostItem ??= CostItemLookups.FirstOrDefault();

        if (IsAdminRole)
        {
            var roles = await scope.Db.Roles.Where(r => !r.IsDeleted).OrderBy(r => r.Code).ToListAsync();
            RoleLookups.Clear();
            foreach (var r in roles) RoleLookups.Add(new LookupItem(r.Id, $"{r.Name} ({r.Code})"));
            NewUserRole ??= RoleLookups.FirstOrDefault();
        }
    }

    private async Task LoadDashboardAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var forecasts = await BuildForecastRowsAsync(scope);
        var limits = await BuildFreeLimitRowsAsync(scope);
        var calDays = await ComputeCalendarAsync(scope, null, 90);
        var notes = await scope.Db.Notifications.OrderBy(n => n.IsRead).ThenByDescending(n => n.CreatedAt).Take(12).ToListAsync();
        var pendingReqs = await scope.Db.PaymentRequests
            .Include(r => r.Contract)
            .Include(r => r.CostItem)
            .Where(r => !r.IsDeleted && (r.Status == RequestStatus.Blocked
                                         || r.Status == RequestStatus.PendingApproval
                                         || r.Status == RequestStatus.ApprovedByManagement
                                         || r.Status == RequestStatus.Approved))
            .OrderByDescending(r => r.CreatedAt)
            .Take(8)
            .ToListAsync();

        Forecasts.Clear();
        foreach (var f in forecasts) Forecasts.Add(f);

        DashboardPendingRequests.Clear();
        foreach (var r in pendingReqs)
        {
            DashboardPendingRequests.Add(new PaymentRequestGridRow(
                r.Id, r.Number, RussianLabels.RequestTypeName(r.Type),
                r.Contract?.Number ?? "—", r.CostItem?.Code ?? "—",
                r.PayableAmount, r.Currency, r.PlannedPaymentDate, r.Status,
                RussianLabels.RequestStatusName(r.Status),
                r.BlockReason ?? r.Purpose ?? "—", r.RowVersion));
        }

        DashboardCashDays.Clear();
        foreach (var d in calDays.Where(x => x.DayIndex <= 5 || x.Inflows > 0 || x.Outflows > 0 || x.CashGap > 0).Take(12))
        {
            DashboardCashDays.Add(new CashDayGridRow(d.Date, d.DayIndex, d.OpeningBalance, d.Inflows, d.Outflows, d.ClosingBalance, d.CashGap));
        }

        Notifications.Clear();
        foreach (var n in notes)
            Notifications.Add(new NotificationGridRow(RussianLabels.SeverityName(n.Severity), n.Title, n.Message));

        KpiContractTotal = $"{forecasts.Sum(f => f.ContractValue):N0} тыс. р.";
        KpiApprovedBudget = $"{limits.Sum(l => l.ApprovedBudget):N0} тыс. р.";
        KpiFreeLimit = $"{limits.Sum(l => l.FreeLimit):N2} тыс. р.";
        KpiReserveP80 = $"{forecasts.Sum(f => f.ReserveP80):N2} тыс. р.";
        int gaps = calDays.Count(d => d.CashGap > 0);
        decimal maxGap = calDays.Count == 0 ? 0m : calDays.Max(d => d.CashGap);
        KpiCashGap = gaps > 0 ? $"{maxGap:N0} тыс. р. ({gaps} дн.)" : "Нет";
    }

    private async Task LoadBudgetsAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var limits = await BuildFreeLimitRowsAsync(scope);
        FreeLimits.Clear();
        foreach (var l in limits) FreeLimits.Add(l);

        var projects = await scope.Db.Projects.Where(p => !p.IsDeleted).ToDictionaryAsync(p => p.Id);
        var objects = await scope.Db.ConstructionObjects.Where(o => !o.IsDeleted).ToDictionaryAsync(o => o.Id);
        var items = await scope.Db.CostItems.Where(c => !c.IsDeleted).ToDictionaryAsync(c => c.Id);

        var versions = await scope.Db.BudgetVersions.Where(v => !v.IsDeleted)
            .OrderBy(v => v.ProjectId).ThenByDescending(v => v.VersionNumber).ToListAsync();
        BudgetVersions.Clear();
        var vMap = new Dictionary<Guid, BudgetVersion>();
        foreach (var v in versions)
        {
            vMap[v.Id] = v;
            projects.TryGetValue(v.ProjectId, out var p);
            BudgetVersions.Add(new BudgetVersionGridRow(
                v.Id, p?.Code ?? "—", v.VersionNumber, v.Status,
                RussianLabels.BudgetVersionStatusName(v.Status), v.Comment ?? "—", v.RowVersion));
        }

        var bLimits = await scope.Db.BudgetLimits.Where(l => !l.IsDeleted).ToListAsync();
        BudgetLimits.Clear();
        foreach (var l in bLimits)
        {
            vMap.TryGetValue(l.BudgetVersionId, out var bv);
            objects.TryGetValue(l.ConstructionObjectId, out var obj);
            items.TryGetValue(l.CostItemId, out var ci);
            var vs = bv?.Status ?? BudgetVersionStatus.Draft;
            BudgetLimits.Add(new BudgetLimitGridRow(
                l.Id, bv?.VersionNumber ?? 0, vs, RussianLabels.BudgetVersionStatusName(vs),
                obj?.Name ?? "—", ci?.Code ?? "—", l.Amount, l.RowVersion));
        }
    }

    private async Task LoadRequestsAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var query = scope.Db.PaymentRequests.Include(r => r.Contract).Include(r => r.CostItem).Where(r => !r.IsDeleted);
        if (!string.IsNullOrEmpty(SelectedRequestStatusFilter.Key) && Enum.TryParse<RequestStatus>(SelectedRequestStatusFilter.Key, out var st))
            query = query.Where(r => r.Status == st);

        var reqs = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
        PaymentRequests.Clear();
        foreach (var r in reqs)
        {
            PaymentRequests.Add(new PaymentRequestGridRow(
                r.Id, r.Number, RussianLabels.RequestTypeName(r.Type), r.Contract?.Number ?? "—", r.CostItem?.Code ?? "—",
                r.PayableAmount, r.Currency, r.PlannedPaymentDate, r.Status, RussianLabels.RequestStatusName(r.Status),
                r.BlockReason ?? r.ReturnOrRejectReason ?? r.EdsSignatureThumbprint ?? r.Purpose ?? "—",
                r.RowVersion));
        }

        if (SelectedRequest == null && PaymentRequests.Count > 0)
            SelectedRequest = PaymentRequests[0];
    }

    private async Task LoadSelectedRequestCardAsync(PaymentRequestGridRow? row)
    {
        if (row == null)
        {
            SelectedRequestCardSummary = "Выберите заявку в таблице выше для просмотра расчёта Lдост, Oост, документов-оснований и истории переходов.";
            SelectedRequestHistoryText = string.Empty;
            return;
        }

        try
        {
            using var scope = _dbFactory.CreateScope();
            var req = await scope.Db.PaymentRequests
                .Include(r => r.Contract)
                .Include(r => r.CostItem)
                .FirstOrDefaultAsync(r => r.Id == row.Id && !r.IsDeleted);
            if (req == null) return;

            var docs = await scope.Db.Documents.Where(d => !d.IsDeleted && d.PaymentRequestId == req.Id).ToListAsync();
            var free = await scope.Budget.CalculateFreeLimitAsync(req.ConstructionObjectId, req.CostItemId);
            string docDesc = docs.Count > 0
                ? string.Join(", ", docs.Select(d => $"{d.Type} №{d.Number} от {d.Date:dd.MM.yyyy}"))
                : "Нет прикреплённого документа";

            SelectedRequestCardSummary =
                $"Заявка {req.Number} ({RussianLabels.RequestStatusName(req.Status)}) · Договор: {req.Contract?.Number ?? "—"} ({req.Contract?.Counterparty ?? "—"})\n" +
                $"Документ-основание: {docDesc} | Статья: {req.CostItem?.Code} — {req.CostItem?.Name}\n" +
                $"К оплате: {req.PayableAmount:N2} {req.Currency} (в BYN: {req.AmountInByn:N2}, НДС: {req.VatAmount:N2}) | Акт: {req.ActAmount:N2}, зачёт аванса: {req.AdvanceOffset:N2}, удержание: {req.RetentionAmount:N2}\n" +
                $"Бюджетный контроль по объекту и статье: B = {free.ApprovedBudget:N2}, O = {free.GrossObligations:N2}, F = {free.PaidFact:N2} → Lдост = {free.FreeLimit:N2} тыс. р., Oост = {free.ObligationRemainder:N2} тыс. р." +
                (req.HasOverdueAdvanceWarning ? " | ВНИМАНИЕ: по договору имеется просроченный аванс!" : string.Empty);

            var stages = new List<string> { $"Создана: {req.CreatedAt:dd.MM HH:mm}" };
            if (req.SubmittedAt.HasValue) stages.Add($"На проверке/согласовании: {req.SubmittedAt.Value:dd.MM HH:mm}");
            if (req.BlockedAt.HasValue) stages.Add($"Заблокирована: {req.BlockedAt.Value:dd.MM HH:mm}");
            if (req.IsOverrunApproved) stages.Add($"Перерасход одобрен (UC04): {req.OverrunJustification}");
            if (req.ApprovedAt.HasValue) stages.Add($"Согласована{(req.EdsSignatureThumbprint != null ? $" (ЭЦП {req.EdsSignatureThumbprint})" : "")}: {req.ApprovedAt.Value:dd.MM HH:mm}");
            if (req.ExecutedAt.HasValue) stages.Add($"Исполнена: {req.ExecutedAt.Value:dd.MM HH:mm}");
            if (req.ClosedAt.HasValue) stages.Add($"Закрыта по выписке: {req.ClosedAt.Value:dd.MM HH:mm}");
            SelectedRequestHistoryText = "История переходов: " + string.Join("  →  ", stages);
        }
        catch
        {
            SelectedRequestCardSummary = $"Заявка {row.Number} ({row.StatusText})";
        }
    }

    private async Task LoadSiteActsAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var objects = await scope.Db.ConstructionObjects.Where(o => !o.IsDeleted).ToDictionaryAsync(o => o.Id);
        var stages = await scope.Db.WorkStages.Where(s => !s.IsDeleted).OrderBy(s => s.PlannedStartDate).ToListAsync();
        WorkStages.Clear();
        foreach (var s in stages)
        {
            objects.TryGetValue(s.ConstructionObjectId, out var o);
            WorkStages.Add(new StageGridRow(
                o?.Name ?? "—", s.Name, $"{s.PlannedStartDate:dd.MM.yyyy} — {s.PlannedEndDate:dd.MM.yyyy}",
                s.PlannedValuePV, s.EarnedValueEV, s.ActualCostAC));
        }

        var acts = await scope.Db.Documents
            .Where(d => !d.IsDeleted && (d.Type == DocumentType.ActS2 || d.Type == DocumentType.CertificateS3))
            .OrderByDescending(d => d.Date)
            .ToListAsync();
        Acts.Clear();
        foreach (var a in acts) Acts.Add(a);
    }

    private async Task RefreshCalendarAsync()
    {
        using var scope = _dbFactory.CreateScope();
        Guid? filterId = CalendarProject.HasValueFilter() ? CalendarProject!.Id : null;
        var days = await ComputeCalendarAsync(scope, filterId, CalendarHorizonDays);
        CalendarDays.Clear();
        foreach (var d in days.Where(x => x.DayIndex <= 10 || x.Inflows > 0 || x.Outflows > 0 || x.CashGap > 0))
            CalendarDays.Add(new CashDayGridRow(d.Date, d.DayIndex, d.OpeningBalance, d.Inflows, d.Outflows, d.ClosingBalance, d.CashGap));

        var contracts = await scope.Db.Contracts
            .Where(c => !c.IsDeleted && (!filterId.HasValue || c.ProjectId == filterId.Value))
            .Select(c => c.Id).ToHashSetAsync();

        var items = await scope.Db.ContractScheduleItems
            .Where(s => !s.IsDeleted && !s.IsPaid && contracts.Contains(s.ContractId))
            .OrderBy(s => s.PlannedDate).ToListAsync();
        ScheduleItems.Clear();
        foreach (var s in items) ScheduleItems.Add(s);

        int gapDays = days.Count(d => d.CashGap > 0);
        decimal maxGap = days.Count == 0 ? 0m : days.Max(d => d.CashGap);
        CalendarSummaryText = $"Горизонт: {CalendarHorizonDays} дн. | Дней с дефицитом: {gapDays} | Макс. кассовый разрыв: {maxGap:N2} тыс. р.";
    }

    private async Task LoadContractsAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var contracts = await scope.Db.Contracts.Include(c => c.Obligations).Where(c => !c.IsDeleted).OrderBy(c => c.Number).ToListAsync();
        Contracts.Clear();
        foreach (var c in contracts)
        {
            decimal gross = c.Obligations.Where(o => !o.IsDeleted).Sum(o => o.GrossAmount);
            decimal paid = c.Obligations.Where(o => !o.IsDeleted).Sum(o => o.PaidAmount);
            Contracts.Add(new ContractGridRow(
                c.Number, c.Counterparty, c.CounterpartyUnp, RussianLabels.ContractDirectionName(c.Direction),
                c.EffectiveTotalAmount, c.Currency, c.RetentionPercent,
                gross, paid, c.EffectiveTotalAmount - gross));
        }

        var advs = await scope.Db.Advances.Include(a => a.Contract).Where(a => !a.IsDeleted).OrderBy(a => a.DueDate).ToListAsync();
        var refDate = new DateTime(2026, 11, 10);
        Advances.Clear();
        foreach (var a in advs)
        {
            Advances.Add(new AdvanceGridRow(
                a.Id, a.Contract?.Number ?? "—", a.FundingSource, a.Amount, a.UsedAmount, a.Remainder,
                a.UtilizationPercent ?? 0m, a.DueDate, RussianLabels.AdvanceStatusName(a.Status, a.IsOverdueAt(refDate))));
        }
    }

    private async Task RefreshRisksAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var prjId = RiskProject?.Id ?? SeedIds.Project1;
        var risks = await scope.Db.Risks.Where(r => !r.IsDeleted && r.ProjectId == prjId)
            .OrderByDescending(r => r.Emv).ThenByDescending(r => r.IntegralScore).ToListAsync();

        Risks.Clear();
        foreach (var r in risks)
        {
            Risks.Add(new RiskGridRow(
                r.Id, r.Code, r.Event, r.OwnerRole, r.Probability, r.MonetaryImpact,
                r.IntegralScore, RiskCalculator.GetRiskZone(r.IntegralScore), r.Emv, r.ResidualEmv ?? r.Emv,
                RussianLabels.RiskStatusName(r.Status), r.CurrentAssessmentVersion, r.EarlyIndicator, r.Mitigation, r.RowVersion));
        }

        RiskMatrix.Clear();
        foreach (var cell in RiskCalculator.BuildRiskMatrix(risks))
        {
            var (bg, fg) = cell.IntegralScore >= 17 ? ("#FEE2E2", "#991B1B")
                : cell.IntegralScore >= 10 ? ("#FFEDD5", "#9A3412")
                : cell.IntegralScore >= 5 ? ("#FEF9C3", "#854D0E")
                : ("#ECFDF5", "#065F46");
            RiskMatrix.Add(new RiskMatrixCellView($"p={cell.ProbabilityScore}, I={cell.ImpactScore} (R={cell.IntegralScore})", cell.RiskCodes, bg, fg));
        }

        var summary = await scope.Risks.CalculateReserveP80Async(prjId);
        RiskSummaryEmv = $"{summary.TotalEmv:N2} тыс. р.";
        RiskSummarySigma = $"{summary.Sigma:N2} тыс. р.";
        RiskSummaryP80 = $"{summary.ReserveP80:N2} тыс. р.";
        RiskSummaryResidual = $"{summary.TotalResidualEmv:N2} тыс. р. (−{summary.EmvReduction:N2})";

        Sensitivity.Clear();
        foreach (var s in RiskCalculator.CalculateEacSensitivity()) Sensitivity.Add(s);
    }

    private async Task LoadReportAsync()
    {
        if (SelectedReportChoice == null) return;
        using var scope = _dbFactory.CreateScope();
        ReportRows.Clear();

        switch (SelectedReportChoice.Key)
        {
            case "project-evm-forecast":
                RCol1 = "Код"; RCol2 = "Проект"; RCol3 = "V / BAC"; RCol4 = "PV / EV / AC"; RCol5 = "CPI / SPI"; RCol6 = "EAC / Фин. рез.";
                var forecasts = await BuildForecastRowsAsync(scope);
                ReportSubtitle = "Прогноз стоимости по EVM и резерву P80";
                foreach (var f in forecasts)
                    ReportRows.Add(new ReportRowView(f.Code, f.Name, $"{f.ContractValue:N0} / {f.Bac:N0}", $"{f.Pv:N0} / {f.Ev:N0} / {f.Ac:N0}", $"{f.CpiText} / {f.SpiText}", $"{f.Eac:N2} / {f.FinancialResult:N2}"));
                break;

            case "risks-reserve-p80":
                RCol1 = "Код"; RCol2 = "Событие"; RCol3 = "p / Ущерб C"; RCol4 = "Интеграл R"; RCol5 = "EMV / Остат."; RCol6 = "Владелец / Статус";
                var risks = await scope.Db.Risks.Where(r => !r.IsDeleted).OrderByDescending(r => r.Emv).ToListAsync();
                var summary = RiskCalculator.CalculatePortfolioSummary(risks);
                ReportSubtitle = $"ΣEMV = {summary.TotalEmv:N2}, σ = {summary.Sigma:N2}, P80 = {summary.ReserveP80:N2} тыс. р.";
                foreach (var r in risks)
                    ReportRows.Add(new ReportRowView(r.Code, r.Event, $"{r.Probability:P0} / {r.MonetaryImpact:N0}", $"{r.IntegralScore} ({RiskCalculator.GetRiskZone(r.IntegralScore)})", $"{r.Emv:N2} / {(r.ResidualEmv ?? r.Emv):N2}", $"{r.OwnerRole} ({RussianLabels.RiskStatusName(r.Status)})"));
                break;

            case "contracts-advances":
                RCol1 = "Договор"; RCol2 = "Контрагент"; RCol3 = "Аванс A"; RCol4 = "Зачтено Aисп"; RCol5 = "Aост (Kисп %)"; RCol6 = "Срок / Статус";
                var contracts = await scope.Db.Contracts.Where(c => !c.IsDeleted).ToDictionaryAsync(c => c.Id);
                var advs = await scope.Db.Advances.Where(a => !a.IsDeleted).OrderBy(a => a.DueDate).ToListAsync();
                ReportSubtitle = "Контроль авансов Aост = A − Aисп";
                foreach (var a in advs)
                {
                    contracts.TryGetValue(a.ContractId, out var c);
                    ReportRows.Add(new ReportRowView(c?.Number ?? "—", c?.Counterparty ?? "—", a.Amount.ToString("N2"), a.UsedAmount.ToString("N2"), $"{a.Remainder:N2} ({(a.UtilizationPercent ?? 0m):N1}%)", $"{a.DueDate:dd.MM.yyyy} / {RussianLabels.AdvanceStatusName(a.Status)}"));
                }
                break;

            case "payment-requests-violations":
                RCol1 = "Номер"; RCol2 = "Тип"; RCol3 = "К оплате"; RCol4 = "Дата"; RCol5 = "Статус"; RCol6 = "Контроль / ЭЦП";
                var reqs = await scope.Db.PaymentRequests.Where(r => !r.IsDeleted).OrderByDescending(r => r.CreatedAt).ToListAsync();
                ReportSubtitle = "Реестр платёжных заявок и блокировок";
                foreach (var r in reqs)
                    ReportRows.Add(new ReportRowView(r.Number, RussianLabels.RequestTypeName(r.Type), $"{r.PayableAmount:N2} {r.Currency}", r.PlannedPaymentDate.ToString("dd.MM.yyyy"), RussianLabels.RequestStatusName(r.Status), r.BlockReason ?? r.EdsSignatureThumbprint ?? r.Purpose ?? "—"));
                break;

            default:
                RCol1 = "Проект"; RCol2 = "Объект / Статья"; RCol3 = "Бюджет B"; RCol4 = "Обязат. O"; RCol5 = "Оплач. F / Q"; RCol6 = "Lдост / Oост";
                var limits = await BuildFreeLimitRowsAsync(scope);
                ReportSubtitle = "План-факт исполнения бюджета (vFreeLimit)";
                foreach (var l in limits)
                    ReportRows.Add(new ReportRowView(l.ProjectCode, $"{l.ObjectName} ({l.CostItemCode})", l.ApprovedBudget.ToString("N2"), l.GrossObligations.ToString("N2"), $"{l.PaidFact:N2} / {l.SubmittedRequests:N2}", $"{l.FreeLimit:N2} / {l.ObligationRemainder:N2}"));
                break;
        }
    }

    private async Task LoadAdminAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var users = await scope.Db.Users.Include(u => u.Role).Where(u => !u.IsDeleted).OrderBy(u => u.Login).ToListAsync();
        Users.Clear();
        foreach (var u in users) Users.Add(u);

        var rules = await scope.Db.NormRules.Where(n => !n.IsDeleted).OrderBy(n => n.Code).ToListAsync();
        NormRules.Clear();
        foreach (var n in rules) NormRules.Add(n);

        var rates = await scope.Db.CurrencyRates.Where(c => !c.IsDeleted).OrderBy(c => c.Currency).ToListAsync();
        CurrencyRates.Clear();
        foreach (var c in rates) CurrencyRates.Add(c);
    }

    private async Task LoadAuditLogsAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var logs = await scope.Db.AuditLogs.OrderByDescending(a => a.Timestamp).Take(200).ToListAsync();
        AuditLogs.Clear();
        foreach (var a in logs) AuditLogs.Add(a);
    }

    // Write Operations (Role-Gated)
    private async Task CreateBudgetVersionAsync()
    {
        if (!CanManageBudgets || BudgetProject == null) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var bv = await scope.Budget.CreateNewVersionAsync(BudgetProject.Id, BudgetComment, _session.UserId, _session.Login);
            await LoadBudgetsAsync();
            SetStatus($"Создана черновая версия v{bv.VersionNumber}.", false);
        });
    }

    private async Task UpdateSelectedLimitAsync()
    {
        if (!CanManageBudgets || SelectedBudgetLimit == null) return;
        await RunSafeAsync(async () =>
        {
            decimal amount = ParseDecimal(LimitAmountInput, "лимит бюджета");
            using var scope = _dbFactory.CreateScope();
            var limit = await scope.Db.BudgetLimits.FirstOrDefaultAsync(l => l.Id == SelectedBudgetLimit.Id && !l.IsDeleted)
                ?? throw new DomainException("Лимит не найден.");
            var bv = await scope.Db.BudgetVersions.FirstOrDefaultAsync(v => v.Id == limit.BudgetVersionId)
                ?? throw new DomainException("Версия не найдена.");

            decimal old = limit.Amount;
            limit.UpdateAmount(amount, bv.Status);
            await scope.Audit.LogAsync(_session.UserId, _session.Login, "BudgetLimit", limit.Id.ToString(), "UPDATE_LIMIT", old.ToString("N2"), amount.ToString("N2"));
            await scope.Db.SaveChangesAsync();
            await LoadBudgetsAsync();
            SetStatus($"Лимит обновлён: {amount:N2} тыс. р.", false);
        });
    }

    private async Task ApproveSelectedVersionAsync()
    {
        if (!CanApproveVersion || SelectedBudgetVersion == null) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var bv = await scope.Budget.ApproveVersionAsync(SelectedBudgetVersion.Id, SelectedBudgetVersion.RowVersion, _session.UserId, _session.Login);
            await LoadBudgetsAsync();
            SetStatus($"Версия v{bv.VersionNumber} утверждена.", false);
        });
    }

    private async Task CreatePaymentRequestAsync()
    {
        if (!CanCreateRequest || NewReqObject == null || NewReqContract == null || NewReqCostItem == null) return;
        await RunSafeAsync(async () =>
        {
            decimal amt = ParseDecimal(NewReqAmount, "сумма заявки");
            var dt = ParseDate(NewReqDate, new DateTime(2026, 11, 15));
            using var scope = _dbFactory.CreateScope();
            var obj = await scope.Db.ConstructionObjects.FirstAsync(o => o.Id == NewReqObject.Id);
            var dto = new CreatePaymentRequestDto(
                obj.ProjectId, obj.Id, NewReqContract.Id, NewReqCostItem.Id,
                amt, amt, 0m, 0m, true, NewReqCurrency, NewReqType.Type, dt,
                NewReqPurpose, NewReqType.Type == RequestType.Advance ? DocumentType.Contract : DocumentType.ActS2,
                NewReqDocNo, new DateTime(2026, 11, 10), null, $"WPF-{Guid.NewGuid():N}", true);
            var (req, checks) = await scope.Requests.CreateAsync(dto, _session.UserId, _session.Login);
            await LoadRequestsAsync();
            SetStatus($"Заявка {req.Number} ({RussianLabels.RequestStatusName(req.Status)}): " + string.Join(" | ", checks.Messages), req.Status == RequestStatus.Blocked);
        });
    }

    private async Task ApproveOverrunAsync(bool approve)
    {
        if (!CanApproveOverrun || SelectedRequest == null) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var req = await scope.Requests.ApproveOverrunAsync(SelectedRequest.Id, approve, ActionReasonInput, SelectedRequest.RowVersion, _session.UserId, _session.Login);
            await LoadRequestsAsync();
            SetStatus($"По заявке {req.Number} установлено решение: {RussianLabels.RequestStatusName(req.Status)}.", false);
        });
    }

    private async Task ApproveSelectedRequestAsync()
    {
        if (!CanApproveRequest || SelectedRequest == null) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var req = await scope.Requests.ApproveAsync(SelectedRequest.Id, "EDS-2026-WPF", SelectedRequest.RowVersion, _session.UserId, _session.Login);
            await LoadRequestsAsync();
            SetStatus($"Заявка {req.Number} согласована (ЭЦП: {req.EdsSignatureThumbprint}).", false);
        });
    }

    private async Task ReturnSelectedRequestAsync()
    {
        if (!CanApproveRequest || SelectedRequest == null) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var req = await scope.Requests.ReturnOrRejectAsync(SelectedRequest.Id, false, ActionReasonInput, SelectedRequest.RowVersion, _session.UserId, _session.Login);
            await LoadRequestsAsync();
            SetStatus($"Заявка {req.Number} возвращена на доработку.", false);
        });
    }

    private async Task ExecuteSelectedPaymentAsync()
    {
        if (!CanExecutePayment || SelectedRequest == null) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var pay = await scope.Requests.ExecuteAsync(SelectedRequest.Id, PaymentOrderInput, SelectedRequest.RowVersion, _session.UserId, _session.Login);
            await LoadRequestsAsync();
            SetStatus($"Платёж проведён: №{pay.PaymentOrderNumber}, {pay.Amount:N2} {pay.Currency}.", false);
        });
    }

    private async Task CreateActAsync()
    {
        if (!CanCreateAct || NewActContract == null || NewActObject == null) return;
        await RunSafeAsync(async () =>
        {
            decimal gross = ParseDecimal(NewActGross, "сумма акта");
            decimal offset = ParseDecimal(NewActOffset, "зачёт аванса");
            decimal retention = ParseDecimal(NewActRetention, "удержание");
            decimal payable = FinancialCalculator.CalculateActPayable(gross, offset, retention).PayableAmount;

            using var scope = _dbFactory.CreateScope();
            var doc = new Document
            {
                ContractId = NewActContract.Id,
                ConstructionObjectId = NewActObject.Id,
                Type = DocumentType.ActS2,
                Number = string.IsNullOrWhiteSpace(NewActNumber) ? $"С-2/{DateTime.UtcNow:HHmm}" : NewActNumber.Trim(),
                Date = new DateTime(2026, 11, 10),
                GrossAmount = gross,
                AdvanceOffset = offset,
                RetentionAmount = retention,
                NetAmount = payable,
                VatIncluded = true,
                Currency = "BYN",
                ActWorkDescription = NewActDesc,
                IsVerifiedByInstruction53 = true
            };
            await scope.Db.Documents.AddAsync(doc);
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(Document), doc.Number, "CREATE_ACT_S2", null, $"Payable={payable:N2}", NewActDesc);
            await scope.Db.SaveChangesAsync();
            await LoadSiteActsAsync();
            SetStatus($"Акт {doc.Number} оформлен: к оплате = {payable:N2} тыс. р.", false);
        });
    }

    private async Task RescheduleSelectedItemAsync()
    {
        if (!CanRescheduleCalendar || SelectedScheduleItem == null) return;
        await RunSafeAsync(async () =>
        {
            var newDate = ParseDate(RescheduleDateInput, SelectedScheduleItem.PlannedDate);
            using var scope = _dbFactory.CreateScope();
            var item = await scope.Db.ContractScheduleItems.FirstOrDefaultAsync(s => s.Id == SelectedScheduleItem.Id && !s.IsDeleted)
                ?? throw new DomainException("Позиция графика не найдена.");
            string oldDate = item.PlannedDate.ToString("yyyy-MM-dd");
            item.PlannedDate = newDate.Date;
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(ContractScheduleItem), item.Id.ToString(), "RESCHEDULE_PAYMENT", oldDate, newDate.ToString("yyyy-MM-dd"));
            await scope.Db.SaveChangesAsync();
            await RefreshCalendarAsync();
            SetStatus($"Платёж «{item.Description}» перенесён на {newDate:dd.MM.yyyy}.", false);
        });
    }

    private async Task CreateContractAsync()
    {
        if (!CanCreateContract || NewContractProject == null) return;
        await RunSafeAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewContractNumber) || string.IsNullOrWhiteSpace(NewContractCounterparty))
                throw new DomainException("Укажите номер договора и контрагента.");
            decimal amt = ParseDecimal(NewContractAmount, "сумма договора");
            if (amt <= 0)
                throw new DomainException("Сумма договора должна быть положительной.");

            using var scope = _dbFactory.CreateScope();
            var contract = new Contract
            {
                ProjectId = NewContractProject.Id,
                Number = NewContractNumber.Trim(),
                Counterparty = NewContractCounterparty.Trim(),
                CounterpartyUnp = string.IsNullOrWhiteSpace(NewContractUnp) ? "190000001" : NewContractUnp.Trim(),
                Direction = ContractDirection.SupplierSubcontractorOutgoing,
                TotalAmount = amt,
                Currency = "BYN",
                StartDate = new DateTime(2026, 11, 10),
                EndDate = new DateTime(2027, 11, 10),
                SpecificationNotes = NewContractSpec.Trim()
            };
            await scope.Db.Contracts.AddAsync(contract);
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(Contract), contract.Number, "CREATE_CONTRACT", null, $"{amt:N2} BYN", contract.Counterparty);
            await scope.Db.SaveChangesAsync();
            await LoadLookupsAsync();
            await LoadContractsAsync();
            SetStatus($"Договор {contract.Number} зарегистрирован.", false);
        });
    }

    private async Task OffsetSelectedAdvanceAsync()
    {
        if (!CanOffsetAdvance || SelectedAdvance == null) return;
        await RunSafeAsync(async () =>
        {
            decimal offset = ParseDecimal(AdvanceOffsetInput, "сумма зачёта");
            using var scope = _dbFactory.CreateScope();
            var adv = await scope.Db.Advances.FirstOrDefaultAsync(a => a.Id == SelectedAdvance.Id && !a.IsDeleted)
                ?? throw new DomainException("Аванс не найден.");
            decimal prev = adv.UsedAmount;
            adv.ApplyOffset(offset);
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(Advance), adv.Id.ToString(), "OFFSET_ADVANCE", prev.ToString("N2"), adv.UsedAmount.ToString("N2"));
            await scope.Db.SaveChangesAsync();
            await LoadContractsAsync();
            SetStatus($"Аванс зачтён. Остаток Aост = {adv.Remainder:N2} тыс. р.", false);
        });
    }

    private async Task ImportStatementAsync()
    {
        if (!CanImportStatement) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var report = await scope.StatementImport.ImportAsync(StatementContent, "statement.csv", _session.UserId, _session.Login);
            SetStatus($"Выписка: обработано {report.TotalOperations}, сопоставлено {report.MatchedCount}, дублей {report.DuplicateSkippedCount}.", false);
        });
    }

    private async Task ImportEstimateAsync()
    {
        if (!CanImportEstimate) return;
        await RunSafeAsync(async () =>
        {
            using var scope = _dbFactory.CreateScope();
            var catalog = await scope.Db.CostItems.Where(c => !c.IsDeleted).ToListAsync();
            var res = scope.EstimateImporter.ImportFromExcelOrXml(EstimateContent, "smeta.csv", catalog);
            SetStatus($"Смета: сопоставлено {res.MatchedLines}/{res.TotalLines} строк ({res.MatchPercent:N1} %).", false);
        });
    }

    private async Task UpdateSelectedRiskAsync()
    {
        if (!CanEditRisks || SelectedRisk == null) return;
        await RunSafeAsync(async () =>
        {
            decimal prob = ParseDecimal(EditRiskProb, "вероятность");
            decimal impact = ParseDecimal(EditRiskImpact, "ущерб");
            using var scope = _dbFactory.CreateScope();
            var risk = await scope.Risks.UpdateAssessmentAsync(
                SelectedRisk.Id, prob, impact, null, "Переоценка в WPF", SelectedRisk.RowVersion, _session.UserId, _session.Login);
            await RefreshRisksAsync();
            SetStatus($"Риск {risk.Code} переоценён: R={risk.IntegralScore}, EMV={risk.Emv:N2} тыс. р.", false);
        });
    }

    private async Task CreateRiskAsync()
    {
        if (!CanEditRisks || RiskProject == null) return;
        await RunSafeAsync(async () =>
        {
            decimal prob = Math.Clamp(ParseDecimal(NewRiskProb, "вероятность"), 0.01m, 0.99m);
            decimal impact = Math.Max(0m, ParseDecimal(NewRiskImpact, "ущерб"));
            using var scope = _dbFactory.CreateScope();
            var risk = new Risk
            {
                ProjectId = RiskProject.Id,
                Code = string.IsNullOrWhiteSpace(NewRiskCode) ? $"R-{DateTime.UtcNow:HHmm}" : NewRiskCode.Trim().ToUpperInvariant(),
                Event = NewRiskEvent.Trim(),
                Cause = "Мониторинг проекта",
                Consequence = "Отклонение стоимости",
                Probability = prob,
                MonetaryImpact = impact,
                ResidualProbability = Math.Round(prob * 0.45m, 2),
                OwnerRole = "PM",
                Status = RiskStatus.Analyzed
            };
            RiskCalculator.RecalculateScores(risk);
            await scope.Db.Risks.AddAsync(risk);
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(Risk), risk.Code, "CREATE_RISK", null, $"EMV={risk.Emv:N2}");
            await scope.Db.SaveChangesAsync();
            await RefreshRisksAsync();
            SetStatus($"Добавлен риск {risk.Code} (EMV={risk.Emv:N2} тыс. р.).", false);
        });
    }

    private async Task CreateUserAsync()
    {
        if (!IsAdminRole || NewUserRole == null) return;
        await RunSafeAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewUserLogin) || NewUserPassword.Length < 12)
                throw new DomainException("Логин обязателен, пароль — минимум 12 символов (НФТ-7).");

            string login = NewUserLogin.Trim().ToLowerInvariant();
            using var scope = _dbFactory.CreateScope();
            if (await scope.Db.Users.AnyAsync(u => u.Login == login))
                throw new DomainException($"Пользователь «{login}» уже существует.");

            var user = new User
            {
                RoleId = NewUserRole.Id,
                Login = login,
                FullName = string.IsNullOrWhiteSpace(NewUserFullName) ? login : NewUserFullName.Trim(),
                Email = $"{login}@stroyfinrisk.by",
                PasswordHash = PasswordHasher.HashPassword(NewUserPassword),
                IsActive = true,
                PersonalDataConsentGiven = true,
                ConsentDate = DateTime.UtcNow
            };
            await scope.Db.Users.AddAsync(user);
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(User), login, "CREATE_USER", null, user.FullName);
            await scope.Db.SaveChangesAsync();
            await LoadAdminAsync();
            SetStatus($"Создан пользователь {login}.", false);
        });
    }

    private async Task ToggleSelectedUserAsync()
    {
        if (!IsAdminRole || SelectedUser == null) return;
        await RunSafeAsync(async () =>
        {
            if (SelectedUser.Login == "admin")
                throw new DomainException("Учётную запись admin нельзя отключить.");
            using var scope = _dbFactory.CreateScope();
            var u = await scope.Db.Users.FirstAsync(x => x.Id == SelectedUser.Id);
            u.IsActive = !u.IsActive;
            u.LockedUntil = null;
            u.FailedLoginCount = 0;
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(User), u.Login, "TOGGLE_USER_ACTIVE", null, u.IsActive.ToString());
            await scope.Db.SaveChangesAsync();
            await LoadAdminAsync();
            SetStatus($"Пользователь {u.Login}: IsActive = {u.IsActive}.", false);
        });
    }

    private async Task AddNormRuleAsync()
    {
        if (!IsAdminRole) return;
        await RunSafeAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(NewRuleCode) || string.IsNullOrWhiteSpace(NewRuleName))
                throw new DomainException("Укажите код и наименование правила.");
            using var scope = _dbFactory.CreateScope();
            var rule = new NormRule
            {
                Code = NewRuleCode.Trim().ToUpperInvariant(),
                Name = NewRuleName.Trim(),
                DocumentSource = "Внутренний стандарт",
                ValueText = NewRuleValue.Trim(),
                ValidFrom = new DateTime(2026, 11, 10),
                ResponsibleRole = "Финансовый менеджер",
                Revision = 1
            };
            await scope.Db.NormRules.AddAsync(rule);
            await scope.Audit.LogAsync(_session.UserId, _session.Login, nameof(NormRule), rule.Code, "ADD_NORM_RULE", null, rule.ValueText);
            await scope.Db.SaveChangesAsync();
            await LoadAdminAsync();
            SetStatus($"Правило {rule.Code} добавлено.", false);
        });
    }

    public byte[] BuildCurrentReportCsvBytes()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', new[] { RCol1, RCol2, RCol3, RCol4, RCol5, RCol6 }.Select(Esc)));
        foreach (var r in ReportRows)
            sb.AppendLine(string.Join(';', new[] { r.Col1, r.Col2, r.Col3, r.Col4, r.Col5, r.Col6 }.Select(Esc)));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

        static string Esc(string? s) => $"\"{(s ?? string.Empty).Replace("\"", "\"\"")}\"";
    }

    public async Task<byte[]> Build1CExportCsvBytesAsync()
    {
        using var scope = _dbFactory.CreateScope();
        var payments = await scope.Db.Payments.Where(p => !p.IsDeleted).ToListAsync();
        var csv = scope.Accounting.ExportEntriesCsv(payments);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
    }

    // Internal Builders
    private static async Task<List<ForecastGridRow>> BuildForecastRowsAsync(DataScope scope)
    {
        var projects = await scope.Db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync();
        var objects = await scope.Db.ConstructionObjects.Where(o => !o.IsDeleted).ToListAsync();
        var stages = await scope.Db.WorkStages.Where(s => !s.IsDeleted).ToListAsync();
        var versions = await scope.Db.BudgetVersions.Where(v => !v.IsDeleted && v.Status == BudgetVersionStatus.Approved).ToListAsync();
        var limits = await scope.Db.BudgetLimits.Where(l => !l.IsDeleted).ToListAsync();
        var risks = await scope.Db.Risks.Where(r => !r.IsDeleted).ToListAsync();

        var list = new List<ForecastGridRow>();
        foreach (var p in projects)
        {
            var objIds = objects.Where(o => o.ProjectId == p.Id).Select(o => o.Id).ToHashSet();
            var prjStages = stages.Where(s => objIds.Contains(s.ConstructionObjectId)).ToList();
            decimal pv = prjStages.Sum(s => s.PlannedValuePV);
            decimal ev = prjStages.Sum(s => s.EarnedValueEV);
            decimal ac = prjStages.Sum(s => s.ActualCostAC);

            var bv = versions.Where(v => v.ProjectId == p.Id).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            decimal bac = bv == null ? 0m : limits.Where(l => l.BudgetVersionId == bv.Id).Sum(l => l.Amount);
            var riskSummary = RiskCalculator.CalculatePortfolioSummary(risks.Where(r => r.ProjectId == p.Id));
            var fc = EarnedValueCalculator.Calculate(bac, pv, ev, ac, p.ContractValue, Math.Round(riskSummary.ReserveP80, 0, MidpointRounding.AwayFromZero));

            list.Add(new ForecastGridRow(
                p.Code, p.Name, fc.ContractValue, fc.Bac, fc.Pv, fc.Ev, fc.Ac,
                fc.Cpi?.ToString("N3") ?? "—", fc.Spi?.ToString("N3") ?? "—",
                fc.Eac, riskSummary.ReserveP80, fc.FinancialResultWithReserve));
        }
        return list;
    }

    private static async Task<List<FreeLimitGridRow>> BuildFreeLimitRowsAsync(DataScope scope)
    {
        var projects = await scope.Db.Projects.Where(p => !p.IsDeleted).ToDictionaryAsync(p => p.Id);
        var objects = await scope.Db.ConstructionObjects.Where(o => !o.IsDeleted).ToListAsync();
        var costItems = await scope.Db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync();
        var limits = await scope.Db.BudgetLimits.Where(l => !l.IsDeleted).ToListAsync();
        var obligations = await scope.Db.Obligations.Where(o => !o.IsDeleted).ToListAsync();

        var rows = new List<FreeLimitGridRow>();
        foreach (var obj in objects)
        {
            if (!projects.TryGetValue(obj.ProjectId, out var prj)) continue;
            foreach (var ci in costItems)
            {
                if (!limits.Any(l => l.ConstructionObjectId == obj.Id && l.CostItemId == ci.Id)
                    && !obligations.Any(o => o.ConstructionObjectId == obj.Id && o.CostItemId == ci.Id))
                    continue;

                var r = await scope.Budget.CalculateFreeLimitAsync(obj.Id, ci.Id);
                rows.Add(new FreeLimitGridRow(prj.Code, obj.Name, ci.Code, ci.Name, r.ApprovedBudget, r.GrossObligations, r.PaidFact, r.SubmittedRequests, r.FreeLimit, r.ObligationRemainder));
            }
        }
        return rows;
    }

    private static async Task<List<CashDayForecast>> ComputeCalendarAsync(DataScope scope, Guid? projectId, int horizonDays)
    {
        var startDate = new DateTime(2026, 11, 10);
        var projects = await scope.Db.Projects.Where(p => !p.IsDeleted).ToListAsync();
        var sel = projectId.HasValue ? projects.Where(p => p.Id == projectId.Value).ToList() : projects;
        var selIds = sel.Select(p => p.Id).ToHashSet();

        var contractIds = await scope.Db.Contracts
            .Where(c => !c.IsDeleted && selIds.Contains(c.ProjectId))
            .Select(c => c.Id).ToHashSetAsync();

        var schedule = await scope.Db.ContractScheduleItems
            .Where(s => !s.IsDeleted && !s.IsPaid && contractIds.Contains(s.ContractId)).ToListAsync();
        var reqs = await scope.Db.PaymentRequests
            .Where(r => !r.IsDeleted && selIds.Contains(r.ProjectId)
                        && (r.Status == RequestStatus.PendingApproval || r.Status == RequestStatus.ApprovedByManagement || r.Status == RequestStatus.Approved))
            .ToListAsync();

        var flows = new Dictionary<DateTime, (decimal Inflows, decimal Outflows)>();
        foreach (var item in schedule.Where(s => s.PlannedDate.Date > startDate.Date))
        {
            var d = item.PlannedDate.Date;
            flows.TryGetValue(d, out var cur);
            flows[d] = item.IsIncoming ? (cur.Inflows + item.Amount, cur.Outflows) : (cur.Inflows, cur.Outflows + item.Amount);
        }
        foreach (var req in reqs.Where(r => r.PlannedPaymentDate.Date > startDate.AddDays(5)))
        {
            var d = req.PlannedPaymentDate.Date;
            flows.TryGetValue(d, out var cur);
            flows[d] = (cur.Inflows, cur.Outflows + req.AmountInByn);
        }

        return FinancialCalculator.CalculateCashCalendar(
            sel.Sum(p => p.InitialCash), sel.Sum(p => p.MinCashBalance), startDate, Math.Clamp(horizonDays, 5, 180), flows);
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void SetStatus(string msg, bool error)
    {
        StatusMessage = msg;
        IsStatusError = error;
    }

    private static decimal ParseDecimal(string text, string fieldName)
    {
        var norm = (text ?? string.Empty).Trim().Replace(',', '.');
        if (!decimal.TryParse(norm, NumberStyles.Number, CultureInfo.InvariantCulture, out var val))
            throw new DomainException($"Некорректное числовое значение в поле «{fieldName}».");
        return val;
    }

    private static DateTime ParseDate(string text, DateTime fallback) =>
        DateTime.TryParse((text ?? string.Empty).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : fallback;
}

internal static class LookupItemExtensions
{
    public static bool HasValueFilter(this LookupItem? item) => item != null && item.Id != Guid.Empty;
}
