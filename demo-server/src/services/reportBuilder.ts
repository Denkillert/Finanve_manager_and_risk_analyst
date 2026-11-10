import type { DatabaseSync } from 'node:sqlite';
import {
  calculateCashCalendar,
  calculateEarnedValue,
  calculateRiskPortfolio,
  calculateSensitivityFactors,
  roundTo,
} from '../domain/calculators.js';
import type { TabularReportModel } from '../adapters/integrations.js';

export function getProjectCashCalendar(db: DatabaseSync, projectId: string | null, startDate = '2026-11-10', horizonDays = 90) {
  const projects = projectId
    ? (db.prepare('SELECT * FROM projects WHERE id = ? AND is_deleted = 0').all(projectId) as Array<Record<string, any>>)
    : (db.prepare('SELECT * FROM projects WHERE is_deleted = 0').all() as Array<Record<string, any>>);

  const initialCash = projects.reduce((s, p) => s + Number(p.initial_cash || 0), 0);
  const minCashBalance = projects.reduce((s, p) => s + Number(p.min_cash_balance || 0), 0);

  const scheduleRows = db
    .prepare(
      `SELECT cs.*, c.project_id, c.number AS contract_number, c.counterparty
       FROM contract_schedule_items cs
       JOIN contracts c ON c.id = cs.contract_id
       WHERE cs.is_deleted = 0 AND cs.is_paid = 0 AND c.is_deleted = 0
         ${projectId ? 'AND c.project_id = ?' : ''}`
    )
    .all(...(projectId ? [projectId] : [])) as Array<Record<string, any>>;

  const reqRows = db
    .prepare(
      `SELECT id, number, planned_payment_date, amount_in_byn, status, purpose
       FROM payment_requests
       WHERE is_deleted = 0 AND status IN ('PendingApproval', 'Approved', 'ApprovedByManagement')
         ${projectId ? 'AND project_id = ?' : ''}`
    )
    .all(...(projectId ? [projectId] : [])) as Array<Record<string, any>>;

  const dailyFlows = new Map<string, { inflows: number; outflows: number }>();

  for (const s of scheduleRows) {
    const d = String(s.planned_date).slice(0, 10);
    const entry = dailyFlows.get(d) || { inflows: 0, outflows: 0 };
    if (Number(s.is_incoming) === 1) entry.inflows += Number(s.amount);
    else entry.outflows += Number(s.amount);
    dailyFlows.set(d, entry);
  }

  for (const r of reqRows) {
    const d = String(r.planned_payment_date).slice(0, 10);
    const entry = dailyFlows.get(d) || { inflows: 0, outflows: 0 };
    entry.outflows += Number(r.amount_in_byn);
    dailyFlows.set(d, entry);
  }

  const days = calculateCashCalendar(initialCash, minCashBalance, startDate, horizonDays, dailyFlows);
  const gapDays = days.filter((d) => d.cashGap > 0);
  const overdueReceivables = scheduleRows.filter(
    (s) => Number(s.is_incoming) === 1 && s.planned_date < '2026-10-31'
  );

  return {
    initialCash,
    minCashBalance,
    startDate,
    horizonDays,
    days,
    gapDaysCount: gapDays.length,
    maxCashGap: gapDays.length > 0 ? Math.max(...gapDays.map((g) => g.cashGap)) : 0,
    overdueReceivables,
    scheduledItems: scheduleRows,
    plannedRequests: reqRows,
  };
}

