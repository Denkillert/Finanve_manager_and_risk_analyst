using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Services;

public static class WebPortfolioHelper
{
    public static async Task<List<ProjectForecastItem>> BuildForecastsAsync(
        AppDbContext db, CancellationToken ct = default)
    {
        var projects = await db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync(ct);
        var objects = await db.ConstructionObjects.Where(o => !o.IsDeleted).ToListAsync(ct);
        var stages = await db.WorkStages.Where(s => !s.IsDeleted).ToListAsync(ct);
        var versions = await db.BudgetVersions.Where(v => !v.IsDeleted && v.Status == BudgetVersionStatus.Approved).ToListAsync(ct);
        var limits = await db.BudgetLimits.Where(l => !l.IsDeleted).ToListAsync(ct);
        var risks = await db.Risks.Where(r => !r.IsDeleted).ToListAsync(ct);

        var result = new List<ProjectForecastItem>();
        foreach (var project in projects)
        {
            var objIds = objects.Where(o => o.ProjectId == project.Id).Select(o => o.Id).ToHashSet();
            var prjStages = stages.Where(s => objIds.Contains(s.ConstructionObjectId)).ToList();

            decimal pv = prjStages.Sum(s => s.PlannedValuePV);
            decimal ev = prjStages.Sum(s => s.EarnedValueEV);
            decimal ac = prjStages.Sum(s => s.ActualCostAC);

            var approvedBv = versions
                .Where(v => v.ProjectId == project.Id)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();

            decimal bac = approvedBv == null
                ? 0m
                : limits.Where(l => l.BudgetVersionId == approvedBv.Id).Sum(l => l.Amount);

            var riskSummary = RiskCalculator.CalculatePortfolioSummary(risks.Where(r => r.ProjectId == project.Id));
            var forecast = EarnedValueCalculator.Calculate(
                bac, pv, ev, ac, project.ContractValue, Math.Round(riskSummary.ReserveP80, 0, MidpointRounding.AwayFromZero));

            result.Add(new ProjectForecastItem
            {
                Project = project,
                Forecast = forecast,
                RiskSummary = riskSummary
            });
        }
        return result;
    }

    public static async Task<List<FreeLimitRowItem>> BuildFreeLimitsAsync(
        AppDbContext db, IBudgetService budgetService, CancellationToken ct = default)
    {
        var projects = await db.Projects.Where(p => !p.IsDeleted).ToDictionaryAsync(p => p.Id, ct);
        var objects = await db.ConstructionObjects.Where(o => !o.IsDeleted).ToListAsync(ct);
        var costItems = await db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync(ct);
        var limits = await db.BudgetLimits.Where(l => !l.IsDeleted).ToListAsync(ct);
        var obligations = await db.Obligations.Where(o => !o.IsDeleted).ToListAsync(ct);

        var rows = new List<FreeLimitRowItem>();
        foreach (var obj in objects)
        {
            if (!projects.TryGetValue(obj.ProjectId, out var proj)) continue;
            foreach (var item in costItems)
            {
                if (!limits.Any(l => l.ConstructionObjectId == obj.Id && l.CostItemId == item.Id)
                    && !obligations.Any(o => o.ConstructionObjectId == obj.Id && o.CostItemId == item.Id))
                    continue;

                var res = await budgetService.CalculateFreeLimitAsync(obj.Id, item.Id, ct);
                rows.Add(new FreeLimitRowItem
                {
                    ProjectCode = proj.Code,
                    ProjectName = proj.Name,
                    ConstructionObjectId = obj.Id,
                    ObjectName = obj.Name,
                    CostItemId = item.Id,
                    CostItemCode = item.Code,
                    CostItemName = item.Name,
                    Result = res
                });
            }
        }
        return rows;
    }

