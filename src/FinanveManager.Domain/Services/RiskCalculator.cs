using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;

namespace FinanveManager.Domain.Services;

public record RiskPortfolioSummary(
    decimal TotalEmv,
    decimal Sigma,
    decimal ReserveP80,
    decimal TotalResidualEmv,
    decimal EmvReduction,
    int HighAndCriticalCount);

public record SensitivityFactorResult(
    string FactorName,
    decimal BaseAmount,
    string RangeLabel,
    decimal ImpactOnEac,
    string Comment);

/// <summary>
/// Калькулятор количественной оценки рисков и резерва P80 (формулы 1.15–1.18, таблицы 1.15–1.18).
/// </summary>
public static class RiskCalculator
{
    public static int GetProbabilityScore(decimal probability)
    {
        if (probability < 0m || probability > 1m)
            throw new DomainException("Вероятность риска должна находиться в диапазоне [0; 1].");

        return probability switch
        {
            <= 0.10m => 1,
            <= 0.24m => 2,
            <= 0.50m => 3,
            <= 0.75m => 4,
            _ => 5
        };
    }

    public static int GetImpactScore(decimal monetaryImpact)
    {
        if (monetaryImpact < 0m)
            throw new DomainException("Денежная оценка последствий не может быть отрицательной.");

        return monetaryImpact switch
        {
            >= 600m => 5,
            >= 400m => 4,
            >= 250m => 3,
            >= 150m => 2,
            _ => 1
        };
    }

    public static string GetRiskZone(int integralScore) => integralScore switch
    {
        <= 4 => "Низкий",
        <= 9 => "Умеренный",
        <= 16 => "Высокий",
        _ => "Критический"
    };

    public static void RecalculateScores(Risk risk)
    {
        risk.ProbabilityScore = GetProbabilityScore(risk.Probability);
        risk.ImpactScore = GetImpactScore(risk.MonetaryImpact);
        risk.IntegralScore = risk.ProbabilityScore * risk.ImpactScore;
        risk.Emv = Math.Round(risk.Probability * risk.MonetaryImpact, 4, MidpointRounding.AwayFromZero);
        if (risk.ResidualProbability.HasValue)
        {
            risk.ResidualEmv = Math.Round(risk.ResidualProbability.Value * risk.MonetaryImpact, 4, MidpointRounding.AwayFromZero);
        }
    }

    public static RiskPortfolioSummary CalculatePortfolioSummary(IEnumerable<Risk> risks, decimal z = 0.84m)
    {
        var list = risks.Where(r => !r.IsDeleted).ToList();
        decimal totalEmv = list.Sum(r => Math.Round(r.Probability * r.MonetaryImpact, 4));
        double variance = list.Sum(r =>
        {
            double c = (double)r.MonetaryImpact;
            double p = (double)r.Probability;
            return c * c * p * (1.0 - p);
        });
        decimal sigma = Math.Round((decimal)Math.Sqrt(variance), 2, MidpointRounding.AwayFromZero);
        decimal reserveP80 = Math.Round(totalEmv + z * sigma, 2, MidpointRounding.AwayFromZero);

        decimal residualEmv = list.Sum(r =>
        {
            decimal rp = r.ResidualProbability ?? r.Probability;
            return Math.Round(rp * r.MonetaryImpact, 4);
        });

        int highCritical = list.Count(r => r.IntegralScore >= 10);

        return new RiskPortfolioSummary(
            TotalEmv: Math.Round(totalEmv, 2),
            Sigma: sigma,
            ReserveP80: reserveP80,
            TotalResidualEmv: Math.Round(residualEmv, 2),
            EmvReduction: Math.Round(totalEmv - residualEmv, 2),
            HighAndCriticalCount: highCritical);
    }

    /// <summary>
    /// Анализ чувствительности EAC по 4 факторам для диаграммы «торнадо» (таблица 1.18, требование 17).
    /// </summary>
    public static List<SensitivityFactorResult> CalculateEacSensitivity(
        decimal materialsBase = 4500m,
        decimal delayMonthlyOverhead = 250m,
        decimal issuedAdvancesBase = 1200m,
        decimal fxCostsBase = 900m)
    {
        var factors = new List<SensitivityFactorResult>
        {
            new("Цена материалов", materialsBase, "+10 %", Math.Round(materialsBase * 0.10m, 2), "Доля материалов в EAC"),
            new("Задержка на один месяц", delayMonthlyOverhead, "+1 месяц", Math.Round(delayMonthlyOverhead, 2), "Стоимость месяца простоя"),
            new("Курс валюты", fxCostsBase, "+10 %", Math.Round(fxCostsBase * 0.10m, 2), "Импортные материалы"),
            new("Просроченные авансы", issuedAdvancesBase, "+5 п. п.", Math.Round(issuedAdvancesBase * 0.05m, 2), "0,05 × выданные авансы")
        };

        return factors.OrderByDescending(f => Math.Abs(f.ImpactOnEac)).ToList();
    }
}
