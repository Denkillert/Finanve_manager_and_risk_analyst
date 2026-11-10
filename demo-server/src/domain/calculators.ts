export interface FreeLimitResult {
  approvedBudget: number;
  grossObligations: number;
  paidFact: number;
  submittedRequests: number;
  freeLimit: number;
  obligationRemainder: number;
  deviation: number;
  deviationPercent: number | null;
  hasMissingBaselinePlan: boolean;
}

export interface EarnedValueResult {
  bac: number;
  pv: number;
  ev: number;
  ac: number;
  contractValue: number;
  reserveP80: number;
  cpi: number | null;
  spi: number | null;
  etc: number;
  eac: number;
  financialResultWithoutReserve: number;
  financialResultWithReserve: number;
  marginWithoutReservePercent: number;
  marginWithReservePercent: number;
  isExpertEstimate: boolean;
  dataQualityWarning: string | null;
}

export interface CashDayForecast {
  date: string;
  dayIndex: number;
  openingBalance: number;
  inflows: number;
  outflows: number;
  closingBalance: number;
  cashGap: number;
}

export interface RiskItemInput {
  probability: number;
  monetaryImpact: number;
  residualProbability?: number | null;
  integralScore?: number;
}

export interface RiskPortfolioSummary {
  totalEmv: number;
  sigma: number;
  reserveP80: number;
  totalResidualEmv: number;
  emvReduction: number;
  highAndCriticalCount: number;
}

export interface SensitivityFactor {
  factorName: string;
  baseAmount: number;
  rangeLabel: string;
  impactOnEac: number;
  comment: string;
}

export function roundTo(val: number, decimals = 2): number {
  const factor = Math.pow(10, decimals);
  return Math.round((val + Number.EPSILON) * factor) / factor;
}

/**
 * Формулы (1.1)–(1.4): Lдост = B - O; Oост = O - F; Δ = O - B; δ = (Δ / B) * 100%
 * Бизнес-правило 7: поданная заявка Q входит в O и повторно не вычитается.
 */
export function calculateFreeLimit(
  approvedBudgetB: number,
  grossObligationsO: number,
  paidFactF: number,
  submittedRequestsQ = 0
): FreeLimitResult {
  const freeLimit = roundTo(approvedBudgetB - grossObligationsO, 4);
  const obligationRemainder = roundTo(grossObligationsO - paidFactF, 4);
  const deviation = roundTo(grossObligationsO - approvedBudgetB, 4);
  const deviationPercent = approvedBudgetB === 0 ? null : roundTo((deviation / approvedBudgetB) * 100, 2);

  return {
    approvedBudget: roundTo(approvedBudgetB, 4),
    grossObligations: roundTo(grossObligationsO, 4),
    paidFact: roundTo(paidFactF, 4),
    submittedRequests: roundTo(submittedRequestsQ, 4),
    freeLimit,
    obligationRemainder,
    deviation,
    deviationPercent,
    hasMissingBaselinePlan: approvedBudgetB === 0,
  };
}

/**
 * Формула (1.12): Sопл = Sакт - Aзач - Uуд
 */
export function calculateActPayable(actAmount: number, advanceOffset: number, retentionAmount: number) {
  if (actAmount < 0 || advanceOffset < 0 || retentionAmount < 0) {
    throw new Error('Суммы акта, зачёта и удержания не могут быть отрицательными.');
  }
  if (advanceOffset + retentionAmount > actAmount + 0.0001) {
    throw new Error('Сумма зачёта аванса и гарантийного удержания превышает стоимость акта.');
  }
  return {
    actAmount: roundTo(actAmount, 4),
    advanceOffset: roundTo(advanceOffset, 4),
    retentionAmount: roundTo(retentionAmount, 4),
    payableAmount: roundTo(actAmount - advanceOffset - retentionAmount, 4),
  };
}

