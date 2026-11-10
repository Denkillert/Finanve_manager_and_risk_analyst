using System.Security.Claims;
using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Models;
using FinanveManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

[Authorize(Roles = "CEO,FinancialManager,ProjectManager")]
public class ProjectsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IBudgetService _budgetService;
    private readonly IAuditService _audit;

    public ProjectsController(AppDbContext db, IBudgetService budgetService, IAuditService audit)
    {
        _db = db;
        _budgetService = budgetService;
        _audit = audit;
    }

    private (Guid UserId, string Login) CurrentUser() =>
        (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
         User.Identity?.Name ?? "unknown");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new ProjectsBudgetsViewModel
        {
            Projects = await WebPortfolioHelper.BuildForecastsAsync(_db, ct),
            Objects = await _db.ConstructionObjects.Where(o => !o.IsDeleted).ToListAsync(ct),
            Stages = await _db.WorkStages.Where(s => !s.IsDeleted).ToListAsync(ct),
            CostItems = await _db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync(ct),
            FreeLimits = await WebPortfolioHelper.BuildFreeLimitsAsync(_db, _budgetService, ct),
            BudgetVersions = await _db.BudgetVersions.Where(v => !v.IsDeleted).OrderBy(v => v.ProjectId).ThenByDescending(v => v.VersionNumber).ToListAsync(ct),
            BudgetLimits = await _db.BudgetLimits.Where(l => !l.IsDeleted).ToListAsync(ct)
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "FinancialManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateVersion(Guid projectId, string comment, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var bv = await _budgetService.CreateNewVersionAsync(projectId, string.IsNullOrWhiteSpace(comment) ? "Корректировка бюджета" : comment, uid, login, ct);
            TempData["Success"] = $"Создана новая черновая версия бюджета v{bv.VersionNumber}.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "FinancialManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateLimit(Guid limitId, decimal amount, string? rowVersionBase64, CancellationToken ct)
    {
        try
        {
            var limit = await _db.BudgetLimits.FirstOrDefaultAsync(l => l.Id == limitId && !l.IsDeleted, ct)
                ?? throw new DomainException("Лимит бюджета не найден.");
            var bv = await _db.BudgetVersions.FirstOrDefaultAsync(v => v.Id == limit.BudgetVersionId && !v.IsDeleted, ct)
                ?? throw new DomainException("Версия бюджета не найдена.");

            byte[] rv = string.IsNullOrEmpty(rowVersionBase64) ? Array.Empty<byte>() : Convert.FromBase64String(rowVersionBase64);
            if (rv.Length > 0 && !limit.RowVersion.SequenceEqual(rv))
                throw new ConcurrencyConflictException();

            decimal oldAmount = limit.Amount;
            limit.UpdateAmount(amount, bv.Status);

            var (uid, login) = CurrentUser();
            await _audit.LogAsync(uid, login, "BudgetLimit", limit.Id.ToString(), "UPDATE_LIMIT", oldAmount.ToString("N2"), amount.ToString("N2"), null, ct);
            await _db.SaveChangesAsync(ct);
            TempData["Success"] = $"Лимит обновлён: {amount:N2} тыс. р.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveVersion(Guid versionId, string? rowVersionBase64, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            byte[] rv = string.IsNullOrEmpty(rowVersionBase64) ? Array.Empty<byte>() : Convert.FromBase64String(rowVersionBase64);
            var bv = await _budgetService.ApproveVersionAsync(versionId, rv, uid, login, ct);
            TempData["Success"] = $"Версия бюджета v{bv.VersionNumber} утверждена.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
