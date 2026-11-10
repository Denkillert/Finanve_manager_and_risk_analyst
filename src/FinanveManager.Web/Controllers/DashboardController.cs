using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Models;
using FinanveManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

[Authorize(Roles = "CEO,FinancialManager,Accountant,ProcurementSpecialist,ProjectManager,RiskSpecialist")]
public class DashboardController : Controller
{
    private readonly AppDbContext _db;
    private readonly IBudgetService _budgetService;

    public DashboardController(AppDbContext db, IBudgetService budgetService)
    {
        _db = db;
        _budgetService = budgetService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var forecasts = await WebPortfolioHelper.BuildForecastsAsync(_db, ct);
        var limits = await WebPortfolioHelper.BuildFreeLimitsAsync(_db, _budgetService, ct);
        var calendar = await WebPortfolioHelper.BuildCalendarAsync(_db, null, 90, ct);
        var pending = await _db.PaymentRequests
            .Where(r => !r.IsDeleted && (r.Status == RequestStatus.Blocked || r.Status == RequestStatus.PendingApproval || r.Status == RequestStatus.Approved))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
        var notifications = await _db.Notifications
            .OrderBy(n => n.IsRead)
            .ThenByDescending(n => n.CreatedAt)
            .Take(12)
            .ToListAsync(ct);

        string dbName = _db.Database.GetDbConnection().Database;
        var vm = new DashboardViewModel
        {
            ProjectCount = forecasts.Count,
            TotalContractValue = forecasts.Sum(f => f.Project.ContractValue),
            TotalApprovedBudget = limits.Sum(l => l.Result.ApprovedBudget),
            TotalGrossObligations = limits.Sum(l => l.Result.GrossObligations),
            TotalFreeLimit = limits.Sum(l => l.Result.FreeLimit),
            TotalReserveP80 = forecasts.Sum(f => f.RiskSummary.ReserveP80),
            GapDaysCount90d = calendar.GapDaysCount,
            MaxCashGap90d = calendar.MaxCashGap,
            DatabaseProvider = $"MS SQL Server ({dbName})",
            Forecasts = forecasts,
            PendingRequests = pending,
            Notifications = notifications
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReadAllNotifications(CancellationToken ct)
    {
        var items = await _db.Notifications.Where(n => !n.IsRead).ToListAsync(ct);
        foreach (var n in items) n.IsRead = true;
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = "Уведомления отмечены как прочитанные.";
        return RedirectToAction(nameof(Index));
    }
}
