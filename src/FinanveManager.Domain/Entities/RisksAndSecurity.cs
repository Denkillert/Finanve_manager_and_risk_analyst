using FinanveManager.Domain.Common;
using FinanveManager.Domain.Enums;

namespace FinanveManager.Domain.Entities;

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
