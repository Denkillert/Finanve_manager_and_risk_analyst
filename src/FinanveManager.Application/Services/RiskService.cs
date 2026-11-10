using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;
using FinanveManager.Domain.Services;

namespace FinanveManager.Application.Services;

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

        foreach (var risk in risks)
            RiskCalculator.RecalculateScores(risk);

        return Math.Round(risks.Sum(r => r.Emv), 2);
    }

    public async Task<RiskPortfolioSummary> CalculateReserveP80Async(Guid projectId, CancellationToken ct = default)
    {
        var risks = (await _riskRepo.GetAllAsync(ct))
            .Where(r => !r.IsDeleted && r.ProjectId == projectId)
            .ToList();

        foreach (var risk in risks)
            RiskCalculator.RecalculateScores(risk);

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
        else if (risk.IntegralScore < 10 && risk.Status == RiskStatus.Analyzed)
            risk.Status = RiskStatus.Monitoring;

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
                Title = $"Высокий/критический риск {risk.Code} (R={risk.IntegralScore})",
                Message = $"{risk.Event}: EMV={risk.Emv:N2} тыс. р., резерв проекта P80={summary.ReserveP80:N2} тыс. р.",
                Severity = risk.IntegralScore >= 17 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
                Category = NotificationCategory.RiskTrigger,
                EntityLink = $"/risks/{risk.Id}"
            }, ct);
        }

        await _auditService.LogAsync(userId, userLogin, nameof(Risk), risk.Id.ToString(),
            "UPDATE_RISK_ASSESSMENT", oldSnapshot, $"v{risk.CurrentAssessmentVersion}:p={risk.Probability},EMV={risk.Emv}", comment, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return risk;
    }
}
