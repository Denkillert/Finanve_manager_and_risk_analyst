using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;

namespace FinanveManager.Data.Services;

public class P80ReserveStrategy : IReserveStrategy
{
    public decimal Z => 0.84m;

    public decimal Calculate(IEnumerable<Risk> risks, out decimal totalEmv, out decimal sigma)
    {
        var summary = RiskCalculator.CalculatePortfolioSummary(risks, Z);
        totalEmv = summary.TotalEmv;
        sigma = summary.Sigma;
        return summary.ReserveP80;
    }
}

public class WorkflowEngine : IWorkflowEngine
{
    public Task SubmitForApprovalAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.Draft or RequestStatus.UnderReview or RequestStatus.Returned or RequestStatus.Blocked or RequestStatus.ApprovedByManagement))
            throw new DomainException($"Недопустимый переход в состояние «На согласовании» из {request.Status}.");

        request.Status = RequestStatus.PendingApproval;
        request.SubmittedAt ??= DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task BlockAsync(PaymentRequest request, string reason, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.Draft or RequestStatus.UnderReview or RequestStatus.PendingApproval or RequestStatus.Returned))
            throw new DomainException($"Недопустимая блокировка из состояния {request.Status}.");

        request.Status = RequestStatus.Blocked;
        request.BlockReason = reason;
        request.BlockedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task ApproveAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.PendingApproval or RequestStatus.ApprovedByManagement))
            throw new DomainException($"Согласование невозможно из состояния {request.Status}.");

        request.Status = RequestStatus.Approved;
        request.ApprovedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task ReturnForRevisionAsync(PaymentRequest request, string reason, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.UnderReview or RequestStatus.PendingApproval or RequestStatus.Draft))
            throw new DomainException($"Возврат на доработку невозможен из состояния {request.Status}.");

        request.Status = RequestStatus.Returned;
        request.ReturnOrRejectReason = reason;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task RejectAsync(PaymentRequest request, string reason, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.Blocked or RequestStatus.PendingApproval or RequestStatus.UnderReview))
            throw new DomainException($"Отклонение невозможно из состояния {request.Status}.");

        request.Status = RequestStatus.Rejected;
        request.ReturnOrRejectReason = reason;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task MarkExecutedAsync(PaymentRequest request, CancellationToken ct = default)
    {
        request.EnsureCanExecute();
        request.Status = RequestStatus.Executed;
        request.ExecutedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task CloseByStatementAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.Status != RequestStatus.Executed)
            throw new DomainException($"Закрытие по выписке возможно только из состояния «Исполнена» (текущее: {request.Status}).");

        request.Status = RequestStatus.Closed;
        request.ClosedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}

public class AdvancePolicy : IAdvancePolicy
{
    private readonly IRepository<Advance> _advanceRepo;

    public AdvancePolicy(IRepository<Advance> advanceRepo)
    {
        _advanceRepo = advanceRepo;
    }

    public static bool IsOverdue(Advance advance, DateTime asOfDate) =>
        !advance.IsDeleted && advance.Status != AdvanceStatus.Closed && advance.IsOverdueAt(asOfDate);

    public async Task<AdvanceCheckResultDto> CheckAsync(
        Guid contractId, RequestType requestType, DateTime asOfDate, CancellationToken ct = default)
    {
        var all = await _advanceRepo.GetAllAsync(ct);
        var overdueList = all.Where(a => !a.IsDeleted && a.ContractId == contractId && IsOverdue(a, asOfDate)).ToList();

        if (overdueList.Count == 0)
            return new AdvanceCheckResultDto(false, false, null, 0m);

        decimal overdueRemainder = overdueList.Sum(a => a.Remainder);
        bool shouldBlock = requestType == RequestType.Advance;
        string message = shouldBlock
            ? $"Выдача нового аванса заблокирована: просроченный остаток по договору {overdueRemainder:N2} тыс. р."
            : $"Предупреждение: имеется просроченный аванс ({overdueRemainder:N2} тыс. р.). Текущий платёж допускается с уведомлением.";

        return new AdvanceCheckResultDto(true, shouldBlock, message, overdueRemainder);
    }
}

