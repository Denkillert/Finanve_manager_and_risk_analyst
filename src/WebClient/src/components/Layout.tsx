import React, { useState } from 'react';
import {
  Activity,
  BarChart3,
  Bell,
  Building2,
  Calendar,
  FileCheck2,
  FileSpreadsheet,
  FolderKanban,
  LogOut,
  Menu,
  ShieldAlert,
  ShieldCheck,
  X,
} from 'lucide-react';
import type { SessionUser } from '../api/client';

export type NavTab =
  | 'dashboard'
  | 'projects'
  | 'requests'
  | 'calendar'
  | 'contracts'
  | 'risks'
  | 'reports'
  | 'admin';

interface LayoutProps {
  user: SessionUser;
  activeTab: NavTab;
  onTabChange: (tab: NavTab) => void;
  onLogout: () => void;
  notifications: any[];
  onMarkAllRead: () => void;
  children: React.ReactNode;
}

const NAV_ITEMS: Array<{ id: NavTab; label: string; shortLabel: string; icon: React.ComponentType<{ className?: string }> }> = [
  { id: 'dashboard', label: 'Главная панель', shortLabel: 'Панель', icon: Activity },
  { id: 'projects', label: 'Проекты и бюджеты', shortLabel: 'Бюджеты', icon: FolderKanban },
  { id: 'requests', label: 'Платёжные заявки и С-2', shortLabel: 'Заявки', icon: FileCheck2 },
  { id: 'calendar', label: 'Платёжный календарь', shortLabel: 'Календарь', icon: Calendar },
  { id: 'contracts', label: 'Договоры и авансы', shortLabel: 'Договоры', icon: Building2 },
  { id: 'risks', label: 'Риски и резерв P80', shortLabel: 'Риски', icon: ShieldAlert },
  { id: 'reports', label: 'Отчёты (XLSX / PDF)', shortLabel: 'Отчёты', icon: FileSpreadsheet },
  { id: 'admin', label: 'Администрирование и аудит', shortLabel: 'Аудит', icon: ShieldCheck },
];

