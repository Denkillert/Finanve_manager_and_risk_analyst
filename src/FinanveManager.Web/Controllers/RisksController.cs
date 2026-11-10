using System.Security.Claims;
using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;
using FinanveManager.Domain.Services;
using FinanveManager.Infrastructure.Persistence;
using FinanveManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

[Authorize]
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
            Summary = summary,
            Sensitivity = sensitivity
        });
    }

    [HttpPost]
    [Authorize(Roles = "RiskSpecialist,ProjectManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAssessment(
        Guid id,
        Guid projectId,
        decimal probability,
        decimal monetaryImpact,
        decimal? residualProbability,
        string comment,
        string? rowVersionBase64,
        CancellationToken ct)
    {
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
        return RedirectToAction(nameof(Index), new { projectId });
    }

    [HttpPost]
    [Authorize(Roles = "RiskSpecialist,ProjectManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid projectId,
        string code,
        string eventText,
        decimal probability,
        decimal monetaryImpact,
        decimal? residualProbability,
        string ownerRole,
        string mitigation,
        string earlyIndicator,
        CancellationToken ct)
    {
        try
        {
            var risk = new Risk
            {
                ProjectId = projectId,
                Code = string.IsNullOrWhiteSpace(code) ? $"R-{DateTime.UtcNow:HHmm}" : code.Trim().ToUpperInvariant(),
                Event = eventText.Trim(),
                Cause = "Выявлено в ходе мониторинга проекта",
                Consequence = "Потенциальное отклонение стоимости/срока",
                Probability = Math.Clamp(probability, 0.01m, 0.99m),
                MonetaryImpact = Math.Max(0m, monetaryImpact),
                ResidualProbability = residualProbability,
                OwnerRole = string.IsNullOrWhiteSpace(ownerRole) ? "PM" : ownerRole.Trim(),
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
        return RedirectToAction(nameof(Index), new { projectId });
    }
}
