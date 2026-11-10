import React, { useEffect, useState } from 'react';
import { apiFetch, clearSession, getSavedSession, type SessionUser } from './api/client';
import { Layout, type NavTab } from './components/Layout';
import { LoginPage } from './pages/LoginPage';
import { DashboardPage } from './pages/DashboardPage';
import { ProjectsBudgetsPage } from './pages/ProjectsBudgetsPage';
import { PaymentRequestsPage } from './pages/PaymentRequestsPage';
import { PaymentCalendarPage } from './pages/PaymentCalendarPage';
import { ContractsPage } from './pages/ContractsPage';
import { RisksPage } from './pages/RisksPage';
import { ReportsPage } from './pages/ReportsPage';
import { AdminAuditPage } from './pages/AdminAuditPage';

export default function App() {
  const [user, setUser] = useState<SessionUser | null>(() => getSavedSession().user);
  const [activeTab, setActiveTab] = useState<NavTab>('dashboard');
  const [notifications, setNotifications] = useState<any[]>([]);

  const refreshNotifications = async () => {
    try {
      const list = await apiFetch('/api/notifications');
      setNotifications(list);
    } catch {
      // ignore if session expired
    }
  };

  useEffect(() => {
    if (user) {
      if (user.roleCode === 'Admin') {
        setActiveTab('admin');
      } else if (user.roleCode === 'SiteWorker') {
        setActiveTab('requests');
      } else {
        setActiveTab('dashboard');
      }
      refreshNotifications();
    }
  }, [user]);

  const handleLogout = () => {
    clearSession();
    setUser(null);
    setNotifications([]);
  };

  const handleMarkAllRead = async () => {
    await apiFetch('/api/notifications/read-all', { method: 'POST' });
    await refreshNotifications();
  };

  if (!user) {
    return <LoginPage onLoginSuccess={(u) => setUser(u)} />;
  }

  return (
    <Layout
      user={user}
      activeTab={activeTab}
      onTabChange={setActiveTab}
      onLogout={handleLogout}
      notifications={notifications}
      onMarkAllRead={handleMarkAllRead}
    >
      {activeTab === 'dashboard' && <DashboardPage onNavigate={setActiveTab} />}
      {activeTab === 'projects' && <ProjectsBudgetsPage />}
      {activeTab === 'requests' && <PaymentRequestsPage user={user} />}
      {activeTab === 'calendar' && <PaymentCalendarPage />}
      {activeTab === 'contracts' && <ContractsPage />}
      {activeTab === 'risks' && <RisksPage />}
      {activeTab === 'reports' && <ReportsPage />}
      {activeTab === 'admin' && <AdminAuditPage />}
    </Layout>
  );
}
