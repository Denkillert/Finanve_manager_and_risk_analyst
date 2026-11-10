import React, { useEffect, useState } from 'react';
import {
  AlertTriangle,
  Camera,
  CheckCircle2,
  FilePlus2,
  KeyRound,
  Plus,
  RotateCcw,
  ShieldCheck,
  XCircle,
} from 'lucide-react';
import { apiFetch, type SessionUser } from '../api/client';
import { fmtMoney, fmtNum, REQUEST_STATUS_MAP } from '../utils/format';

interface PaymentRequestsPageProps {
  user: SessionUser;
}

export const PaymentRequestsPage: React.FC<PaymentRequestsPageProps> = ({ user }) => {
  const [subTab, setSubTab] = useState<'requests' | 'acts'>('requests');
  const [requests, setRequests] = useState<any[]>([]);
  const [projects, setProjects] = useState<any[]>([]);
  const [contracts, setContracts] = useState<any[]>([]);
  const [costItems, setCostItems] = useState<any[]>([]);
  const [acts, setActs] = useState<any[]>([]);
  const [statusFilter, setStatusFilter] = useState<string>('ALL');
  const [selected, setSelected] = useState<any>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [lastCheckResult, setLastCheckResult] = useState<any>(null);
  const [msg, setMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null);

  const [form, setForm] = useState({
    projectId: '',
    constructionObjectId: '',
    contractId: '',
    costItemId: '',
    amount: '500',
    currency: 'BYN',
    type: 'CurrentPayment',
    plannedPaymentDate: '2026-11-20',
    purpose: 'Оплата по акту выполненных работ',
    documentType: 'ActS2',
    documentNumber: `С2-${Math.floor(100 + Math.random() * 900)}`,
    documentDate: '2026-11-10',
    actAmount: '500',
    advanceOffset: '0',
    retentionAmount: '0',
  });

  const [overrunReason, setOverrunReason] = useState('Критично для завершения этапа строительства');
  const [edsThumbprint, setEdsThumbprint] = useState('BY-AVEST-2026-FIN-01');
  const [paymentOrderNumber, setPaymentOrderNumber] = useState(`ПП-${Math.floor(200 + Math.random() * 800)}`);

  const [actForm, setActForm] = useState({
    projectId: '',
    constructionObjectId: '',
    contractId: '',
    type: 'ActS2',
    number: `С2-${Math.floor(100 + Math.random() * 900)}`,
    date: '2026-11-10',
    grossAmount: '1200',
    advanceOffset: '300',
    retentionPercent: '5',
    actWorkDescription: 'Монтаж железобетонных конструкций секции 1',
    photoFileName: '',
    photoPreviewUrl: '',
  });

  const loadAll = async () => {
    const [reqs, projData, ctrs, actList] = await Promise.all([
      apiFetch('/api/payment-requests'),
      apiFetch('/api/projects'),
      apiFetch('/api/contracts'),
      apiFetch('/api/acts'),
    ]);
    const projs = projData.projects || [];
    const items = projData.costItems || [];
    const outgoing = (ctrs || []).filter((c: any) => c.direction === 'SupplierSubcontractorOutgoing');

    setRequests(reqs);
    setProjects(projs);
    setContracts(ctrs);
    setCostItems(items);
    setActs(actList);

    if (projs.length > 0 && !form.projectId) {
      const p = projs[0];
      const objId = p.objects?.[0]?.id || '';
      const defCtr = outgoing[0]?.id || ctrs[0]?.id || '';
      setForm((f) => ({
        ...f,
        projectId: p.id,
        constructionObjectId: objId,
        contractId: defCtr,
        costItemId: items[0]?.id || '',
      }));
      setActForm((a) => ({
        ...a,
        projectId: p.id,
        constructionObjectId: objId,
        contractId: defCtr,
      }));
    }
    if (selected) {
      const refreshed = reqs.find((r: any) => r.id === selected.id);
      setSelected(refreshed || null);
    }
  };

  useEffect(() => {
    loadAll().catch((e) => setMsg({ type: 'err', text: e.message }));
  }, []);

  const filteredRequests =
    statusFilter === 'ALL' ? requests : requests.filter((r) => r.status === statusFilter);

  const selectedProject = projects.find((p) => p.id === form.projectId);
  const projectObjects = selectedProject?.objects || [];
  const outgoingContracts = contracts.filter((c) => c.direction === 'SupplierSubcontractorOutgoing');

  const handleCreateRequest = async (e: React.FormEvent) => {
    e.preventDefault();
    setMsg(null);
    setLastCheckResult(null);
    try {
      const amt = Number(form.amount);
      const res = await apiFetch('/api/payment-requests', {
        method: 'POST',
        body: JSON.stringify({
          ...form,
          amount: amt,
          actAmount: Number(form.actAmount) || amt,
          advanceOffset: Number(form.advanceOffset) || 0,
          retentionAmount: Number(form.retentionAmount) || 0,
          vatIncluded: true,
          submitImmediately: true,
        }),
      });
      setLastCheckResult(res.checks);
      await loadAll();
      setShowCreate(false);
      setMsg({
        type: res.request.status === 'Blocked' ? 'err' : 'ok',
        text: `Заявка ${res.request.number} создана. Статус: ${
          REQUEST_STATUS_MAP[res.request.status]?.label || res.request.status
        }`,
      });
    } catch (err: any) {
      setMsg({ type: 'err', text: err.message });
    }
  };

  const runAction = async (endpoint: string, body: any, successText: string) => {
    setMsg(null);
    try {
      await apiFetch(endpoint, { method: 'POST', body: JSON.stringify(body) });
      await loadAll();
      setMsg({ type: 'ok', text: successText });
    } catch (err: any) {
      setMsg({ type: 'err', text: err.message });
    }
  };

  const handlePhotoChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const url = URL.createObjectURL(file);
    setActForm((a) => ({ ...a, photoFileName: file.name, photoPreviewUrl: url }));
  };

  const handleCreateAct = async (e: React.FormEvent) => {
    e.preventDefault();
    setMsg(null);
    try {
      const res = await apiFetch('/api/acts', {
        method: 'POST',
        body: JSON.stringify({
          contractId: actForm.contractId,
          constructionObjectId: actForm.constructionObjectId,
          type: actForm.type,
          number: actForm.number,
          date: actForm.date,
          grossAmount: Number(actForm.grossAmount),
          advanceOffset: Number(actForm.advanceOffset),
          retentionPercent: Number(actForm.retentionPercent),
          actWorkDescription: actForm.actWorkDescription,
          photoEvidencePaths: actForm.photoFileName || null,
        }),
      });
      await loadAll();
      setMsg({
        type: 'ok',
        text: `Акт ${actForm.number} сохранён: к оплате Sопл = ${fmtMoney(res.payable?.payableAmount)}`,
      });
    } catch (err: any) {
      setMsg({ type: 'err', text: err.message });
    }
  };

  const gross = Number(actForm.grossAmount || 0);
  const net = gross / 1.2;
  const estRetention = net * (Number(actForm.retentionPercent || 0) / 100);
  const computedActPayable = gross - Number(actForm.advanceOffset || 0) - estRetention;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="inline-flex rounded-lg border border-slate-200 bg-white p-1">
          <button
            onClick={() => setSubTab('requests')}
            className={`rounded-md px-3 py-1.5 text-xs font-semibold ${
              subTab === 'requests' ? 'bg-sky-600 text-white' : 'text-slate-600 hover:bg-slate-50'
            }`}
          >
            Реестр платёжных заявок ({requests.length})
          </button>
          <button
            onClick={() => setSubTab('acts')}
            className={`rounded-md px-3 py-1.5 text-xs font-semibold ${
              subTab === 'acts' ? 'bg-sky-600 text-white' : 'text-slate-600 hover:bg-slate-50'
            }`}
          >
            Акты С-2 / С-3 и фотофиксация ({acts.length})
          </button>
        </div>

        {subTab === 'requests' && user.roleCode !== 'Admin' && (
          <button
            onClick={() => setShowCreate((s) => !s)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-sky-600 px-3 py-2 text-xs font-semibold text-white shadow-sm hover:bg-sky-700"
          >
            <Plus className="h-4 w-4" /> Создать платёжную заявку
          </button>
        )}
      </div>

      {msg && (
        <div
          className={`rounded-xl border p-3 text-xs font-medium ${
            msg.type === 'ok' ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-rose-200 bg-rose-50 text-rose-800'
          }`}
        >
          {msg.text}
        </div>
      )}

      {lastCheckResult && (
        <div className="rounded-xl border border-sky-200 bg-sky-50 p-3.5 text-xs">
          <div className="font-bold text-sky-900">Результаты 4 автоматических проверок при подаче заявки:</div>
          <div className="mt-2 grid gap-2 sm:grid-cols-4">
            <div className="rounded-lg border border-sky-100 bg-white p-2">
              <div className="text-[11px] text-slate-500">Договор и документ</div>
              <div className={`font-semibold ${lastCheckResult.contractAndDocumentsValid ? 'text-emerald-700' : 'text-rose-600'}`}>
                {lastCheckResult.contractAndDocumentsValid ? 'Проверено ✓' : 'Превышение договора ✕'}
              </div>
            </div>
            <div className="rounded-lg border border-sky-100 bg-white p-2">
              <div className="text-[11px] text-slate-500">Лимит Lдост</div>
              <div className={`font-semibold ${lastCheckResult.freeLimitSufficient ? 'text-emerald-700' : 'text-rose-600'}`}>
                {lastCheckResult.freeLimitSufficient
                  ? `В пределах (${fmtNum(lastCheckResult.freeLimit)}) ✓`
                  : `Перерасход (Lдост=${fmtNum(lastCheckResult.freeLimit)})`}
              </div>
            </div>
            <div className="rounded-lg border border-sky-100 bg-white p-2">
              <div className="text-[11px] text-slate-500">Контроль авансов</div>
              <div className={`font-semibold ${lastCheckResult.advanceCheckPassed ? 'text-emerald-700' : 'text-rose-600'}`}>
                {lastCheckResult.advanceCheckPassed
                  ? lastCheckResult.hasOverdueAdvanceWarning
                    ? 'Просроч. аванс (предупр.) ⚠'
                    : 'ОК ✓'
                  : 'Заблокировано ✕'}
              </div>
            </div>
            <div className="rounded-lg border border-sky-100 bg-white p-2">
              <div className="text-[11px] text-slate-500">Кассовый разрыв</div>
              <div className={`font-semibold ${lastCheckResult.hasCashGapOnPlannedDate ? 'text-amber-700' : 'text-emerald-700'}`}>
                {lastCheckResult.hasCashGapOnPlannedDate
                  ? `Разрыв ${fmtNum(lastCheckResult.cashGapAmount)} тыс. р. ⚠`
                  : 'Остаток достаточен ✓'}
              </div>
            </div>
          </div>
        </div>
      )}

      {subTab === 'requests' ? (
        <>
          {showCreate && (
            <form onSubmit={handleCreateRequest} className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
              <div className="text-sm font-bold text-slate-900">
                Новая платёжная заявка (контроль лимита Lдост, авансов, дублей и кассы)
              </div>
              <div className="grid gap-3 text-xs sm:grid-cols-2 lg:grid-cols-4">
                <div>
                  <label className="font-semibold text-slate-600">Проект</label>
                  <select
                    value={form.projectId}
                    onChange={(e) => {
                      const p = projects.find((x) => x.id === e.target.value);
                      setForm({ ...form, projectId: e.target.value, constructionObjectId: p?.objects?.[0]?.id || '' });
                    }}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  >
                    {projects.map((p) => (
                      <option key={p.id} value={p.id}>{p.code} {p.name}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Объект</label>
                  <select
                    value={form.constructionObjectId}
                    onChange={(e) => setForm({ ...form, constructionObjectId: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  >
                    {projectObjects.map((o: any) => (
                      <option key={o.id} value={o.id}>{o.name}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Договор</label>
                  <select
                    value={form.contractId}
                    onChange={(e) => setForm({ ...form, contractId: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  >
                    {outgoingContracts.map((c) => (
                      <option key={c.id} value={c.id}>{c.number} — {c.counterparty}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Статья затрат</label>
                  <select
                    value={form.costItemId}
                    onChange={(e) => setForm({ ...form, costItemId: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  >
                    {costItems.map((c) => (
                      <option key={c.id} value={c.id}>{c.code} {c.name}</option>
                    ))}
                  </select>
                </div>

                <div>
                  <label className="font-semibold text-slate-600">Тип платежа</label>
                  <select
                    value={form.type}
                    onChange={(e) => setForm({ ...form, type: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  >
                    <option value="CurrentPayment">Текущий платёж (по акту)</option>
                    <option value="Advance">Новый аванс</option>
                  </select>
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Сумма платежа</label>
                  <input
                    type="number"
                    step="0.01"
                    required
                    value={form.amount}
                    onChange={(e) => setForm({ ...form, amount: e.target.value, actAmount: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  />
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Валюта</label>
                  <select
                    value={form.currency}
                    onChange={(e) => setForm({ ...form, currency: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  >
                    <option value="BYN">BYN</option>
                    <option value="USD">USD</option>
                    <option value="EUR">EUR</option>
                    <option value="RUB">RUB</option>
                  </select>
                </div>
                <div>
                  <label className="font-semibold text-slate-600">План. дата оплаты</label>
                  <input
                    type="date"
                    required
                    value={form.plannedPaymentDate}
                    onChange={(e) => setForm({ ...form, plannedPaymentDate: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  />
                </div>

                <div>
                  <label className="font-semibold text-slate-600">Документ-основание</label>
                  <select
                    value={form.documentType}
                    onChange={(e) => setForm({ ...form, documentType: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  >
                    <option value="ActS2">Акт С-2</option>
                    <option value="CertificateS3">Справка С-3</option>
                    <option value="Invoice">Счёт на оплату</option>
                  </select>
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Номер документа</label>
                  <input
                    required
                    value={form.documentNumber}
                    onChange={(e) => setForm({ ...form, documentNumber: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  />
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Дата документа</label>
                  <input
                    type="date"
                    required
                    value={form.documentDate}
                    onChange={(e) => setForm({ ...form, documentDate: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  />
                </div>
                <div>
                  <label className="font-semibold text-slate-600">Назначение платежа</label>
                  <input
                    required
                    value={form.purpose}
                    onChange={(e) => setForm({ ...form, purpose: e.target.value })}
                    className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                  />
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button type="button" onClick={() => setShowCreate(false)} className="rounded-lg border border-slate-300 px-3 py-1.5 text-xs">
                  Отмена
                </button>
                <button type="submit" className="rounded-lg bg-emerald-600 px-4 py-1.5 text-xs font-semibold text-white hover:bg-emerald-700">
                  Подать заявку и выполнить проверки
                </button>
              </div>
            </form>
          )}

          {/* Filter Pills */}
          <div className="flex flex-wrap gap-1.5">
            {['ALL', 'Blocked', 'PendingApproval', 'Approved', 'Executed', 'Closed', 'Returned', 'Rejected'].map((st) => (
              <button
                key={st}
                onClick={() => setStatusFilter(st)}
                className={`rounded-full border px-3 py-1 text-xs font-medium ${
                  statusFilter === st
                    ? 'border-sky-600 bg-sky-600 text-white'
                    : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
                }`}
              >
                {st === 'ALL' ? 'Все' : REQUEST_STATUS_MAP[st]?.label || st}
              </button>
            ))}
          </div>

          <div className="grid gap-4 lg:grid-cols-12">
            <div className="space-y-2.5 lg:col-span-7">
              {filteredRequests.map((req) => {
                const st = REQUEST_STATUS_MAP[req.status] || { label: req.status, badge: 'bg-slate-100 text-slate-700' };
                const active = selected?.id === req.id;
                return (
                  <button
                    key={req.id}
                    onClick={() => setSelected(req)}
                    className={`w-full rounded-xl border p-3.5 text-left transition ${
                      active ? 'border-sky-500 bg-sky-50/40 ring-1 ring-sky-500' : 'border-slate-200 bg-white hover:border-slate-300'
                    }`}
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <div className="flex items-center gap-2">
                        <span className="font-mono text-xs font-bold text-slate-900">{req.number}</span>
                        <span className={`rounded-full border px-2 py-0.5 text-[11px] font-medium ${st.badge}`}>{st.label}</span>
                        <span className="rounded bg-slate-100 px-1.5 py-0.5 text-[10px] text-slate-600">
                          {req.type === 'Advance' ? 'Аванс' : 'Текущий'}
                        </span>
                      </div>
                      <span className="text-sm font-bold text-slate-900 tabular-nums">{fmtMoney(req.amount_in_byn)}</span>
                    </div>
                    <div className="mt-1.5 text-xs font-medium text-slate-800">{req.purpose}</div>
                    <div className="mt-1 flex flex-wrap items-center justify-between gap-2 text-[11px] text-slate-500">
                      <span>{req.project_name} · {req.counterparty}</span>
                      <span>Статья: {req.cost_item_code} · План: {req.planned_payment_date}</span>
                    </div>
                  </button>
                );
              })}
            </div>

            <div className="lg:col-span-5">
              {selected ? (
                <div className="sticky top-20 space-y-3.5 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm">
                  <div className="flex items-start justify-between gap-2 border-b border-slate-100 pb-3">
                    <div>
                      <div className="font-mono text-sm font-bold text-slate-900">{selected.number}</div>
                      <div className="text-[11px] text-slate-500">
                        Версия (rowVersion): {selected.row_version} · Автор: {selected.author_name}
                      </div>
                    </div>
                    <span className={`rounded-full border px-2.5 py-0.5 text-[11px] font-semibold ${REQUEST_STATUS_MAP[selected.status]?.badge}`}>
                      {REQUEST_STATUS_MAP[selected.status]?.label}
                    </span>
                  </div>

                  <div className="grid grid-cols-2 gap-2 rounded-lg bg-slate-50 p-3 tabular-nums">
                    <div>
                      <div className="text-[11px] text-slate-500">Сумма к оплате</div>
                      <div className="text-sm font-bold text-slate-900">{fmtMoney(selected.amount_in_byn)}</div>
                    </div>
                    <div>
                      <div className="text-[11px] text-slate-500">Статья затрат</div>
                      <div className="font-semibold text-slate-800">{selected.cost_item_code} {selected.cost_item_name}</div>
                    </div>
                    <div>
                      <div className="text-[11px] text-slate-500">Договор</div>
                      <div className="font-medium text-slate-800">{selected.contract_number}</div>
                    </div>
                    <div>
                      <div className="text-[11px] text-slate-500">План. дата</div>
                      <div className="font-medium text-slate-800">{selected.planned_payment_date}</div>
                    </div>
                  </div>

                  {selected.block_reason && (
                    <div className="rounded-lg border border-rose-200 bg-rose-50 p-2.5 text-rose-800">
                      <span className="font-semibold">Причина блокировки:</span> {selected.block_reason}
                    </div>
                  )}

                  {selected.has_overdue_advance_warning === 1 && (
                    <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-2.5 text-amber-900">
                      <AlertTriangle className="h-4 w-4 shrink-0" />
                      <span>Внимание: по договору числится просроченный непогашенный аванс.</span>
                    </div>
                  )}

                  {selected.eds_signature_thumbprint && (
                    <div className="flex items-center gap-2 rounded-lg border border-emerald-200 bg-emerald-50 p-2.5 text-emerald-900">
                      <KeyRound className="h-4 w-4 shrink-0" />
                      <span>Подписано ЭЦП: <span className="font-mono font-semibold">{selected.eds_signature_thumbprint}</span></span>
                    </div>
                  )}

                  <div className="space-y-2.5 border-t border-slate-100 pt-3">
                    {selected.status === 'Blocked' && (user.roleCode === 'CEO' || user.roleCode === 'FinancialManager') && (
                      <div className="space-y-2 rounded-lg border border-violet-200 bg-violet-50 p-3">
                        <div className="font-semibold text-violet-900">Согласование перерасхода (UC04 — Руководитель организации)</div>
                        <input
                          value={overrunReason}
                          onChange={(e) => setOverrunReason(e.target.value)}
                          placeholder="Обоснование перерасхода..."
                          className="w-full rounded border border-violet-300 bg-white px-2.5 py-1.5 text-xs"
                        />
                        <div className="flex gap-2">
                          <button
                            onClick={() =>
                              runAction(
                                `/api/payment-requests/${selected.id}/approve-overrun`,
                                { approve: true, reason: overrunReason, rowVersion: selected.row_version },
                                'Перерасход согласован руководителем, заявка передана на утверждение'
                              )
                            }
                            className="flex-1 rounded-lg bg-violet-700 px-3 py-1.5 font-semibold text-white hover:bg-violet-800"
                          >
                            Одобрить перерасход
                          </button>
                          <button
                            onClick={() =>
                              runAction(
                                `/api/payment-requests/${selected.id}/approve-overrun`,
                                { approve: false, reason: overrunReason, rowVersion: selected.row_version },
                                'Заявка отклонена по перерасходу'
                              )
                            }
                            className="rounded-lg bg-rose-600 px-3 py-1.5 font-semibold text-white"
                          >
                            Отклонить
                          </button>
                        </div>
                      </div>
                    )}

                    {selected.status === 'PendingApproval' && (
                      <div className="space-y-2 rounded-lg border border-indigo-200 bg-indigo-50 p-3">
                        <div className="font-semibold text-indigo-900">Утверждение заявки с ЭЦП</div>
                        <input
                          value={edsThumbprint}
                          onChange={(e) => setEdsThumbprint(e.target.value)}
                          placeholder="Отпечаток сертификата ЭЦП (Авест)"
                          className="w-full rounded border border-indigo-300 bg-white px-2.5 py-1.5 font-mono text-xs"
                        />
                        <div className="flex flex-wrap gap-2">
                          <button
                            onClick={() =>
                              runAction(
                                `/api/payment-requests/${selected.id}/approve`,
                                { edsCertificateThumbprint: edsThumbprint, rowVersion: selected.row_version },
                                'Заявка согласована и подписана ЭЦП'
                              )
                            }
                            className="inline-flex flex-1 items-center justify-center gap-1 rounded-lg bg-emerald-600 px-3 py-1.5 font-semibold text-white hover:bg-emerald-700"
                          >
                            <ShieldCheck className="h-3.5 w-3.5" /> Согласовать (ЭЦП)
                          </button>
                          <button
                            onClick={() =>
                              runAction(
                                `/api/payment-requests/${selected.id}/return-or-reject`,
                                { rejectPermanently: false, reason: 'Требуется уточнение спецификации', rowVersion: selected.row_version },
                                'Заявка возвращена инициатору на доработку'
                              )
                            }
                            className="inline-flex items-center gap-1 rounded-lg bg-amber-500 px-3 py-1.5 font-semibold text-white"
                          >
                            <RotateCcw className="h-3.5 w-3.5" /> Вернуть
                          </button>
                          <button
                            onClick={() =>
                              runAction(
                                `/api/payment-requests/${selected.id}/return-or-reject`,
                                { rejectPermanently: true, reason: 'Отклонено финансовой службой', rowVersion: selected.row_version },
                                'Заявка отклонена'
                              )
                            }
                            className="inline-flex items-center gap-1 rounded-lg bg-rose-600 px-3 py-1.5 font-semibold text-white"
                          >
                            <XCircle className="h-3.5 w-3.5" /> Отклонить
                          </button>
                        </div>
                      </div>
                    )}

                    {selected.status === 'Approved' && (
                      <div className="space-y-2 rounded-lg border border-teal-200 bg-teal-50 p-3">
                        <div className="font-semibold text-teal-900">Исполнение платежа (Бухгалтер)</div>
                        <input
                          value={paymentOrderNumber}
                          onChange={(e) => setPaymentOrderNumber(e.target.value)}
                          placeholder="Номер платёжного поручения"
                          className="w-full rounded border border-teal-300 bg-white px-2.5 py-1.5 font-mono text-xs"
                        />
                        <button
                          onClick={() =>
                            runAction(
                              `/api/payment-requests/${selected.id}/execute`,
                              { paymentOrderNumber, rowVersion: selected.row_version },
                              'Платёж исполнен бухгалтером; заявка ожидает банковскую выписку'
                            )
                          }
                          className="flex w-full items-center justify-center gap-1.5 rounded-lg bg-teal-700 px-3 py-2 font-semibold text-white hover:bg-teal-800"
                        >
                          <CheckCircle2 className="h-4 w-4" /> Сформировать платёжное поручение
                        </button>
                      </div>
                    )}
                  </div>
                </div>
              ) : (
                <div className="rounded-xl border border-dashed border-slate-300 bg-white p-8 text-center text-xs text-slate-500">
                  Выберите заявку слева, чтобы просмотреть проверки и доступные по роли действия.
                </div>
              )}
            </div>
          </div>
        </>
      ) : (
        <div className="grid gap-5 lg:grid-cols-12">
          <form onSubmit={handleCreateAct} className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm lg:col-span-5">
            <div className="flex items-center gap-2 text-sm font-bold text-slate-900">
              <FilePlus2 className="h-4 w-4 text-sky-600" />
              Подготовка акта С-2 / справки С-3 и фотофиксация работ
            </div>
            <p className="text-[11px] text-slate-500">
              Расчёт к оплате по Инструкции № 53 / Постановлению № 29: Sопл = Sакт − Aзач − Uуд.
            </p>

            <div className="grid gap-2.5 sm:grid-cols-2">
              <div>
                <label className="font-semibold text-slate-600">Объект строительства</label>
                <select
                  value={actForm.constructionObjectId}
                  onChange={(e) => setActForm({ ...actForm, constructionObjectId: e.target.value })}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                >
                  {projects.flatMap((p) => p.objects || []).map((o: any) => (
                    <option key={o.id} value={o.id}>{o.name}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="font-semibold text-slate-600">Договор</label>
                <select
                  value={actForm.contractId}
                  onChange={(e) => setActForm({ ...actForm, contractId: e.target.value })}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                >
                  {contracts.map((c) => (
                    <option key={c.id} value={c.id}>{c.number}</option>
                  ))}
                </select>
              </div>
              <div>
                <label className="font-semibold text-slate-600">Тип документа</label>
                <select
                  value={actForm.type}
                  onChange={(e) => setActForm({ ...actForm, type: e.target.value })}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                >
                  <option value="ActS2">Акт С-2 (выполненные работы)</option>
                  <option value="CertificateS3">Справка С-3 (расчёты за работы)</option>
                </select>
              </div>
              <div>
                <label className="font-semibold text-slate-600">Номер акта</label>
                <input
                  required
                  value={actForm.number}
                  onChange={(e) => setActForm({ ...actForm, number: e.target.value })}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                />
              </div>

              <div>
                <label className="font-semibold text-slate-600">Сумма по акту (Sакт)</label>
                <input
                  type="number"
                  step="0.01"
                  required
                  value={actForm.grossAmount}
                  onChange={(e) => setActForm({ ...actForm, grossAmount: e.target.value })}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                />
              </div>
              <div>
                <label className="font-semibold text-slate-600">Зачёт аванса (Aзач)</label>
                <input
                  type="number"
                  step="0.01"
                  required
                  value={actForm.advanceOffset}
                  onChange={(e) => setActForm({ ...actForm, advanceOffset: e.target.value })}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                />
              </div>
              <div>
                <label className="font-semibold text-slate-600">Удержание (% от базы)</label>
                <input
                  type="number"
                  step="0.1"
                  required
                  value={actForm.retentionPercent}
                  onChange={(e) => setActForm({ ...actForm, retentionPercent: e.target.value })}
                  className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
                />
              </div>
              <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-2">
                <div className="text-[10px] font-semibold text-emerald-700">К оплате Sопл (ф-ла 1.12)</div>
                <div className="text-sm font-bold text-emerald-900 tabular-nums">{fmtMoney(computedActPayable)}</div>
              </div>
            </div>

            <div>
              <label className="font-semibold text-slate-600">Описание выполненных работ</label>
              <input
                required
                value={actForm.actWorkDescription}
                onChange={(e) => setActForm({ ...actForm, actWorkDescription: e.target.value })}
                className="mt-1 w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
              />
            </div>

            <div>
              <label className="font-semibold text-slate-600">Фотофиксация выполненных работ на объекте</label>
              <label className="mt-1 flex cursor-pointer items-center justify-center gap-2 rounded-lg border border-dashed border-sky-300 bg-sky-50/50 p-3 text-sky-800 hover:bg-sky-50">
                <Camera className="h-4 w-4" />
                <span>{actForm.photoFileName || 'Прикрепить фото с площадки (.jpg / .png)'}</span>
                <input type="file" accept="image/*" onChange={handlePhotoChange} className="hidden" />
              </label>
              {actForm.photoPreviewUrl && (
                <img src={actForm.photoPreviewUrl} alt="Фотофиксация" className="mt-2 h-28 w-full rounded-lg border border-slate-200 object-cover" />
              )}
            </div>

            <button type="submit" className="w-full rounded-lg bg-sky-600 py-2 font-semibold text-white hover:bg-sky-700">
              Сохранить акт С-2 / С-3
            </button>
          </form>

          <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-7">
            <h3 className="text-sm font-semibold text-slate-900">Реестр актов С-2 и справок С-3</h3>
            <div className="mt-3 overflow-x-auto">
              <table className="w-full text-left text-xs tabular-nums">
                <thead>
                  <tr className="border-b border-slate-200 text-[11px] text-slate-500">
                    <th className="py-2">Акт</th>
                    <th className="py-2">Объект / Договор</th>
                    <th className="py-2 text-right">Sакт</th>
                    <th className="py-2 text-right">Aзач</th>
                    <th className="py-2 text-right">Uуд</th>
                    <th className="py-2 text-right">Sопл</th>
                    <th className="py-2">Фото</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {acts.map((a) => {
                    const payable = Number(a.gross_amount) - Number(a.advance_offset) - Number(a.retention_amount);
                    return (
                      <tr key={a.id}>
                        <td className="py-2 font-semibold">
                          {a.number}
                          <div className="text-[10px] text-slate-400">{a.date}</div>
                        </td>
                        <td className="py-2">
                          {a.object_name}
                          <div className="text-[10px] text-slate-500">{a.contract_number}</div>
                        </td>
                        <td className="py-2 text-right">{fmtNum(a.gross_amount)}</td>
                        <td className="py-2 text-right text-amber-700">{fmtNum(a.advance_offset)}</td>
                        <td className="py-2 text-right text-slate-600">{fmtNum(a.retention_amount)}</td>
                        <td className="py-2 text-right font-bold text-emerald-700">{fmtNum(payable)}</td>
                        <td className="py-2 text-[11px] text-sky-700">{a.photo_evidence_paths || '—'}</td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
