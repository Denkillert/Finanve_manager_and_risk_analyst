using FinanveManager.Application.DTOs;
using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;
using FinanveManager.Domain.Services;

namespace FinanveManager.Application.Services;

/// <summary>
/// Сервис управления жизненным циклом платёжных заявок (UC01, UC04, табл. 1.10, табл. 1.11, рис. 2.14).
/// </summary>
public class PaymentRequestService : IPaymentRequestService
{
    private readonly IRepository<PaymentRequest> _requestRepo;
    private readonly IRepository<Contract> _contractRepo;
    private readonly IRepository<Document> _documentRepo;
    private readonly IRepository<Project> _projectRepo;
    private readonly IRepository<Payment> _paymentRepo;
    private readonly IRepository<Obligation> _obligationRepo;
    private readonly IBudgetService _budgetService;
    private readonly IAdvancePolicy _advancePolicy;
    private readonly IWorkflowEngine _workflow;
    private readonly ICurrencyRateProvider _rateProvider;
    private readonly IEdsSignatureService _edsService;
    private readonly INotificationPublisher _publisher;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;

    public PaymentRequestService(
        IRepository<PaymentRequest> requestRepo,
        IRepository<Contract> contractRepo,
        IRepository<Document> documentRepo,
        IRepository<Project> projectRepo,
        IRepository<Payment> paymentRepo,
        IRepository<Obligation> obligationRepo,
        IBudgetService budgetService,
        IAdvancePolicy advancePolicy,
        IWorkflowEngine workflow,
        ICurrencyRateProvider rateProvider,
        IEdsSignatureService edsService,
        INotificationPublisher publisher,
        IAuditService auditService,
        IUnitOfWork unitOfWork)
    {
        _requestRepo = requestRepo;
        _contractRepo = contractRepo;
        _documentRepo = documentRepo;
        _projectRepo = projectRepo;
        _paymentRepo = paymentRepo;
        _obligationRepo = obligationRepo;
        _budgetService = budgetService;
        _advancePolicy = advancePolicy;
        _workflow = workflow;
        _rateProvider = rateProvider;
        _edsService = edsService;
        _publisher = publisher;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
    }

    public async Task<(PaymentRequest Request, RequestCheckSummaryDto Checks)> CreateAsync(
        CreatePaymentRequestDto dto, Guid currentUserId, string currentLogin, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(dto.IdempotencyKey))
        {
            var existingByKey = (await _requestRepo.GetAllAsync(ct))
                .FirstOrDefault(r => !r.IsDeleted && r.IdempotencyKey == dto.IdempotencyKey);
            if (existingByKey != null)
            {
                var summary = await RunAutomatedChecksAsync(existingByKey, dto.DocumentType, dto.DocumentNumber, dto.DocumentDate, ct);
                return (existingByKey, summary);
            }
        }

        // Контроль дублей (правило 16, требование 23): один документ-основание оплачивается один раз
        var allDocs = await _documentRepo.GetAllAsync(ct);
        var duplicateDoc = allDocs.FirstOrDefault(d =>
            !d.IsDeleted && d.Type == dto.DocumentType
            && string.Equals(d.Number.Trim(), dto.DocumentNumber.Trim(), StringComparison.OrdinalIgnoreCase)
            && d.Date.Date == dto.DocumentDate.Date);

        if (duplicateDoc != null)
        {
            throw new DuplicateEntityException(
                $"Документ-основание {dto.DocumentType} № {dto.DocumentNumber} от {dto.DocumentDate:dd.MM.yyyy} уже зарегистрирован в системе (заявка ID: {duplicateDoc.PaymentRequestId}). Повторная оплата запрещена.");
        }

        decimal rate = await _rateProvider.GetRateToBynAsync(dto.Currency, dto.PlannedPaymentDate, ct);
        var vatBreakdown = FinancialCalculator.NormalizeVat(dto.Amount, dto.VatIncluded, 20m);
        decimal actGross = dto.ActAmount > 0 ? dto.ActAmount : dto.Amount;
        var payable = FinancialCalculator.CalculateActPayable(actGross, dto.AdvanceOffset, dto.RetentionAmount);
        decimal payableInByn = Math.Round(payable.PayableAmount * rate, 4);

