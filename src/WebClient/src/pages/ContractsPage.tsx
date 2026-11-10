import React, { useEffect, useState } from 'react';
import { Download, FileUp, Plus } from 'lucide-react';
import { apiFetch, downloadFile } from '../api/client';
import { fmtMoney, fmtNum } from '../utils/format';

const SAMPLE_ESTIMATE_XML = `<Estimate>
  <Item Code="ST-01" Name="Бетон, арматура и материалы" Amount="4500" />
  <Item Code="ST-02" Name="Субподрядные монтажные работы" Amount="3200" />
  <Item Code="ST-03" Name="Механизмы и аренда башенного крана" Amount="1200" />
</Estimate>`;

export const ContractsPage: React.FC = () => {
  const [contracts, setContracts] = useState<any[]>([]);
  const [advances, setAdvances] = useState<any[]>([]);
  const [projects, setProjects] = useState<any[]>([]);
  const [showAdd, setShowAdd] = useState(false);
  const [estContent, setEstContent] = useState(SAMPLE_ESTIMATE_XML);
  const [estResult, setEstResult] = useState<any>(null);
  const [msg, setMsg] = useState<string | null>(null);

  const [newContract, setNewContract] = useState({
    projectId: '',
    number: `ДГ-2026/${Math.floor(10 + Math.random() * 89)}`,
    counterparty: 'ООО «БелСтройПоставка»',
    counterpartyUnp: '190887766',
    direction: 'SupplierSubcontractorOutgoing',
    totalAmount: '2500',
    currency: 'BYN',
  });

  const loadAll = async () => {
    const [ctrs, advs, projData] = await Promise.all([
      apiFetch('/api/contracts'),
      apiFetch('/api/advances'),
      apiFetch('/api/projects'),
    ]);
    const projs = projData.projects || [];
    setContracts(ctrs);
    setAdvances(advs);
    setProjects(projs);
    if (projs.length > 0 && !newContract.projectId) {
      setNewContract((c) => ({ ...c, projectId: projs[0].id }));
    }
  };

  useEffect(() => {
    loadAll().catch((e) => setMsg(e.message));
  }, []);

  const obligations = contracts.flatMap((c) =>
    (c.obligations || []).map((o: any) => ({ ...o, contract_number: c.number, project_code: c.project_code }))
  );

  const handleCreateContract = async (e: React.FormEvent) => {
    e.preventDefault();
    setMsg(null);
    try {
      await apiFetch('/api/contracts', {
        method: 'POST',
        body: JSON.stringify({
          ...newContract,
          totalAmount: Number(newContract.totalAmount),
          vatIncluded: true,
          retentionPercent: 5,
        }),
      });
      setShowAdd(false);
      await loadAll();
      setMsg(`Договор ${newContract.number} зарегистрирован.`);
    } catch (err: any) {
      setMsg(err.message);
    }
  };

  const handleOffsetAdvance = async (advId: string) => {
    setMsg(null);
    try {
      await apiFetch(`/api/advances/${advId}/offset`, {
        method: 'POST',
        body: JSON.stringify({ offsetAmount: 100 }),
      });
      await loadAll();
      setMsg('Зачтено 100 тыс. р. аванса по акту выполненных работ.');
    } catch (err: any) {
      setMsg(err.message);
    }
  };

  const handleImportEstimate = async () => {
    setMsg(null);
    try {
      const res = await apiFetch('/api/integrations/estimate-import', {
        method: 'POST',
        body: JSON.stringify({ fileName: 'estimate.xml', content: estContent }),
      });
      setEstResult(res);
    } catch (err: any) {
      setMsg(err.message);
    }
  };

  return (
    <div className="space-y-5">
      {msg && <div className="rounded-xl border border-sky-200 bg-sky-50 p-3 text-xs font-medium text-sky-900">{msg}</div>}

      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-sm font-semibold text-slate-900">Реестр договоров, обязательств и авансов</h2>
        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => downloadFile('/api/integrations/accounting-export', '1c_accounting_export.csv')}
            className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-1.5 text-xs font-semibold text-slate-700 hover:bg-slate-50"
          >
            <Download className="h-3.5 w-3.5" /> Выгрузка в 1С (CSV)
          </button>
          <button
            onClick={() => setShowAdd((s) => !s)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-sky-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-sky-700"
          >
            <Plus className="h-3.5 w-3.5" /> Новый договор
          </button>
        </div>
      </div>

      {showAdd && (
        <form onSubmit={handleCreateContract} className="grid gap-3 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm sm:grid-cols-3">
          <select
            value={newContract.projectId}
            onChange={(e) => setNewContract({ ...newContract, projectId: e.target.value })}
            className="rounded-lg border border-slate-300 px-2.5 py-1.5"
          >
            {projects.map((p) => (
              <option key={p.id} value={p.id}>{p.code} {p.name}</option>
            ))}
          </select>
          <input
            required
            placeholder="Номер договора"
            value={newContract.number}
            onChange={(e) => setNewContract({ ...newContract, number: e.target.value })}
            className="rounded-lg border border-slate-300 px-2.5 py-1.5"
          />
          <input
            required
            placeholder="Контрагент"
            value={newContract.counterparty}
            onChange={(e) => setNewContract({ ...newContract, counterparty: e.target.value })}
            className="rounded-lg border border-slate-300 px-2.5 py-1.5"
          />
          <select
            value={newContract.direction}
            onChange={(e) => setNewContract({ ...newContract, direction: e.target.value })}
            className="rounded-lg border border-slate-300 px-2.5 py-1.5"
          >
            <option value="SupplierSubcontractorOutgoing">Расходный (поставщик / субподряд)</option>
            <option value="CustomerIncoming">Доходный (заказчик)</option>
          </select>
          <input
            type="number"
            step="0.01"
            required
            placeholder="Сумма договора"
            value={newContract.totalAmount}
            onChange={(e) => setNewContract({ ...newContract, totalAmount: e.target.value })}
            className="rounded-lg border border-slate-300 px-2.5 py-1.5"
          />
          <button type="submit" className="rounded-lg bg-emerald-600 px-3 py-1.5 font-semibold text-white">
            Сохранить договор
          </button>
        </form>
      )}

      <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[700px] text-left text-xs tabular-nums">
            <thead>
              <tr className="border-b border-slate-200 text-[11px] text-slate-500">
                <th className="py-2">Номер</th>
                <th className="py-2">Направление</th>
                <th className="py-2">Контрагент (УНП)</th>
                <th className="py-2">Проект</th>
                <th className="py-2 text-right">Сумма</th>
                <th className="py-2 text-right">НДС</th>
                <th className="py-2 text-right">Удерж.</th>
                <th className="py-2">График</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {contracts.map((c) => (
                <tr key={c.id}>
                  <td className="py-2 font-mono font-bold text-slate-900">{c.number}</td>
                  <td className="py-2">
                    <span
                      className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                        c.direction === 'CustomerIncoming' ? 'bg-emerald-50 text-emerald-800' : 'bg-sky-50 text-sky-800'
                      }`}
                    >
                      {c.direction === 'CustomerIncoming' ? 'Доходный' : 'Расходный'}
                    </span>
                  </td>
                  <td className="py-2">
                    {c.counterparty} <span className="text-slate-400">({c.counterparty_unp})</span>
                  </td>
                  <td className="py-2 text-slate-600">{c.project_name}</td>
                  <td className="py-2 text-right font-semibold">{fmtNum(c.effectiveTotal ?? c.total_amount)} {c.currency}</td>
                  <td className="py-2 text-right">{c.vat_included ? `${c.vat_rate}% (вкл.)` : 'Без НДС'}</td>
                  <td className="py-2 text-right">{c.retention_percent}%</td>
                  <td className="py-2 text-[11px] text-slate-500">{(c.schedule || []).length} этап(ов)</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div className="grid gap-5 lg:grid-cols-2">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <h3 className="text-sm font-semibold text-slate-900">Контроль авансов (Aост = A − Aисп, Kисп = Aисп / A)</h3>
          <div className="mt-3 space-y-2.5 text-xs">
            {advances.map((a) => (
              <div
                key={a.id}
                className={`rounded-lg border p-3 ${
                  a.status === 'Overdue' ? 'border-rose-300 bg-rose-50/50' : 'border-slate-200'
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <div>
                    <span className="font-mono font-bold text-slate-900">{a.contract_number}</span> · {a.counterparty}
                  </div>
                  <span
                    className={`rounded-full px-2 py-0.5 text-[10px] font-bold ${
                      a.status === 'Overdue'
                        ? 'bg-rose-600 text-white'
                        : a.status === 'Closed'
                        ? 'bg-emerald-100 text-emerald-800'
                        : 'bg-sky-100 text-sky-800'
                    }`}
                  >
                    {a.status === 'Overdue' ? 'ПРОСРОЧЕН' : a.status === 'Closed' ? 'Погашен' : 'Активный'}
                  </span>
                </div>
                <div className="mt-2 grid grid-cols-4 gap-2 tabular-nums">
                  <div>Выдано A: <b>{fmtNum(a.amount)}</b></div>
                  <div>Зачтено: <b>{fmtNum(a.used_amount)}</b></div>
                  <div>Остаток Aост: <b className="text-rose-700">{fmtNum(a.remainder)}</b></div>
                  <div>Kисп: <b>{a.utilizationPercent}%</b></div>
                </div>
                <div className="mt-2 flex items-center justify-between text-[11px] text-slate-500">
                  <span>Срок закрытия: {a.due_date}</span>
                  {a.remainder > 0 && (
                    <button onClick={() => handleOffsetAdvance(a.id)} className="font-semibold text-sky-600 hover:underline">
                      Зачесть 100 тыс. р. по акту
                    </button>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm">
          <div className="flex items-center gap-2 text-sm font-semibold text-slate-900">
            <FileUp className="h-4 w-4 text-indigo-600" />
            Импорт сметы (XML / CSV) и сопоставление со статьями затрат (НФТ-7 ≥ 95%)
          </div>
          <textarea
            rows={4}
            value={estContent}
            onChange={(e) => setEstContent(e.target.value)}
            className="w-full rounded-lg border border-slate-300 p-2 font-mono text-[11px]"
          />
          <button
            onClick={handleImportEstimate}
            className="rounded-lg bg-indigo-600 px-3 py-1.5 font-semibold text-white hover:bg-indigo-700"
          >
            Импортировать смету и сопоставить статьи
          </button>
          {estResult && (
            <div className="rounded-lg bg-indigo-50 p-3 text-indigo-950">
              Сопоставлено: <b>{estResult.matchedCount}</b> из <b>{estResult.totalLines}</b> ({estResult.matchPercent}%), сумма:{' '}
              <b>{fmtMoney(estResult.totalAmount)}</b>
            </div>
          )}

          <div className="border-t border-slate-100 pt-3">
            <div className="mb-2 font-semibold text-slate-800">Активные обязательства ({obligations.length})</div>
            <div className="space-y-1.5">
              {obligations.map((o) => (
                <div key={o.id} className="flex items-center justify-between rounded bg-slate-50 px-2.5 py-1.5 tabular-nums">
                  <span>{o.project_code} · {o.cost_item_code} · {o.contract_number}</span>
                  <span>O: <b>{fmtNum(o.gross_amount)}</b> / F: <b>{fmtNum(o.paid_amount)}</b></span>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
};