export const Layout: React.FC<LayoutProps> = ({
  user,
  activeTab,
  onTabChange,
  onLogout,
  notifications,
  onMarkAllRead,
  children,
}) => {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);

  const unreadCount = notifications.filter((n) => !n.is_read).length;

  const visibleNav = NAV_ITEMS.filter((item) => {
    if (user.roleCode === 'SiteWorker') {
      return ['dashboard', 'requests', 'projects'].includes(item.id);
    }
    if (user.roleCode === 'Admin') {
      return ['dashboard', 'admin'].includes(item.id);
    }
    return true;
  });

  const SidebarContent = () => (
    <div className="flex h-full flex-col bg-navy-900 text-slate-100">
      <div className="flex items-center justify-between border-b border-white/10 px-4 py-4">
        <div className="flex items-center gap-2.5">
          <div className="flex h-9 w-9 items-center justify-center rounded-lg bg-sky-500 font-bold text-white shadow">
            СФ
          </div>
          <div>
            <div className="text-sm font-semibold tracking-tight text-white">СтройФинРиск</div>
            <div className="text-[11px] text-slate-400">ООО «БелСтройИнвест»</div>
          </div>
        </div>
        <button
          onClick={() => setMobileMenuOpen(false)}
          className="rounded-lg p-1 text-slate-400 hover:bg-white/10 lg:hidden"
          aria-label="Закрыть меню"
        >
          <X className="h-5 w-5" />
        </button>
      </div>

      <nav className="flex-1 space-y-1 overflow-y-auto px-3 py-4">
        {visibleNav.map((item) => {
          const Icon = item.icon;
          const active = activeTab === item.id;
          return (
            <button
              key={item.id}
              onClick={() => {
                onTabChange(item.id);
                setMobileMenuOpen(false);
              }}
              className={`flex w-full items-center gap-3 rounded-lg px-3 py-2.5 text-left text-sm font-medium transition ${
                active
                  ? 'bg-sky-600 text-white shadow-sm'
                  : 'text-slate-300 hover:bg-white/10 hover:text-white'
              }`}
            >
              <Icon className="h-4 w-4 shrink-0" />
              <span className="truncate">{item.label}</span>
            </button>
          );
        })}
      </nav>

      <div className="border-t border-white/10 p-4">
        <div className="rounded-lg bg-white/5 p-3">
          <div className="text-xs font-semibold text-white truncate">{user.fullName}</div>
          <div className="mt-0.5 text-[11px] text-sky-300 truncate">{user.roleName}</div>
          <div className="mt-2 flex items-center justify-between text-[11px] text-slate-400">
            <span>{user.login}</span>
            <button
              onClick={onLogout}
              className="inline-flex items-center gap-1 rounded px-2 py-1 text-rose-300 hover:bg-rose-500/20"
            >
              <LogOut className="h-3.5 w-3.5" />
              Выйти
            </button>
          </div>
        </div>
      </div>
    </div>
  );

  return (
    <div className="flex min-h-screen bg-slate-50">
      {/* Desktop Sidebar */}
      <aside className="hidden w-64 shrink-0 lg:block">
        <div className="sticky top-0 h-screen">
          <SidebarContent />
        </div>
      </aside>

      {/* Mobile Slide-Over Drawer */}
      {mobileMenuOpen && (
        <div className="fixed inset-0 z-50 flex lg:hidden">
          <div className="fixed inset-0 bg-slate-900/60" onClick={() => setMobileMenuOpen(false)} />
          <div className="relative z-10 w-72 max-w-[85vw]">
            <SidebarContent />
          </div>
        </div>
      )}

      {/* Main Content */}
      <div className="flex min-w-0 flex-1 flex-col pb-16 lg:pb-0">
        <header className="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-slate-200 bg-white/95 px-3 backdrop-blur sm:px-6">
          <div className="flex items-center gap-2.5 min-w-0">
            <button
              onClick={() => setMobileMenuOpen(true)}
              className="rounded-lg p-1.5 text-slate-600 hover:bg-slate-100 lg:hidden"
              aria-label="Открыть меню"
            >
              <Menu className="h-5 w-5" />
            </button>
            <div className="flex items-center gap-2 min-w-0">
              <BarChart3 className="hidden h-4 w-4 text-sky-600 sm:block" />
              <h1 className="truncate text-sm font-semibold text-slate-900 sm:text-base">
                {NAV_ITEMS.find((i) => i.id === activeTab)?.label}
              </h1>
            </div>
          </div>

          <div className="flex items-center gap-2 sm:gap-3">
            <span className="hidden rounded-full border border-sky-200 bg-sky-50 px-2.5 py-0.5 text-xs font-medium text-sky-800 sm:inline-flex">
              {user.roleName}
            </span>

            {/* Notification Bell */}
            <div className="relative">
              <button
                onClick={() => setNotifOpen((p) => !p)}
                className="relative rounded-lg border border-slate-200 p-1.5 text-slate-600 hover:bg-slate-50"
                aria-label="Уведомления"
              >
                <Bell className="h-4 w-4" />
                {unreadCount > 0 && (
                  <span className="absolute -right-1 -top-1 flex h-4 min-w-[16px] items-center justify-center rounded-full bg-rose-600 px-1 text-[10px] font-bold text-white">
                    {unreadCount}
                  </span>
                )}
              </button>

              {notifOpen && (
                <div className="absolute right-0 mt-2 w-80 max-w-[90vw] rounded-xl border border-slate-200 bg-white shadow-xl z-50">
                  <div className="flex items-center justify-between border-b border-slate-100 px-3.5 py-2.5">
                    <span className="text-xs font-semibold text-slate-800">Уведомления системы</span>
                    {unreadCount > 0 && (
                      <button
                        onClick={() => {
                          onMarkAllRead();
                          setNotifOpen(false);
                        }}
                        className="text-[11px] font-medium text-sky-600 hover:underline"
                      >
                        Прочитать все
                      </button>
                    )}
                  </div>
                  <div className="max-h-72 overflow-y-auto divide-y divide-slate-100">
                    {notifications.length === 0 && (
                      <div className="p-4 text-center text-xs text-slate-500">Нет уведомлений</div>
                    )}
                    {notifications.slice(0, 10).map((n) => (
                      <div key={n.id} className={`p-3 text-xs ${n.is_read ? 'bg-white' : 'bg-sky-50/60'}`}>
                        <div className="font-semibold text-slate-900">{n.title}</div>
                        <div className="mt-0.5 text-slate-600">{n.message}</div>
                        <div className="mt-1 text-[10px] text-slate-400">
                          {new Date(n.created_at).toLocaleString('ru-RU')}
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>

            <button
              onClick={onLogout}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-600 hover:bg-rose-50 hover:text-rose-700 lg:hidden"
              title="Выйти"
            >
              <LogOut className="h-4 w-4" />
            </button>
          </div>
        </header>

        <main className="mx-auto w-full max-w-[1440px] flex-1 p-3 sm:p-5 lg:p-6">
          {children}
        </main>
      </div>

      {/* Mobile Quick Bottom Bar (360px support) */}
      <nav className="fixed bottom-0 left-0 right-0 z-40 grid grid-cols-4 border-t border-slate-200 bg-white lg:hidden">
        {[
          visibleNav.find((v) => v.id === 'dashboard'),
          visibleNav.find((v) => v.id === 'requests'),
          visibleNav.find((v) => v.id === 'calendar') || visibleNav.find((v) => v.id === 'projects'),
          visibleNav.find((v) => v.id === 'risks') || visibleNav.find((v) => v.id === 'admin'),
        ]
          .filter(Boolean)
          .map((item) => {
            const nav = item!;
            const Icon = nav.icon;
            const active = activeTab === nav.id;
            return (
              <button
                key={nav.id}
                onClick={() => onTabChange(nav.id)}
                className={`flex flex-col items-center justify-center py-2 text-[11px] font-medium ${
                  active ? 'text-sky-600' : 'text-slate-500'
                }`}
              >
                <Icon className="mb-0.5 h-4 w-4" />
                <span className="truncate">{nav.shortLabel}</span>
              </button>
            );
          })}
      </nav>
    </div>
  );
};
