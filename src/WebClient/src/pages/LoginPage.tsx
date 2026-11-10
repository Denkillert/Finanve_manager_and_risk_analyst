import React, { useState } from 'react';
import { Lock, ShieldCheck, UserCheck, AlertCircle } from 'lucide-react';
import { ApiError, apiFetch, saveSession, type SessionUser } from '../api/client';

interface LoginPageProps {
  onLoginSuccess: (user: SessionUser) => void;
}

const DEMO_ROLES = [
  { login: 'finmanager', role: 'Финансовый менеджер', client: 'Веб-клиент', desc: 'Лимиты, согласование с ЭЦП, отчётность' },
  { login: 'director', role: 'Руководитель организации', client: 'WPF / Веб', desc: 'Утверждение бюджета и перерасхода' },
  { login: 'pm', role: 'Руководитель проекта', client: 'Веб-клиент', desc: 'Заявки, акты С-2/С-3, прогноз EAC' },
  { login: 'risk', role: 'Специалист по рискам', client: 'Веб-клиент', desc: 'Матрица 5×5, EMV, резерв P80' },
  { login: 'accountant', role: 'Бухгалтер', client: 'WPF / Веб', desc: 'Исполнение платежей, выписки MT940, 1С' },
  { login: 'supply', role: 'Специалист по снабжению', client: 'WPF / Веб', desc: 'Договоры, авансы, заявки поставщикам' },
  { login: 'siteworker', role: 'Работник на объекте', client: 'Веб (360px)', desc: 'Подготовка С-2 и фотофиксация работ' },
  { login: 'admin', role: 'Администратор системы', client: 'WPF / 2FA', desc: 'Пользователи, RBAC, правила (код 246810)' },
];

export const LoginPage: React.FC<LoginPageProps> = ({ onLoginSuccess }) => {
  const [login, setLogin] = useState('finmanager');
  const [password, setPassword] = useState('StroyFin2026!');
  const [twoFactorCode, setTwoFactorCode] = useState('');
  const [need2fa, setNeed2fa] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const performLogin = async (l: string, p: string, code?: string) => {
    setLoading(true);
    setError(null);
    try {
      const data = await apiFetch<{ token: string; user: SessionUser }>('/api/auth/login', {
        method: 'POST',
        body: JSON.stringify({ login: l, password: p, twoFactorCode: code || undefined }),
      });
      saveSession(data.token, data.user);
      onLoginSuccess(data.user);
    } catch (err) {
      if (err instanceof ApiError) {
        if (err.requiresTwoFactor) setNeed2fa(true);
        setError(err.message);
      } else {
        setError('Не удалось подключиться к серверу API');
      }
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    performLogin(login, password, need2fa ? twoFactorCode : undefined);
  };

  const quickRoleLogin = (roleLogin: string) => {
    setLogin(roleLogin);
    setPassword('StroyFin2026!');
    if (roleLogin === 'admin') {
      setNeed2fa(true);
      setTwoFactorCode('246810');
      performLogin('admin', 'StroyFin2026!', '246810');
    } else {
      setNeed2fa(false);
      setTwoFactorCode('');
      performLogin(roleLogin, 'StroyFin2026!');
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-navy-900 via-navy-800 to-slate-900 px-4 py-8 flex items-center justify-center">
      <div className="grid w-full max-w-5xl gap-6 lg:grid-cols-12">
        {/* Left Auth Card */}
        <div className="rounded-2xl bg-white p-6 shadow-2xl sm:p-8 lg:col-span-5">
          <div className="flex items-center gap-3">
            <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-sky-600 text-lg font-bold text-white shadow">
              СФ
            </div>
            <div>
              <h1 className="text-lg font-bold text-slate-900">СтройФинРиск</h1>
              <p className="text-xs text-slate-500">Управление финансами и рисками строительной компании</p>
            </div>
          </div>

          <form onSubmit={handleSubmit} className="mt-6 space-y-4">
            <div>
              <label className="block text-xs font-semibold uppercase tracking-wide text-slate-600">
                Логин пользователя
              </label>
              <input
                type="text"
                value={login}
                onChange={(e) => setLogin(e.target.value)}
                required
                className="mt-1.5 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-sky-500 focus:outline-none focus:ring-2 focus:ring-sky-500/20"
                placeholder="finmanager"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold uppercase tracking-wide text-slate-600">
                Пароль (политика ≥ 12 символов)
              </label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                className="mt-1.5 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-sky-500 focus:outline-none focus:ring-2 focus:ring-sky-500/20"
              />
            </div>

            {(need2fa || login === 'admin') && (
              <div className="rounded-lg border border-amber-200 bg-amber-50 p-3">
                <label className="flex items-center gap-1.5 text-xs font-semibold text-amber-900">
                  <ShieldCheck className="h-4 w-4" />
                  Код 2FA администратора (демо: 246810)
                </label>
                <input
                  type="text"
                  value={twoFactorCode}
                  onChange={(e) => setTwoFactorCode(e.target.value)}
                  placeholder="246810"
                  className="mt-1.5 w-full rounded-md border border-amber-300 bg-white px-3 py-1.5 text-sm font-mono focus:border-amber-500 focus:outline-none"
                />
              </div>
            )}

            {error && (
              <div className="flex items-start gap-2 rounded-lg border border-rose-200 bg-rose-50 p-3 text-xs text-rose-800">
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              className="flex w-full items-center justify-center gap-2 rounded-lg bg-sky-600 px-4 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-sky-700 disabled:opacity-50"
            >
              <Lock className="h-4 w-4" />
              {loading ? 'Выполняется вход...' : 'Войти в систему'}
            </button>
          </form>

          <div className="mt-5 border-t border-slate-100 pt-4 text-[11px] text-slate-500 space-y-1">
            <div>• Защита: PBKDF2-SHA256, блокировка после 5 неудачных попыток</div>
            <div>• RBAC: 8 ролей с разделением полномочий (НФТ-8)</div>
            <div>• Адаптация: экран от 360 px до 1920 px</div>
          </div>
        </div>

        {/* Right Quick Role Matrix */}
        <div className="rounded-2xl border border-white/10 bg-white/5 p-6 text-white backdrop-blur lg:col-span-7">
          <div className="flex items-center gap-2 text-sm font-semibold text-sky-300">
            <UserCheck className="h-4 w-4" />
            Быстрый вход по ролям для проверки матрицы RBAC (пароль: StroyFin2026!)
          </div>
          <p className="mt-1 text-xs text-slate-300">
            Нажмите на любую роль, чтобы мгновенно протестировать соответствующие экраны и ограничения прав доступа.
          </p>

          <div className="mt-4 grid gap-2.5 sm:grid-cols-2">
            {DEMO_ROLES.map((item) => (
              <button
                key={item.login}
                onClick={() => quickRoleLogin(item.login)}
                disabled={loading}
                className="group rounded-xl border border-white/15 bg-white/5 p-3 text-left transition hover:border-sky-400 hover:bg-sky-500/15"
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-xs font-bold text-white group-hover:text-sky-200">{item.role}</span>
                  <span className="rounded bg-white/10 px-1.5 py-0.5 text-[10px] text-sky-200">{item.client}</span>
                </div>
                <div className="mt-1 text-[11px] text-slate-300">{item.desc}</div>
                <div className="mt-1.5 font-mono text-[10px] text-slate-400">логин: {item.login}</div>
              </button>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
};
