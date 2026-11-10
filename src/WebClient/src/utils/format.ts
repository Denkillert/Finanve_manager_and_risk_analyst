export function fmtMoney(val: number | null | undefined, suffix = 'тыс. р.'): string {
  if (val === null || val === undefined || Number.isNaN(Number(val))) return '—';
  const formatted = new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(Number(val));
  return suffix ? `${formatted} ${suffix}` : formatted;
}

export function fmtNum(val: number | null | undefined, digits = 2): string {
  if (val === null || val === undefined || Number.isNaN(Number(val))) return '—';
  return new Intl.NumberFormat('ru-RU', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  }).format(Number(val));
}

export const REQUEST_STATUS_MAP: Record<string, { label: string; badge: string }> = {
  Draft: { label: 'Черновик', badge: 'bg-slate-100 text-slate-700 border-slate-300' },
  UnderReview: { label: 'На проверке', badge: 'bg-sky-50 text-sky-700 border-sky-200' },
  Returned: { label: 'Возвращена', badge: 'bg-amber-50 text-amber-800 border-amber-300' },
  Blocked: { label: 'Заблокирована', badge: 'bg-rose-50 text-rose-800 border-rose-300' },
  PendingApproval: { label: 'На согласовании', badge: 'bg-indigo-50 text-indigo-800 border-indigo-200' },
  ApprovedByManagement: { label: 'Перерасход одобрен', badge: 'bg-violet-50 text-violet-800 border-violet-200' },
  Approved: { label: 'Согласована', badge: 'bg-emerald-50 text-emerald-800 border-emerald-300' },
  Executed: { label: 'Исполнена', badge: 'bg-teal-50 text-teal-800 border-teal-300' },
  Closed: { label: 'Закрыта', badge: 'bg-emerald-100 text-emerald-900 border-emerald-400' },
  Rejected: { label: 'Отклонена', badge: 'bg-slate-200 text-slate-700 border-slate-400' },
};

export const RISK_STATUS_MAP: Record<string, { label: string; badge: string }> = {
  Identified: { label: 'Идентифицирован', badge: 'bg-slate-100 text-slate-700' },
  Analyzed: { label: 'Проанализирован', badge: 'bg-sky-100 text-sky-800' },
  Treating: { label: 'Обрабатывается', badge: 'bg-amber-100 text-amber-900' },
  Monitoring: { label: 'Мониторинг', badge: 'bg-emerald-100 text-emerald-800' },
  Realized: { label: 'Реализован', badge: 'bg-rose-100 text-rose-800' },
  Closed: { label: 'Закрыт', badge: 'bg-slate-200 text-slate-700' },
};

export function riskZoneColor(score: number): { label: string; cls: string } {
  if (score >= 17) return { label: 'Критический', cls: 'bg-rose-600 text-white' };
  if (score >= 10) return { label: 'Высокий', cls: 'bg-orange-500 text-white' };
  if (score >= 5) return { label: 'Умеренный', cls: 'bg-amber-200 text-amber-950' };
  return { label: 'Низкий', cls: 'bg-emerald-100 text-emerald-900' };
}
