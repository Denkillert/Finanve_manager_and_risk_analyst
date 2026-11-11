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

[Authorize]
public class RequestsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IPaymentRequestService _requestService;
    private readonly IAuditService _audit;

    public RequestsController(AppDbContext db, IPaymentRequestService requestService, IAuditService audit)
    {
        _db = db;
        _requestService = requestService;
        _audit = audit;
    }

    private (Guid UserId, string Login) CurrentUser() =>
        (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
         User.Identity?.Name ?? "unknown");

    private static byte[] DecodeRv(string? b64) =>
        string.IsNullOrWhiteSpace(b64) ? Array.Empty<byte>() : Convert.FromBase64String(b64);

    [HttpGet]
    [Authorize(Roles = "CEO,FinancialManager,Accountant,ProcurementSpecialist,ProjectManager")]
    public async Task<IActionResult> Index(string? status = null, CancellationToken ct = default)
    {
        var (uid, _) = CurrentUser();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var allowedIds = await WebPortfolioHelper.GetVisibleProjectIdsAsync(_db, role, uid, ct);

        var query = _db.PaymentRequests
            .Include(r => r.Contract)
            .Include(r => r.CostItem)
            .Where(r => !r.IsDeleted);

        if (allowedIds != null)
            query = query.Where(r => allowedIds.Contains(r.ProjectId));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RequestStatus>(status, true, out var parsed))
            query = query.Where(r => r.Status == parsed);

        var projQuery = _db.Projects.Where(p => !p.IsDeleted);
        var objQuery = _db.ConstructionObjects.Where(o => !o.IsDeleted);
        var conQuery = _db.Contracts.Where(c => !c.IsDeleted && c.Direction == ContractDirection.SupplierSubcontractorOutgoing);
        if (allowedIds != null)
        {
            projQuery = projQuery.Where(p => allowedIds.Contains(p.Id));
            objQuery = objQuery.Where(o => allowedIds.Contains(o.ProjectId));
            conQuery = conQuery.Where(c => allowedIds.Contains(c.ProjectId));
        }

        var visibleObjIds = (await objQuery.Select(o => o.Id).ToListAsync(ct)).ToHashSet();

        var vm = new PaymentRequestsViewModel
        {
            StatusFilter = status,
            Requests = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct),
            Projects = await projQuery.OrderBy(p => p.Code).ToListAsync(ct),
            Objects = await objQuery.ToListAsync(ct),
            Contracts = await conQuery.ToListAsync(ct),
            CostItems = await _db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync(ct),
            Acts = await _db.Documents
                .Where(d => !d.IsDeleted && (d.Type == DocumentType.ActS2 || d.Type == DocumentType.CertificateS3)
                            && (allowedIds == null || (d.ConstructionObjectId.HasValue && visibleObjIds.Contains(d.ConstructionObjectId.Value))))
                .OrderByDescending(d => d.Date)
                .ToListAsync(ct)
        };
        return View(vm);
    }

    [HttpGet]
    [Authorize(Roles = "ProjectManager,Accountant,ProcurementSpecialist")]
    public async Task<IActionResult> Acts(CancellationToken ct = default)
    {
        var (uid, _) = CurrentUser();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        var allowedIds = await WebPortfolioHelper.GetVisibleProjectIdsAsync(_db, role, uid, ct);

        var objQuery = _db.ConstructionObjects.Where(o => !o.IsDeleted);
        var conQuery = _db.Contracts.Where(c => !c.IsDeleted && c.Direction == ContractDirection.SupplierSubcontractorOutgoing);
        if (allowedIds != null)
        {
            objQuery = objQuery.Where(o => allowedIds.Contains(o.ProjectId));
            conQuery = conQuery.Where(c => allowedIds.Contains(c.ProjectId));
        }

        var objs = await objQuery.ToListAsync(ct);
        var objIds = objs.Select(o => o.Id).ToHashSet();

        var vm = new SiteActsViewModel
        {
            Objects = objs,
            Stages = await _db.WorkStages.Where(s => !s.IsDeleted && objIds.Contains(s.ConstructionObjectId)).ToListAsync(ct),
            Contracts = await conQuery.ToListAsync(ct),
            Acts = await _db.Documents
                .Where(d => !d.IsDeleted && (d.Type == DocumentType.ActS2 || d.Type == DocumentType.CertificateS3)
                            && (allowedIds == null || (d.ConstructionObjectId.HasValue && objIds.Contains(d.ConstructionObjectId.Value))))
                .OrderByDescending(d => d.Date)
                .ToListAsync(ct)
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager,ProcurementSpecialist,FinancialManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        Guid constructionObjectId,
        Guid contractId,
        Guid costItemId,
        RequestType type,
        decimal actAmount,
        decimal advanceOffset,
        decimal retentionAmount,
        decimal amount,
        string currency,
        DateTime plannedPaymentDate,
        string purpose,
        string documentNumber,
        bool vatIncluded = true,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var (uid, login) = CurrentUser();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var allowedIds = await WebPortfolioHelper.GetVisibleProjectIdsAsync(_db, role, uid, ct);

            var obj = await _db.ConstructionObjects.FirstOrDefaultAsync(o => o.Id == constructionObjectId && !o.IsDeleted, ct)
                ?? throw new DomainException("Объект строительства не найден.");
            if (allowedIds != null && !allowedIds.Contains(obj.ProjectId))
                throw new DomainException("Доступ запрещён: вы не назначены руководителем этого проекта.");
            if (!await _db.Contracts.AnyAsync(c => c.Id == contractId && !c.IsDeleted, ct))
                throw new DomainException("Договор не найден.");
            if (!await _db.CostItems.AnyAsync(ci => ci.Id == costItemId && !ci.IsDeleted, ct))
                throw new DomainException("Статья затрат не найдена.");

            decimal effectiveAmount = type == RequestType.CurrentPayment && actAmount > 0 ? actAmount : amount;
            if (effectiveAmount <= 0)
                throw new DomainException("Сумма заявки должна быть положительной.");

            var dto = new CreatePaymentRequestDto(
                obj.ProjectId, constructionObjectId, contractId, costItemId,
                effectiveAmount, actAmount, advanceOffset, retentionAmount, vatIncluded,
                string.IsNullOrWhiteSpace(currency) ? "BYN" : currency.Trim().ToUpperInvariant(),
                type,
                plannedPaymentDate == default ? new DateTime(2026, 11, 15) : plannedPaymentDate,
                string.IsNullOrWhiteSpace(purpose) ? "Оплата по договору" : purpose,
                type == RequestType.Advance ? DocumentType.Contract : DocumentType.ActS2,
                string.IsNullOrWhiteSpace(documentNumber) ? $"С-2/{DateTime.UtcNow:HHmmss}" : documentNumber,
                new DateTime(2026, 11, 10),
                null,
                $"WEB-{Guid.NewGuid():N}",
                true);

            var (created, checks) = await _requestService.CreateAsync(dto, uid, login, ct);
            var msg = $"Заявка {created.Number} создана в статусе «{RussianLabels.RequestStatusName(created.Status)}». " + string.Join(" | ", checks.Messages);
            if (created.Status == RequestStatus.Blocked)
                TempData["Error"] = msg;
            else
                TempData["Success"] = msg;
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Не удалось создать заявку: проверьте уникальность номера документа и корректность данных.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager,ProcurementSpecialist,FinancialManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Resubmit(Guid id, string? rowVersionBase64, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var (req, checks) = await _requestService.SubmitAsync(id, DecodeRv(rowVersionBase64), uid, login, ct);
            var msg = $"Заявка {req.Number} отправлена повторно: статус «{RussianLabels.RequestStatusName(req.Status)}». " + string.Join(" | ", checks.Messages);
            if (req.Status == RequestStatus.Blocked)
                TempData["Error"] = msg;
            else
                TempData["Success"] = msg;
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
    public async Task<IActionResult> ApproveOverrun(Guid id, bool approve, string reason, string? rowVersionBase64, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var req = await _requestService.ApproveOverrunAsync(
                id, approve, string.IsNullOrWhiteSpace(reason) ? "Согласовано руководителем" : reason,
                DecodeRv(rowVersionBase64), uid, login, ct);
            TempData["Success"] = $"По заявке {req.Number} зафиксировано решение: {req.Status}.";
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
    public async Task<IActionResult> Approve(Guid id, string? edsThumbprint, string? rowVersionBase64, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var req = await _requestService.ApproveAsync(id, edsThumbprint, DecodeRv(rowVersionBase64), uid, login, ct);
            TempData["Success"] = $"Заявка {req.Number} согласована к оплате (ЭЦП: {req.EdsSignatureThumbprint}).";
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
    public async Task<IActionResult> ReturnOrReject(Guid id, bool rejectPermanently, string reason, string? rowVersionBase64, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var req = await _requestService.ReturnOrRejectAsync(
                id, rejectPermanently, string.IsNullOrWhiteSpace(reason) ? "Требуется уточнение документов" : reason,
                DecodeRv(rowVersionBase64), uid, login, ct);
            TempData["Success"] = $"Заявка {req.Number} переведена в статус {req.Status}.";
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
    public async Task<IActionResult> Execute(Guid id, string? paymentOrderNumber, string? rowVersionBase64, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var payment = await _requestService.ExecuteAsync(id, paymentOrderNumber, DecodeRv(rowVersionBase64), uid, login, ct);
            TempData["Success"] = $"Платёж проведён: ПП №{payment.PaymentOrderNumber}, сумма {payment.Amount:N2} {payment.Currency}.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager,Accountant,ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateAct(
        Guid contractId,
        Guid constructionObjectId,
        DocumentType documentType,
        string number,
        decimal grossAmount,
        decimal advanceOffset,
        decimal retentionAmount,
        string actWorkDescription,
        string? photoEvidencePaths,
        string? returnTo,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return string.Equals(returnTo, "Acts", StringComparison.OrdinalIgnoreCase)
                ? RedirectToAction(nameof(Acts))
                : RedirectToAction(nameof(Index));
        }

        try
        {
            if (contractId == Guid.Empty || constructionObjectId == Guid.Empty)
                throw new DomainException("Выберите договор и объект строительства.");

            var (uid, login) = CurrentUser();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var allowedIds = await WebPortfolioHelper.GetVisibleProjectIdsAsync(_db, role, uid, ct);
            var obj = await _db.ConstructionObjects.FirstOrDefaultAsync(o => o.Id == constructionObjectId && !o.IsDeleted, ct)
                ?? throw new DomainException("Объект строительства не найден.");
            if (allowedIds != null && !allowedIds.Contains(obj.ProjectId))
                throw new DomainException("Доступ запрещён: вы не назначены руководителем этого проекта.");

            var docType = documentType == DocumentType.CertificateS3 ? DocumentType.CertificateS3 : DocumentType.ActS2;
            string docNumber = string.IsNullOrWhiteSpace(number) ? $"С-2/{DateTime.UtcNow:MMdd-HHmm}" : number.Trim();
            var docDate = new DateTime(2026, 11, 10);

            if (await _db.Documents.AnyAsync(d => !d.IsDeleted && d.Type == docType && d.Number == docNumber && d.Date == docDate, ct))
                throw new DuplicateEntityException($"Документ {docNumber} от {docDate:dd.MM.yyyy} уже зарегистрирован.");

            decimal payable = FinancialCalculator.CalculateActPayable(grossAmount, advanceOffset, retentionAmount).PayableAmount;
            var doc = new Document
            {
                ContractId = contractId,
                ConstructionObjectId = constructionObjectId,
                Type = docType,
                Number = docNumber,
                Date = docDate,
                GrossAmount = grossAmount,
                AdvanceOffset = advanceOffset,
                RetentionAmount = retentionAmount,
                NetAmount = payable,
                VatIncluded = true,
                Currency = "BYN",
                ActWorkDescription = actWorkDescription,
                PhotoEvidencePaths = photoEvidencePaths,
                IsVerifiedByInstruction53 = true
            };
            await _db.Documents.AddAsync(doc, ct);
            await _audit.LogAsync(uid, login, nameof(Document), doc.Number, "CREATE_ACT_S2", null,
                $"Gross={grossAmount:N2};Payable={payable:N2}", actWorkDescription, ct);
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = $"Акт {doc.Number} зарегистрирован: к оплате = {payable:N2} тыс. р.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Не удалось зарегистрировать акт: проверьте уникальность номера и корректность сумм.";
        }
        return string.Equals(returnTo, "Acts", StringComparison.OrdinalIgnoreCase)
            ? RedirectToAction(nameof(Acts))
            : RedirectToAction(nameof(Index));
    }
}
