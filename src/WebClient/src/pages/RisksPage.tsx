import React, { useEffect, useState } from 'react';
import { RefreshCw, ShieldAlert, Sliders } from 'lucide-react';
import { apiFetch } from '../api/client';
import { fmtMoney, fmtNum, RISK_STATUS_MAP, riskZoneColor } from '../utils/format';

export const RisksPage: React.FC = () => {
  const [data, setData] = useState<any>(null);
  const [selectedId, setSelectedId] = useState<string>('');
  const [assessForm, setAssessForm] = useState({
    probability: '0.25',
    monetaryImpact: '400',
    residualProbability: '0.12',
    comment: 'Плановый пересмотр оценки риска',
  });
  const [whatIfMatDelta, setWhatIfMatDelta] = useState(10);
  const [whatIfDelayMonths, setWhatIfDelayMonths] = useState(2);
  const [msg, setMsg] = useState<string | null>(null);

  const loadRisks = async () => {
    const res = await apiFetch('/api/risks');
    setData(res);
    if (res.risks?.length > 0 && !selectedId) {
      selectRisk(res.risks[0]);
    }
  };

  useEffect(() => {
    loadRisks().catch((e) => setMsg(e.message));
  }, []);

  const selectRisk = (r: any) => {
    setSelectedId(r.id);
    setAssessForm({
      probability: String(r.probability),
      monetaryImpact: String(r.monetary_impact),
      residualProbability: String(r.residual_probability ?? r.probability),
      comment: 'Переоценка вероятности и ущерба',
    });
  };

  const selectedRisk = (data?.risks || []).find((r: any) => r.id === selectedId) || null;

  const submitAssessment = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedRisk) return;
    setMsg(null);
    try {
      await apiFetch(`/api/risks/${selectedRisk.id}/assessment`, {
        method: 'PUT',
        body: JSON.stringify({
          probability: Number(assessForm.probability),
          monetaryImpact: Number(assessForm.monetaryImpact),
          residualProbability: Number(assessForm.residualProbability),
          comment: assessForm.comment,
          rowVersion: selectedRisk.row_version,
        }),
      });
      await loadRisks();
      setMsg(`Риск ${selectedRisk.code} переоценён; портфельный резерв P80 пересчитан автоматически.`);
    } catch (err: any) {
      setMsg(err.message);
    }
  };

  if (!data) return <div className="p-8 text-center text-sm text-slate-500">Загрузка реестра рисков...</div>;

  const { risks, summary, sensitivity } = data;
  const maxSens = Math.max(...(sensitivity || []).map((s: any) => s.impactOnEac), 1);
  const simulatedEac = 12100 + whatIfMatDelta * 45 + whatIfDelayMonths * 130;

  return (
    <div className="space-y-5">
      {msg && <div className="rounded-xl border border-emerald-200 bg-emerald-50 p-3 text-xs font-medium text-emerald-900">{msg}</div>}

      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
        <div className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm">
          <div className="text-[11px] text-slate-500">ΣEMV до мер</div>
          <div className="mt-1 text-lg font-bold text-slate-900 tabular-nums">{fmtMoney(summary.totalEmv)}</div>
        </div>
        <div className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm">
          <div className="text-[11px] text-slate-500">Стандартное откл. σ</div>
          <div className="mt-1 text-lg font-bold text-slate-800 tabular-nums">{fmtNum(summary.sigma, 2)} тыс. р.</div>
        </div>
        <div className="rounded-xl border border-amber-200 bg-amber-50 p-3.5 shadow-sm">
          <div className="text-[11px] font-medium text-amber-800">Резерв P80 (Z = 0.84)</div>
          <div className="mt-1 text-lg font-bold text-amber-900 tabular-nums">{fmtMoney(summary.reserveP80)}</div>
        </div>
        <div className="rounded-xl border border-slate-200 bg-white p-3.5 shadow-sm">
          <div className="text-[11px] text-slate-500">Остаточный ΣEMV</div>
          <div className="mt-1 text-lg font-bold text-emerald-700 tabular-nums">{fmtMoney(summary.totalResidualEmv)}</div>
        </div>
        <div className="rounded-xl border border-emerald-200 bg-emerald-50 p-3.5 shadow-sm">
          <div className="text-[11px] font-medium text-emerald-800">Снижение потерь (ΔEMV)</div>
          <div className="mt-1 text-lg font-bold text-emerald-900 tabular-nums">-{fmtMoney(summary.emvReduction)}</div>
        </div>
      </div>

      <div className="grid gap-5 lg:grid-cols-12">
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-6">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold text-slate-900">Матрица рисков 5×5 (Вероятность p × Ущерб I)</h2>
            <span className="text-[11px] text-slate-500">Аппетит: R ≤ 9</span>
          </div>

          <div className="mt-3 grid grid-cols-6 gap-1 text-[10px]">
            <div />
            {[1, 2, 3, 4, 5].map((iScore) => (
              <div key={iScore} className="py-1 text-center font-semibold text-slate-500">
                I={iScore}
              </div>
            ))}
            {[5, 4, 3, 2, 1].map((pScore) => (
              <React.Fragment key={pScore}>
                <div className="flex items-center justify-center font-semibold text-slate-500">p={pScore}</div>
                {[1, 2, 3, 4, 5].map((iScore) => {
                  const score = pScore * iScore;
                  const cellRisks = risks.filter((r: any) => r.probability_score === pScore && r.impact_score === iScore);
                  const zone = riskZoneColor(score);
                  return (
                    <div
                      key={`${pScore}-${iScore}`}
                      className={`flex min-h-[46px] flex-wrap content-start items-start gap-0.5 rounded border border-white/60 p-1 ${zone.cls}`}
                    >
                      <span className="w-full text-right text-[9px] opacity-60">{score}</span>
                      {cellRisks.map((cr: any) => (
                        <button
                          key={cr.id}
                          onClick={() => selectRisk(cr)}
                          className="rounded bg-navy-900 px-1 py-0.5 text-[9px] font-bold text-white hover:bg-sky-600"
                          title={`${cr.code} ${cr.event} (EMV=${cr.emv})`}
                        >
                          {cr.code}
                        </button>
                      ))}
                    </div>
                  );
                })}
              </React.Fragment>
            ))}
          </div>
        </div>

        <div className="space-y-4 rounded-xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-6">
          <div>
            <h2 className="text-sm font-semibold text-slate-900">Диаграмма «Торнадо»: анализ чувствительности EAC (табл. 1.18)</h2>
            <p className="text-xs text-slate-500">Влияние отклонений ключевых факторов на итоговую стоимость проекта</p>
          </div>

          <div className="space-y-3 text-xs">
            {(sensitivity || []).map((s: any) => (
              <div key={s.factorName}>
                <div className="flex justify-between font-medium text-slate-800">
                  <span>{s.factorName} ({s.rangeLabel})</span>
                  <span className="font-mono font-bold text-rose-700">+{fmtNum(s.impactOnEac, 0)} тыс. р.</span>
                </div>
                <div className="mt-1 h-4 w-full overflow-hidden rounded-full bg-slate-100">
                  <div
                    className="h-full rounded-full bg-gradient-to-r from-amber-500 to-rose-600"
                    style={{ width: `${Math.round((s.impactOnEac / maxSens) * 100)}%` }}
                  />
                </div>
              </div>
            ))}
          </div>

          <div className="space-y-2 rounded-lg border border-sky-200 bg-sky-50 p-3 text-xs">
            <div className="flex items-center justify-between font-semibold text-sky-950">
              <span className="inline-flex items-center gap-1.5">
                <Sliders className="h-3.5 w-3.5" /> Стресс-тест прогноза EAC
              </span>
              <span className="font-mono text-sm font-bold text-sky-800">EAC* = {fmtMoney(simulatedEac)}</span>
            </div>
            <div className="grid gap-2 sm:grid-cols-2">
              <label className="block">
                <span className="text-[11px] text-slate-600">Рост цен материалов: +{whatIfMatDelta}%</span>
                <input
                  type="range"
                  min={0}
                  max={25}
                  value={whatIfMatDelta}
                  onChange={(e) => setWhatIfMatDelta(Number(e.target.value))}
                  className="w-full"
                />
              </label>
              <label className="block">
                <span className="text-[11px] text-slate-600">Задержка графика: +{whatIfDelayMonths} мес.</span>
                <input
                  type="range"
                  min={0}
                  max={6}
                  value={whatIfDelayMonths}
                  onChange={(e) => setWhatIfDelayMonths(Number(e.target.value))}
                  className="w-full"
                />
              </label>
            </div>
          </div>
        </div>
      </div>

      <div className="grid gap-5 lg:grid-cols-12">
        <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-8">
          <h3 className="mb-3 text-sm font-semibold text-slate-900">
            Приоритетный реестр рисков (сортировка по EMV ↓, R ↓)
          </h3>
          <table className="w-full min-w-[660px] text-left text-xs tabular-nums">
            <thead>
              <tr className="border-b border-slate-200 text-[11px] text-slate-500">
                <th className="py-2">Код</th>
                <th className="py-2">Риск и владелец</th>
                <th className="py-2 text-right">p</th>
                <th className="py-2 text-right">C</th>
                <th className="py-2 text-center">R = p×I</th>
                <th className="py-2 text-right">EMV</th>
                <th className="py-2 text-right">Ост. EMV</th>
                <th className="py-2">Статус</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {risks.map((r: any) => {
                const zone = riskZoneColor(r.integral_score);
                const st = RISK_STATUS_MAP[r.status] || { label: r.status, badge: 'bg-slate-100 text-slate-700' };
                return (
                  <tr
                    key={r.id}
                    onClick={() => selectRisk(r)}
                    className={`cursor-pointer ${selectedRisk?.id === r.id ? 'bg-sky-50' : 'hover:bg-slate-50'}`}
                  >
                    <td className="py-2 font-mono font-bold text-navy-900">{r.code}</td>
                    <td className="py-2">
                      <div className="font-medium text-slate-900">{r.event}</div>
                      <div className="text-[10px] text-slate-500">отв.: {r.owner_role}</div>
                    </td>
                    <td className="py-2 text-right">{r.probability}</td>
                    <td className="py-2 text-right">{fmtNum(r.monetary_impact, 0)}</td>
                    <td className="py-2 text-center">
                      <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-bold ${zone.cls}`}>
                        {r.integral_score}
                      </span>
                    </td>
                    <td className="py-2 text-right font-bold">{fmtNum(r.emv)}</td>
                    <td className="py-2 text-right text-emerald-700">{fmtNum(r.residual_emv)}</td>
                    <td className="py-2">
                      <span className={`rounded-full px-2 py-0.5 text-[10px] font-semibold ${st.badge}`}>{st.label}</span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <div className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm lg:col-span-4">
          {selectedRisk ? (
            <>
              <div className="flex items-start justify-between gap-2 border-b border-slate-100 pb-2">
                <div>
                  <span className="font-mono font-bold text-sky-700">{selectedRisk.code}</span>
                  <h3 className="text-sm font-bold text-slate-900">{selectedRisk.event}</h3>
                </div>
                <ShieldAlert className="h-4 w-4 shrink-0 text-amber-600" />
              </div>

              <div className="space-y-1 rounded-lg bg-slate-50 p-2.5 text-[11px] text-slate-700">
                <div><b>Причина:</b> {selectedRisk.cause}</div>
                <div><b>Индикатор:</b> {selectedRisk.early_indicator}</div>
                <div><b>Меры:</b> {selectedRisk.mitigation}</div>
              </div>

              <form onSubmit={submitAssessment} className="space-y-2.5">
                <div className="flex items-center gap-1 font-semibold text-slate-800">
                  <RefreshCw className="h-3.5 w-3.5 text-sky-600" /> Новая версия оценки риска (v{selectedRisk.current_assessment_version})
                </div>
                <div className="grid grid-cols-3 gap-2">
                  <div>
                    <label className="text-[10px] text-slate-500">p (0..1)</label>
                    <input
                      type="number"
                      step="0.01"
                      min="0.01"
                      max="0.99"
                      value={assessForm.probability}
                      onChange={(e) => setAssessForm({ ...assessForm, probability: e.target.value })}
                      className="w-full rounded border border-slate-300 px-2 py-1"
                    />
                  </div>
                  <div>
                    <label className="text-[10px] text-slate-500">Ущерб C</label>
                    <input
                      type="number"
                      step="1"
                      value={assessForm.monetaryImpact}
                      onChange={(e) => setAssessForm({ ...assessForm, monetaryImpact: e.target.value })}
                      className="w-full rounded border border-slate-300 px-2 py-1"
                    />
                  </div>
                  <div>
                    <label className="text-[10px] text-slate-500">Ост. p′</label>
                    <input
                      type="number"
                      step="0.01"
                      min="0.01"
                      max="0.99"
                      value={assessForm.residualProbability}
                      onChange={(e) => setAssessForm({ ...assessForm, residualProbability: e.target.value })}
                      className="w-full rounded border border-slate-300 px-2 py-1"
                    />
                  </div>
                </div>
                <input
                  value={assessForm.comment}
                  onChange={(e) => setAssessForm({ ...assessForm, comment: e.target.value })}
                  placeholder="Обоснование переоценки"
                  className="w-full rounded border border-slate-300 px-2.5 py-1.5"
                />
                <button type="submit" className="w-full rounded-lg bg-sky-600 py-2 font-semibold text-white hover:bg-sky-700">
                  Сохранить оценку и пересчитать P80
                </button>
              </form>

              {(selectedRisk.assessments || []).length > 0 && (
                <div className="border-t border-slate-100 pt-2">
                  <div className="mb-1 font-semibold text-slate-600">История оценок ({selectedRisk.assessments.length}):</div>
                  <div className="max-h-32 space-y-1 overflow-y-auto text-[11px]">
                    {selectedRisk.assessments.map((a: any) => (
                      <div key={a.id} className="rounded bg-slate-50 px-2 py-1">
                        v{a.version}: p={a.probability}, C={a.monetary_impact}, EMV={a.emv} ({a.assessor_name})
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </>
          ) : null}
        </div>
      </div>
    </div>
  );
};