public class BudgetService : IBudgetService
{
    private readonly IRepository<BudgetVersion> _versionRepo;
    private readonly IRepository<BudgetLimit> _limitRepo;
    private readonly IRepository<Obligation> _obligationRepo;
    private readonly IRepository<PaymentRequest> _requestRepo;
    private readonly IRepository<ConstructionObject> _objectRepo;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;

    public BudgetService(
        IRepository<BudgetVersion> versionRepo,
        IRepository<BudgetLimit> limitRepo,
        IRepository<Obligation> obligationRepo,
        IRepository<PaymentRequest> requestRepo,
        IRepository<ConstructionObject> objectRepo,
        IAuditService auditService,
        IUnitOfWork unitOfWork)
    {
        _versionRepo = versionRepo;
        _limitRepo = limitRepo;
        _obligationRepo = obligationRepo;
        _requestRepo = requestRepo;
        _objectRepo = objectRepo;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
    }

    public async Task<FreeLimitResult> CalculateFreeLimitAsync(
        Guid constructionObjectId, Guid costItemId, CancellationToken ct = default)
    {
        var obj = await _objectRepo.GetByIdAsync(constructionObjectId, ct)
            ?? throw new DomainException("Объект строительства не найден.");

        var approvedVersion = (await _versionRepo.GetAllAsync(ct))
            .Where(v => !v.IsDeleted && v.ProjectId == obj.ProjectId && v.Status == BudgetVersionStatus.Approved)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        decimal budgetB = 0m;
        if (approvedVersion != null)
        {
            budgetB = (await _limitRepo.GetAllAsync(ct))
                .Where(l => !l.IsDeleted && l.BudgetVersionId == approvedVersion.Id
                            && l.ConstructionObjectId == constructionObjectId && l.CostItemId == costItemId)
                .Sum(l => l.Amount);
        }

        var obligations = (await _obligationRepo.GetAllAsync(ct))
            .Where(o => !o.IsDeleted && o.ConstructionObjectId == constructionObjectId && o.CostItemId == costItemId)
            .ToList();

        decimal grossO = obligations.Sum(o => o.GrossAmount);
        decimal paidF = obligations.Sum(o => o.PaidAmount);

        decimal submittedQ = (await _requestRepo.GetAllAsync(ct))
            .Where(r => !r.IsDeleted && r.ConstructionObjectId == constructionObjectId && r.CostItemId == costItemId
                        && r.Status is RequestStatus.UnderReview or RequestStatus.Blocked or RequestStatus.PendingApproval or RequestStatus.ApprovedByManagement or RequestStatus.Approved)
            .Sum(r => r.AmountInByn);

        return FinancialCalculator.CalculateFreeLimit(budgetB, grossO, paidF, submittedQ);
    }

