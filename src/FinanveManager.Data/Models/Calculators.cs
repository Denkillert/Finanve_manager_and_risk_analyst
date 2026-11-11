namespace FinanveManager.Data.Models;

public record FreeLimitResult(
    decimal ApprovedBudget,
    decimal GrossObligations,
    decimal PaidFact,
    decimal SubmittedRequests,
    decimal FreeLimit,
    decimal ObligationRemainder,
    decimal Deviation,
    decimal? DeviationPercent,
    bool HasMissingBaselinePlan);

public record ActPayableResult(
    decimal ActAmount,
    decimal AdvanceOffset,
    decimal RetentionAmount,
    decimal PayableAmount);

public record CashDayForecast(
    DateTime Date,
    int DayIndex,
    decimal OpeningBalance,
    decimal Inflows,
    decimal Outflows,
    decimal ClosingBalance,
    decimal CashGap);

public static class FinancialCalculator
{
    public static FreeLimitResult CalculateFreeLimit(
        decimal approvedBudgetB,
        decimal grossObligationsO,
        decimal paidFactF,
        decimal submittedRequestsQ = 0m)
    {
        if (approvedBudgetB < 0 || grossObligationsO < 0 || paidFactF < 0 || submittedRequestsQ < 0)
            throw new DomainException("Финансовые суммы для расчёта лимита не могут быть отрицательными.");

        decimal freeLimit = approvedBudgetB - grossObligationsO;
        decimal obligationRemainder = grossObligationsO - paidFactF;
        decimal deviation = grossObligationsO - approvedBudgetB;
        decimal? deviationPercent = approvedBudgetB == 0m
            ? null
            : Math.Round(deviation / approvedBudgetB * 100m, 2, MidpointRounding.AwayFromZero);

        return new FreeLimitResult(
            ApprovedBudget: Math.Round(approvedBudgetB, 4),
            GrossObligations: Math.Round(grossObligationsO, 4),
            PaidFact: Math.Round(paidFactF, 4),
            SubmittedRequests: Math.Round(submittedRequestsQ, 4),
            FreeLimit: Math.Round(freeLimit, 4),
            ObligationRemainder: Math.Round(obligationRemainder, 4),
            Deviation: Math.Round(deviation, 4),
            DeviationPercent: deviationPercent,
            HasMissingBaselinePlan: approvedBudgetB == 0m);
    }

    public static ActPayableResult CalculateActPayable(
        decimal actAmount,
        decimal advanceOffset,
        decimal retentionAmount)
    {
        if (actAmount < 0 || advanceOffset < 0 || retentionAmount < 0)
            throw new DomainException("Суммы акта, зачёта и удержания не могут быть отрицательными.");
        if (advanceOffset + retentionAmount > actAmount)
            throw new DomainException("Сумма зачёта аванса и гарантийного удержания превышает стоимость акта.");

        decimal payable = actAmount - advanceOffset - retentionAmount;
        return new ActPayableResult(actAmount, advanceOffset, retentionAmount, Math.Round(payable, 4));
    }

    public static List<CashDayForecast> CalculateCashCalendar(
        decimal initialCash,
        decimal minCashBalance,
        DateTime startDate,
        int horizonDays,
        IReadOnlyDictionary<DateTime, (decimal Inflows, decimal Outflows)> dailyFlows)
    {
        var result = new List<CashDayForecast>(horizonDays + 1);
        decimal running = initialCash;

        for (int day = 0; day <= horizonDays; day++)
        {
            DateTime date = startDate.Date.AddDays(day);
            decimal inflows = 0m;
            decimal outflows = 0m;
            if (day > 0 && dailyFlows.TryGetValue(date, out var flow))
            {
                inflows = flow.Inflows;
                outflows = flow.Outflows;
            }

            decimal opening = running;
            running = opening + inflows - outflows;
            decimal gap = Math.Max(0m, minCashBalance - running);

            result.Add(new CashDayForecast(
                Date: date,
                DayIndex: day,
                OpeningBalance: Math.Round(opening, 4),
                Inflows: Math.Round(inflows, 4),
                Outflows: Math.Round(outflows, 4),
                ClosingBalance: Math.Round(running, 4),
                CashGap: Math.Round(gap, 4)));
        }

        return result;
    }

    public static (decimal NetAmount, decimal VatAmount, decimal GrossAmount) NormalizeVat(
        decimal amount,
        bool vatIncluded,
        decimal vatRate = 20m)
    {
        if (amount < 0)
            throw new DomainException("Сумма не может быть отрицательной.");

        if (vatIncluded)
        {
            decimal net = Math.Round(amount / (1m + vatRate / 100m), 4, MidpointRounding.AwayFromZero);
            decimal vat = Math.Round(amount - net, 4, MidpointRounding.AwayFromZero);
            return (net, vat, amount);
        }

        decimal addedVat = Math.Round(amount * (vatRate / 100m), 4, MidpointRounding.AwayFromZero);
        return (amount, addedVat, amount + addedVat);
    }

    public static decimal CalculatePenalty(
        decimal obligationAmount,
        int overdueDays,
        decimal dailyRatePercent = 0.1m,
        decimal maxPercent = 10m)
    {
        if (overdueDays <= 0 || obligationAmount <= 0)
            return 0m;

        decimal rawPenalty = obligationAmount * (dailyRatePercent / 100m) * overdueDays;
        decimal cap = obligationAmount * (maxPercent / 100m);
        return Math.Round(Math.Min(rawPenalty, cap), 4, MidpointRounding.AwayFromZero);
    }
}