export function getProjectEarnedValue(db: DatabaseSync, projectId: string) {
  const project = db.prepare('SELECT * FROM projects WHERE id = ? AND is_deleted = 0').get(projectId) as Record<string, any> | undefined;
  if (!project) return null;

  const limitRow = db
    .prepare(
      `SELECT COALESCE(SUM(bl.amount), 0) AS bac
       FROM budget_limits bl
       JOIN budget_versions bv ON bv.id = bl.budget_version_id
       WHERE bv.project_id = ? AND bv.status = 'Approved' AND bv.is_deleted = 0 AND bl.is_deleted = 0`
    )
    .get(projectId) as { bac: number };

  const stageAgg = db
    .prepare(
      `SELECT COALESCE(SUM(ws.planned_value_pv), 0) AS pv,
              COALESCE(SUM(ws.earned_value_ev), 0) AS ev,
              COALESCE(SUM(ws.actual_cost_ac), 0) AS ac
       FROM work_stages ws
       JOIN construction_objects co ON co.id = ws.construction_object_id
       WHERE co.project_id = ? AND ws.is_deleted = 0 AND co.is_deleted = 0`
    )
    .get(projectId) as { pv: number; ev: number; ac: number };

  const risks = db
    .prepare('SELECT probability, monetary_impact, residual_probability, integral_score FROM risks WHERE project_id = ? AND is_deleted = 0')
    .all(projectId) as Array<{ probability: number; monetary_impact: number; residual_probability: number | null; integral_score: number }>;

  const riskSummary = calculateRiskPortfolio(
    risks.map((r) => ({
      probability: r.probability,
      monetaryImpact: r.monetary_impact,
      residualProbability: r.residual_probability,
      integralScore: r.integral_score,
    }))
  );

  return {
    project,
    riskSummary,
    forecast: calculateEarnedValue(
      Number(limitRow.bac),
      Number(stageAgg.pv),
      Number(stageAgg.ev),
      Number(stageAgg.ac),
      Number(project.contract_value),
      riskSummary.reserveP80
    ),
  };
}