    public static async Task<CalendarViewModel> BuildCalendarAsync(
        AppDbContext db, Guid? projectId, int horizonDays = 90, CancellationToken ct = default)
    {
        var startDate = new DateTime(2026, 11, 10);
        var projects = await db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync(ct);
        var selectedProjects = projectId.HasValue
            ? projects.Where(p => p.Id == projectId.Value).ToList()
            : projects;

        decimal initialCash = selectedProjects.Sum(p => p.InitialCash);
        decimal minCash = selectedProjects.Sum(p => p.MinCashBalance);
        var selectedIds = selectedProjects.Select(p => p.Id).ToHashSet();

        var contracts = await db.Contracts
            .Where(c => !c.IsDeleted && selectedIds.Contains(c.ProjectId))
            .ToListAsync(ct);
        var contractIds = contracts.Select(c => c.Id).ToHashSet();

        var scheduleItems = await db.ContractScheduleItems
            .Where(s => !s.IsDeleted && !s.IsPaid && contractIds.Contains(s.ContractId))
            .OrderBy(s => s.PlannedDate)
            .ToListAsync(ct);

        var pendingRequests = await db.PaymentRequests
            .Where(r => !r.IsDeleted && selectedIds.Contains(r.ProjectId)
                        && (r.Status == RequestStatus.PendingApproval
                            || r.Status == RequestStatus.ApprovedByManagement
                            || r.Status == RequestStatus.Approved))
            .ToListAsync(ct);

        var dailyFlows = new Dictionary<DateTime, (decimal Inflows, decimal Outflows)>();
        foreach (var item in scheduleItems.Where(s => s.PlannedDate.Date > startDate.Date))
        {
            var d = item.PlannedDate.Date;
            dailyFlows.TryGetValue(d, out var cur);
            dailyFlows[d] = item.IsIncoming
                ? (cur.Inflows + item.Amount, cur.Outflows)
                : (cur.Inflows, cur.Outflows + item.Amount);
        }

        foreach (var req in pendingRequests.Where(r => r.PlannedPaymentDate.Date > startDate.AddDays(5)))
        {
            var d = req.PlannedPaymentDate.Date;
            dailyFlows.TryGetValue(d, out var cur);
            dailyFlows[d] = (cur.Inflows, cur.Outflows + req.AmountInByn);
        }

        var forecastDays = FinancialCalculator.CalculateCashCalendar(
            initialCash, minCash, startDate, horizonDays, dailyFlows);

        var overdueCustomer = scheduleItems
            .Where(s => s.IsIncoming && (startDate.Date - s.PlannedDate.Date).TotalDays > 10)
            .ToList();

        return new CalendarViewModel
        {
            SelectedProjectId = projectId,
            Projects = projects,
            InitialCash = initialCash,
            MinCashBalance = minCash,
            GapDaysCount = forecastDays.Count(d => d.CashGap > 0m),
            MaxCashGap = forecastDays.Count == 0 ? 0m : forecastDays.Max(d => d.CashGap),
            Days = forecastDays,
            ScheduleItems = scheduleItems,
            OverdueCustomerItems = overdueCustomer
        };
    }

