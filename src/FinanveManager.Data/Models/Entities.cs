namespace FinanveManager.Data.Models;

public class Organization : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Unp { get; set; } = string.Empty;
    public List<Portfolio> Portfolios { get; set; } = new();
}

public class Portfolio : BaseEntity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Project> Projects { get; set; } = new();
}

public class Project : BaseEntity
{
    public Guid PortfolioId { get; set; }
    public Portfolio? Portfolio { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal ContractValue { get; set; }
    public string Currency { get; set; } = "BYN";
    public decimal InitialCash { get; set; }
    public decimal MinCashBalance { get; set; }
    public DateTime PlannedStartDate { get; set; }
    public DateTime PlannedEndDate { get; set; }

    public List<ConstructionObject> Objects { get; set; } = new();
    public List<Risk> Risks { get; set; } = new();
    public List<BudgetVersion> BudgetVersions { get; set; } = new();
    public List<Contract> Contracts { get; set; } = new();
    public List<ProjectAssignment> Assignments { get; set; } = new();
}

public class ProjectAssignment : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string ProjectRole { get; set; } = "Руководитель проекта (прораб)";
    public bool IsPrimaryManager { get; set; }
    public Guid? AssignedByUserId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class ConstructionObject : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string QueueName { get; set; } = "1-я очередь";
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public List<WorkStage> Stages { get; set; } = new();
}

public class WorkStage : BaseEntity
{
    public Guid ConstructionObjectId { get; set; }
    public ConstructionObject? ConstructionObject { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime PlannedStartDate { get; set; }
    public DateTime PlannedEndDate { get; set; }
    public decimal PlannedValuePV { get; set; }
    public decimal EarnedValueEV { get; set; }
    public decimal ActualCostAC { get; set; }
}

public class CostItem : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}