        var request = new PaymentRequest
        {
            ProjectId = dto.ProjectId,
            ConstructionObjectId = dto.ConstructionObjectId,
            ContractId = dto.ContractId,
            CostItemId = dto.CostItemId,
            AuthorUserId = currentUserId,
            Number = $"ЗП-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpperInvariant()}",
            Amount = dto.Amount,
            ActAmount = actGross,
            AdvanceOffset = dto.AdvanceOffset,
            RetentionAmount = dto.RetentionAmount,
            PayableAmount = payable.PayableAmount,
            VatIncluded = dto.VatIncluded,
            VatRate = 20m,
            VatAmount = vatBreakdown.VatAmount,
            Currency = dto.Currency.ToUpperInvariant(),
            ExchangeRate = rate,
            AmountInByn = payableInByn,
            Type = dto.Type,
            Status = RequestStatus.Draft,
            PlannedPaymentDate = dto.PlannedPaymentDate.Date,
            Purpose = dto.Purpose,
            IdempotencyKey = dto.IdempotencyKey
        };

        var doc = new Document
        {
            PaymentRequestId = request.Id,
            ContractId = dto.ContractId,
            ConstructionObjectId = dto.ConstructionObjectId,
            Type = dto.DocumentType,
            Number = dto.DocumentNumber.Trim(),
            Date = dto.DocumentDate.Date,
            GrossAmount = vatBreakdown.GrossAmount,
            NetAmount = vatBreakdown.NetAmount,
            VatIncluded = dto.VatIncluded,
            Currency = request.Currency,
            AdvanceOffset = dto.AdvanceOffset,
            RetentionAmount = dto.RetentionAmount,
            FilePath = dto.FilePath,
            IsVerifiedByInstruction53 = dto.DocumentType is DocumentType.ActS2 or DocumentType.CertificateS3
        };

        await _requestRepo.AddAsync(request, ct);
        await _documentRepo.AddAsync(doc, ct);

        var checks = await RunAutomatedChecksAsync(request, dto.DocumentType, dto.DocumentNumber, dto.DocumentDate, ct);

