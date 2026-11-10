namespace FinanveManager.Domain.Common;

/// <summary>
/// Базовая сущность доменной модели (рис. 2.11, п. 2.2.1).
/// Обеспечивает единый ключ Guid, аудит дат, мягкое удаление и оптимистическую блокировку.
/// </summary>
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
    public ConcurrencyConflictException(string message = "Данные были изменены другим пользователем. Перечитайте карточку и повторите операцию.")
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