    public static async Task<ReportGridModel> BuildReportAsync(
        AppDbContext db, IBudgetService budgetService, string reportType, CancellationToken ct = default)
    {
        switch (reportType)
        {
            case "project-evm-forecast":
            {
                var forecasts = await BuildForecastsAsync(db, ct);
                return new ReportGridModel
                {
                    ReportType = reportType,
                    Title = "Прогноз стоимости проектов (метод освоенного объёма EVM)",
                    Subtitle = "Показатели CPI, SPI, ETC, EAC и финансовый результат с учётом резерва P80",
                    Headers = ["Код", "Проект", "Цена V / BAC", "PV / EV / AC", "CPI / SPI", "EAC / Фин. рез. (с P80)"],
                    Rows = forecasts.Select(f => new List<string>
                    {
                        f.Project.Code,
                        f.Project.Name,
                        $"{f.Forecast.ContractValue:N0} / {f.Forecast.Bac:N0}",
                        $"{f.Forecast.Pv:N0} / {f.Forecast.Ev:N0} / {f.Forecast.Ac:N0}",
                        $"{f.Forecast.Cpi?.ToString("N3") ?? "—"} / {f.Forecast.Spi?.ToString("N3") ?? "—"}",
                        $"{f.Forecast.Eac:N2} / {f.Forecast.FinancialResultWithReserve:N2}"
                    }).ToList()
                };
            }

            case "risks-reserve-p80":
            {
                var risks = await db.Risks.Where(r => !r.IsDeleted).OrderByDescending(r => r.Emv).ToListAsync(ct);
                var summary = RiskCalculator.CalculatePortfolioSummary(risks);
                return new ReportGridModel
                {
                    ReportType = reportType,
                    Title = "Реестр рисков и расчёт резерва P80",
                    Subtitle = $"ΣEMV = {summary.TotalEmv:N2} тыс. р., σ = {summary.Sigma:N2} тыс. р., резерв P80 = {summary.ReserveP80:N2} тыс. р., остаточный EMV = {summary.TotalResidualEmv:N2} тыс. р.",
                    Headers = ["Код", "Событие", "p / Ущерб C", "Интеграл R", "EMV / Остат. EMV", "Владелец / Статус"],
                    Rows = risks.Select(r => new List<string>
                    {
                        r.Code,
                        r.Event,
                        $"{r.Probability:P0} / {r.MonetaryImpact:N0}",
                        $"{r.IntegralScore} ({RiskCalculator.GetRiskZone(r.IntegralScore)})",
                        $"{r.Emv:N2} / {(r.ResidualEmv ?? r.Emv):N2}",
                        $"{r.OwnerRole} ({RussianLabels.RiskStatusName(r.Status)})"
                    }).ToList()
                };
            }

            case "contracts-advances":
            {
                var contracts = await db.Contracts.Where(c => !c.IsDeleted).ToDictionaryAsync(c => c.Id, ct);
                var advances = await db.Advances.Where(a => !a.IsDeleted).OrderBy(a => a.DueDate).ToListAsync(ct);
                return new ReportGridModel
                {
                    ReportType = reportType,
                    Title = "Договоры, обязательства и контроль выданных авансов",
                    Subtitle = "Контроль остатков Aост = A − Aисп и доли использования Kисп",
                    Headers = ["Договор", "Контрагент", "Аванс A", "Зачтено Aисп", "Остаток Aост / Kисп %", "Срок / Статус"],
                    Rows = advances.Select(a =>
                    {
                        contracts.TryGetValue(a.ContractId, out var c);
                        return new List<string>
                        {
                            c?.Number ?? a.ContractId.ToString(),
                            c?.Counterparty ?? "—",
                            a.Amount.ToString("N2"),
                            a.UsedAmount.ToString("N2"),
                            $"{a.Remainder:N2} ({(a.UtilizationPercent?.ToString("N1") ?? "0.0")} %)",
                            $"{a.DueDate:dd.MM.yyyy} / {RussianLabels.AdvanceStatusName(a.Status)}"
                        };
                    }).ToList()
                };
            }

            case "payment-requests-violations":
            {
                var reqs = await db.PaymentRequests.Where(r => !r.IsDeleted).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
                return new ReportGridModel
                {
                    ReportType = reportType,
                    Title = "Реестр платёжных заявок и нарушений лимитов",
                    Subtitle = "Статусы заявок, блокировки перерасхода (UC04) и ЭЦП",
                    Headers = ["Номер", "Тип", "К оплате", "Дата", "Статус", "Причина блокировки / ЭЦП"],
                    Rows = reqs.Select(r => new List<string>
                    {
                        r.Number,
                        RussianLabels.RequestTypeName(r.Type),
                        $"{r.PayableAmount:N2} {r.Currency}",
                        r.PlannedPaymentDate.ToString("dd.MM.yyyy"),
                        RussianLabels.RequestStatusName(r.Status),
                        r.BlockReason ?? r.EdsSignatureThumbprint ?? r.Purpose ?? "—"
                    }).ToList()
                };
            }

            default:
            {
                var limits = await BuildFreeLimitsAsync(db, budgetService, ct);
                return new ReportGridModel
                {
                    ReportType = "budget-plan-fact",
                    Title = "План-факт исполнения бюджета (vFreeLimit)",
                    Subtitle = "Формулы Lдост = B − O, Oост = O − F",
                    Headers = ["Проект", "Объект / Статья", "Бюджет B", "Обязательства O", "Оплачено F / Заявки Q", "Lдост / Oост"],
                    Rows = limits.Select(l => new List<string>
                    {
                        l.ProjectCode,
                        $"{l.ObjectName} ({l.CostItemCode})",
                        l.Result.ApprovedBudget.ToString("N2"),
                        l.Result.GrossObligations.ToString("N2"),
                        $"{l.Result.PaidFact:N2} / {l.Result.SubmittedRequests:N2}",
                        $"{l.Result.FreeLimit:N2} / {l.Result.ObligationRemainder:N2}"
                    }).ToList()
                };
            }
        }
    }
}
