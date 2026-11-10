using FinanveManager.Domain.Common;
using FinanveManager.Domain.Enums;

namespace FinanveManager.Domain.Entities;

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

    /// <summary>
    /// Доменный инвариант 1 (п. 2.3.1) и бизнес-правило 5:
    /// Сумма обязательств/платежей по договору не превышает его цену с учётом доп. соглашений.
    /// </summary>
    public void EnsureWithinContractLimit(decimal additionalGrossAmount)
    {
        if (TotalObligationsGross + additionalGrossAmount > EffectiveTotalAmount)
        {
            throw new DomainException(
                $"Превышена сумма договора {Number} с учётом доп. соглашений ({EffectiveTotalAmount:N2} {Currency}).",
                "CONTRACT_LIMIT_EXCEEDED");
        }
    }
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

    /// <summary>Формула (1.10): Aост = A - Aисп</summary>
    public decimal Remainder => Amount - UsedAmount;

    /// <summary>Формула (1.11): Kисп = (Aисп / A) * 100 %</summary>
    public decimal? UtilizationPercent => Amount == 0 ? null : Math.Round(UsedAmount / Amount * 100m, 2);

    public bool IsOverdueAt(DateTime referenceDate) =>
        referenceDate.Date > DueDate.Date && UsedAmount < Amount;

    /// <summary>
    /// Доменный инвариант 2 (п. 2.3.1): зачёт аванса не превышает сумму выданного аванса.
    /// </summary>
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

    /// <summary>
    /// Доменный инвариант 3 (п. 2.3.1, п. 2.3.4):
    /// Заявка не может быть исполнена без состояния «Согласована».
    /// </summary>
    public void EnsureCanExecute()
    {
        if (Status != RequestStatus.Approved)
            throw new DomainException(
                $"Исполнение запрещено: заявка {Number} находится в статусе {Status}, требуется статус Approved (Согласована).",
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
