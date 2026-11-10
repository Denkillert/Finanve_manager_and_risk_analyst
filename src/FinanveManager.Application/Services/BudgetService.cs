using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;
using FinanveManager.Domain.Services;

namespace FinanveManager.Application.Services;

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

        var versions = await _versionRepo.GetAllAsync(ct);
        var approvedVersion = versions
            .Where(v => !v.IsDeleted && v.ProjectId == obj.ProjectId && v.Status == BudgetVersionStatus.Approved)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefault();

        decimal budgetB = 0m;
        if (approvedVersion != null)
        {
            var limits = await _limitRepo.GetAllAsync(ct);
            budgetB = limits
                .Where(l => !l.IsDeleted && l.BudgetVersionId == approvedVersion.Id
                            && l.ConstructionObjectId == constructionObjectId && l.CostItemId == costItemId)
                .Sum(l => l.Amount);
        }

        var obligations = (await _obligationRepo.GetAllAsync(ct))
            .Where(o => !o.IsDeleted && o.ConstructionObjectId == constructionObjectId && o.CostItemId == costItemId)
            .ToList();

        decimal grossO = obligations.Sum(o => o.GrossAmount);
        decimal paidF = obligations.Sum(o => o.PaidAmount);

        var requests = await _requestRepo.GetAllAsync(ct);
        decimal submittedQ = requests
            .Where(r => !r.IsDeleted && r.ConstructionObjectId == constructionObjectId && r.CostItemId == costItemId
                        && r.Status is RequestStatus.UnderReview or RequestStatus.Blocked or RequestStatus.PendingApproval or RequestStatus.ApprovedByManagement or RequestStatus.Approved)
            .Sum(r => r.AmountInByn);

        return FinancialCalculator.CalculateFreeLimit(budgetB, grossO, paidF, submittedQ);
    }

    public async Task<decimal> CalculateObligationRemainderAsync(
        Guid constructionObjectId, Guid costItemId, CancellationToken ct = default)
    {
        var result = await CalculateFreeLimitAsync(constructionObjectId, costItemId, ct);
        return result.ObligationRemainder;
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

        var projectVersions = (await _versionRepo.GetAllAsync(ct))
            .Where(v => !v.IsDeleted && v.ProjectId == version.ProjectId && v.Id != version.Id && v.Status == BudgetVersionStatus.Approved)
            .ToList();

        foreach (var prev in projectVersions)
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
