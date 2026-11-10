import React, { useEffect, useState } from 'react';
import { Lock, Plus, Shield, Unlock } from 'lucide-react';
import { apiFetch } from '../api/client';

export const AdminAuditPage: React.FC = () => {
  const [users, setUsers] = useState<any[]>([]);
  const [roles, setRoles] = useState<any[]>([]);
  const [rules, setRules] = useState<any[]>([]);
  const [rates, setRates] = useState<any[]>([]);
  const [logs, setLogs] = useState<any[]>([]);
  const [msg, setMsg] = useState<{ type: 'ok' | 'err'; text: string } | null>(null);

  const [newUser, setNewUser] = useState({
    login: '',
    fullName: '',
    email: '',
    roleId: '',
    password: 'StrongPassword2026!',
    twoFactorEnabled: false,
  });

  const [newRule, setNewRule] = useState({
    code: 'RISK_APPETITE',
    name: 'Порог аппетита к риску R (новая редакция)',
    valueText: '9 баллов',
    numericValue: '9',
    validFrom: '2026-12-01',
    documentSource: 'Регламент № 2026-12',
  });

  const loadAll = async () => {
    const [r, nr, cr] = await Promise.all([
      apiFetch('/api/admin/roles'),
      apiFetch('/api/admin/norm-rules'),
      apiFetch('/api/admin/currency-rates'),
    ]);
    setRoles(r);
    setRules(nr);
    setRates(cr);
    if (r.length > 0 && !newUser.roleId) {
      setNewUser((u) => ({ ...u, roleId: r[0].id }));
    }

    try {
      const [u, al] = await Promise.all([apiFetch('/api/admin/users'), apiFetch('/api/audit-logs')]);
      setUsers(u);
      setLogs(al);
    } catch {
      // Не-администраторы видят справочники правил и курсов
    }
  };

  useEffect(() => {
    loadAll().catch((e) => setMsg({ type: 'err', text: e.message }));
  }, []);

  const handleCreateUser = async (e: React.FormEvent) => {
    e.preventDefault();
    setMsg(null);
    try {
      await apiFetch('/api/admin/users', { method: 'POST', body: JSON.stringify(newUser) });
      setNewUser((u) => ({ ...u, login: '', fullName: '', email: '' }));
      await loadAll();
      setMsg({ type: 'ok', text: 'Пользователь создан с проверкой политики паролей (≥ 12 символов)' });
    } catch (err: any) {
      setMsg({ type: 'err', text: err.message });
    }
  };

  const toggleBlockUser = async (u: any) => {
    try {
      await apiFetch(`/api/admin/users/${u.id}/toggle-active`, { method: 'PUT' });
      await loadAll();
    } catch (err: any) {
      setMsg({ type: 'err', text: err.message });
    }
  };

  const handleCreateRule = async (e: React.FormEvent) => {
    e.preventDefault();
    setMsg(null);
    try {
      await apiFetch('/api/admin/norm-rules', {
        method: 'POST',
        body: JSON.stringify({
          ...newRule,
          numericValue: newRule.numericValue ? Number(newRule.numericValue) : null,
        }),
      });
      await loadAll();
      setMsg({ type: 'ok', text: 'Добавлена новая версия нормативного правила без изменения кода системы (НФТ-12)' });
    } catch (err: any) {
      setMsg({ type: 'err', text: err.message });
    }
  };

  return (
    <div className="space-y-5">
      {msg && (
        <div
          className={`rounded-xl border p-3 text-xs font-medium ${
            msg.type === 'ok' ? 'border-emerald-200 bg-emerald-50 text-emerald-800' : 'border-rose-200 bg-rose-50 text-rose-800'
          }`}
        >
          {msg.text}
        </div>
      )}

      {users.length > 0 && (
        <div className="grid gap-5 lg:grid-cols-12">
          <div className="overflow-x-auto rounded-xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-8">
            <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-slate-900">
              <Shield className="h-4 w-4 text-sky-600" />
              Пользователи и матрица RBAC (8 ролей)
            </div>
            <table className="w-full min-w-[600px] text-left text-xs">
              <thead>
                <tr className="border-b border-slate-200 text-[11px] text-slate-500">
                  <th className="py-2">Логин</th>
                  <th className="py-2">ФИО</th>
                  <th className="py-2">Роль</th>
                  <th className="py-2">Клиент</th>
                  <th className="py-2">2FA</th>
                  <th className="py-2 text-right">Статус</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {users.map((u) => (
                  <tr key={u.id}>
                    <td className="py-2 font-mono font-semibold">{u.login}</td>
                    <td className="py-2">{u.full_name}</td>
                    <td className="py-2">{u.role_name}</td>
                    <td className="py-2 text-slate-500">{u.client_type}</td>
                    <td className="py-2">{u.two_factor_enabled ? 'Да' : 'Нет'}</td>
                    <td className="py-2 text-right">
                      <button
                        onClick={() => toggleBlockUser(u)}
                        className={`inline-flex items-center gap-1 rounded px-2 py-0.5 text-[11px] font-medium ${
                          u.is_active === 1 ? 'bg-emerald-50 text-emerald-800' : 'bg-rose-100 text-rose-800'
                        }`}
                      >
                        {u.is_active === 1 ? <Unlock className="h-3 w-3" /> : <Lock className="h-3 w-3" />}
                        {u.is_active === 1 ? 'Активен' : 'Заблокирован'}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <form onSubmit={handleCreateUser} className="space-y-2.5 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm lg:col-span-4">
            <div className="flex items-center gap-1.5 text-sm font-semibold text-slate-900">
              <Plus className="h-4 w-4 text-sky-600" /> Новый пользователь
            </div>
            <input
              required
              placeholder="Логин"
              value={newUser.login}
              onChange={(e) => setNewUser({ ...newUser, login: e.target.value })}
              className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
            />
            <input
              required
              placeholder="ФИО"
              value={newUser.fullName}
              onChange={(e) => setNewUser({ ...newUser, fullName: e.target.value })}
              className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
            />
            <input
              required
              type="email"
              placeholder="Email"
              value={newUser.email}
              onChange={(e) => setNewUser({ ...newUser, email: e.target.value })}
              className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
            />
            <select
              value={newUser.roleId}
              onChange={(e) => setNewUser({ ...newUser, roleId: e.target.value })}
              className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
            >
              {roles.map((r) => (
                <option key={r.id} value={r.id}>{r.name} ({r.client_type})</option>
              ))}
            </select>
            <input
              required
              placeholder="Пароль (≥ 12 символов)"
              value={newUser.password}
              onChange={(e) => setNewUser({ ...newUser, password: e.target.value })}
              className="w-full rounded-lg border border-slate-300 px-2.5 py-1.5"
            />
            <button type="submit" className="w-full rounded-lg bg-sky-600 py-2 font-semibold text-white hover:bg-sky-700">
              Создать пользователя
            </button>
          </form>
        </div>
      )}

      <div className="grid gap-5 lg:grid-cols-12">
        <div className="space-y-3 rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm lg:col-span-8">
          <h3 className="text-sm font-semibold text-slate-900">
            Параметризованные нормативные правила (НФТ-12: сроки действия без перекомпиляции)
          </h3>
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead>
                <tr className="border-b border-slate-200 text-[11px] text-slate-500">
                  <th className="py-1.5">Код</th>
                  <th className="py-1.5">Правило (ред.)</th>
                  <th className="py-1.5 text-right">Значение</th>
                  <th className="py-1.5">Действует с</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {rules.map((r) => (
                  <tr key={r.id}>
                    <td className="py-1.5 font-mono font-semibold text-sky-700">{r.code}</td>
                    <td className="py-1.5">{r.name} (v{r.revision})</td>
                    <td className="py-1.5 text-right font-bold">{r.value_text}</td>
                    <td className="py-1.5 font-mono">{r.valid_from}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <form onSubmit={handleCreateRule} className="grid gap-2 border-t border-slate-100 pt-3 sm:grid-cols-4">
            <input
              value={newRule.code}
              onChange={(e) => setNewRule({ ...newRule, code: e.target.value })}
              className="rounded border border-slate-300 px-2 py-1 font-mono"
            />
            <input
              value={newRule.name}
              onChange={(e) => setNewRule({ ...newRule, name: e.target.value })}
              className="rounded border border-slate-300 px-2 py-1 sm:col-span-2"
            />
            <div className="flex gap-1.5">
              <input
                value={newRule.valueText}
                onChange={(e) => setNewRule({ ...newRule, valueText: e.target.value })}
                className="w-24 rounded border border-slate-300 px-2 py-1"
              />
              <button type="submit" className="flex-1 rounded bg-emerald-600 px-2 py-1 font-semibold text-white">
                + Версия
              </button>
            </div>
          </form>
        </div>

        <div className="rounded-xl border border-slate-200 bg-white p-4 text-xs shadow-sm lg:col-span-4">
          <h3 className="mb-2 text-sm font-semibold text-slate-900">Курсы валют НБ РБ</h3>
          <div className="space-y-2">
            {rates.map((r) => (
              <div key={r.id} className="flex items-center justify-between rounded-lg bg-slate-50 px-3 py-2">
                <span className="font-mono font-bold">{r.currency} / BYN</span>
                <span className="font-semibold tabular-nums">{r.rate_to_byn} ({r.rate_date})</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {logs.length > 0 && (
        <div className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
          <h3 className="text-sm font-semibold text-slate-900">Неизменяемый журнал аудита (AuditLogs — только вставка)</h3>
          <div className="mt-3 max-h-80 overflow-auto">
            <table className="w-full min-w-[700px] text-left text-xs">
              <thead className="sticky top-0 bg-slate-50">
                <tr className="border-b border-slate-200 text-[11px] text-slate-500">
                  <th className="px-2 py-2">Время (UTC)</th>
                  <th className="px-2 py-2">Пользователь</th>
                  <th className="px-2 py-2">Действие</th>
                  <th className="px-2 py-2">Объект</th>
                  <th className="px-2 py-2">Детали</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {logs.map((l) => (
                  <tr key={l.id}>
                    <td className="px-2 py-1.5 font-mono text-[11px] text-slate-500">
                      {new Date(l.timestamp).toLocaleString('ru-RU')}
                    </td>
                    <td className="px-2 py-1.5 font-medium">{l.user_login}</td>
                    <td className="px-2 py-1.5 font-mono text-sky-700">{l.action}</td>
                    <td className="px-2 py-1.5">{l.object_name}</td>
                    <td className="px-2 py-1.5 text-slate-600">{l.new_value || l.details}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
};
