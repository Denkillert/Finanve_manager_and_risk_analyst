using System.Security.Claims;
using System.Text;
using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

[Authorize(Roles = "FinancialManager,Accountant,ProcurementSpecialist")]
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
            Projects = await _db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync(ct),
            Contracts = await _db.Contracts
                .Include(c => c.Supplements)
                .Include(c => c.Obligations)
                .Include(c => c.Advances)
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Number)
                .ToListAsync(ct),
            Advances = await _db.Advances.Include(a => a.Contract).Where(a => !a.IsDeleted).OrderBy(a => a.DueDate).ToListAsync(ct),
            ImportReportSummary = TempData["ImportReport"] as string,
            EstimateImportSummary = TempData["EstimateReport"] as string
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "FinancialManager,ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateContract(
        Guid projectId, string number, string counterparty, string counterpartyUnp,
        ContractDirection direction, decimal totalAmount, string currency, string? specificationNotes, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(counterparty) || totalAmount <= 0)
                throw new DomainException("Укажите номер договора, контрагента и положительную сумму.");

            var contract = new Contract
            {
                ProjectId = projectId,
                Number = number.Trim(),
                Counterparty = counterparty.Trim(),
                CounterpartyUnp = string.IsNullOrWhiteSpace(counterpartyUnp) ? "190000001" : counterpartyUnp.Trim(),
                Direction = direction,
                TotalAmount = totalAmount,
                Currency = string.IsNullOrWhiteSpace(currency) ? "BYN" : currency.Trim().ToUpperInvariant(),
                StartDate = new DateTime(2026, 11, 10),
                EndDate = new DateTime(2027, 11, 10),
                SpecificationNotes = specificationNotes?.Trim()
            };
            await _db.Contracts.AddAsync(contract, ct);
            var (uid, login) = CurrentUser();
            await _audit.LogAsync(uid, login, nameof(Contract), contract.Number, "CREATE_CONTRACT", null,
                $"{contract.TotalAmount:N2} {contract.Currency}", contract.Counterparty, ct);
            await _db.SaveChangesAsync(ct);
            TempData["Success"] = $"Договор {contract.Number} зарегистрирован.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "Accountant")]
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
        TempData["Success"] = $"Выписка обработана ({report.MatchedCount} сопоставлено, {report.DuplicateSkippedCount} дублей отсечено).";
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
            await _audit.LogAsync(uid, login, nameof(Advance), adv.Id.ToString(), "OFFSET_ADVANCE",
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
    [Authorize(Roles = "FinancialManager,ProcurementSpecialist")]
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
            $"Строк в смете: {res.TotalLines}, сопоставлено со справочником статей: {res.MatchedLines} ({res.MatchPercent:N1} %).";
        TempData["Success"] = "Импорт сметы выполнен.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "Accountant,FinancialManager")]
    public async Task<IActionResult> Export1C(CancellationToken ct)
    {
        var payments = await _db.Payments.Where(p => !p.IsDeleted).ToListAsync(ct);
        var csv = _accountingAdapter.ExportEntriesCsv(payments);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "1c-accounting-export.csv");
    }
}