        if (dto.SubmitImmediately)
        {
            await ApplyAutomatedWorkflowTransitionAsync(request, checks, ct);
        }

        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "CREATE_REQUEST", null, $"{request.Number}:{request.Status}:{request.PayableAmount} {request.Currency}", dto.Purpose, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return (request, checks);
    }

    public async Task<PaymentRequest> ResubmitAsync(
        Guid requestId, CreatePaymentRequestDto dto, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default)
    {
        var request = await _requestRepo.GetByIdAsync(requestId, ct)
            ?? throw new DomainException("Заявка не найдена.");

        if (rowVersion.Length > 0 && !request.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        if (request.Status is not (RequestStatus.Draft or RequestStatus.Returned))
            throw new DomainException("Повторная отправка возможна только из статуса «Черновик» или «Возвращена на доработку».");

        decimal rate = await _rateProvider.GetRateToBynAsync(dto.Currency, dto.PlannedPaymentDate, ct);
        var payable = FinancialCalculator.CalculateActPayable(dto.ActAmount > 0 ? dto.ActAmount : dto.Amount, dto.AdvanceOffset, dto.RetentionAmount);

        string oldState = $"{request.Status}:{request.PayableAmount}";
        request.ConstructionObjectId = dto.ConstructionObjectId;
        request.ContractId = dto.ContractId;
        request.CostItemId = dto.CostItemId;
        request.Amount = dto.Amount;
        request.ActAmount = dto.ActAmount > 0 ? dto.ActAmount : dto.Amount;
        request.AdvanceOffset = dto.AdvanceOffset;
        request.RetentionAmount = dto.RetentionAmount;
        request.PayableAmount = payable.PayableAmount;
        request.Currency = dto.Currency.ToUpperInvariant();
        request.ExchangeRate = rate;
        request.AmountInByn = Math.Round(payable.PayableAmount * rate, 4);
        request.Type = dto.Type;
        request.PlannedPaymentDate = dto.PlannedPaymentDate.Date;
        request.Purpose = dto.Purpose;
        request.Status = RequestStatus.UnderReview;

        var checks = await RunAutomatedChecksAsync(request, dto.DocumentType, dto.DocumentNumber, dto.DocumentDate, ct);
        await ApplyAutomatedWorkflowTransitionAsync(request, checks, ct);

        await _requestRepo.UpdateAsync(request, ct);
        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "RESUBMIT_REQUEST", oldState, $"{request.Status}:{request.PayableAmount}", null, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return request;
    }

    public async Task<PaymentRequest> ApproveOverrunAsync(
        Guid requestId, bool approve, string justificationOrReason, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default)
    {
        var request = await _requestRepo.GetByIdAsync(requestId, ct)
            ?? throw new DomainException("Заявка не найдена.");

        if (rowVersion.Length > 0 && !request.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        if (request.Status != RequestStatus.Blocked)
            throw new DomainException("Согласование перерасхода применимо только к заблокированной заявке (UC04).");

        if (!approve)
        {
            await _workflow.RejectAsync(request, justificationOrReason, ct);
            await _requestRepo.UpdateAsync(request, ct);
            await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
                "REJECT_OVERRUN", "Blocked", "Rejected", justificationOrReason, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return request;
        }

        if (string.IsNullOrWhiteSpace(justificationOrReason))
            throw new DomainException("Необходимо указать обоснование перерасхода (UC04).");

        request.IsOverrunApproved = true;
        request.OverrunJustification = justificationOrReason;

        // Шаг 5 табл. 1.10 и UC04 шаг 6: повторная проверка авансов и кассового разрыва!
        var advCheck = await _advancePolicy.CheckAsync(request.ContractId, request.Type, DateTime.UtcNow, ct);
        if (advCheck.ShouldBlockRequest)
        {
            await _workflow.BlockAsync(request, advCheck.WarningMessage ?? "Просроченный аванс", ct);
        }
        else
        {
            request.HasOverdueAdvanceWarning = advCheck.HasOverdueAdvance;
            await _workflow.SubmitForApprovalAsync(request, ct);
        }

        await _requestRepo.UpdateAsync(request, ct);
        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "APPROVE_OVERRUN", "Blocked", request.Status.ToString(), justificationOrReason, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return request;
    }

    public async Task<PaymentRequest> ApproveAsync(
        Guid requestId, string? edsCertificateThumbprint, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default)
    {
        var request = await _requestRepo.GetByIdAsync(requestId, ct)
            ?? throw new DomainException("Заявка не найдена.");

        if (rowVersion.Length > 0 && !request.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        if (!string.IsNullOrWhiteSpace(edsCertificateThumbprint))
        {
            var eds = _edsService.SignApproval(request.Number, currentLogin, edsCertificateThumbprint);
            request.EdsSignatureThumbprint = eds.CertificateThumbprint;
            request.EdsSignedAt = eds.SignedAt;
        }

        string prevStatus = request.Status.ToString();
        await _workflow.ApproveAsync(request, ct);
        await _requestRepo.UpdateAsync(request, ct);

        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "APPROVE_REQUEST", prevStatus, request.Status.ToString(),
            request.EdsSignatureThumbprint != null ? $"ЭЦП: {request.EdsSignatureThumbprint}" : "Без ЭЦП", ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return request;
    }

    public async Task<PaymentRequest> ReturnOrRejectAsync(
        Guid requestId, bool rejectPermanently, string reason, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default)
    {
        var request = await _requestRepo.GetByIdAsync(requestId, ct)
            ?? throw new DomainException("Заявка не найдена.");

        if (rowVersion.Length > 0 && !request.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        string prevStatus = request.Status.ToString();
        if (rejectPermanently)
            await _workflow.RejectAsync(request, reason, ct);
        else
            await _workflow.ReturnForRevisionAsync(request, reason, ct);

        await _requestRepo.UpdateAsync(request, ct);
        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            rejectPermanently ? "REJECT_REQUEST" : "RETURN_REQUEST", prevStatus, request.Status.ToString(), reason, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return request;
    }

    public async Task<Payment> ExecuteAsync(
        Guid requestId, string? paymentOrderNumber, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default)
    {
        var request = await _requestRepo.GetByIdAsync(requestId, ct)
            ?? throw new DomainException("Заявка не найдена.");

        if (rowVersion.Length > 0 && !request.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        await _workflow.MarkExecutedAsync(request, ct);

        decimal rate = await _rateProvider.GetRateToBynAsync(request.Currency, DateTime.UtcNow, ct);
        decimal execByn = Math.Round(request.PayableAmount * rate, 4);
        decimal fxDiff = execByn - request.AmountInByn;

        var payment = new Payment
        {
            PaymentRequestId = request.Id,
            BankOperationId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{request.Number}",
            PaymentDate = DateTime.UtcNow.Date,
            Amount = request.PayableAmount,
            Currency = request.Currency,
            ExchangeRate = rate,
            AmountInByn = execByn,
            FxDifference = fxDiff,
            Status = "Executed",
            PaymentOrderNumber = paymentOrderNumber ?? $"ПП-{DateTime.UtcNow:yyyyMMdd}-{request.Number[^4..]}"
        };

        await _paymentRepo.AddAsync(payment, ct);
        await _requestRepo.UpdateAsync(request, ct);
        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "EXECUTE_PAYMENT", "Approved", "Executed", $"Платёжное поручение {payment.PaymentOrderNumber}", ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return payment;
    }

    /// <summary>
    /// Автоматическое отклонение заблокированных заявок через 30 календарных дней (табл. 1.11, UC04 A2).
    /// </summary>
    public async Task<int> ExpireStaleBlockedRequestsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var requests = await _requestRepo.GetAllAsync(ct);
        var stale = requests
            .Where(r => !r.IsDeleted && r.Status == RequestStatus.Blocked
                        && r.BlockedAt.HasValue && (nowUtc - r.BlockedAt.Value).TotalDays >= 30.0)
            .ToList();

        foreach (var req in stale)
        {
            await _workflow.RejectAsync(req, "Истёк срок согласования перерасхода (30 календарных дней).", ct);
            await _requestRepo.UpdateAsync(req, ct);
            await _auditService.LogAsync(null, "SYSTEM", nameof(PaymentRequest), req.Id.ToString(),
                "AUTO_EXPIRE_BLOCKED", "Blocked", "Rejected", "30-day timeout", ct);
        }

        if (stale.Count > 0)
            await _unitOfWork.SaveChangesAsync(ct);

        return stale.Count;
    }

    private async Task<RequestCheckSummaryDto> RunAutomatedChecksAsync(
        PaymentRequest request, DocumentType docType, string docNumber, DateTime docDate, CancellationToken ct)
    {
        var messages = new List<string>();
        var contract = await _contractRepo.GetByIdAsync(request.ContractId, ct);
        bool contractValid = contract != null && !contract.IsDeleted && contract.ProjectId == request.ProjectId
                             && !string.IsNullOrWhiteSpace(docNumber);

        if (!contractValid)
        {
            messages.Add("Договор не найден, не относится к проекту или отсутствуют реквизиты документа-основания.");
        }
        else if (contract!.EffectiveTotalAmount < contract.TotalObligationsGross)
        {
            contractValid = false;
            messages.Add("Превышена общая сумма договора с учётом дополнительных соглашений (правило 5).");
        }

        var freeLimit = await _budgetService.CalculateFreeLimitAsync(request.ConstructionObjectId, request.CostItemId, ct);
        bool limitOk = freeLimit.FreeLimit >= request.AmountInByn;
        if (!limitOk)
        {
            messages.Add($"Недостаточно свободного лимита: Lдост = {freeLimit.FreeLimit:N2} тыс. р., заявка = {request.AmountInByn:N2} тыс. р. (Δ = {freeLimit.Deviation:N2}).");
        }

        var advCheck = await _advancePolicy.CheckAsync(request.ContractId, request.Type, DateTime.UtcNow, ct);
        if (advCheck.WarningMessage != null)
            messages.Add(advCheck.WarningMessage);

        var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct);
        decimal cashGap = 0m;
        DateTime? suggestedDate = null;
        if (project != null)
        {
            decimal projectedBalance = project.InitialCash - request.AmountInByn;
            if (projectedBalance < project.MinCashBalance)
            {
                cashGap = project.MinCashBalance - projectedBalance;
                suggestedDate = request.PlannedPaymentDate.AddDays(3);
                messages.Add($"Предупреждение: прогнозируется кассовый разрыв {cashGap:N2} тыс. р. на {request.PlannedPaymentDate:dd.MM.yyyy}. Рекомендуемая дата: {suggestedDate:dd.MM.yyyy}.");
            }
        }

        return new RequestCheckSummaryDto(
            ContractAndDocumentsValid: contractValid,
            FreeLimitSufficient: limitOk,
            FreeLimit: freeLimit.FreeLimit,
            Delta: freeLimit.Deviation,
            AdvanceCheckPassed: !advCheck.ShouldBlockRequest,
            HasOverdueAdvanceWarning: advCheck.HasOverdueAdvance,
            HasCashGapOnPlannedDate: cashGap > 0m,
            CashGapAmount: cashGap,
            SuggestedPaymentDate: suggestedDate,
            Messages: messages);
    }

    private async Task ApplyAutomatedWorkflowTransitionAsync(
        PaymentRequest request, RequestCheckSummaryDto checks, CancellationToken ct)
    {
        request.HasOverdueAdvanceWarning = checks.HasOverdueAdvanceWarning;

        if (!checks.ContractAndDocumentsValid)
        {
            await _workflow.ReturnForRevisionAsync(request, string.Join(" ", checks.Messages), ct);
            return;
        }

        if (!checks.AdvanceCheckPassed)
        {
            await _workflow.BlockAsync(request, string.Join(" ", checks.Messages), ct);
            await _publisher.PublishAsync(new Notification
            {
                TargetRoleCode = "FinancialManager",
                Title = $"Блокировка нового аванса ({request.Number})",
                Message = string.Join(" ", checks.Messages),
                Severity = NotificationSeverity.Warning,
                Category = NotificationCategory.OverdueAdvance,
                EntityLink = $"/requests/{request.Id}"
            }, ct);
            return;
        }

        if (!checks.FreeLimitSufficient)
        {
            await _workflow.BlockAsync(request, string.Join(" ", checks.Messages), ct);
            await _publisher.PublishAsync(new Notification
            {
                TargetRoleCode = "CEO",
                Title = $"Требуется согласование перерасхода ({request.Number})",
                Message = string.Join(" ", checks.Messages),
                Severity = NotificationSeverity.Critical,
                Category = NotificationCategory.ApprovalDue,
                EntityLink = $"/requests/{request.Id}"
            }, ct);
            return;
        }

        await _workflow.SubmitForApprovalAsync(request, ct);
        await _publisher.PublishAsync(new Notification
        {
            TargetRoleCode = "FinancialManager",
            Title = $"Новая заявка на согласование ({request.Number})",
            Message = $"Сумма к оплате: {request.PayableAmount:N2} {request.Currency}",
            Severity = checks.HasCashGapOnPlannedDate ? NotificationSeverity.Warning : NotificationSeverity.Info,
            Category = checks.HasCashGapOnPlannedDate ? NotificationCategory.CashGap : NotificationCategory.ApprovalDue,
            EntityLink = $"/requests/{request.Id}"
        }, ct);
    }
}
