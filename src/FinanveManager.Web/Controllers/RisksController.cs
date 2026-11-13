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

    /// <summary>
    /// Экспорт реестра рисков и матрицы в Excel с форматированием (вместо CSV).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportXlsx(Guid? projectId = null, CancellationToken ct = default)
    {
        var projects = await _db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync(ct);
        var selectedProject = projectId ?? projects.FirstOrDefault()?.Id ?? Guid.Empty;
        var project = projects.FirstOrDefault(p => p.Id == selectedProject);

        var risks = await _db.Risks
            .Where(r => !r.IsDeleted && r.ProjectId == selectedProject)
            .OrderByDescending(r => r.Emv)
            .ThenByDescending(r => r.IntegralScore)
            .ToListAsync(ct);
        var summary = await _riskService.CalculateReserveP80Async(selectedProject, ct);

        var wb = new XlsxWorkbook();

        var sheet = wb.AddSheet("Реестр рисков");
        sheet.SetWidths(10, 44, 40, 12, 14, 14, 18, 16, 24, 16);
        sheet.FreezeRows(5);
        sheet.AddTitle("РЕЕСТР РИСКОВ И РАСЧЁТ РЕЗЕРВА P80", 10);
        sheet.AddSubtitle($"Проект: {(project == null ? "—" : $"{project.Code} — {project.Name}")}");
        sheet.AddSubtitle($"ΣEMV = {summary.TotalEmv:N2} тыс. р.; σ = {summary.Sigma:N2} тыс. р.; резерв P80 = {summary.ReserveP80:N2} тыс. р.; остаточный EMV = {summary.TotalResidualEmv:N2} тыс. р.");
        sheet.AddHeader("Код", "Событие", "Причина", "p", "Ущерб C", "EMV", "Интеграл R", "Зона", "Владелец", "Статус");

        decimal totalEmv = 0, totalResidual = 0;
        foreach (var r in risks)
        {
            sheet.AddRow(r.Code, r.Event, r.Cause, r.Probability, r.MonetaryImpact, r.Emv,
                r.IntegralScore, RiskCalculator.GetRiskZone(r.IntegralScore), r.OwnerRole,
                RussianLabels.RiskStatusName(r.Status));
            totalEmv += r.Emv;
            totalResidual += r.ResidualEmv ?? r.Emv;
        }
        sheet.AddTotalRow("", "ИТОГО", "", "", "", totalEmv, "", "", "", "");
        sheet.AddBlank();
        sheet.AddSubtitle($"Остаточный EMV после мероприятий: {totalResidual:N2} тыс. р. Резерв P80: {summary.ReserveP80:N2} тыс. р.");
        sheet.AddSubtitle("Методика: ГОСТ ISO 31000-2020, z = 0,84 для доверительной вероятности 80 %.");

        var matrix = wb.AddSheet("Матрица рисков");
        matrix.SetWidths(14, 14, 14, 16, 44);
        matrix.FreezeRows(4);
        matrix.AddTitle("МАТРИЦА РИСКОВ 5×5", 5);
        matrix.AddSubtitle("Ячейки содержат интегральную оценку R = P × C и коды рисков.");
        matrix.AddBlank();
        matrix.AddHeader("Вероятность", "Ущерб", "Интеграл R", "Зона", "Коды рисков");
        foreach (var cell in RiskCalculator.BuildRiskMatrix(risks).OrderByDescending(c => c.ProbabilityScore).ThenBy(c => c.ImpactScore))
            matrix.AddRow(cell.ProbabilityScore, cell.ImpactScore, cell.IntegralScore, cell.Zone,
                string.IsNullOrWhiteSpace(cell.RiskCodes) ? "—" : cell.RiskCodes);

        var sensitivity = wb.AddSheet("Чувствительность EAC");
        sensitivity.SetWidths(40, 18, 18, 18, 44);
        sensitivity.AddTitle("АНАЛИЗ ЧУВСТВИТЕЛЬНОСТИ EAC", 5);
        sensitivity.AddBlank();
        sensitivity.AddHeader("Фактор", "Базовая сумма", "Диапазон", "Влияние на EAC", "Комментарий");
        foreach (var s in RiskCalculator.CalculateEacSensitivity())
            sensitivity.AddRow(s.FactorName, s.BaseAmount, s.RangeLabel, s.ImpactOnEac, s.Comment);

        var bytes = wb.Build();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"risks-{project?.Code ?? "portfolio"}-{DateTime.Now:yyyyMMdd}.xlsx");
    }
}
