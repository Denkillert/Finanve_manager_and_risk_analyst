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
    private readonly IMaterialRequestService _materialRequests;
    private readonly IAuditService _audit;
    private readonly IWebHostEnvironment _env;

    public RequestsController(
        AppDbContext db,
        IPaymentRequestService requestService,
        IMaterialRequestService materialRequests,
        IAuditService audit,
        IWebHostEnvironment env)
    {
        _db = db;
        _requestService = requestService;
        _materialRequests = materialRequests;
        _audit = audit;
        _env = env;
    }

    private (Guid UserId, string Login) CurrentUser() =>
        (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
         User.Identity?.Name ?? "unknown");

    private static byte[] DecodeRv(string? b64) =>
        string.IsNullOrWhiteSpace(b64) ? Array.Empty<byte>() : Convert.FromBase64String(b64);

    [HttpGet]
    [Authorize(Roles = "CEO,FinancialManager,Accountant,ProcurementSpecialist,ProjectManager")]
    public async Task<IActionResult> Index(string? status = null, Guid? editMaterial = null, CancellationToken ct = default)
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

        var materialQuery = _db.MaterialRequests
            .Include(r => r.Project)
            .Include(r => r.ConstructionObject)
            .Include(r => r.CostItem)
            .Include(r => r.Contract)
            .Where(r => !r.IsDeleted);
        if (allowedIds != null)
            materialQuery = materialQuery.Where(r => allowedIds.Contains(r.ProjectId));

        var vm = new PaymentRequestsViewModel
        {
            StatusFilter = status,
            Requests = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct),
            Projects = await projQuery.OrderBy(p => p.Code).ToListAsync(ct),
            Objects = await objQuery.ToListAsync(ct),
            Contracts = await conQuery.ToListAsync(ct),
            CostItems = await _db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync(ct),
            MaterialRequests = await materialQuery.OrderByDescending(r => r.CreatedAt).ToListAsync(ct),
            Acts = await _db.Documents
                .Where(d => !d.IsDeleted && (d.Type == DocumentType.ActS2 || d.Type == DocumentType.CertificateS3)
                            && (allowedIds == null || (d.ConstructionObjectId.HasValue && visibleObjIds.Contains(d.ConstructionObjectId.Value))))
                .OrderByDescending(d => d.Date)
                .ToListAsync(ct)
        };

        if (editMaterial.HasValue)
        {
            var (editUid, _) = CurrentUser();
            vm.EditingMaterial = vm.MaterialRequests.FirstOrDefault(r => r.Id == editMaterial.Value);
            if (vm.EditingMaterial != null && vm.EditingMaterial.AuthorUserId != editUid)
                vm.EditingMaterial = null;
        }

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
    [Authorize(Roles = "ProcurementSpecialist,FinancialManager,CEO")]
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
    [Authorize(Roles = "ProjectManager")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMaterial(
        Guid constructionObjectId,
        Guid? costItemId,
        string materialName,
        string unit,
        decimal quantity,
        decimal estimatedUnitPrice,
        DateTime requiredDate,
        string? purpose,
        string? specificationNotes,
        bool submitImmediately = true,
        CancellationToken ct = default)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var allowedIds = await WebPortfolioHelper.GetVisibleProjectIdsAsync(_db, role, uid, ct);

            var obj = await _db.ConstructionObjects.FirstOrDefaultAsync(o => o.Id == constructionObjectId && !o.IsDeleted, ct)
                ?? throw new DomainException("Объект строительства не найден.");
            if (allowedIds != null && !allowedIds.Contains(obj.ProjectId))
                throw new DomainException("Доступ запрещён: вы не назначены руководителем этого проекта.");

            var dto = new CreateMaterialRequestDto(
                obj.ProjectId, obj.Id, costItemId, materialName, unit, quantity, estimatedUnitPrice,
                requiredDate, purpose, specificationNotes, submitImmediately);

            var created = await _materialRequests.CreateAsync(dto, uid, login, ct);
            TempData["Success"] = submitImmediately
                ? $"Заявка на материал {created.Number} передана снабженцу."
                : $"Черновик заявки на материал {created.Number} сохранён.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMaterial(
        Guid id,
        Guid? costItemId,
        string materialName,
        string unit,
        decimal quantity,
        decimal estimatedUnitPrice,
        DateTime requiredDate,
        string? purpose,
        string? specificationNotes,
        bool submitImmediately = true,
        CancellationToken ct = default)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var existing = await _db.MaterialRequests.FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
                ?? throw new DomainException("Заявка на материал не найдена.");

            var dto = new CreateMaterialRequestDto(
                existing.ProjectId, existing.ConstructionObjectId, costItemId, materialName, unit,
                quantity, estimatedUnitPrice, requiredDate, purpose, specificationNotes, submitImmediately);

            var updated = await _materialRequests.UpdateAsync(id, dto, uid, login, ct);
            TempData["Success"] = submitImmediately
                ? $"Заявка {updated.Number} доработана и повторно передана снабженцу."
                : $"Изменения по заявке {updated.Number} сохранены.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitMaterial(Guid id, CancellationToken ct)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var req = await _materialRequests.SubmitAsync(id, uid, login, ct);
            TempData["Success"] = $"Заявка на материал {req.Number} передана снабженцу.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    [Authorize(Roles = "ProjectManager,ProcurementSpecialist,FinancialManager,CEO")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var (uid, _) = CurrentUser();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

        var request = await _db.PaymentRequests
            .Include(r => r.Contract)
            .Include(r => r.CostItem)
            .FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct);
        if (request == null)
            return NotFound();

        if (request.Status is not (RequestStatus.Returned or RequestStatus.Draft))
        {
            TempData["Error"] = "Доработка доступна только для заявок в статусе «Возвращена на доработку» или «Черновик».";
            return RedirectToAction(nameof(Index));
        }

        if (request.AuthorUserId != uid && role is not ("FinancialManager" or "CEO"))
        {
            TempData["Error"] = "Дорабатывать заявку может её автор, финансовый менеджер или руководитель организации.";
            return RedirectToAction(nameof(Index));
        }

        var allowedIds = await WebPortfolioHelper.GetVisibleProjectIdsAsync(_db, role, uid, ct);
        var objQuery = _db.ConstructionObjects.Where(o => !o.IsDeleted && o.ProjectId == request.ProjectId);
        var conQuery = _db.Contracts.Where(c => !c.IsDeleted && c.ProjectId == request.ProjectId);

        var vm = new RequestEditViewModel
        {
            Request = request,
            Objects = await objQuery.ToListAsync(ct),
            Contracts = await conQuery.ToListAsync(ct),
            CostItems = await _db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync(ct),
            CanEdit = allowedIds == null || allowedIds.Contains(request.ProjectId)
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager,ProcurementSpecialist,FinancialManager,CEO")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        Guid id,
        decimal actAmount,
        decimal advanceOffset,
        decimal retentionAmount,
        decimal amount,
        DateTime plannedPaymentDate,
        string purpose,
        string documentNumber,
        bool resubmit = true,
        CancellationToken ct = default)
    {
        try
        {
            var (uid, login) = CurrentUser();
            var role = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            var request = await _db.PaymentRequests.FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
                ?? throw new DomainException("Заявка не найдена.");

            if (request.Status is not (RequestStatus.Returned or RequestStatus.Draft))
                throw new DomainException("Доработка доступна только для возвращённых заявок и черновиков.");
            if (request.AuthorUserId != uid && role is not ("FinancialManager" or "CEO"))
                throw new DomainException("Дорабатывать заявку может её автор, финансовый менеджер или руководитель организации.");

            decimal effectiveAmount = request.Type == RequestType.CurrentPayment && actAmount > 0 ? actAmount : amount;
            if (effectiveAmount <= 0)
                throw new DomainException("Сумма заявки должна быть положительной.");

            var calc = FinancialCalculator.CalculateActPayable(effectiveAmount, advanceOffset, retentionAmount);
            request.Amount = effectiveAmount;
            request.ActAmount = actAmount;
            request.AdvanceOffset = advanceOffset;
            request.RetentionAmount = retentionAmount;
            request.PayableAmount = calc.PayableAmount;
            request.PlannedPaymentDate = plannedPaymentDate == default ? request.PlannedPaymentDate : plannedPaymentDate;
            request.Purpose = string.IsNullOrWhiteSpace(purpose) ? request.Purpose : purpose.Trim();
            request.ReturnOrRejectReason = null;
            request.UpdatedAt = DateTime.UtcNow;

            var doc = await _db.Documents.FirstOrDefaultAsync(d => !d.IsDeleted && d.PaymentRequestId == request.Id, ct);
            if (doc != null && !string.IsNullOrWhiteSpace(documentNumber))
            {
                doc.Number = documentNumber.Trim();
                doc.GrossAmount = effectiveAmount;
                doc.AdvanceOffset = advanceOffset;
                doc.RetentionAmount = retentionAmount;
                doc.NetAmount = calc.PayableAmount;
            }

            await _audit.LogAsync(uid, login, nameof(PaymentRequest), request.Id.ToString(), "EDIT_RETURNED_REQUEST",
                request.Status.ToString(), $"Amount={effectiveAmount:N2}", purpose, ct);
            await _db.SaveChangesAsync(ct);

            if (resubmit)
            {
                var (req, checks) = await _requestService.SubmitAsync(id, Array.Empty<byte>(), uid, login, ct);
                var msg = $"Заявка {req.Number} доработана и отправлена повторно: статус «{RussianLabels.RequestStatusName(req.Status)}». "
                          + string.Join(" | ", checks.Messages);
                if (req.Status == RequestStatus.Blocked)
                    TempData["Error"] = msg;
                else
                    TempData["Success"] = msg;
            }
            else
            {
                TempData["Success"] = $"Изменения по заявке {request.Number} сохранены без отправки.";
            }
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = "Не удалось сохранить изменения: проверьте уникальность номера документа.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "ProjectManager,Accountant,ProcurementSpecialist")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPhoto(IFormFile? photo, CancellationToken ct)
    {
        if (photo == null || photo.Length == 0)
            return BadRequest(new { error = "Файл не выбран." });
        if (photo.Length > 15 * 1024 * 1024)
            return BadRequest(new { error = "Размер файла не должен превышать 15 МБ." });

        string ext = Path.GetExtension(photo.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".heic"))
            return BadRequest(new { error = "Поддерживаются форматы JPG, PNG, WEBP, HEIC." });

        string root = string.IsNullOrWhiteSpace(_env.WebRootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
            : _env.WebRootPath;
        string dir = Path.Combine(root, "uploads", "acts");
        Directory.CreateDirectory(dir);

        string fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{ext}";
        await using (var stream = System.IO.File.Create(Path.Combine(dir, fileName)))
            await photo.CopyToAsync(stream, ct);

        string relative = $"/uploads/acts/{fileName}";
        var (uid, login) = CurrentUser();
        await _audit.LogAsync(uid, login, "PhotoEvidence", fileName, "UPLOAD_PHOTO", null, relative, null, ct);
        await _db.SaveChangesAsync(ct);
        return Json(new { path = relative });
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
