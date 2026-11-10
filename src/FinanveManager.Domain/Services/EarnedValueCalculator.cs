namespace FinanveManager.Domain.Services;

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

/// <summary>
/// Калькулятор освоенного объёма и прогноза стоимости (формулы 1.5–1.7, 1.13–1.14, таблица 2.6).
/// </summary>
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

        // Сохраняем полную точность CPI при расчёте ETC (пример п. 1.3.2: 8000 / (3000/3300) = 8800.00)
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

    /// <summary>
    /// Чистая текущая стоимость NPV при ставке дисконтирования 10 % годовых для инвестиционных решений (п. 1.3.2).
    /// </summary>
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
