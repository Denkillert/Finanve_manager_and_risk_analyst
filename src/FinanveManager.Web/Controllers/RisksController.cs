using System.Security.Claims;
using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

[Authorize(Roles = "CEO,RiskSpecialist")]
public class RisksController : Controller
{
    private readonly AppDbContext _db;
    private readonly IRiskService _riskService;
    private readonly IAuditService _audit;

    public RisksController(AppDbContext db, IRiskService riskService, IAuditService audit)
    {
        _db = db;
        _riskService = riskService;
        _audit = audit;
    }

    private (Guid UserId, string Login) CurrentUser() =>
        (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
         User.Identity?.Name ?? "unknown");

    [HttpGet]
    public async Task<IActionResult> Index(Guid? projectId = null, CancellationToken ct = default)
    {
        var projects = await _db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync(ct);
        var selectedProject = projectId ?? projects.FirstOrDefault()?.Id ?? Guid.Empty;

        var risks = await _db.Risks
            .Where(r => !r.IsDeleted && r.ProjectId == selectedProject)
            .OrderByDescending(r => r.Emv)
            .ThenByDescending(r => r.IntegralScore)
            .ToListAsync(ct);

        var summary = await _riskService.CalculateReserveP80Async(selectedProject, ct);
        var sensitivity = RiskCalculator.CalculateEacSensitivity();

        return View(new RisksViewModel
        {
            SelectedProjectId = selectedProject,
            Projects = projects,
            Risks = risks,
            Matrix = RiskCalculator.BuildRiskMatrix(risks),
            Summary = summary,
            Sensitivity = sensitivity
        });
    }

    [HttpPost]
    [Authorize(Roles = "RiskSpecialist,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAssessment(
        Guid id, Guid projectId, decimal probability, decimal monetaryImpact,
        decimal? residualProbability, string comment, string? rowVersionBase64, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { projectId });
        }

        try
        {
            var (uid, login) = CurrentUser();
            byte[] rv = string.IsNullOrWhiteSpace(rowVersionBase64) ? Array.Empty<byte>() : Convert.FromBase64String(rowVersionBase64);
            var risk = await _riskService.UpdateAssessmentAsync(
                id, probability, monetaryImpact, residualProbability,
                string.IsNullOrWhiteSpace(comment) ? "Плановая переоценка риска" : comment, rv, uid, login, ct);
            TempData["Success"] = $"Риск {risk.Code} переоценён: R={risk.IntegralScore}, EMV={risk.Emv:N2} тыс. р.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Ошибка сохранения оценки риска в БД. Обновите страницу и повторите.";
        }
        return RedirectToAction(nameof(Index), new { projectId });
    }

    [HttpPost]
    [Authorize(Roles = "RiskSpecialist,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid projectId, string code, string eventText, decimal probability,
        decimal monetaryImpact, decimal? residualProbability, string ownerRole,
        string mitigation, string earlyIndicator, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index), new { projectId });
        }

        try
        {
            if (projectId == Guid.Empty || !await _db.Projects.AnyAsync(p => p.Id == projectId && !p.IsDeleted, ct))
                throw new DomainException("Сначала выберите существующий проект (в базе данных нет активных проектов).");

            if (string.IsNullOrWhiteSpace(eventText))
                throw new DomainException("Укажите описание рискового события.");

            string normalizedCode = string.IsNullOrWhiteSpace(code) ? $"R-{DateTime.UtcNow:HHmm}" : code.Trim().ToUpperInvariant();
            if (await _db.Risks.AnyAsync(r => !r.IsDeleted && r.ProjectId == projectId && r.Code == normalizedCode, ct))
                throw new DuplicateEntityException($"Риск с кодом {normalizedCode} уже существует в этом проекте.");

            var risk = new Risk
            {
                ProjectId = projectId,
                Code = normalizedCode,
                Event = eventText.Trim(),
                Cause = "Выявлено в ходе мониторинга проекта",
                Consequence = "Потенциальное отклонение стоимости/срока",
                Probability = Math.Clamp(probability, 0.01m, 0.99m),
                MonetaryImpact = Math.Max(0m, monetaryImpact),
                ResidualProbability = residualProbability.HasValue ? Math.Clamp(residualProbability.Value, 0m, 0.99m) : null,
                OwnerRole = string.IsNullOrWhiteSpace(ownerRole) ? "Руководитель проекта" : ownerRole.Trim(),
                Mitigation = mitigation ?? string.Empty,
                EarlyIndicator = earlyIndicator ?? string.Empty,
                Status = RiskStatus.Analyzed
            };
            RiskCalculator.RecalculateScores(risk);
            await _db.Risks.AddAsync(risk, ct);

            var (uid, login) = CurrentUser();
            await _audit.LogAsync(uid, login, nameof(Risk), risk.Code, "CREATE_RISK", null, $"EMV={risk.Emv:N2}", risk.Event, ct);
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = $"Добавлен риск {risk.Code} (R={risk.IntegralScore}, EMV={risk.Emv:N2} тыс. р.).";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Не удалось сохранить риск: проверьте уникальность кода риска в проекте.";
        }
        return RedirectToAction(nameof(Index), new { projectId });
    }
}