    public async Task<BudgetVersion> CreateNewVersionAsync(
        Guid projectId, string comment, Guid userId, string userLogin, CancellationToken ct = default)
    {
        var versions = (await _versionRepo.GetAllAsync(ct))
            .Where(v => !v.IsDeleted && v.ProjectId == projectId)
            .OrderByDescending(v => v.VersionNumber)
            .ToList();

        int nextNumber = (versions.FirstOrDefault()?.VersionNumber ?? 0) + 1;
        var newVersion = new BudgetVersion
        {
            ProjectId = projectId,
            VersionNumber = nextNumber,
            Status = BudgetVersionStatus.Draft,
            Comment = comment
        };
        await _versionRepo.AddAsync(newVersion, ct);

        var latest = versions.FirstOrDefault();
        if (latest != null)
        {
            var prevLimits = (await _limitRepo.GetAllAsync(ct))
                .Where(l => !l.IsDeleted && l.BudgetVersionId == latest.Id)
                .ToList();
            foreach (var pl in prevLimits)
            {
                await _limitRepo.AddAsync(new BudgetLimit
                {
                    BudgetVersionId = newVersion.Id,
                    ConstructionObjectId = pl.ConstructionObjectId,
                    WorkStageId = pl.WorkStageId,
                    CostItemId = pl.CostItemId,
                    PeriodStart = pl.PeriodStart,
                    PeriodEnd = pl.PeriodEnd,
                    Amount = pl.Amount
                }, ct);
            }
        }

        await _auditService.LogAsync(userId, userLogin, nameof(BudgetVersion), newVersion.Id.ToString(),
            "CREATE_BUDGET_VERSION", null, $"v{nextNumber}", comment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return newVersion;
    }

    public async Task<BudgetVersion> ApproveVersionAsync(
        Guid versionId, byte[] rowVersion, Guid userId, string userLogin, CancellationToken ct = default)
    {
        var version = await _versionRepo.GetByIdAsync(versionId, ct)
            ?? throw new DomainException("Версия бюджета не найдена.");

        if (rowVersion.Length > 0 && !version.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        var prevApproved = (await _versionRepo.GetAllAsync(ct))
            .Where(v => !v.IsDeleted && v.ProjectId == version.ProjectId && v.Id != version.Id && v.Status == BudgetVersionStatus.Approved)
            .ToList();

        foreach (var prev in prevApproved)
        {
            prev.Status = BudgetVersionStatus.Archived;
            prev.UpdatedAt = DateTime.UtcNow;
            await _versionRepo.UpdateAsync(prev, ct);
        }

        version.Approve(userId);
        await _versionRepo.UpdateAsync(version, ct);
        await _auditService.LogAsync(userId, userLogin, nameof(BudgetVersion), version.Id.ToString(),
            "APPROVE_BUDGET_VERSION", "Draft", $"Approved v{version.VersionNumber}", version.Comment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return version;
    }
}

public class PaymentRequestService : IPaymentRequestService
{
    private readonly IRepository<PaymentRequest> _requestRepo;
    private readonly IRepository<Contract> _contractRepo;
    private readonly IRepository<Document> _documentRepo;
    private readonly IRepository<Project> _projectRepo;
    private readonly IRepository<Payment> _paymentRepo;
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
                var summary = await RunAutomatedChecksAsync(existingByKey, dto.DocumentNumber, ct);
                return (existingByKey, summary);
            }
        }

        var allDocs = await _documentRepo.GetAllAsync(ct);
        var duplicateDoc = allDocs.FirstOrDefault(d =>
            !d.IsDeleted && d.Type == dto.DocumentType
            && string.Equals(d.Number.Trim(), dto.DocumentNumber.Trim(), StringComparison.OrdinalIgnoreCase)
            && d.Date.Date == dto.DocumentDate.Date);

        if (duplicateDoc != null)
        {
            throw new DuplicateEntityException(
                $"Документ {dto.DocumentType} № {dto.DocumentNumber} от {dto.DocumentDate:dd.MM.yyyy} уже зарегистрирован. Повторная оплата запрещена.");
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

        var checks = await RunAutomatedChecksAsync(request, dto.DocumentNumber, ct);
        if (dto.SubmitImmediately)
            await ApplyAutomatedWorkflowTransitionAsync(request, checks, ct);

        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "CREATE_REQUEST", null, $"{request.Number}:{request.Status}:{request.PayableAmount} {request.Currency}", dto.Purpose, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return (request, checks);
    }

    public async Task<(PaymentRequest Request, RequestCheckSummaryDto Checks)> SubmitAsync(
        Guid requestId, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default)
    {
        var request = await _requestRepo.GetByIdAsync(requestId, ct)
            ?? throw new DomainException("Заявка не найдена.");

        if (rowVersion.Length > 0 && !request.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        if (request.Status is not (RequestStatus.Draft or RequestStatus.Returned))
            throw new DomainException(
                $"Повторная отправка доступна только для черновиков и возвращённых заявок (текущий статус: {RussianLabels.RequestStatusName(request.Status)}).");

        var docs = (await _documentRepo.GetAllAsync(ct))
            .Where(d => !d.IsDeleted && d.PaymentRequestId == request.Id)
            .ToList();
        string docNumber = docs.FirstOrDefault()?.Number ?? request.Number;

        string prevStatus = request.Status.ToString();
        request.ReturnOrRejectReason = null;
        request.BlockReason = null;

        var checks = await RunAutomatedChecksAsync(request, docNumber, ct);
        await ApplyAutomatedWorkflowTransitionAsync(request, checks, ct);

        await _requestRepo.UpdateAsync(request, ct);
        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "RESUBMIT_REQUEST", prevStatus, request.Status.ToString(), request.Number, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return (request, checks);
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

        var advCheck = await _advancePolicy.CheckAsync(request.ContractId, request.Type, DateTime.UtcNow, ct);
        if (advCheck.ShouldBlockRequest)
            await _workflow.BlockAsync(request, advCheck.WarningMessage ?? "Просроченный аванс", ct);
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

        var payment = new Payment
        {
            PaymentRequestId = request.Id,
            BankOperationId = $"ORD-{DateTime.UtcNow:yyyyMMddHHmmss}-{request.Number}",
            PaymentDate = DateTime.UtcNow.Date,
            Amount = request.PayableAmount,
            Currency = request.Currency,
            ExchangeRate = rate,
            AmountInByn = execByn,
            FxDifference = execByn - request.AmountInByn,
            Status = "Executed",
            PaymentOrderNumber = string.IsNullOrWhiteSpace(paymentOrderNumber)
                ? $"ПП-{DateTime.UtcNow:yyyyMMdd}-{request.Number[^4..]}"
                : paymentOrderNumber.Trim()
        };

        await _paymentRepo.AddAsync(payment, ct);
        await _requestRepo.UpdateAsync(request, ct);
        await _auditService.LogAsync(currentUserId, currentLogin, nameof(PaymentRequest), request.Id.ToString(),
            "EXECUTE_PAYMENT", "Approved", "Executed", $"Платёжное поручение {payment.PaymentOrderNumber}", ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return payment;
    }

    public async Task<int> ExpireStaleBlockedRequestsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var stale = (await _requestRepo.GetAllAsync(ct))
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
        PaymentRequest request, string docNumber, CancellationToken ct)
    {
        var messages = new List<string>();
        var contract = await _contractRepo.GetByIdAsync(request.ContractId, ct);
        bool contractValid = contract != null && !contract.IsDeleted && contract.ProjectId == request.ProjectId
                             && !string.IsNullOrWhiteSpace(docNumber);

        if (!contractValid)
            messages.Add("Договор не найден, не относится к проекту или отсутствуют реквизиты документа.");

        var freeLimit = await _budgetService.CalculateFreeLimitAsync(request.ConstructionObjectId, request.CostItemId, ct);
        bool limitOk = freeLimit.FreeLimit >= request.AmountInByn;
        if (!limitOk)
            messages.Add($"Недостаточно свободного лимита: Lдост = {freeLimit.FreeLimit:N2} тыс. р., заявка = {request.AmountInByn:N2} тыс. р.");

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
                messages.Add($"Прогноз кассового разрыва: {cashGap:N2} тыс. р. Рекомендуемая дата: {suggestedDate:dd.MM.yyyy}.");
            }
        }

        return new RequestCheckSummaryDto(
            contractValid, limitOk, freeLimit.FreeLimit, freeLimit.Deviation,
            !advCheck.ShouldBlockRequest, advCheck.HasOverdueAdvance,
            cashGap > 0m, cashGap, suggestedDate, messages);
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

        if (!checks.AdvanceCheckPassed || !checks.FreeLimitSufficient)
        {
            await _workflow.BlockAsync(request, string.Join(" ", checks.Messages), ct);
            await _publisher.PublishAsync(new Notification
            {
                TargetRoleCode = !checks.FreeLimitSufficient ? "CEO" : "FinancialManager",
                Title = $"Блокировка заявки {request.Number}",
                Message = string.Join(" ", checks.Messages),
                Severity = !checks.FreeLimitSufficient ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                Category = !checks.FreeLimitSufficient ? NotificationCategory.ApprovalDue : NotificationCategory.OverdueAdvance,
                EntityLink = "/Requests"
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
            EntityLink = "/Requests"
        }, ct);
    }
}

public class RiskService : IRiskService
{
    private readonly IRepository<Risk> _riskRepo;
    private readonly IRepository<RiskAssessment> _assessmentRepo;
    private readonly IReserveStrategy _reserveStrategy;
    private readonly INotificationPublisher _publisher;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;

