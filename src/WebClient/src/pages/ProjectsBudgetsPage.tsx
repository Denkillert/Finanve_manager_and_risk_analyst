import React, { useEffect, useState } from 'react';
import { AlertCircle, CheckCircle, CopyPlus, FolderTree, Lock, Plus } from 'lucide-react';
import { apiFetch } from '../api/client';
import { fmtMoney, fmtNum } from '../utils/format';

export const ProjectsBudgetsPage: React.FC = () => {
  const [projects, setProjects] = useState<any[]>([]);
  const [freeLimits, setFreeLimits] = useState<any[]>([]);
  const [versions, setVersions] = useState<any[]>([]);
  const [costItems, setCostItems] = useState<any[]>([]);
  const [selectedVersionId, setSelectedVersionId] = useState<string>('');
  const [editingLimit, setEditingLimit] = useState<any>(null);
  const [newLimitAmount, setNewLimitAmount] = useState('');
  const [showCreateProject, setShowCreateProject] = useState(false);
  const [newProj, setNewProj] = useState({ code: '', name: '', contractValue: '10000' });
  const [msg, setMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null);

  const loadAll = async () => {
    const [projData, bv] = await Promise.all([
      apiFetch('/api/projects'),
      apiFetch('/api/budget/versions'),
    ]);
    setProjects(projData.projects || []);
    setCostItems(projData.costItems || []);
    setFreeLimits(projData.freeLimits || []);
    setVersions(bv || []);
    if (bv.length > 0 && (!selectedVersionId || !bv.some((v: any) => v.id === selectedVersionId))) {
      setSelectedVersionId(bv[0].id);
    }
  };

  useEffect(() => {
    loadAll().catch((e) => setMsg({ type: 'err', text: e.message }));
  }, []);

  const selectedVersion = versions.find((v) => v.id === selectedVersionId) || versions[0] || null;

  const createBudgetVersion = async (projectId: string) => {
    setMsg(null);
    try {
      const res = await apiFetch('/api/budget/versions', {
        method: 'POST',
        body: JSON.stringify({ projectId, comment: 'Новая корректировка лимитов' }),
      });
      await loadAll();
      setSelectedVersionId(res.id);
      setMsg({ type: 'ok', text: `Создана черновая версия бюджета v${res.versionNumber}` });
    } catch (e: any) {
      setMsg({ type: 'err', text: e.message });
    }
  };

  const approveVersion = async (v: any) => {
    setMsg(null);
    try {
      await apiFetch(`/api/budget/versions/${v.id}/approve`, {
        method: 'POST',
        body: JSON.stringify({ rowVersion: v.row_version }),
      });
      await loadAll();
      setMsg({ type: 'ok', text: `Версия бюджета v${v.version_number} утверждена` });
    } catch (e: any) {
      setMsg({ type: 'err', text: e.message });
    }
  };

  const saveLimitEdit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingLimit) return;
    setMsg(null);
    try {
      await apiFetch(`/api/budget/limits/${editingLimit.id}`, {
        method: 'PUT',
        body: JSON.stringify({ amount: Number(newLimitAmount), rowVersion: editingLimit.row_version }),
      });
      setEditingLimit(null);
      await loadAll();
      setMsg({ type: 'ok', text: 'Лимит статьи обновлён с оптимистической блокировкой' });
    } catch (e: any) {
      setMsg({ type: 'err', text: e.message });
    }
  };

  const createProject = async (e: React.FormEvent) => {
    e.preventDefault();
    setMsg(null);
    try {
      await apiFetch('/api/projects', {
        method: 'POST',
        body: JSON.stringify({
          code: newProj.code,
          name: newProj.name,
          contractValue: Number(newProj.contractValue),
          plannedStartDate: '2026-11-10',
          plannedEndDate: '2027-12-31',
        }),
      });
      setShowCreateProject(false);
      setNewProj({ code: '', name: '', contractValue: '10000' });
      await loadAll();
      setMsg({ type: 'ok', text: 'Проект, стартовый объект и черновой бюджет созданы' });
    } catch (e: any) {
      setMsg({ type: 'err', text: e.message });
    }
  };

  return (
    <div className="space-y-5">
      {msg && (
        <div
          className={`flex items-center gap-2 rounded-xl border p-3 text-xs ${
            msg.type === 'ok' ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-rose-200 bg-rose-50 text-rose-800'
          }`}
        >
          <AlertCircle className="h-4 w-4 shrink-0" />
          <span>{msg.text}</span>
        </div>
      )}

      {/* Hierarchy & Create Project */}
      <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <FolderTree className="h-4 w-4 text-sky-600" />
            <h2 className="text-sm font-semibold text-slate-900">Иерархия портфеля проектов, объектов и этапов (PV / EV / AC)</h2>
          </div>
          <button
            onClick={() => setShowCreateProject((s) => !s)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-sky-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-sky-700"
          >
            <Plus className="h-3.5 w-3.5" /> Новый проект
          </button>
        </div>

        {showCreateProject && (
          <form onSubmit={createProject} className="mt-4 grid gap-3 rounded-lg border border-slate-200 bg-slate-50 p-3 sm:grid-cols-4">
            <input
              required
              placeholder="Код (PRJ-2026-03)"
              value={newProj.code}
              onChange={(e) => setNewProj({ ...newProj, code: e.target.value })}
              className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs"
            />
            <input
              required
              placeholder="Название проекта"
              value={newProj.name}
              onChange={(e) => setNewProj({ ...newProj, name: e.target.value })}
              className="rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs sm:col-span-2"
            />
            <div className="flex gap-2">
              <input
                type="number"
                required
                placeholder="Выручка V"
                value={newProj.contractValue}
                onChange={(e) => setNewProj({ ...newProj, contractValue: e.target.value })}
                className="w-full rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs"
              />
              <button type="submit" className="rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white">
                Создать
              </button>
            </div>
          </form>
        )}

        <div className="mt-4 grid gap-4 md:grid-cols-2">
          {projects.map((p) => (
            <div key={p.id} className="rounded-xl border border-slate-200 p-3.5">
              <div className="flex items-start justify-between gap-2">
                <div>
                  <span className="font-mono text-[11px] font-semibold text-sky-700">{p.code}</span>
                  <h3 className="text-sm font-bold text-slate-900">{p.name}</h3>
                  <div className="text-[11px] text-slate-500">
                    Выручка V: {fmtMoney(p.contract_value)} · Мин. остаток: {fmtMoney(p.min_cash_balance)}
                  </div>
                </div>
                <button
                  onClick={() => createBudgetVersion(p.id)}
                  className="inline-flex items-center gap-1 rounded-md border border-slate-200 px-2 py-1 text-[11px] font-medium text-slate-700 hover:bg-slate-50"
                >
                  <CopyPlus className="h-3.5 w-3.5" /> Новая версия
                </button>
              </div>

              <div className="mt-3 space-y-2">
                {(p.objects || []).map((obj: any) => (
                  <div key={obj.id} className="rounded-lg bg-slate-50 p-2.5 text-xs">
                    <div className="font-semibold text-slate-800">
                      {obj.queue_name} → {obj.name}
                    </div>
                    <div className="mt-1.5 space-y-1">
                      {(obj.stages || []).map((st: any) => (
                        <div key={st.id} className="flex flex-wrap items-center justify-between gap-2 border-t border-slate-200/70 pt-1 text-[11px]">
                          <span className="font-medium text-slate-700">{st.name}</span>
                          <span className="font-mono text-slate-600 tabular-nums">
                            PV {fmtNum(st.planned_value_pv, 0)} | EV {fmtNum(st.earned_value_ev, 0)} | AC {fmtNum(st.actual_cost_ac, 0)}
                          </span>
                        </div>
                      ))}
                    </div>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* v_free_limit Live Control Table */}
      <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-sm font-semibold text-slate-900">
          Контроль свободного лимита в реальном времени (SQL-представление v_free_limit: Lдост = B − O)
        </h2>
        <p className="mt-0.5 text-xs text-slate-500">
          Неисполненные заявки Q отображаются справочно и повторно из Lдост не вычитаются (Бизнес-правило 7).
        </p>

        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[780px] text-left text-xs">
            <thead>
              <tr className="border-b border-slate-200 bg-slate-50 text-[11px] font-semibold uppercase text-slate-600">
                <th className="px-3 py-2">Проект / Объект</th>
                <th className="px-3 py-2">Статья затрат</th>
                <th className="px-3 py-2 text-right">Лимит B</th>
                <th className="px-3 py-2 text-right">Обязат. O</th>
                <th className="px-3 py-2 text-right">Факт F</th>
                <th className="px-3 py-2 text-right">Заявки Q</th>
                <th className="px-3 py-2 text-right">Свободно Lдост</th>
                <th className="px-3 py-2 text-right">Остаток Oост</th>
                <th className="px-3 py-2 text-right">Откл. Δ (%)</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 tabular-nums">
              {freeLimits.map((row, idx) => (
                <tr key={idx} className={row.free_limit < 0 ? 'bg-rose-50/40' : 'hover:bg-slate-50'}>
                  <td className="px-3 py-2">
                    <div className="font-semibold text-slate-900">{row.project_code}</div>
                    <div className="text-[11px] text-slate-500">{row.construction_object_name}</div>
                  </td>
                  <td className="px-3 py-2">
                    <span className="font-mono text-sky-700">{row.cost_item_code}</span> {row.cost_item_name}
                  </td>
                  <td className="px-3 py-2 text-right font-medium">{fmtNum(row.approved_budget)}</td>
                  <td className="px-3 py-2 text-right">{fmtNum(row.gross_obligations)}</td>
                  <td className="px-3 py-2 text-right">{fmtNum(row.paid_fact)}</td>
                  <td className="px-3 py-2 text-right text-slate-500">{fmtNum(row.submitted_requests)}</td>
                  <td className={`px-3 py-2 text-right font-bold ${row.free_limit < 0 ? 'text-rose-600' : 'text-emerald-700'}`}>
                    {fmtNum(row.free_limit)}
                  </td>
                  <td className="px-3 py-2 text-right">{fmtNum(row.obligation_remainder)}</td>
                  <td className={`px-3 py-2 text-right font-medium ${row.deviation > 0 ? 'text-rose-600' : 'text-emerald-700'}`}>
                    {row.deviation > 0 ? '+' : ''}
                    {fmtNum(row.deviation)} ({row.deviation_percent > 0 ? '+' : ''}
                    {row.deviation_percent}%)
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Budget Versions Card */}
      <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <h2 className="text-sm font-semibold text-slate-900">Версии бюджета проекта (неизменяемость утверждённых версий)</h2>
        <div className="mt-3 grid gap-4 lg:grid-cols-12">
          <div className="space-y-2 lg:col-span-4">
            {versions.map((v) => (
              <button
                key={v.id}
                onClick={() => setSelectedVersionId(v.id)}
                className={`w-full rounded-lg border p-3 text-left text-xs transition ${
                  selectedVersion?.id === v.id ? 'border-sky-500 bg-sky-50' : 'border-slate-200 hover:bg-slate-50'
                }`}
              >
                <div className="flex items-center justify-between">
                  <span className="font-bold text-slate-900">
                    {v.project_name} · v{v.version_number}
                  </span>
                  <span
                    className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${
                      v.status === 'Approved'
                        ? 'bg-emerald-100 text-emerald-800'
                        : v.status === 'Draft'
                        ? 'bg-amber-100 text-amber-800'
                        : 'bg-slate-100 text-slate-600'
                    }`}
                  >
                    {v.status === 'Approved' ? 'Утверждена' : v.status === 'Draft' ? 'Черновик' : 'Архив'}
                  </span>
                </div>
                <div className="mt-1 text-[11px] text-slate-500">{v.comment}</div>
              </button>
            ))}
          </div>

          {selectedVersion && (
            <div className="rounded-xl border border-slate-200 p-4 lg:col-span-8">
              <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 pb-3">
                <div>
                  <div className="text-sm font-bold text-slate-900">
                    Карточка бюджета {selectedVersion.project_code} · v{selectedVersion.version_number}
                  </div>
                  <div className="text-xs text-slate-500">{selectedVersion.comment}</div>
                </div>
                {selectedVersion.status === 'Draft' ? (
                  <button
                    onClick={() => approveVersion(selectedVersion)}
                    className="inline-flex items-center gap-1.5 rounded-lg bg-emerald-600 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-700"
                  >
                    <CheckCircle className="h-3.5 w-3.5" /> Утвердить бюджет (Руководитель)
                  </button>
                ) : (
                  <span className="inline-flex items-center gap-1 text-xs font-medium text-slate-500">
                    <Lock className="h-3.5 w-3.5" /> Редактирование закрыто
                  </span>
                )}
              </div>

              <div className="mt-3 overflow-x-auto">
                <table className="w-full text-left text-xs">
                  <thead>
                    <tr className="border-b border-slate-200 text-[11px] text-slate-500">
                      <th className="py-2">Объект</th>
                      <th className="py-2">Статья затрат</th>
                      <th className="py-2 text-right">Лимит (тыс. р.)</th>
                      <th className="py-2 text-right">Действие</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100 tabular-nums">
                    {(selectedVersion.limits || []).map((l: any) => (
                      <tr key={l.id}>
                        <td className="py-2">{l.object_name}</td>
                        <td className="py-2">
                          <span className="font-mono text-sky-700">{l.cost_item_code}</span> {l.cost_item_name}
                        </td>
                        <td className="py-2 text-right font-semibold">{fmtNum(l.amount)}</td>
                        <td className="py-2 text-right">
                          {selectedVersion.status === 'Draft' ? (
                            <button
                              onClick={() => {
                                setEditingLimit(l);
                                setNewLimitAmount(String(l.amount));
                              }}
                              className="font-medium text-sky-600 hover:underline"
                            >
                              Изменить
                            </button>
                          ) : (
                            <span className="text-slate-400">—</span>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              {editingLimit && (
                <form onSubmit={saveLimitEdit} className="mt-3 flex flex-wrap items-center gap-2 rounded-lg bg-amber-50 p-3 text-xs">
                  <span className="font-medium text-amber-900">
                    Новый лимит по статье «{editingLimit.cost_item_name}» (версия строки {editingLimit.row_version}):
                  </span>
                  <input
                    type="number"
                    step="0.01"
                    required
                    value={newLimitAmount}
                    onChange={(e) => setNewLimitAmount(e.target.value)}
                    className="w-32 rounded border border-amber-300 bg-white px-2 py-1"
                  />
                  <button type="submit" className="rounded bg-amber-600 px-3 py-1 font-semibold text-white">
                    Сохранить
                  </button>
                  <button type="button" onClick={() => setEditingLimit(null)} className="text-slate-600">
                    Отмена
                  </button>
                </form>
              )}
              <div className="mt-2 text-[11px] text-slate-400">
                Статьи справочника: {costItems.map((c) => c.code).join(', ')}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
};
