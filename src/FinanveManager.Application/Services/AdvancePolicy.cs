using FinanveManager.Application.DTOs;
using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;

namespace FinanveManager.Application.Services;

public class AdvancePolicy : IAdvancePolicy
{
    private readonly IRepository<Advance> _advanceRepo;

    public AdvancePolicy(IRepository<Advance> advanceRepo)
    {
        _advanceRepo = advanceRepo;
    }

    public bool IsOverdue(Advance advance, DateTime asOfDate)
    {
        return !advance.IsDeleted
               && advance.Status != AdvanceStatus.Closed
               && advance.IsOverdueAt(asOfDate);
    }

    public bool BlockNewAdvance(IEnumerable<Advance> advances, RequestType requestType, DateTime asOfDate)
    {
        if (requestType != RequestType.Advance)
            return false;

        return advances.Any(a => IsOverdue(a, asOfDate));
    }

    public async Task<AdvanceCheckResultDto> CheckAsync(
        Guid contractId, RequestType requestType, DateTime asOfDate, CancellationToken ct = default)
    {
        var all = await _advanceRepo.GetAllAsync(ct);
        var contractAdvances = all.Where(a => !a.IsDeleted && a.ContractId == contractId).ToList();
        var overdueList = contractAdvances.Where(a => IsOverdue(a, asOfDate)).ToList();

        if (overdueList.Count == 0)
        {
            return new AdvanceCheckResultDto(
                HasOverdueAdvance: false,
                ShouldBlockRequest: false,
                WarningMessage: null,
                ContractOverdueAmount: 0m);
        }

        decimal overdueRemainder = overdueList.Sum(a => a.Remainder);
        bool shouldBlock = BlockNewAdvance(contractAdvances, requestType, asOfDate);

        string message = shouldBlock
            ? $"Выдача нового аванса заблокирована: по договору имеется просроченный аванс (незакрытый остаток {overdueRemainder:N2} тыс. р.)."
            : $"Предупреждение: по договору имеется просроченный аванс ({overdueRemainder:N2} тыс. р.). Текущий платёж по акту допускается с уведомлением.";

        return new AdvanceCheckResultDto(
            HasOverdueAdvance: true,
            ShouldBlockRequest: shouldBlock,
            WarningMessage: message,
            ContractOverdueAmount: overdueRemainder);
    }
}
