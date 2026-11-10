using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Services;

namespace FinanveManager.Application.Strategies;

/// <summary>
/// Паттерн Strategy (рис. 2.12): расчёт резерва на риски при уровне обеспеченности P80 (z = 0,84).
/// </summary>
public class P80ReserveStrategy : IReserveStrategy
{
    public decimal Z => 0.84m;

    public decimal Calculate(IEnumerable<Risk> risks, out decimal totalEmv, out decimal sigma)
    {
        var summary = RiskCalculator.CalculatePortfolioSummary(risks, Z);
        totalEmv = summary.TotalEmv;
        sigma = summary.Sigma;
        return summary.ReserveP80;
    }
}