export function buildReportModel(db: DatabaseSync, type: string, projectId: string | null): TabularReportModel {
  const targetProjectId =
    projectId ||
    ((db.prepare('SELECT id FROM projects WHERE is_deleted = 0 ORDER BY code LIMIT 1').get() as { id: string } | undefined)?.id ?? '');

  if (type === 'budget-fact') {
    const rows = db.prepare('SELECT * FROM v_free_limit').all() as Array<Record<string, any>>;
    const totalBudget = rows.reduce((s, r) => s + Number(r.approved_budget), 0);
    const totalObl = rows.reduce((s, r) => s + Number(r.gross_obligations), 0);
    const totalFact = rows.reduce((s, r) => s + Number(r.paid_fact), 0);
    const totalFree = rows.reduce((s, r) => s + Number(r.free_limit), 0);
    return {
      title: 'Отчёт «Бюджет – Обязательства – Факт – Свободный лимит»',
      subtitle: `Сформировано ${new Date().toISOString().slice(0, 10)} (тыс. р., формулы 1.1–1.4)`,
      columns: ['Проект', 'Объект', 'Статья', 'Бюджет (B)', 'Обязат. (O)', 'Факт (F)', 'Своб. лимит (Lдост)', 'Отклон. (%)'],
      rows: rows.map((r) => [
        r.project_code,
        r.construction_object_name,
        `${r.cost_item_code} ${r.cost_item_name}`,
        roundTo(r.approved_budget, 2),
        roundTo(r.gross_obligations, 2),
        roundTo(r.paid_fact, 2),
        roundTo(r.free_limit, 2),
        r.deviation_percent === null ? 'н/д' : `${r.deviation_percent}%`,
      ]),
      summaryLines: [
        `Итого утверждённый бюджет (B): ${roundTo(totalBudget, 2)} тыс. р.`,
        `Итого валовые обязательства (O): ${roundTo(totalObl, 2)} тыс. р. | Факт (F): ${roundTo(totalFact, 2)} тыс. р. | Свободный лимит (Lдост): ${roundTo(totalFree, 2)} тыс. р.`,
      ],
    };
  }

  if (type === 'cash-calendar') {
    const cal = getProjectCashCalendar(db, targetProjectId, '2026-11-10', 30);
    return {
      title: 'Отчёт «Платёжный календарь и кассовые разрывы»',
      subtitle: `Начальный остаток Cash0 = ${cal.initialCash} тыс. р., минимальный остаток CashMin = ${cal.minCashBalance} тыс. р.`,
      columns: ['Дата', 'День', 'Начальный остаток', 'Поступления', 'Платежи', 'Остаток на конец дня', 'Кассовый разрыв'],
      rows: cal.days.map((d) => [d.date, d.dayIndex, d.openingBalance, d.inflows, d.outflows, d.closingBalance, d.cashGap]),
      summaryLines: [
        `Дней с кассовым разрывом (горизонт 30 дн.): ${cal.gapDaysCount}`,
        `Максимальный кассовый разрыв: ${roundTo(cal.maxCashGap, 2)} тыс. р.`,
      ],
    };
  }

  if (type === 'advances') {
    const advs = db
      .prepare(
        `SELECT a.*, c.number AS contract_number, c.counterparty, co.name AS object_name
         FROM advances a
         JOIN contracts c ON c.id = a.contract_id
         JOIN construction_objects co ON co.id = a.construction_object_id
         WHERE a.is_deleted = 0`
      )
      .all() as Array<Record<string, any>>;
    return {
      title: 'Отчёт «Контроль авансовых платежей»',
      subtitle: 'Формулы (1.10)–(1.11): Aост = A − Aисп, Kисп = Aисп / A × 100 %',
      columns: ['Договор', 'Контрагент', 'Объект', 'Выдано (A)', 'Зачтено (Aисп)', 'Остаток (Aост)', 'Kисп (%)', 'Срок', 'Статус'],
      rows: advs.map((a) => {
        const rem = roundTo(Number(a.amount) - Number(a.used_amount), 2);
        const k = roundTo((Number(a.used_amount) / Number(a.amount)) * 100, 1);
        return [a.contract_number, a.counterparty, a.object_name, a.amount, a.used_amount, rem, `${k}%`, a.due_date, a.status];
      }),
      summaryLines: [
        `Всего авансов: ${advs.length} | Просрочено: ${advs.filter((a) => a.status === 'Overdue').length}`,
        `Общий остаток неотработанных авансов: ${roundTo(advs.reduce((s, a) => s + (Number(a.amount) - Number(a.used_amount)), 0), 2)} тыс. р.`,
      ],
    };
  }

  if (type === 'risks') {
    const risks = db
      .prepare('SELECT * FROM risks WHERE project_id = ? AND is_deleted = 0 ORDER BY emv DESC, integral_score DESC')
      .all(targetProjectId) as Array<Record<string, any>>;
    const sum = calculateRiskPortfolio(
      risks.map((r) => ({
        probability: r.probability,
        monetaryImpact: r.monetary_impact,
        residualProbability: r.residual_probability,
        integralScore: r.integral_score,
      }))
    );
    return {
      title: 'Отчёт «Реестр рисков, EMV и резерв P80 (СТБ ISO 31000-2020)»',
      subtitle: 'Формулы (1.15)–(1.18): R = pб × I, EMV = p × C, Rрез = ΣEMV + 0,84 × σ',
      columns: ['Код', 'Событие', 'p', 'C (тыс. р.)', 'R', 'EMV', 'p′ (ост.)', 'Ост. EMV', 'Статус'],
      rows: risks.map((r) => [
        r.code,
        r.event,
        r.probability,
        r.monetary_impact,
        r.integral_score,
        roundTo(r.emv, 2),
        r.residual_probability ?? r.probability,
        roundTo(r.residual_emv ?? r.emv, 2),
        r.status,
      ]),
      summaryLines: [
        `ΣEMV = ${sum.totalEmv} тыс. р. | σ = ${sum.sigma} тыс. р. | Резерв P80 = ${sum.reserveP80} тыс. р.`,
        `Остаточный ΣEMV после мероприятий = ${sum.totalResidualEmv} тыс. р. (снижение на ${sum.emvReduction} тыс. р.)`,
      ],
    };
  }

  // sensitivity
  const factors = calculateSensitivityFactors();
  return {
    title: 'Отчёт «Анализ чувствительности прогноза EAC (диаграмма торнадо)»',
    subtitle: 'Таблица 1.18, требование 17: ранжирование факторов по модулю влияния на EAC',
    columns: ['Фактор', 'Базовая величина (тыс. р.)', 'Диапазон изменения', 'Влияние на EAC (тыс. р.)', 'Комментарий'],
    rows: factors.map((f) => [f.factorName, f.baseAmount, f.rangeLabel, `+${f.impactOnEac}`, f.comment]),
    summaryLines: [
      `Наиболее критичный фактор: ${factors[0].factorName} (+${factors[0].impactOnEac} тыс. р. к EAC)`,
    ],
  };
}
