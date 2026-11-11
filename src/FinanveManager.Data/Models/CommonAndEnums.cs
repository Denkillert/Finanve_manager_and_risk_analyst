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

public static class RussianLabels
{
    public static string RequestStatusName(RequestStatus status) => status switch
    {
        RequestStatus.Draft => "Черновик",
        RequestStatus.UnderReview => "На проверке",
        RequestStatus.Returned => "Возвращена на доработку",
        RequestStatus.Blocked => "Заблокирована",
        RequestStatus.PendingApproval => "На согласовании",
        RequestStatus.ApprovedByManagement => "Перерасход согласован",
        RequestStatus.Approved => "Согласована",
        RequestStatus.Executed => "Исполнена",
        RequestStatus.Closed => "Закрыта",
        RequestStatus.Rejected => "Отклонена",
        _ => status.ToString()
    };

    public static string RequestTypeName(RequestType type) => type switch
    {
        RequestType.Advance => "Новый аванс",
        RequestType.CurrentPayment => "Текущий платёж",
        _ => type.ToString()
    };

    public static string ContractDirectionName(ContractDirection dir) => dir switch
    {
        ContractDirection.CustomerIncoming => "Доходный (заказчик / ДДУ)",
        ContractDirection.SupplierSubcontractorOutgoing => "Расходный (подряд / поставка)",
        _ => dir.ToString()
    };

    public static string BudgetVersionStatusName(BudgetVersionStatus status) => status switch
    {
        BudgetVersionStatus.Draft => "Черновик",
        BudgetVersionStatus.Approved => "Утверждена",
        BudgetVersionStatus.Archived => "Архив",
        _ => status.ToString()
    };

    public static string RiskStatusName(RiskStatus status) => status switch
    {
        RiskStatus.Identified => "Идентифицирован",
        RiskStatus.Analyzed => "Проанализирован",
        RiskStatus.Treating => "Обрабатывается",
        RiskStatus.Monitoring => "Мониторинг",
        RiskStatus.Realized => "Реализован",
        RiskStatus.Closed => "Закрыт",
        _ => status.ToString()
    };

    public static string AdvanceStatusName(AdvanceStatus status, bool isOverdue = false) =>
        isOverdue ? "Просрочен" : status switch
        {
            AdvanceStatus.Active => "Активен",
            AdvanceStatus.Overdue => "Просрочен",
            AdvanceStatus.Closed => "Закрыт",
            _ => status.ToString()
        };

    public static string SeverityName(NotificationSeverity severity) => severity switch
    {
        NotificationSeverity.Info => "Инфо",
        NotificationSeverity.Warning => "Предупреждение",
        NotificationSeverity.Critical => "Критично",
        _ => severity.ToString()
    };
}