    public RiskService(
        IRepository<Risk> riskRepo,
        IRepository<RiskAssessment> assessmentRepo,
        IReserveStrategy reserveStrategy,
        INotificationPublisher publisher,
        IAuditService auditService,
        IUnitOfWork unitOfWork)
    {
        _riskRepo = riskRepo;
        _assessmentRepo = assessmentRepo;
        _reserveStrategy = reserveStrategy;
        _publisher = publisher;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
    }

    public async Task<decimal> CalculateEmvAsync(Guid projectId, CancellationToken ct = default)
    {
        var risks = (await _riskRepo.GetAllAsync(ct))
            .Where(r => !r.IsDeleted && r.ProjectId == projectId)
            .ToList();
        foreach (var r in risks) RiskCalculator.RecalculateScores(r);
        return Math.Round(risks.Sum(r => r.Emv), 2);
    }

    public async Task<RiskPortfolioSummary> CalculateReserveP80Async(Guid projectId, CancellationToken ct = default)
    {
        var risks = (await _riskRepo.GetAllAsync(ct))
            .Where(r => !r.IsDeleted && r.ProjectId == projectId)
            .ToList();
        foreach (var r in risks) RiskCalculator.RecalculateScores(r);
        return RiskCalculator.CalculatePortfolioSummary(risks, _reserveStrategy.Z);
    }