export function normalizeVat(amount: number, vatIncluded: boolean, vatRate = 20) {
  if (amount < 0) throw new Error('Сумма не может быть отрицательной.');
  if (vatIncluded) {
    const netAmount = roundTo(amount / (1 + vatRate / 100), 4);
    const vatAmount = roundTo(amount - netAmount, 4);
    return { netAmount, vatAmount, grossAmount: roundTo(amount, 4) };
  }
  const vatAmount = roundTo(amount * (vatRate / 100), 4);
  return { netAmount: roundTo(amount, 4), vatAmount, grossAmount: roundTo(amount + vatAmount, 4) };
}

/**
 * Формулы (1.8)–(1.9): Cash_t и Gap_t = max(0, Cash_min - Cash_t)
 */
export function calculateCashCalendar(
  initialCash: number,
  minCashBalance: number,
  startDateIso: string,
  horizonDays: number,
  dailyFlows: Map<string, { inflows: number; outflows: number }>
): CashDayForecast[] {
  const result: CashDayForecast[] = [];
  let running = initialCash;
  const start = new Date(startDateIso + 'T00:00:00Z');

  for (let day = 0; day <= horizonDays; day++) {
    const d = new Date(start.getTime() + day * 86400000);
    const dateKey = d.toISOString().slice(0, 10);
    const flow = day > 0 ? dailyFlows.get(dateKey) : undefined;
    const inflows = flow?.inflows ?? 0;
    const outflows = flow?.outflows ?? 0;
    const opening = running;
    running = roundTo(opening + inflows - outflows, 4);
    const cashGap = roundTo(Math.max(0, minCashBalance - running), 4);

    result.push({
      date: dateKey,
      dayIndex: day,
      openingBalance: roundTo(opening, 4),
      inflows: roundTo(inflows, 4),
      outflows: roundTo(outflows, 4),
      closingBalance: running,
      cashGap,
    });
  }

  return result;
}

/**
 * Формулы (1.5)–(1.7), (1.13)–(1.14): CPI, SPI, ETC, EAC, Rфин (без резерва и с резервом P80)
 */
export function calculateEarnedValue(
  bac: number,
  pv: number,
  ev: number,
  ac: number,
  contractValue: number,
  reserveP80: number,
  expertEtc?: number
): EarnedValueResult {
  if (ac <= 0 || ev <= 0) {
    const fallbackEtc = expertEtc ?? Math.max(0, bac - ev);
    const fallbackEac = ac + fallbackEtc;
    const finNoRes = contractValue - fallbackEac;
    const finWithRes = contractValue - fallbackEac - reserveP80;
    return {
      bac,
      pv,
      ev,
      ac,
      contractValue,
      reserveP80,
      cpi: null,
      spi: pv > 0 ? roundTo(ev / pv, 3) : null,
      etc: roundTo(fallbackEtc, 2),
      eac: roundTo(fallbackEac, 2),
      financialResultWithoutReserve: roundTo(finNoRes, 2),
      financialResultWithReserve: roundTo(finWithRes, 2),
      marginWithoutReservePercent: contractValue > 0 ? roundTo((finNoRes / contractValue) * 100, 1) : 0,
      marginWithReservePercent: contractValue > 0 ? roundTo((finWithRes / contractValue) * 100, 1) : 0,
      isExpertEstimate: true,
      dataQualityWarning: 'Недостаточно данных для расчёта CPI; использована экспертная оценка ETC.',
    };
  }

  const rawCpi = ev / ac;
  const cpiDisplay = roundTo(rawCpi, 3);
  const spiDisplay = pv > 0 ? roundTo(ev / pv, 3) : null;
  const etc = (bac - ev) / rawCpi;
  const eac = ac + etc;
  const finNoRes = contractValue - eac;
  const finWithRes = contractValue - eac - reserveP80;

  return {
    bac: roundTo(bac, 2),
    pv: roundTo(pv, 2),
    ev: roundTo(ev, 2),
    ac: roundTo(ac, 2),
    contractValue: roundTo(contractValue, 2),
    reserveP80: roundTo(reserveP80, 2),
    cpi: cpiDisplay,
    spi: spiDisplay,
    etc: roundTo(etc, 2),
    eac: roundTo(eac, 2),
    financialResultWithoutReserve: roundTo(finNoRes, 2),
    financialResultWithReserve: roundTo(finWithRes, 2),
    marginWithoutReservePercent: contractValue > 0 ? roundTo((finNoRes / contractValue) * 100, 1) : 0,
    marginWithReservePercent: contractValue > 0 ? roundTo((finWithRes / contractValue) * 100, 1) : 0,
    isExpertEstimate: false,
    dataQualityWarning: null,
  };
}

