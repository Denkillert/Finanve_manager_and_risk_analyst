using System.Security.Claims;
using System.Text;
using FinanveManager.Application.Interfaces;
using FinanveManager.Application.Services;
using FinanveManager.Domain.Common;
using FinanveManager.Infrastructure.Persistence;
using FinanveManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

[Authorize]
public class ContractsController : Controller
{
    private readonly AppDbContext _db;
    private readonly StatementImportService _statementService;
    private readonly IEstimateImporter _estimateImporter;
    private readonly IAccountingAdapter _accountingAdapter;
    private readonly IAuditService _audit;

    public ContractsController(
        AppDbContext db,
        StatementImportService statementService,
        IEstimateImporter estimateImporter,
        IAccountingAdapter accountingAdapter,
        IAuditService audit)
    {
        _db = db;
        _statementService = statementService;
        _estimateImporter = estimateImporter;
        _accountingAdapter = accountingAdapter;
        _audit = audit;
    }

    private (Guid UserId, string Login) CurrentUser() =>
        (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
         User.Identity?.Name ?? "unknown");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new ContractsViewModel
        {
            Contracts = await _db.Contracts
                .Include(c => c.Supplements)
                .Include(c => c.Obligations)
                .Include(c => c.Advances)
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Number)
                .ToListAsync(ct),
            Projects = await _db.Projects.Where(p => !p.IsDeleted).ToListAsync(ct),
            Objects = await _db.ConstructionObjects.Where(o => !o.IsDeleted).ToListAsync(ct),
            CostItems = await _db.CostItems.Where(c => !c.IsDeleted).ToListAsync(ct),
            Advances = await _db.Advances.Include(a => a.Contract).Where(a => !a.IsDeleted).OrderBy(a => a.DueDate).ToListAsync(ct),
            ImportReportSummary = TempData["ImportReport"] as string,
            EstimateImportSummary = TempData["EstimateReport"] as string
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "Accountant,Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportStatement(string fileName, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            TempData["Error"] = "Вставьте текст банковской выписки (CSV или MT940).";
            return RedirectToAction(nameof(Index));
        }

        var (uid, login) = CurrentUser();
        var report = await _statementService.ImportAsync(
            content, string.IsNullOrWhiteSpace(fileName) ? "statement.csv" : fileName.Trim(), uid, login, ct);

        TempData["ImportReport"] =
            $"Обработано: {report.TotalOperations}, сопоставлено: {report.MatchedCount}, дублей пропущено: {report.DuplicateSkippedCount}, несопоставлено: {report.UnmatchedCount}. "
            + string.Join(" ", report.Warnings);
        TempData["Success"] = $"Банковская выписка импортирована ({report.MatchedCount} сопоставлено, {report.DuplicateSkippedCount} дублей отсечено).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "Accountant,FinancialManager")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OffsetAdvance(Guid advanceId, decimal offsetAmount, CancellationToken ct)
    {
        try
        {
            var adv = await _db.Advances.FirstOrDefaultAsync(a => a.Id == advanceId && !a.IsDeleted, ct)
                ?? throw new DomainException("Аванс не найден.");
            decimal prev = adv.UsedAmount;
            adv.ApplyOffset(offsetAmount);
            var (uid, login) = CurrentUser();
            await _audit.LogAsync(uid, login, nameof(adv), adv.Id.ToString(), "OFFSET_ADVANCE",
                prev.ToString("N2"), adv.UsedAmount.ToString("N2"), null, ct);
            await _db.SaveChangesAsync(ct);
            TempData["Success"] = $"Зачёт аванса {offsetAmount:N2} выполнен. Остаток Aост = {adv.Remainder:N2} тыс. р.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "FinancialManager,ProjectManager,ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportEstimate(string fileName, string content, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            TempData["Error"] = "Введите строки сметы (КодСтатьи;Наименование;Сумма).";
            return RedirectToAction(nameof(Index));
        }

        var catalog = await _db.CostItems.Where(c => !c.IsDeleted).ToListAsync(ct);
        var res = _estimateImporter.ImportFromExcelOrXml(content, string.IsNullOrWhiteSpace(fileName) ? "smeta.csv" : fileName, catalog);
        TempData["EstimateReport"] =
            $"Строк в смете: {res.TotalLines}, сопоставлено со справочником статей: {res.MatchedLines} ({res.MatchPercent:N1} % >= 95 % целевого показателя).";
        TempData["Success"] = "Импорт и маппинг сметы выполнен.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Export1C(CancellationToken ct)
    {
        var payments = await _db.Payments.Where(p => !p.IsDeleted).ToListAsync(ct);
        var docs = await _db.Documents.Where(d => !d.IsDeleted).ToListAsync(ct);
        var csv = _accountingAdapter.ExportEntriesCsv(payments, docs);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "1c-accounting-export.csv");
    }
}
