import React, { useEffect, useState } from 'react';
import { FileDown, FileSpreadsheet, FileText } from 'lucide-react';
import { apiFetch, downloadFile } from '../api/client';

const REPORT_TYPES = [
  { id: 'budget-fact', label: 'Бюджет – Факт' },
  { id: 'cash-calendar', label: 'Платёжный календарь' },
  { id: 'advances', label: 'Контроль авансов' },
  { id: 'risks', label: 'Реестр рисков и P80' },
  { id: 'sensitivity', label: 'Чувствительность EAC' },
];

export const ReportsPage: React.FC = () => {
  const [activeType, setActiveType] = useState('budget-fact');
  const [report, setReport] = useState<any>(null);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    setLoading(true);
    apiFetch(`/api/reports/${activeType}`)
      .then(setReport)
      .finally(() => setLoading(false));
  }, [activeType]);

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap gap-1.5">
          {REPORT_TYPES.map((rt) => (
            <button
              key={rt.id}
              onClick={() => setActiveType(rt.id)}
              className={`rounded-lg border px-3 py-1.5 text-xs font-semibold ${
                activeType === rt.id
                  ? 'border-sky-600 bg-sky-600 text-white'
                  : 'border-slate-200 bg-white text-slate-700 hover:bg-slate-50'
              }`}
            >
              {rt.label}
            </button>
          ))}
        </div>

        <div className="flex gap-2">
          <button
            onClick={() => downloadFile(`/api/reports/${activeType}/export?format=xlsx`, `report-${activeType}.xlsx`)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-emerald-700 px-3 py-1.5 text-xs font-semibold text-white hover:bg-emerald-800"
          >
            <FileSpreadsheet className="h-3.5 w-3.5" /> Экспорт XLSX
          </button>
          <button
            onClick={() => downloadFile(`/api/reports/${activeType}/export?format=pdf`, `report-${activeType}.pdf`)}
            className="inline-flex items-center gap-1.5 rounded-lg bg-rose-700 px-3 py-1.5 text-xs font-semibold text-white hover:bg-rose-800"
          >
            <FileText className="h-3.5 w-3.5" /> Экспорт PDF
          </button>
        </div>
      </div>

      {loading || !report ? (
        <div className="rounded-xl border border-slate-200 bg-white p-8 text-center text-xs text-slate-500">
          Формирование отчёта...
        </div>
      ) : (
        <div className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-100 pb-3">
            <div>
              <h2 className="text-sm font-bold text-slate-900">{report.title}</h2>
              <p className="text-xs text-slate-500">{report.subtitle}</p>
            </div>
            <span className="inline-flex items-center gap-1 text-[11px] text-slate-400">
              <FileDown className="h-3.5 w-3.5" /> Готово к выгрузке в Excel / PDF
            </span>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs tabular-nums">
              <thead>
                <tr className="border-b border-slate-200 bg-slate-50 text-[11px] font-semibold text-slate-600">
                  {(report.columns || []).map((h: string, i: number) => (
                    <th key={i} className="px-3 py-2">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {(report.rows || []).map((row: any[], rIdx: number) => (
                  <tr key={rIdx} className="hover:bg-slate-50">
                    {row.map((cell, cIdx) => (
                      <td key={cIdx} className="px-3 py-2">{cell}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {(report.summaryLines || []).length > 0 && (
            <div className="space-y-1 rounded-lg bg-slate-50 p-3 text-xs">
              {report.summaryLines.map((line: string, i: number) => (
                <div key={i} className="font-medium text-slate-800">• {line}</div>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
};
