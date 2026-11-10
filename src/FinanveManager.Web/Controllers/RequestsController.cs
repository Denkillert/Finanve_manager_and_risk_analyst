using System.Security.Claims;
using FinanveManager.Application.DTOs;
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
    public async Task<IActionResult> Index(string? status = null, CancellationToken ct = default)
    {
        var query = _db.PaymentRequests
            .Include(r => r.Contract)
            .Include(r => r.CostItem)
            .Where(r => !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<RequestStatus>(status, true, out var parsed))
            query = query.Where(r => r.Status == parsed);

        var vm = new PaymentRequestsViewModel
        {
            StatusFilter = status,
            Requests = await query.OrderByDescending(r => r.CreatedAt).ToListAsync(ct),
            Projects = await _db.Projects.Where(p => !p.IsDeleted).OrderBy(p => p.Code).ToListAsync(ct),
            Objects = await _db.ConstructionObjects.Where(o => !o.IsDeleted).ToListAsync(ct),
            Contracts = await _db.Contracts.Where(c => !c.IsDeleted && c.Direction == ContractDirection.SupplierSubcontractorOutgoing).ToListAsync(ct),
            CostItems = await _db.CostItems.Where(c => !c.IsDeleted).OrderBy(c => c.Code).ToListAsync(ct),
            Acts = await _db.Documents
                .Where(d => !d.IsDeleted && (d.Type == DocumentType.ActS2 || d.Type == DocumentType.CertificateS3))
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
        bool submitImmediately = true,
        CancellationToken ct = default)
    {
        try
        {
            var obj = await _db.ConstructionObjects.FirstOrDefaultAsync(o => o.Id == constructionObjectId && !o.IsDeleted, ct)
                ?? throw new DomainException("Объект строительства не найден.");

            var dto = new CreatePaymentRequestDto(
                ProjectId: obj.ProjectId,
                ConstructionObjectId: constructionObjectId,
                ContractId: contractId,
                CostItemId: costItemId,
                Amount: amount,
                ActAmount: actAmount,
                AdvanceOffset: advanceOffset,
                RetentionAmount: retentionAmount,
                VatIncluded: vatIncluded,
                Currency: string.IsNullOrWhiteSpace(currency) ? "BYN" : currency.Trim().ToUpperInvariant(),
                Type: type,
                PlannedPaymentDate: plannedPaymentDate == default ? new DateTime(2026, 11, 15) : plannedPaymentDate,
                Purpose: string.IsNullOrWhiteSpace(purpose) ? "Оплата по договору" : purpose,
                DocumentType: type == RequestType.Advance ? DocumentType.Contract : DocumentType.ActS2,
                DocumentNumber: string.IsNullOrWhiteSpace(documentNumber) ? $"С-2/{DateTime.UtcNow:HHmmss}" : documentNumber,
                DocumentDate: new DateTime(2026, 11, 10),
                FilePath: null,
                IdempotencyKey: $"WEB-{Guid.NewGuid():N}",
                SubmitImmediately: submitImmediately);

            var (uid, login) = CurrentUser();
            var (created, checks) = await _requestService.CreateAsync(dto, uid, login, ct);
            var msg = $"Заявка {created.Number} создана в статусе {created.Status}. " + string.Join(" | ", checks.Messages);
            if (created.Status == RequestStatus.Blocked)
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
            TempData["Success"] = $"По заявке {req.Number} зафиксировано решение руководителя: {req.Status}.";
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
    [Authorize(Roles = "ProjectManager,SiteWorker,Accountant,ProcurementSpecialist")]
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
        CancellationToken ct)
    {
        try
        {
            decimal payable = FinancialCalculator.CalculateActPayable(grossAmount, advanceOffset, retentionAmount);
            var doc = new Document
            {
                ContractId = contractId,
                ConstructionObjectId = constructionObjectId,
                Type = documentType == DocumentType.CertificateS3 ? DocumentType.CertificateS3 : DocumentType.ActS2,
                Number = string.IsNullOrWhiteSpace(number) ? $"С-2/{DateTime.UtcNow:MMdd-HHmm}" : number.Trim(),
                Date = new DateTime(2026, 11, 10),
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
            var (uid, login) = CurrentUser();
            await _audit.LogAsync(uid, login, nameof(Document), doc.Number, "CREATE_ACT_S2", null,
                $"Gross={grossAmount:N2};Payable={payable:N2}", actWorkDescription, ct);
            await _db.SaveChangesAsync(ct);

            TempData["Success"] = $"Акт {doc.Number} зарегистрирован: к оплате = {grossAmount:N2} − {advanceOffset:N2} − {retentionAmount:N2} = {payable:N2} тыс. р.";
        }
        catch (DomainException ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }
}