public record EarnedValueForecastResult(
    decimal Bac,
    decimal Pv,
    decimal Ev,
    decimal Ac,
    decimal ContractValue,
    decimal ReserveP80,
    decimal? Cpi,
    decimal? Spi,
    decimal Etc,
    decimal Eac,
    decimal FinancialResultWithoutReserve,
    decimal FinancialResultWithReserve,
    decimal MarginWithoutReservePercent,
    decimal MarginWithReservePercent,
    bool IsExpertEstimate,
    string? DataQualityWarning);

public static class EarnedValueCalculator
{
    public static EarnedValueForecastResult Calculate(
        decimal bac,
        decimal pv,
        decimal ev,
        decimal ac,
        decimal contractValue,
        decimal reserveP80,
        decimal? expertEtc = null)
    {
        if (ac <= 0m || ev <= 0m)
        {
            decimal fallbackEtc = expertEtc ?? Math.Max(0m, bac - ev);
            decimal fallbackEac = ac + fallbackEtc;
            decimal finNoRes = contractValue - fallbackEac;
            decimal finWithRes = contractValue - fallbackEac - reserveP80;

            return new EarnedValueForecastResult(
                Bac: bac, Pv: pv, Ev: ev, Ac: ac, ContractValue: contractValue, ReserveP80: reserveP80,
                Cpi: null,
                Spi: pv > 0m ? Math.Round(ev / pv, 3, MidpointRounding.AwayFromZero) : null,
                Etc: Math.Round(fallbackEtc, 2),
                Eac: Math.Round(fallbackEac, 2),
                FinancialResultWithoutReserve: Math.Round(finNoRes, 2),
                FinancialResultWithReserve: Math.Round(finWithRes, 2),
                MarginWithoutReservePercent: contractValue > 0m ? Math.Round(finNoRes / contractValue * 100m, 1) : 0m,
                MarginWithReservePercent: contractValue > 0m ? Math.Round(finWithRes / contractValue * 100m, 1) : 0m,
                IsExpertEstimate: true,
                DataQualityWarning: "Недостаточно данных для достоверного расчёта CPI; применён экспертный ETC.");
        }

        decimal rawCpi = ev / ac;
        decimal cpiDisplay = Math.Round(rawCpi, 3, MidpointRounding.AwayFromZero);
        decimal? spiDisplay = pv > 0m ? Math.Round(ev / pv, 3, MidpointRounding.AwayFromZero) : null;

        decimal etc = (bac - ev) / rawCpi;
        decimal eac = ac + etc;
        decimal resultWithoutReserve = contractValue - eac;
        decimal resultWithReserve = contractValue - eac - reserveP80;

        return new EarnedValueForecastResult(
            Bac: Math.Round(bac, 2),
            Pv: Math.Round(pv, 2),
            Ev: Math.Round(ev, 2),
            Ac: Math.Round(ac, 2),
            ContractValue: Math.Round(contractValue, 2),
            ReserveP80: Math.Round(reserveP80, 2),
            Cpi: cpiDisplay,
            Spi: spiDisplay,
            Etc: Math.Round(etc, 2, MidpointRounding.AwayFromZero),
            Eac: Math.Round(eac, 2, MidpointRounding.AwayFromZero),
            FinancialResultWithoutReserve: Math.Round(resultWithoutReserve, 2, MidpointRounding.AwayFromZero),
            FinancialResultWithReserve: Math.Round(resultWithReserve, 2, MidpointRounding.AwayFromZero),
            MarginWithoutReservePercent: contractValue > 0m ? Math.Round(resultWithoutReserve / contractValue * 100m, 1, MidpointRounding.AwayFromZero) : 0m,
            MarginWithReservePercent: contractValue > 0m ? Math.Round(resultWithReserve / contractValue * 100m, 1, MidpointRounding.AwayFromZero) : 0m,
            IsExpertEstimate: false,
            DataQualityWarning: null);
    }

    public static decimal CalculateInvestmentNpv(IReadOnlyList<decimal> yearlyNetCashFlows, decimal discountRate = 0.10m)
    {
        decimal npv = 0m;
        for (int year = 0; year < yearlyNetCashFlows.Count; year++)
        {
            double factor = Math.Pow(1.0 + (double)discountRate, year);
            npv += yearlyNetCashFlows[year] / (decimal)factor;
        }
        return Math.Round(npv, 2, MidpointRounding.AwayFromZero);
    }
}

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

public record RiskMatrixCell(
    int ProbabilityScore,
    int ImpactScore,
    int IntegralScore,
    string Zone,
    string RiskCodes);

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

    public static List<RiskMatrixCell> BuildRiskMatrix(IEnumerable<Risk> risks)
    {
        var active = risks.Where(r => !r.IsDeleted).ToList();
        var cells = new List<RiskMatrixCell>(25);
        for (int p = 5; p >= 1; p--)
        {
            for (int imp = 1; imp <= 5; imp++)
            {
                int score = p * imp;
                string zone = GetRiskZone(score);
                var codes = active
                    .Where(r => r.ProbabilityScore == p && r.ImpactScore == imp)
                    .Select(r => r.Code)
                    .ToList();
                cells.Add(new RiskMatrixCell(p, imp, score, zone, codes.Count == 0 ? "—" : string.Join(", ", codes)));
            }
        }
        return cells;
    }
}
