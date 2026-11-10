import React, { useEffect, useState } from 'react';
import { AlertTriangle, ArrowRight, CheckCircle2, ShieldAlert, TrendingUp, Wallet } from 'lucide-react';
import { apiFetch } from '../api/client';
import { fmtMoney, fmtNum, REQUEST_STATUS_MAP } from '../utils/format';
import type { NavTab } from '../components/Layout';

interface DashboardPageProps {
  onNavigate: (tab: NavTab) => void;
}

export const DashboardPage: React.FC<DashboardPageProps> = ({ onNavigate }) => {
  const [data, setData] = useState<any>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiFetch('/api/dashboard')
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) {
    return <div className="p-8 text-center text-sm text-slate-500">Загрузка панели управления...</div>;
  }
  if (error || !data || !data.portfolioSummary) {
    return (
      <div className="rounded-xl border border-rose-200 bg-rose-50 p-4 text-sm text-rose-800">
        {error || 'Не удалось загрузить показатели панели'}
      </div>
    );
  }

  const { portfolioSummary, forecasts = [], cashCalendar, pendingRequests = [] } = data;
  const days: any[] = (cashCalendar?.days || []).slice(0, 15);
  const minLimit = Number(cashCalendar?.minCashBalance ?? 1000);

  const pendingApprovalCount = pendingRequests.filter((r: any) => r.status === 'PendingApproval').length;
  const blockedCount = pendingRequests.filter((r: any) => r.status === 'Blocked').length;

  const chartW = 700;
  const chartH = 190;
  const pad = 24;
  const balances = days.map((d) => Number(d.closingBalance));
  const maxVal = Math.max(minLimit * 1.3, ...balances, 2500);
  const minVal = Math.min(0, ...balances);
  const span = Math.max(1, maxVal - minVal);
  const toX = (idx: number) => pad + (idx / Math.max(1, days.length - 1)) * (chartW - pad * 2);
  const toY = (val: number) => chartH - pad - ((val - minVal) / span) * (chartH - pad * 2);
  const polyPoints = days.map((d, i) => `${toX(i).toFixed(1)},${toY(Number(d.closingBalance)).toFixed(1)}`).join(' ');
  const minLineY = toY(minLimit);

  return (
    <div className="space-y-5">
      {/* Top KPI Cards */}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="text-xs font-medium text-slate-500">Утверждённый бюджет (B)</div>
          <div className="mt-1.5 text-lg font-bold text-slate-900 tabular-nums">
            {fmtMoney(portfolioSummary.totalApprovedBudget)}
          </div>
          <div className="mt-1 text-[11px] text-slate-500">
            Проектов: {portfolioSummary.projectCount} · Портфель V: {fmtNum(portfolioSummary.totalContractValue, 0)}
          </div>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="text-xs font-medium text-slate-500">Свободный лимит (Lдост)</div>
          <div
            className={`mt-1.5 text-lg font-bold tabular-nums ${
              portfolioSummary.totalFreeLimit < 0 ? 'text-rose-600' : 'text-emerald-700'
            }`}
          >
            {fmtMoney(portfolioSummary.totalFreeLimit)}
          </div>
          <div className="mt-1 text-[11px] text-slate-500">
            Обязательства (O): {fmtMoney(portfolioSummary.totalGrossObligations)}
          </div>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="flex items-center justify-between text-xs font-medium text-slate-500">
            <span>Макс. кассовый разрыв</span>
            <Wallet className="h-4 w-4 text-rose-500" />
          </div>
          <div
            className={`mt-1.5 text-lg font-bold tabular-nums ${
              portfolioSummary.maxCashGap90d > 0 ? 'text-rose-600' : 'text-emerald-700'
            }`}
          >
            {fmtMoney(portfolioSummary.maxCashGap90d)}
          </div>
          <div className="mt-1 text-[11px] text-slate-500">
            Дней с разрывом (90 дн.): <span className="font-semibold text-rose-600">{portfolioSummary.gapDaysCount90d}</span>
          </div>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="flex items-center justify-between text-xs font-medium text-slate-500">
            <span>Резерв рисков P80</span>
            <ShieldAlert className="h-4 w-4 text-amber-600" />
          </div>
          <div className="mt-1.5 text-lg font-bold text-amber-700 tabular-nums">
            {fmtMoney(portfolioSummary.totalReserveP80)}
          </div>
          <div className="mt-1 text-[11px] text-slate-500">Уровень доверия 80% (z = 0,84)</div>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="text-xs font-medium text-slate-500">Заявки в очереди</div>
          <div className="mt-1.5 flex items-baseline gap-2">
            <span className="text-lg font-bold text-indigo-700 tabular-nums">{pendingApprovalCount}</span>
            <span className="text-xs text-slate-500">на согласовании</span>
          </div>
          <div className="mt-1 text-[11px] font-medium text-rose-600">
            Заблокировано (лимит/аванс): {blockedCount}
          </div>
        </div>
      </div>

      {/* Main Grid: Liquidity Chart + Earned Value Cards */}
      <div className="grid gap-5 lg:grid-cols-12">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-7">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold text-slate-900">
                Прогноз денежного остатка и кассовых разрывов (14 дн.)
              </h2>
              <p className="text-xs text-slate-500">
                Минимальный неснижаемый остаток Cash_min = {fmtMoney(minLimit)}
              </p>
            </div>
            <button
              onClick={() => onNavigate('calendar')}
              className="inline-flex items-center gap-1 rounded-lg bg-slate-100 px-2.5 py-1.5 text-xs font-medium text-slate-700 hover:bg-slate-200"
            >
              Календарь 90 дн. <ArrowRight className="h-3.5 w-3.5" />
            </button>
          </div>

          <div className="mt-3 overflow-x-auto">
            <svg viewBox={`0 0 ${chartW} ${chartH}`} className="h-48 w-full min-w-[320px]">
              <line
                x1={pad}
                x2={chartW - pad}
                y1={minLineY}
                y2={minLineY}
                stroke="#EF4444"
                strokeDasharray="5 4"
                strokeWidth="1.5"
              />
              <text x={pad + 4} y={Math.max(12, minLineY - 5)} fontSize="10" fill="#DC2626">
                Мин. остаток ({minLimit})
              </text>

              {days.length > 1 && <polyline fill="none" stroke="#0284C7" strokeWidth="2.5" points={polyPoints} />}

              {days.map((d, i) => (
                <g key={d.date}>
                  <circle
                    cx={toX(i)}
                    cy={toY(Number(d.closingBalance))}
                    r={d.cashGap > 0 ? 4.5 : 3}
                    fill={d.cashGap > 0 ? '#E11D48' : '#0284C7'}
                  />
                  {i % 2 === 0 && (
                    <text x={toX(i)} y={chartH - 5} textAnchor="middle" fontSize="9" fill="#64748B">
                      {String(d.date).slice(5)}
                    </text>
                  )}
                </g>
              ))}
            </svg>
          </div>
        </div>

        {/* Earned Value Cards */}
        <div className="space-y-3 lg:col-span-5">
          {forecasts.map((item: any) => {
            const p = item.project;
            const ev = item.forecast;
            return (
              <div key={p.id} className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <span className="font-mono text-[11px] font-semibold text-sky-700">{p.code}</span>
                    <h3 className="text-sm font-semibold text-slate-900">{p.name}</h3>
                  </div>
                  <TrendingUp className="h-4 w-4 shrink-0 text-sky-600" />
                </div>

                <div className="mt-3 grid grid-cols-4 gap-2 text-center">
                  <div className="rounded-lg bg-slate-50 p-2">
                    <div className="text-[10px] text-slate-500">CPI</div>
                    <div className={`text-sm font-bold tabular-nums ${(ev.cpi ?? 1) < 0.95 ? 'text-rose-600' : 'text-emerald-700'}`}>
                      {ev.cpi !== null ? fmtNum(ev.cpi, 3) : '—'}
                    </div>
                  </div>
                  <div className="rounded-lg bg-slate-50 p-2">
                    <div className="text-[10px] text-slate-500">SPI</div>
                    <div className={`text-sm font-bold tabular-nums ${(ev.spi ?? 1) < 0.95 ? 'text-amber-700' : 'text-emerald-700'}`}>
                      {ev.spi !== null ? fmtNum(ev.spi, 3) : '—'}
                    </div>
                  </div>
                  <div className="rounded-lg bg-slate-50 p-2">
                    <div className="text-[10px] text-slate-500">EAC</div>
                    <div className="text-sm font-bold text-slate-900 tabular-nums">{fmtNum(ev.eac, 0)}</div>
                  </div>
                  <div className="rounded-lg bg-slate-50 p-2">
                    <div className="text-[10px] text-slate-500">Выручка V</div>
                    <div className="text-sm font-bold text-slate-900 tabular-nums">{fmtNum(ev.contractValue, 0)}</div>
                  </div>
                </div>

                <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-slate-100 pt-2.5 text-xs">
                  <div>
                    <span className="text-slate-500">Прибыль без резерва: </span>
                    <span className="font-semibold text-slate-800 tabular-nums">
                      {fmtNum(ev.financialResultWithoutReserve, 0)} ({ev.marginWithoutReservePercent}%)
                    </span>
                  </div>
                  <div>
                    <span className="text-slate-500">С резервом P80: </span>
                    <span
                      className={`font-bold tabular-nums ${
                        ev.financialResultWithReserve < 500 ? 'text-amber-700' : 'text-emerald-700'
                      }`}
                    >
                      {fmtNum(ev.financialResultWithReserve, 0)} ({ev.marginWithReservePercent}%)
                    </span>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      </div>

      {/* Pending Decisions Queue */}
      <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-sm font-semibold text-slate-900">Очередь управленческих решений и контроля</h2>
          <button
            onClick={() => onNavigate('requests')}
            className="inline-flex items-center gap-1 text-xs font-semibold text-sky-600 hover:underline"
          >
            Все заявки <ArrowRight className="h-3.5 w-3.5" />
          </button>
        </div>

        <div className="mt-3 divide-y divide-slate-100">
          {pendingRequests.length === 0 && (
            <div className="flex items-center gap-2 py-4 text-xs text-slate-500">
              <CheckCircle2 className="h-4 w-4 text-emerald-500" />
              Очередь согласования пуста
            </div>
          )}
          {pendingRequests.map((req: any) => {
            const st = REQUEST_STATUS_MAP[req.status] || { label: req.status, badge: 'bg-slate-100 text-slate-700' };
            return (
              <div key={req.id} className="flex flex-wrap items-center justify-between gap-2 py-2.5 text-xs">
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono font-bold text-slate-900">{req.number}</span>
                    <span className={`rounded-full border px-2 py-0.5 text-[11px] font-medium ${st.badge}`}>{st.label}</span>
                    {req.has_overdue_advance_warning === 1 && (
                      <span className="inline-flex items-center gap-1 text-[11px] text-amber-700">
                        <AlertTriangle className="h-3.5 w-3.5" /> Просроченный аванс
                      </span>
                    )}
                  </div>
                  <div className="mt-0.5 truncate text-slate-600">
                    {req.object_name} · {req.counterparty} · {req.purpose}
                  </div>
                </div>
                <div className="text-right tabular-nums">
                  <div className="font-bold text-slate-900">{fmtMoney(req.amount_in_byn)}</div>
                  <div className="text-[11px] text-slate-500">План: {req.planned_payment_date}</div>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