    public async Task<Risk> UpdateAssessmentAsync(
        Guid riskId, decimal probability, decimal monetaryImpact, decimal? residualProbability,
        string comment, byte[] rowVersion, Guid userId, string userLogin, CancellationToken ct = default)
    {
        var risk = await _riskRepo.GetByIdAsync(riskId, ct)
            ?? throw new DomainException("Риск не найден.");

        if (rowVersion.Length > 0 && !risk.RowVersion.SequenceEqual(rowVersion))
            throw new ConcurrencyConflictException();

        string oldSnapshot = $"p={risk.Probability},C={risk.MonetaryImpact},EMV={risk.Emv}";
        risk.Probability = probability;
        risk.MonetaryImpact = monetaryImpact;
        risk.ResidualProbability = residualProbability;
        RiskCalculator.RecalculateScores(risk);
        risk.CurrentAssessmentVersion += 1;
        if (risk.IntegralScore >= 10 && risk.Status is RiskStatus.Identified or RiskStatus.Analyzed)
            risk.Status = RiskStatus.Treating;
        risk.UpdatedAt = DateTime.UtcNow;
        await _riskRepo.UpdateAsync(risk, ct);

        var summary = await CalculateReserveP80Async(risk.ProjectId, ct);
        await _assessmentRepo.AddAsync(new RiskAssessment
        {
            RiskId = risk.Id,
            Version = risk.CurrentAssessmentVersion,
            Probability = risk.Probability,
            MonetaryImpact = risk.MonetaryImpact,
            Emv = risk.Emv,
            ResidualProbability = risk.ResidualProbability,
            ResidualEmv = risk.ResidualEmv,
            ProjectReserveP80Snapshot = summary.ReserveP80,
            AssessedAt = DateTime.UtcNow,
            AssessedByUserId = userId,
            Comment = comment
        }, ct);

        if (risk.IntegralScore >= 10)
        {
            await _publisher.PublishAsync(new Notification
            {
                TargetRoleCode = "CEO",
                Title = $"Высокий риск {risk.Code} (R={risk.IntegralScore})",
                Message = $"{risk.Event}: EMV={risk.Emv:N2} тыс. р., резерв P80={summary.ReserveP80:N2} тыс. р.",
                Severity = risk.IntegralScore >= 17 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                Category = NotificationCategory.RiskTrigger,
                EntityLink = "/Risks"
            }, ct);
        }

        await _auditService.LogAsync(userId, userLogin, nameof(Risk), risk.Id.ToString(),
            "UPDATE_RISK_ASSESSMENT", oldSnapshot, $"v{risk.CurrentAssessmentVersion}:p={risk.Probability},EMV={risk.Emv}", comment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return risk;
    }
}

public class StatementImportService
{
    private readonly IBankAdapter _bankAdapter;
    private readonly IRepository<Payment> _paymentRepo;
    private readonly IRepository<PaymentRequest> _requestRepo;
    private readonly IRepository<Obligation> _obligationRepo;
    private readonly IWorkflowEngine _workflow;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;