public class BudgetVersion : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public int VersionNumber { get; set; }
    public BudgetVersionStatus Status { get; set; } = BudgetVersionStatus.Draft;
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public string? Comment { get; set; }
    public List<BudgetLimit> Limits { get; set; } = new();

    public void Approve(Guid userId)
    {
        if (Status == BudgetVersionStatus.Approved)
            throw new DomainException("Версия бюджета уже утверждена.");
        Status = BudgetVersionStatus.Approved;
        ApprovedByUserId = userId;
        ApprovedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class BudgetLimit : BaseEntity
{
    public Guid BudgetVersionId { get; set; }
    public BudgetVersion? BudgetVersion { get; set; }
    public Guid ConstructionObjectId { get; set; }
    public ConstructionObject? ConstructionObject { get; set; }
    public Guid? WorkStageId { get; set; }
    public Guid CostItemId { get; set; }
    public CostItem? CostItem { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public decimal Amount { get; set; }

    public void UpdateAmount(decimal newAmount, BudgetVersionStatus versionStatus)
    {
        if (versionStatus == BudgetVersionStatus.Approved)
            throw new DomainException("Утверждённая версия бюджета неизменяема. Создайте новую черновую версию (правило 12).");
        if (newAmount < 0)
            throw new DomainException("Сумма лимита не может быть отрицательной.");
        Amount = newAmount;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class Contract : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public string CounterpartyUnp { get; set; } = string.Empty;
    public ContractDirection Direction { get; set; } = ContractDirection.SupplierSubcontractorOutgoing;
    public decimal TotalAmount { get; set; }
    public bool VatIncluded { get; set; } = true;
    public decimal VatRate { get; set; } = 20m;
    public string Currency { get; set; } = "BYN";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal RetentionPercent { get; set; } = 5m;
    public decimal PenaltyRatePerDay { get; set; } = 0.1m;
    public decimal MaxPenaltyPercent { get; set; } = 10m;
    public string? SpecificationNotes { get; set; }

    public List<ContractSupplement> Supplements { get; set; } = new();
    public List<ContractScheduleItem> PaymentSchedule { get; set; } = new();
    public List<Obligation> Obligations { get; set; } = new();
    public List<Advance> Advances { get; set; } = new();

    public decimal EffectiveTotalAmount => TotalAmount + Supplements.Where(s => !s.IsDeleted).Sum(s => s.AmountDelta);
    public decimal TotalObligationsGross => Obligations.Where(o => !o.IsDeleted).Sum(o => o.GrossAmount);
}

public class ContractSupplement : BaseEntity
{
    public Guid ContractId { get; set; }
    public Contract? Contract { get; set; }
    public string Number { get; set; } = string.Empty;
    public decimal AmountDelta { get; set; }
    public DateTime SignedDate { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ContractScheduleItem : BaseEntity
{
    public Guid ContractId { get; set; }
    public Contract? Contract { get; set; }
    public DateTime PlannedDate { get; set; }
    public decimal Amount { get; set; }
    public bool IsIncoming { get; set; }
    public bool IsPaid { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class Obligation : BaseEntity
{
    public Guid ContractId { get; set; }
    public Contract? Contract { get; set; }
    public Guid ConstructionObjectId { get; set; }
    public Guid CostItemId { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public ObligationStatus Status { get; set; } = ObligationStatus.Open;

    public decimal Remainder => GrossAmount - PaidAmount;

    public void RegisterPayment(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Сумма оплаты по обязательству должна быть положительной.");
        if (PaidAmount + amount > GrossAmount)
            throw new DomainException("Оплаченная сумма не может превышать валовую сумму обязательства.");

        PaidAmount += amount;
        Status = PaidAmount >= GrossAmount ? ObligationStatus.Closed : ObligationStatus.PartiallyPaid;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class Advance : BaseEntity
{
    public Guid ContractId { get; set; }
    public Contract? Contract { get; set; }
    public Guid ConstructionObjectId { get; set; }
    public Guid CostItemId { get; set; }
    public decimal Amount { get; set; }
    public decimal UsedAmount { get; set; }
    public DateTime IssuedDate { get; set; }
    public DateTime DueDate { get; set; }
    public AdvanceStatus Status { get; set; } = AdvanceStatus.Active;
    public string FundingSource { get; set; } = "Собственные средства";

    public decimal Remainder => Amount - UsedAmount;
    public decimal? UtilizationPercent => Amount == 0 ? null : Math.Round(UsedAmount / Amount * 100m, 2);

    public bool IsOverdueAt(DateTime referenceDate) =>
        referenceDate.Date > DueDate.Date && UsedAmount < Amount;

    public void ApplyOffset(decimal offsetAmount)
    {
        if (offsetAmount < 0)
            throw new DomainException("Зачёт аванса не может быть отрицательным.");
        if (UsedAmount + offsetAmount > Amount)
            throw new DomainException("Зачёт аванса не может превышать остаток выданного аванса.", "ADVANCE_OFFSET_EXCEEDED");

        UsedAmount += offsetAmount;
        if (UsedAmount >= Amount)
            Status = AdvanceStatus.Closed;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class MaterialRequest : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid ConstructionObjectId { get; set; }
    public ConstructionObject? ConstructionObject { get; set; }
    public Guid AuthorUserId { get; set; }
    public Guid? CostItemId { get; set; }
    public CostItem? CostItem { get; set; }
    public Guid? ContractId { get; set; }
    public Contract? Contract { get; set; }

    public string Number { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string Unit { get; set; } = "шт.";
    public decimal Quantity { get; set; }
    public decimal EstimatedUnitPrice { get; set; }
    public decimal EstimatedTotal { get; set; }
    public DateTime RequiredDate { get; set; }
    public string? Purpose { get; set; }
    public string? SpecificationNotes { get; set; }

    public MaterialRequestStatus Status { get; set; } = MaterialRequestStatus.Draft;
    public string? SupplierName { get; set; }
    public decimal? AgreedUnitPrice { get; set; }
    public decimal? AgreedTotal { get; set; }
    public string? SupplyComment { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? OrderedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? RejectedAt { get; set; }

    public void RecalculateTotals()
    {
        if (Quantity < 0)
            throw new DomainException("Количество материала не может быть отрицательным.");
        if (EstimatedUnitPrice < 0)
            throw new DomainException("Ориентировочная цена не может быть отрицательной.");
        EstimatedTotal = Math.Round(Quantity * EstimatedUnitPrice, 2);
        AgreedTotal = AgreedUnitPrice.HasValue ? Math.Round(Quantity * AgreedUnitPrice.Value, 2) : null;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class PaymentRequest : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConstructionObjectId { get; set; }
    public Guid ContractId { get; set; }
    public Contract? Contract { get; set; }
    public Guid CostItemId { get; set; }
    public CostItem? CostItem { get; set; }
    public Guid? BudgetVersionId { get; set; }
    public Guid AuthorUserId { get; set; }

    public string Number { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal ActAmount { get; set; }
    public decimal AdvanceOffset { get; set; }
    public decimal RetentionAmount { get; set; }
    public decimal PayableAmount { get; set; }
    public bool VatIncluded { get; set; } = true;
    public decimal VatRate { get; set; } = 20m;
    public decimal VatAmount { get; set; }
    public string Currency { get; set; } = "BYN";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal AmountInByn { get; set; }

    public RequestType Type { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Draft;
    public DateTime PlannedPaymentDate { get; set; }
    public string? Purpose { get; set; }
    public string? BlockReason { get; set; }
    public string? ReturnOrRejectReason { get; set; }
    public string? OverrunJustification { get; set; }
    public bool HasOverdueAdvanceWarning { get; set; }
    public bool IsOverrunApproved { get; set; }
    public string? EdsSignatureThumbprint { get; set; }
    public DateTime? EdsSignedAt { get; set; }
    public string? IdempotencyKey { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? BlockedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public List<Document> Documents { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();

    public void EnsureCanExecute()
    {
        if (Status != RequestStatus.Approved)
            throw new DomainException(
                $"Исполнение запрещено: заявка {Number} находится в статусе {Status}, требуется статус Approved.",
                "INVALID_STATE_FOR_EXECUTION");
    }
}

public class Document : BaseEntity
{
    public Guid? PaymentRequestId { get; set; }
    public Guid? ContractId { get; set; }
    public Guid? ConstructionObjectId { get; set; }
    public DocumentType Type { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal NetAmount { get; set; }
    public bool VatIncluded { get; set; } = true;
    public string Currency { get; set; } = "BYN";
    public decimal AdvanceOffset { get; set; }
    public decimal RetentionAmount { get; set; }
    public string? FilePath { get; set; }
    public string? PhotoEvidencePaths { get; set; }
    public string? ActWorkDescription { get; set; }
    public string? ContractorSigner { get; set; }
    public string? CustomerSigner { get; set; }
    public bool IsVerifiedByInstruction53 { get; set; }
}

public class Payment : BaseEntity
{
    public Guid PaymentRequestId { get; set; }
    public PaymentRequest? PaymentRequest { get; set; }
    public string BankOperationId { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "BYN";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal AmountInByn { get; set; }
    public decimal FxDifference { get; set; }
    public string Status { get; set; } = "Executed";
    public string? PaymentOrderNumber { get; set; }
    public string? CounterpartyAccount { get; set; }
}

public class CurrencyRate : BaseEntity
{
    public string Currency { get; set; } = "BYN";
    public DateTime RateDate { get; set; }
    public decimal RateToByn { get; set; }
}

public class NormRule : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DocumentSource { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public decimal? NumericValue { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string ResponsibleRole { get; set; } = string.Empty;
    public int Revision { get; set; } = 1;
}

public class Risk : BaseEntity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string Cause { get; set; } = string.Empty;
    public string Consequence { get; set; } = string.Empty;
    public decimal Probability { get; set; }
    public decimal MonetaryImpact { get; set; }
    public int ProbabilityScore { get; set; }
    public int ImpactScore { get; set; }
    public int IntegralScore { get; set; }
    public decimal Emv { get; set; }
    public string OwnerRole { get; set; } = string.Empty;
    public string Mitigation { get; set; } = string.Empty;
    public string EarlyIndicator { get; set; } = string.Empty;
    public bool TriggerFired { get; set; }
    public decimal? ResidualProbability { get; set; }
    public decimal? ResidualEmv { get; set; }
    public RiskStatus Status { get; set; } = RiskStatus.Identified;
    public Guid? LinkedContractId { get; set; }
    public Guid? LinkedCostItemId { get; set; }
    public int CurrentAssessmentVersion { get; set; } = 1;

    public List<RiskAssessment> Assessments { get; set; } = new();
}

public class RiskAssessment : BaseEntity
{
    public Guid RiskId { get; set; }
    public Risk? Risk { get; set; }
    public int Version { get; set; }
    public decimal Probability { get; set; }
    public decimal MonetaryImpact { get; set; }
    public decimal Emv { get; set; }
    public decimal? ResidualProbability { get; set; }
    public decimal? ResidualEmv { get; set; }
    public decimal ProjectReserveP80Snapshot { get; set; }
    public DateTime AssessedAt { get; set; } = DateTime.UtcNow;
    public Guid AssessedByUserId { get; set; }
    public string? Comment { get; set; }
}

public class Role : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClientType { get; set; } = "Web";
    public string? Description { get; set; }
}

public class User : BaseEntity
{
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public string Login { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public string? TwoFactorSecret { get; set; }
    public bool PersonalDataConsentGiven { get; set; }
    public DateTime? ConsentDate { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public Guid? UserId { get; set; }
    public string UserLogin { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ObjectName { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Details { get; set; }
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TargetUserId { get; set; }
    public string? TargetRoleCode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationSeverity Severity { get; set; }
    public NotificationCategory Category { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? EntityLink { get; set; }
}
