using FinanveManager.Domain.Common;
using FinanveManager.Domain.Enums;

namespace FinanveManager.Domain.Entities;

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
    public List<CostItem> CostItems { get; set; } = new();
}

public class CostItem : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<BudgetVersion> BudgetVersions { get; set; } = new();
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
            throw new DomainException("Утверждённая версия бюджета неизменяема. Создайте новую версию бюджета (правило 12).");
        if (newAmount < 0)
            throw new DomainException("Сумма лимита не может быть отрицательной.");
        Amount = newAmount;
        UpdatedAt = DateTime.UtcNow;
    }
}
