using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Services;

namespace FinanveManager.Web.Models;

public record DemoAccountOption(string Login, string FullName, string RoleCode, string RoleName, string ClientType, bool Requires2Fa);

public class LoginViewModel
{
    public string Login { get; set; } = "finmanager";
    public string Password { get; set; } = "StroyFin2026!";
    public string? TwoFactorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresTwoFactor { get; set; }
    public List<DemoAccountOption> DemoUsers { get; set; } = new();
}

public class ProjectForecastItem
{
    public Project Project { get; set; } = null!;
    public EarnedValueForecastResult Forecast { get; set; } = null!;
    public RiskPortfolioSummary RiskSummary { get; set; } = null!;
}

public class FreeLimitRowItem
{
    public string ProjectCode { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public Guid ConstructionObjectId { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public Guid CostItemId { get; set; }
    public string CostItemCode { get; set; } = string.Empty;
    public string CostItemName { get; set; } = string.Empty;
    public FreeLimitResult Result { get; set; } = null!;
}

public class DashboardViewModel
{
    public int ProjectCount { get; set; }
    public decimal TotalContractValue { get; set; }
    public decimal TotalApprovedBudget { get; set; }
    public decimal TotalGrossObligations { get; set; }
    public decimal TotalFreeLimit { get; set; }
    public decimal TotalReserveP80 { get; set; }
    public int GapDaysCount90d { get; set; }
    public decimal MaxCashGap90d { get; set; }
    public string DatabaseProvider { get; set; } = "InMemory";

    public List<ProjectForecastItem> Forecasts { get; set; } = new();
    public List<PaymentRequest> PendingRequests { get; set; } = new();
    public List<Notification> Notifications { get; set; } = new();
}

public class ProjectsBudgetsViewModel
{
    public List<ProjectForecastItem> Projects { get; set; } = new();
    public List<ConstructionObject> Objects { get; set; } = new();
    public List<WorkStage> Stages { get; set; } = new();
    public List<CostItem> CostItems { get; set; } = new();
    public List<FreeLimitRowItem> FreeLimits { get; set; } = new();
    public List<BudgetVersion> BudgetVersions { get; set; } = new();
    public List<BudgetLimit> BudgetLimits { get; set; } = new();
}

public class PaymentRequestsViewModel
{
    public string? StatusFilter { get; set; }
    public List<PaymentRequest> Requests { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    public List<ConstructionObject> Objects { get; set; } = new();
    public List<Contract> Contracts { get; set; } = new();
    public List<CostItem> CostItems { get; set; } = new();
    public List<Document> Acts { get; set; } = new();
}

public class CalendarViewModel
{
    public Guid? SelectedProjectId { get; set; }
    public List<Project> Projects { get; set; } = new();
    public decimal InitialCash { get; set; }
    public decimal MinCashBalance { get; set; }
    public int GapDaysCount { get; set; }
    public decimal MaxCashGap { get; set; }
    public List<CashDayForecast> Days { get; set; } = new();
    public List<ContractScheduleItem> ScheduleItems { get; set; } = new();
    public List<ContractScheduleItem> OverdueCustomerItems { get; set; } = new();
}

public class ContractsViewModel
{
    public List<Contract> Contracts { get; set; } = new();
    public List<Project> Projects { get; set; } = new();
    public List<ConstructionObject> Objects { get; set; } = new();
    public List<CostItem> CostItems { get; set; } = new();
    public List<Advance> Advances { get; set; } = new();
    public string? ImportReportSummary { get; set; }
    public string? EstimateImportSummary { get; set; }
}

public class RisksViewModel
{
    public Guid SelectedProjectId { get; set; }
    public List<Project> Projects { get; set; } = new();
    public List<Risk> Risks { get; set; } = new();
    public RiskPortfolioSummary Summary { get; set; } = null!;
    public List<SensitivityFactorResult> Sensitivity { get; set; } = new();
}

public class ReportGridModel
{
    public string ReportType { get; set; } = "budget-plan-fact";
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}

public class AdminViewModel
{
    public List<Role> Roles { get; set; } = new();
    public List<FinanveManager.Domain.Entities.User> Users { get; set; } = new();
    public List<NormRule> NormRules { get; set; } = new();
    public List<CurrencyRate> CurrencyRates { get; set; } = new();
    public List<AuditLog> AuditLogs { get; set; } = new();
}