export function getProbabilityScore(p: number): number {
  if (p < 0 || p > 1) throw new Error('Вероятность должна быть в диапазоне [0; 1].');
  if (p <= 0.10) return 1;
  if (p <= 0.24) return 2;
  if (p <= 0.50) return 3;
  if (p <= 0.75) return 4;
  return 5;
}

export function getImpactScore(monetaryImpact: number): number {
  if (monetaryImpact < 0) throw new Error('Денежная оценка последствий не может быть отрицательной.');
  if (monetaryImpact >= 600) return 5;
  if (monetaryImpact >= 400) return 4;
  if (monetaryImpact >= 250) return 3;
  if (monetaryImpact >= 150) return 2;
  return 1;
}

export function calculateRiskPortfolio(risks: RiskItemInput[], z = 0.84): RiskPortfolioSummary {
  let totalEmv = 0;
  let variance = 0;
  let totalResidualEmv = 0;
  let highAndCriticalCount = 0;

  for (const r of risks) {
    const emv = r.probability * r.monetaryImpact;
    totalEmv += emv;
    variance += r.monetaryImpact * r.monetaryImpact * r.probability * (1 - r.probability);
    const rp = r.residualProbability ?? r.probability;
    totalResidualEmv += rp * r.monetaryImpact;
    const score = r.integralScore ?? getProbabilityScore(r.probability) * getImpactScore(r.monetaryImpact);
    if (score >= 10) highAndCriticalCount++;
  }

  const sigma = roundTo(Math.sqrt(variance), 2);
  const reserveP80 = roundTo(totalEmv + z * sigma, 2);

  return {
    totalEmv: roundTo(totalEmv, 2),
    sigma,
    reserveP80,
    totalResidualEmv: roundTo(totalResidualEmv, 2),
    emvReduction: roundTo(totalEmv - totalResidualEmv, 2),
    highAndCriticalCount,
  };
}

export function calculateSensitivityFactors(
  materialsBase = 4500,
  delayMonthlyOverhead = 250,
  issuedAdvancesBase = 1200,
  fxCostsBase = 900
): SensitivityFactor[] {
  const factors: SensitivityFactor[] = [
    {
      factorName: 'Цена материалов',
      baseAmount: materialsBase,
      rangeLabel: '+10 %',
      impactOnEac: roundTo(materialsBase * 0.10, 2),
      comment: 'Доля материалов в EAC (37,2 %)',
    },
    {
      factorName: 'Задержка на один месяц',
      baseAmount: delayMonthlyOverhead,
      rangeLabel: '+1 месяц',
      impactOnEac: roundTo(delayMonthlyOverhead, 2),
      comment: 'Стоимость месяца простоя и накладных',
    },
    {
      factorName: 'Курс валюты',
      baseAmount: fxCostsBase,
      rangeLabel: '+10 %',
      impactOnEac: roundTo(fxCostsBase * 0.10, 2),
      comment: 'Импортные материалы и оборудование',
    },
    {
      factorName: 'Просроченные авансы',
      baseAmount: issuedAdvancesBase,
      rangeLabel: '+5 п. п.',
      impactOnEac: roundTo(issuedAdvancesBase * 0.05, 2),
      comment: '0,05 × выданные авансы',
    },
  ];
  return factors.sort((a, b) => Math.abs(b.impactOnEac) - Math.abs(a.impactOnEac));
}

export function calculatePenalty(obligationAmount: number, overdueDays: number, dailyRatePercent = 0.1, maxPercent = 10): number {
  if (overdueDays <= 0 || obligationAmount <= 0) return 0;
  const raw = obligationAmount * (dailyRatePercent / 100) * overdueDays;
  const cap = obligationAmount * (maxPercent / 100);
  return roundTo(Math.min(raw, cap), 4);
}
