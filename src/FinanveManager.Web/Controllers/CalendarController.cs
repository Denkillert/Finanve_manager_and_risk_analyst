using System.Security.Claims;
using FinanveManager.Application.Interfaces;
using FinanveManager.Infrastructure.Persistence;
using FinanveManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

[Authorize]
public class CalendarController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public CalendarController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> Index(Guid? projectId = null, int horizonDays = 90, CancellationToken ct = default)
    {
        int clamped = Math.Clamp(horizonDays, 5, 180);
        ViewBag.HorizonDays = clamped;
        var vm = await WebPortfolioHelper.BuildCalendarAsync(_db, projectId, clamped, ct);
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "FinancialManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reschedule(Guid scheduleItemId, DateTime newDate, Guid? projectId, int horizonDays = 90, CancellationToken ct = default)
    {
        var item = await _db.ContractScheduleItems.FirstOrDefaultAsync(s => s.Id == scheduleItemId && !s.IsDeleted, ct);
        if (item == null)
        {
            TempData["Error"] = "Позиция графика платежей не найдена.";
            return RedirectToAction(nameof(Index), new { projectId, horizonDays });
        }

        string oldDate = item.PlannedDate.ToString("yyyy-MM-dd");
        item.PlannedDate = newDate.Date;
        item.UpdatedAt = DateTime.UtcNow;

        var uid = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        await _audit.LogAsync(uid, User.Identity?.Name ?? "", nameof(item), item.Id.ToString(),
            "RESCHEDULE_PAYMENT", oldDate, newDate.ToString("yyyy-MM-dd"), item.Description, ct);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Платёж «{item.Description}» перенесён с {oldDate} на {newDate:yyyy-MM-dd}.";
        return RedirectToAction(nameof(Index), new { projectId, horizonDays });
    }
}
