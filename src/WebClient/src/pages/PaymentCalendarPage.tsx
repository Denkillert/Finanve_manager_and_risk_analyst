import React, { useEffect, useState } from 'react';
import { AlertTriangle, CalendarClock, UploadCloud } from 'lucide-react';
import { apiFetch } from '../api/client';
import { fmtMoney, fmtNum } from '../utils/format';

const SAMPLE_CSV = `BankOperationId;Date;Amount;Currency;Counterparty;RequestNumber;Purpose
BNK-2026-0850;2026-11-10;850;BYN;БелМеталлПоставка;ЗП-20261110-0003;Оплата по акту С-2 № 18
BNK-2026-UNMATCHED;2026-11-10;42.50;BYN;Неизвестный Контрагент;;Комиссия за расчётно-кассовое обслуживание`;

const SAMPLE_MT940 = `:20:STMT20261110
:60F:C261110BYN2000,00
:61:261110D850,00NTRFNONREF//MT940-OP-850
:86:Оплата по заявке ЗП-20261110-0003 акт С-2`;

export const PaymentCalendarPage: React.FC = () => {
  const [horizon, setHorizon] = useState<number>(30);
  const [calendar, setCalendar] = useState<any>(null);
  const [requests, setRequests] = useState<any[]>([]);
  const [bankText, setBankText] = useState(SAMPLE_CSV);
  const [bankFileName, setBankFileName] = useState('statement.csv');
  const [importResult, setImportResult] = useState<any>(null);
  const [rescheduleId, setRescheduleId] = useState('');
  const [newDate, setNewDate] = useState('2026-11-12');
  const [msg, setMsg] = useState<string | null>(null);

  const loadData = async (h = horizon) => {
    const [cal, reqs] = await Promise.all([
      apiFetch(`/api/cash-calendar?horizon=${h}`),
      apiFetch('/api/payment-requests'),
    ]);
    setCalendar(cal);
    setRequests(reqs);
    const active = reqs.find((r: any) => !['Closed', 'Rejected'].includes(r.status));
    if (active && !rescheduleId) setRescheduleId(active.id);
  };

  useEffect(() => {
    loadData(horizon).catch((e) => setMsg(e.message));
  }, [horizon]);

  const handleReschedule = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!rescheduleId) return;
    setMsg(null);
    try {
      await apiFetch('/api/cash-calendar/reschedule', {
        method: 'POST',
        body: JSON.stringify({ itemType: 'request', itemId: rescheduleId, newDate }),
      });
      await loadData(horizon);
      setMsg(`Дата оплаты заявки перенесена на ${newDate}; кассовый календарь пересчитан.`);
    } catch (err: any) {
      setMsg(err.message);
    }
  };

  const handleImportBank = async () => {
    setMsg(null);
    try {
      const res = await apiFetch('/api/integrations/bank-statement', {
        method: 'POST',
        body: JSON.stringify({ fileName: bankFileName, content: bankText }),
      });
      setImportResult(res);
      await loadData(horizon);
    } catch (err: any) {
      setMsg(err.message);
    }
  };

  const days: any[] = calendar?.days || [];

  return (
    <div className="space-y-5">
      {msg && <div className="rounded-xl border border-sky-200 bg-sky-50 p-3 text-xs font-medium text-sky-900">{msg}</div>}

      {(calendar?.overdueReceivables || []).length > 0 && (
        <div className="flex items-start gap-2.5 rounded-xl border border-rose-200 bg-rose-50 p-3.5 text-xs text-rose-900">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0 text-rose-600" />
          <div>
            <div className="font-bold">Риск R-08: Просроченная дебиторская задолженность заказчика (&gt; 10 дней)</div>
            <div className="mt-1">
              {calendar.overdueReceivables.map((r: any) => (
                <span key={r.id} className="mr-3 inline-block">
                  Договор <b>{r.contract_number}</b> ({r.counterparty}): <b>{fmtMoney(r.amount)}</b>, срок {r.planned_date}
                </span>
              ))}
            </div>
          </div>
        </div>
      )}

      <div className="grid gap-4 lg:grid-cols-12">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-7">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold text-slate-900">Платёжный календарь (формулы 1.8–1.9)</h2>
              <p className="text-xs text-slate-500">
                Стартовый остаток: {fmtMoney(calendar?.initialCash)} · Мин. буфер Cash_min: {fmtMoney(calendar?.minCashBalance)} ·
                Макс. разрыв: <span className="font-bold text-rose-600">{fmtMoney(calendar?.maxCashGap)}</span>
              </p>
            </div>
            <div className="inline-flex rounded-lg border border-slate-200 p-0.5 text-xs">
              {[5, 30, 90].map((h) => (
                <button
                  key={h}
                  onClick={() => setHorizon(h)}
                  className={`rounded-md px-2.5 py-1 font-semibold ${horizon === h ? 'bg-sky-600 text-white' : 'text-slate-600'}`}
                >
                  {h} дн.
                </button>
              ))}
            </div>
          </div>

          <div className="mt-3 max-h-[420px] overflow-auto">
            <table className="w-full text-left text-xs tabular-nums">
              <thead className="sticky top-0 bg-slate-50">
                <tr className="border-b border-slate-200 text-[11px] text-slate-600">
                  <th className="px-2.5 py-2">Дата</th>
                  <th className="px-2.5 py-2 text-right">Нач. ост.</th>
                  <th className="px-2.5 py-2 text-right">Поступления</th>
                  <th className="px-2.5 py-2 text-right">Выплаты</th>
                  <th className="px-2.5 py-2 text-right">Кон. ост.</th>
                  <th className="px-2.5 py-2 text-right">Касс. разрыв</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {days.map((d) => (
                  <tr key={d.date} className={d.cashGap > 0 ? 'bg-rose-50/70' : ''}>
                    <td className="px-2.5 py-1.5 font-mono">{d.date}</td>
                    <td className="px-2.5 py-1.5 text-right">{fmtNum(d.openingBalance)}</td>
                    <td className="px-2.5 py-1.5 text-right text-emerald-700">{d.inflows > 0 ? `+${fmtNum(d.inflows)}` : '0,00'}</td>
                    <td className="px-2.5 py-1.5 text-right text-slate-700">{d.outflows > 0 ? `-${fmtNum(d.outflows)}` : '0,00'}</td>
                    <td className={`px-2.5 py-1.5 text-right font-semibold ${d.cashGap > 0 ? 'text-rose-700' : 'text-slate-900'}`}>
                      {fmtNum(d.closingBalance)}
                    </td>
                    <td className="px-2.5 py-1.5 text-right font-bold text-rose-600">
                      {d.cashGap > 0 ? fmtNum(d.cashGap) : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>

        <div className="space-y-4 lg:col-span-5">
          <form onSubmit={handleReschedule} className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm">
            <div className="flex items-center gap-2 text-sm font-semibold text-slate-900">
              <CalendarClock className="h-4 w-4 text-sky-600" />
              Устранение кассового разрыва: перенос даты платежа
            </div>
            <select
              value={rescheduleId}
              onChange={(e) => setRescheduleId(e.target.value)}
              className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
            >
              {requests
                .filter((r) => !['Closed', 'Rejected'].includes(r.status))
                .map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.number} — {fmtNum(r.amount_in_byn)} тыс. р. (сейчас: {r.planned_payment_date})
                  </option>
                ))}
            </select>
            <div className="flex gap-2">
              <input
                type="date"
                value={newDate}
                onChange={(e) => setNewDate(e.target.value)}
                className="flex-1 rounded-lg border border-slate-300 px-2.5 py-1.5"
              />
              <button type="submit" className="rounded-lg bg-sky-600 px-3 py-1.5 font-semibold text-white hover:bg-sky-700">
                Перенести
              </button>
            </div>
          </form>

          <div className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm">
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-2 text-sm font-semibold text-slate-900">
                <UploadCloud className="h-4 w-4 text-teal-600" />
                Импорт банковской выписки (MT940 / CSV)
              </div>
              <div className="flex gap-1.5">
                <button
                  type="button"
                  onClick={() => {
                    setBankFileName('statement.csv');
                    setBankText(SAMPLE_CSV);
                  }}
                  className="rounded border border-slate-200 px-2 py-0.5 text-[11px] hover:bg-slate-50"
                >
                  Шаблон CSV
                </button>
                <button
                  type="button"
                  onClick={() => {
                    setBankFileName('statement.mt940');
                    setBankText(SAMPLE_MT940);
                  }}
                  className="rounded border border-slate-200 px-2 py-0.5 text-[11px] hover:bg-slate-50"
                >
                  Шаблон MT940
                </button>
              </div>
            </div>

            <textarea
              rows={5}
              value={bankText}
              onChange={(e) => setBankText(e.target.value)}
              className="w-full rounded-lg border border-slate-300 p-2 font-mono text-[11px]"
            />

            <button
              type="button"
              onClick={handleImportBank}
              className="w-full rounded-lg bg-teal-700 py-2 font-semibold text-white hover:bg-teal-800"
            >
              Загрузить и сверить выписку (идемпотентно по BankOperationId)
            </button>

            {importResult && (
              <div className="space-y-1.5 rounded-lg border border-slate-200 bg-slate-50 p-3">
                <div className="font-semibold text-slate-900">
                  Обработано строк: {importResult.totalLines} · Сопоставлено: {importResult.matchedCount} · Пропущено дублей:{' '}
                  {importResult.duplicateSkippedCount}
                </div>
                {(importResult.unmatchedQueue || []).length > 0 && (
                  <div className="text-amber-800">
                    Нераспознанная очередь ({importResult.unmatchedQueue.length}):{' '}
                    {importResult.unmatchedQueue.map((u: any) => `${u.bankOperationId} (${u.amount} ${u.currency})`).join(', ')}
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
