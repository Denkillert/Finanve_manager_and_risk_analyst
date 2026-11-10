using FinanveManager.Domain.Common;

namespace FinanveManager.Domain.Services;

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

/// <summary>
/// Финансовый калькулятор предметной области (таблица 2.6, формулы 1.1–1.4, 1.8–1.12).
/// </summary>
public static class FinancialCalculator
{
    /// <summary>
    /// Расчёт свободного лимита и остатка обязательств по формулам (1.1)–(1.4).
    /// Бизнес-правило 7: поданная заявка Q входит в O и повторно из B не вычитается.
    /// </summary>
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

    /// <summary>
    /// Формула (1.12): Sопл = Sакт - Aзач - Uуд
    /// </summary>
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

    /// <summary>
    /// Расчёт остатка денежных средств и кассового разрыва по формулам (1.8)–(1.9).
    /// </summary>
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

    /// <summary>
    /// Приведение суммы с НДС / без НДС к сопоставимому виду (таблица 1.6).
    /// </summary>
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

    /// <summary>
    /// Типовая пеня за просрочку: 0,1 % в день, но не более 10 % суммы обязательства (п. 1.3.4).
    /// </summary>
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