    public StatementImportService(
        IBankAdapter bankAdapter,
        IRepository<Payment> paymentRepo,
        IRepository<PaymentRequest> requestRepo,
        IRepository<Obligation> obligationRepo,
        IWorkflowEngine workflow,
        IAuditService auditService,
        IUnitOfWork unitOfWork)
    {
        _bankAdapter = bankAdapter;
        _paymentRepo = paymentRepo;
        _requestRepo = requestRepo;
        _obligationRepo = obligationRepo;
        _workflow = workflow;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
    }

    public async Task<StatementImportReportDto> ImportAsync(
        string fileContent, string fileName, Guid userId, string userLogin, CancellationToken ct = default)
    {
        var parsedOps = _bankAdapter.ParseStatement(fileContent, fileName);
        var existingPayments = await _paymentRepo.GetAllAsync(ct);
        var requests = await _requestRepo.GetAllAsync(ct);
        var obligations = await _obligationRepo.GetAllAsync(ct);

        int matched = 0;
        int duplicates = 0;
        var unmatched = new List<BankStatementOperationDto>();
        var warnings = new List<string>();

        foreach (var op in parsedOps)
        {
            if (existingPayments.Any(p => !p.IsDeleted && string.Equals(p.BankOperationId, op.BankOperationId, StringComparison.OrdinalIgnoreCase)))
            {
                duplicates++;
                warnings.Add($"Операция {op.BankOperationId} уже загружена ранее — повтор пропущен.");
                continue;
            }

            var req = requests.FirstOrDefault(r =>
                !r.IsDeleted
                && ((op.PaymentRequestNumber != null && r.Number.Equals(op.PaymentRequestNumber, StringComparison.OrdinalIgnoreCase))
                    || (r.Status == RequestStatus.Executed && Math.Abs(r.PayableAmount - op.Amount) < 0.01m)));

            if (req == null)
            {
                unmatched.Add(op);
                warnings.Add($"Платёж {op.BankOperationId} ({op.Amount:N2} {op.Currency}) не сопоставлен с заявкой.");
                continue;
            }

            if (req.Status == RequestStatus.Approved)
                await _workflow.MarkExecutedAsync(req, ct);

            if (req.Status == RequestStatus.Executed)
            {
                await _workflow.CloseByStatementAsync(req, ct);
                await _requestRepo.UpdateAsync(req, ct);
            }

            var payment = new Payment
            {
                PaymentRequestId = req.Id,
                BankOperationId = op.BankOperationId,
                PaymentDate = op.OperationDate.Date,
                Amount = op.Amount,
                Currency = op.Currency,
                ExchangeRate = req.ExchangeRate,
                AmountInByn = Math.Round(op.Amount * req.ExchangeRate, 4),
                Status = "Reconciled",
                PaymentOrderNumber = op.PaymentOrderNumber
            };
            await _paymentRepo.AddAsync(payment, ct);
            existingPayments.Add(payment);

            var obl = obligations.FirstOrDefault(o =>
                !o.IsDeleted && o.ContractId == req.ContractId
                && o.ConstructionObjectId == req.ConstructionObjectId
                && o.CostItemId == req.CostItemId && o.Status != ObligationStatus.Closed);
            if (obl != null)
            {
                decimal toApply = Math.Min(obl.Remainder, payment.AmountInByn);
                if (toApply > 0)
                {
                    obl.RegisterPayment(toApply);
                    await _obligationRepo.UpdateAsync(obl, ct);
                }
            }

            matched++;
        }

        await _auditService.LogAsync(userId, userLogin, "BankStatement", fileName, "IMPORT_BANK_STATEMENT",
            null, $"Matched={matched};Duplicates={duplicates};Unmatched={unmatched.Count}", null, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new StatementImportReportDto(parsedOps.Count, matched, duplicates, unmatched.Count, warnings, unmatched);
    }
}
