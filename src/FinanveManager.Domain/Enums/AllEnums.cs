namespace FinanveManager.Domain.Enums;

public enum RequestType
{
    Advance = 1,        // Новый аванс
    CurrentPayment = 2  // Текущий платёж по акту / поставке
}

/// <summary>
/// Состояния платёжной заявки (таблица 1.11, рис. 2.11, рис. 2.18).
/// </summary>
public enum RequestStatus
{
    Draft = 0,               // Черновик
    UnderReview = 1,         // На проверке
    Returned = 2,            // Возвращена на доработку
    Blocked = 3,             // Заблокирована (перерасход / просроченный аванс)
    PendingApproval = 4,     // На согласовании (финансовый менеджер)
    ApprovedByManagement = 5,// Перерасход согласован руководителем организации
    Approved = 6,            // Согласована к оплате
    Executed = 7,            // Исполнена (платёж проведён бухгалтером)
    Closed = 8,              // Закрыта (подтверждена банковской выпиской)
    Rejected = 9             // Отклонена
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

/// <summary>
/// Состояния риска (рис. 2.19, СТБ ISO 31000-2020).
/// </summary>
public enum RiskStatus
{
    Identified = 0,  // Идентифицирован
    Analyzed = 1,    // Проанализирован
    Treating = 2,    // Обрабатывается
    Monitoring = 3,  // Мониторинг
    Realized = 4,    // Реализован
    Closed = 5       // Закрыт
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
