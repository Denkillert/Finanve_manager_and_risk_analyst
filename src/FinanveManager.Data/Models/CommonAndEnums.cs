namespace FinanveManager.Data.Models;

public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public class DomainException : Exception
{
    public string ErrorCode { get; }

    public DomainException(string message, string errorCode = "DOMAIN_ERROR")
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

public class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string message = "Данные были изменены другим пользователем. Обновите экран и повторите операцию.")
        : base(message, "CONCURRENCY_CONFLICT")
    {
    }
}

public class DuplicateEntityException : DomainException
{
    public DuplicateEntityException(string message)
        : base(message, "DUPLICATE_DETECTED")
    {
    }
}

public enum RequestType
{
    Advance = 1,
    CurrentPayment = 2
}

public enum RequestStatus
{
    Draft = 0,
    UnderReview = 1,
    Returned = 2,
    Blocked = 3,
    PendingApproval = 4,
    ApprovedByManagement = 5,
    Approved = 6,
    Executed = 7,
    Closed = 8,
    Rejected = 9
}

public enum DocumentType
{
    Contract = 1,
    ActS2 = 2,
    CertificateS3 = 3,
    Invoice = 4,
    BankStatement = 5,
    Specification = 6
}

public enum ObligationStatus
{
    Open = 0,
    PartiallyPaid = 1,
    Closed = 2
}

public enum AdvanceStatus
{
    Active = 0,
    Overdue = 1,
    Closed = 2
}

public enum BudgetVersionStatus
{
    Draft = 0,
    Approved = 1,
    Archived = 2
}

public enum ContractDirection
{
    CustomerIncoming = 1,
    SupplierSubcontractorOutgoing = 2
}

public enum RiskStatus
{
    Identified = 0,
    Analyzed = 1,
    Treating = 2,
    Monitoring = 3,
    Realized = 4,
    Closed = 5
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum NotificationCategory
{
    RiskTrigger = 0,
    CashGap = 1,
    ApprovalDue = 2,
    OverdueAdvance = 3,
    System = 4
}
