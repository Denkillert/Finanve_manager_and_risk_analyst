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
    private readonly IAccountingAdapter _accountingAdapter;
    private readonly IMaterialRequestService _materialRequests;
    private readonly IAuditService _audit;

    public ContractsController(
        AppDbContext db,
        StatementImportService statementService,
        IAccountingAdapter accountingAdapter,
        IMaterialRequestService materialRequests,
        IAuditService audit)
    {
        _db = db;
        _statementService = statementService;
        _accountingAdapter = accountingAdapter;
        _materialRequests = materialRequests;
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
            MaterialRequests = await _db.MaterialRequests
                .Include(r => r.Project)
                .Include(r => r.ConstructionObject)
                .Include(r => r.CostItem)
                .Include(r => r.Contract)
                .Where(r => !r.IsDeleted)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct),
            ImportReportSummary = TempData["ImportReport"] as string
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateContract(
        Guid projectId, string number, string counterparty, string counterpartyUnp,
        ContractDirection direction, decimal totalAmount, string currency, string? specificationNotes, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        try
        {
            if (projectId == Guid.Empty || !await _db.Projects.AnyAsync(p => p.Id == projectId && !p.IsDeleted, ct))
                throw new DomainException("Выберите существующий проект для регистрации договора.");
            if (string.IsNullOrWhiteSpace(number) || string.IsNullOrWhiteSpace(counterparty) || totalAmount <= 0)
                throw new DomainException("Укажите номер договора, контрагента и положительную сумму.");

            string normNum = number.Trim();
            if (await _db.Contracts.AnyAsync(c => !c.IsDeleted && c.ProjectId == projectId && c.Number == normNum, ct))
                throw new DuplicateEntityException($"Договор № {normNum} уже существует в выбранном проекте.");

            var contract = new Contract
            {
                ProjectId = projectId,
                Number = normNum,
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
            TempData["Success"] = $"Договор {contract.Number} зарегистрирован снабженцем.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Ошибка сохранения договора: проверьте уникальность номера в проекте.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveMaterial(
        Guid id, Guid? contractId, string? supplierName, decimal? agreedUnitPrice, string? comment, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var request = await _materialRequests.ApproveAsync(
                id, new SupplyMaterialRequestDto(contractId, supplierName, agreedUnitPrice, comment), uid, login, ct);
            TempData["Success"] = $"Заявка {request.Number} согласована. Поставщик: {request.SupplierName}.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectMaterial(Guid id, string reason, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var request = await _materialRequests.RejectAsync(id, reason, uid, login, ct);
            TempData["Success"] = $"Заявка {request.Number} возвращена прорабу на уточнение.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> OrderMaterial(
        Guid id, Guid? contractId, string? supplierName, decimal? agreedUnitPrice, string? comment, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var request = await _materialRequests.MarkOrderedAsync(
                id, new SupplyMaterialRequestDto(contractId, supplierName, agreedUnitPrice, comment), uid, login, ct);
            TempData["Success"] = $"Заявка {request.Number} заказана у поставщика {request.SupplierName}.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeliverMaterial(Guid id, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var request = await _materialRequests.MarkDeliveredAsync(id, uid, login, ct);
            TempData["Success"] = $"Поставка по заявке {request.Number} подтверждена.";
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
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        try
        {
            if (offsetAmount <= 0)
                throw new DomainException("Сумма зачёта аванса должна быть положительной.");

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

    [HttpGet]
    [Authorize(Roles = "Accountant,FinancialManager")]
    public async Task<IActionResult> Export1C(CancellationToken ct)
    {
        var context = await BuildAccountingContextAsync(ct);
        var bytes = _accountingAdapter.Export1CClientBankExchange(context);
        return File(bytes, "text/plain; charset=utf-8", $"1c-client-bank-exchange-{DateTime.Now:yyyyMMdd}.txt");
    }

    [HttpGet]
    [Authorize(Roles = "Accountant,FinancialManager")]
    public async Task<IActionResult> ExportAccounting(string report = "balance", CancellationToken ct = default)
    {
        var context = await BuildAccountingContextAsync(ct);
        var (bytes, fileName) = report switch
        {
            "pnl" => (_accountingAdapter.ExportProfitAndLoss(context), "forma-2.xlsx"),
            "vat" => (_accountingAdapter.ExportVatRegister(context), "reestr-nds.xlsx"),
            "documents" => (_accountingAdapter.ExportPrimaryDocumentsRegister(context), "reestr-pervichnyh-dokumentov.xlsx"),
            "debts" => (_accountingAdapter.ExportReceivablesPayables(context), "debitorka-kreditorka.xlsx"),
            _ => (_accountingAdapter.ExportBalanceSheet(context), "buhgalterskiy-balans.xlsx")
        };
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    private async Task<AccountingReportContext> BuildAccountingContextAsync(CancellationToken ct)
    {
        var org = await _db.Organizations.FirstOrDefaultAsync(o => !o.IsDeleted, ct);
        var requests = await _db.PaymentRequests
            .Include(r => r.Contract)
            .Include(r => r.CostItem)
            .Where(r => !r.IsDeleted)
            .ToListAsync(ct);
        var payments = await _db.Payments
            .Include(p => p.PaymentRequest)
            .Where(p => !p.IsDeleted)
            .ToListAsync(ct);
        var contracts = await _db.Contracts
            .Include(c => c.Obligations)
            .Include(c => c.Advances)
            .Where(c => !c.IsDeleted)
            .ToListAsync(ct);

        return new AccountingReportContext
        {
            OrganizationName = org?.Name ?? "ООО «СтройФинРиск»",
            OrganizationUnp = org?.Unp ?? "191234567",
            ResponsiblePerson = "Громов А.Н., директор",
            ChiefAccountant = "Ковалёва И.П., главный бухгалтер",
            PeriodStart = new DateTime(DateTime.UtcNow.Year, 1, 1),
            PeriodEnd = DateTime.UtcNow.Date,
            Projects = await _db.Projects.Where(p => !p.IsDeleted).ToListAsync(ct),
            Contracts = contracts,
            Requests = requests,
            Payments = payments,
            Documents = await _db.Documents.Where(d => !d.IsDeleted).ToListAsync(ct),
            MaterialRequests = await _db.MaterialRequests.Where(m => !m.IsDeleted).ToListAsync(ct)
        };
    }
}
