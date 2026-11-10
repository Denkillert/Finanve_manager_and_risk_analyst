import express from 'express';
import cors from 'cors';
import crypto from 'node:crypto';
import type { DatabaseSync } from 'node:sqlite';
import {
  calculateActPayable,
  calculateFreeLimit,
  calculatePenalty,
  calculateRiskPortfolio,
  calculateSensitivityFactors,
  getImpactScore,
  getProbabilityScore,
  normalizeVat,
  roundTo,
} from './domain/calculators.js';
import {
  hashPassword,
  requireAuth,
  requireRoles,
  signJwt,
  verifyPassword,
  type AuthedRequest,
} from './security/auth.js';
import {
  generateExcelReportBuffer,
  generatePdfReportBuffer,
  importEstimateContent,
  parseBankStatement,
} from './adapters/integrations.js';
import {
  buildReportModel,
  getProjectCashCalendar,
  getProjectEarnedValue,
} from './services/reportBuilder.js';

export function createApp(db: DatabaseSync) {
  const app = express();
  app.use(cors());
  app.use(express.json({ limit: '10mb' }));

  const writeAudit = (
    userId: string | null,
    userLogin: string,
    objectName: string,
    objectId: string,
    action: string,
    oldValue: string | null = null,
    newValue: string | null = null,
    details: string | null = null
  ) => {
    db.prepare(
      'INSERT INTO audit_logs (user_id, user_login, timestamp, object_name, object_id, action, old_value, new_value, details) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)'
    ).run(userId, userLogin, new Date().toISOString(), objectName, objectId, action, oldValue, newValue, details);
  };

  const publishNotification = (
    targetRoleCode: string,
    title: string,
    message: string,
    severity: 'Info' | 'Warning' | 'Critical',
    category: 'RiskTrigger' | 'CashGap' | 'ApprovalDue' | 'OverdueAdvance' | 'System',
    entityLink: string
  ) => {
    db.prepare(
      'INSERT INTO notifications (id, target_role_code, title, message, severity, category, created_at, entity_link) VALUES (?, ?, ?, ?, ?, ?, ?, ?)'
    ).run(crypto.randomUUID(), targetRoleCode, title, message, severity, category, new Date().toISOString(), entityLink);
  };

  const getRateToByn = (currency: string): number => {
    const row = db
      .prepare('SELECT rate_to_byn FROM currency_rates WHERE currency = ? ORDER BY rate_date DESC LIMIT 1')
      .get(currency.toUpperCase()) as { rate_to_byn: number } | undefined;
    return row ? Number(row.rate_to_byn) : 1;
  };

  const getActiveNormRule = (code: string, asOfDate: string) => {
    return db
      .prepare(
        `SELECT * FROM norm_rules
         WHERE code = ? AND valid_from <= ? AND (valid_to IS NULL OR valid_to >= ?)
         ORDER BY revision DESC LIMIT 1`
      )
      .get(code, asOfDate, asOfDate) as Record<string, any> | undefined;
  };

  const getFreeLimitForObjectItem = (objectId: string, costItemId: string) => {
    const obj = db.prepare('SELECT * FROM construction_objects WHERE id = ? AND is_deleted = 0').get(objectId) as Record<string, any> | undefined;
    if (!obj) throw new Error('Объект строительства не найден.');
    const bv = db
      .prepare("SELECT * FROM budget_versions WHERE project_id = ? AND status = 'Approved' AND is_deleted = 0 ORDER BY version_number DESC LIMIT 1")
      .get(obj.project_id) as Record<string, any> | undefined;

    let b = 0;
    if (bv) {
      const lim = db
        .prepare('SELECT COALESCE(SUM(amount), 0) AS s FROM budget_limits WHERE budget_version_id = ? AND construction_object_id = ? AND cost_item_id = ? AND is_deleted = 0')
        .get(bv.id, objectId, costItemId) as { s: number };
      b = Number(lim.s);
    }

    const obl = db
      .prepare('SELECT COALESCE(SUM(gross_amount), 0) AS gross, COALESCE(SUM(paid_amount), 0) AS paid FROM obligations WHERE construction_object_id = ? AND cost_item_id = ? AND is_deleted = 0')
      .get(objectId, costItemId) as { gross: number; paid: number };

    const req = db
      .prepare("SELECT COALESCE(SUM(amount_in_byn), 0) AS q FROM payment_requests WHERE construction_object_id = ? AND cost_item_id = ? AND is_deleted = 0 AND status IN ('UnderReview','Blocked','PendingApproval','ApprovedByManagement','Approved')")
      .get(objectId, costItemId) as { q: number };

    return calculateFreeLimit(b, Number(obl.gross), Number(obl.paid), Number(req.q));
  };

  app.get('/api/health', (_req, res) => {
    res.json({ status: 'ok', service: 'FinanveManager.Api', timestamp: new Date().toISOString() });
  });

  // =========================================================================
  // AUTH & DEMO ROLES (НФТ-7, НФТ-8, НФТ-10)
  // =========================================================================
  app.get('/api/auth/demo-users', (_req, res) => {
    const users = db
      .prepare(
        `SELECT u.login, u.full_name, u.two_factor_enabled, r.code AS role_code, r.name AS role_name, r.client_type
         FROM users u JOIN roles r ON r.id = u.role_id
         WHERE u.is_active = 1 ORDER BY r.code`
      )
      .all();
    res.json(users);
  });

  app.post('/api/auth/login', (req, res) => {
    const { login, password, twoFactorCode } = req.body || {};
    if (!login || !password) {
      res.status(400).json({ error: 'Укажите логин и пароль.' });
      return;
    }

    const user = db
      .prepare(
        `SELECT u.*, r.code AS role_code, r.name AS role_name, r.client_type
         FROM users u JOIN roles r ON r.id = u.role_id
         WHERE u.login = ?`
      )
      .get(String(login).trim()) as Record<string, any> | undefined;

    if (!user || !user.is_active) {
      res.status(401).json({ error: 'Неверный логин или пароль, либо учётная запись отключена.' });
      return;
    }

    if (user.locked_until && new Date(user.locked_until).getTime() > Date.now()) {
      res.status(423).json({
        error: `Учётная запись заблокирована после 5 неудачных попыток до ${new Date(user.locked_until).toLocaleTimeString('ru-RU')}.`,
      });
      return;
    }

    if (!verifyPassword(String(password), user.password_hash)) {
      const nextFails = Number(user.failed_login_count) + 1;
      const lockedUntil = nextFails >= 5 ? new Date(Date.now() + 15 * 60000).toISOString() : null;
      db.prepare('UPDATE users SET failed_login_count = ?, locked_until = ? WHERE id = ?').run(nextFails, lockedUntil, user.id);
      writeAudit(user.id, user.login, 'Auth', user.login, 'LOGIN_FAILED', null, `attempt=${nextFails}`, lockedUntil ? 'ACCOUNT_LOCKED' : null);
      res.status(lockedUntil ? 423 : 401).json({
        error: lockedUntil
          ? 'Превышено 5 неудачных попыток. Учётная запись заблокирована на 15 минут (НФТ-7).'
          : `Неверный пароль (попытка ${nextFails} из 5).`,
      });
      return;
    }

    if (Number(user.two_factor_enabled) === 1 && String(twoFactorCode || '') !== String(user.two_factor_secret || '246810')) {
      res.status(401).json({
        error: 'Для администратора требуется код двухфакторной аутентификации (2FA).',
        requiresTwoFactor: true,
      });
      return;
    }

    db.prepare('UPDATE users SET failed_login_count = 0, locked_until = NULL WHERE id = ?').run(user.id);
    writeAudit(user.id, user.login, 'Auth', user.login, 'LOGIN_SUCCESS', null, user.role_code, null);

    const authUser = {
      id: user.id,
      login: user.login,
      fullName: user.full_name,
      roleCode: user.role_code,
      roleName: user.role_name,
    };
    const token = signJwt(authUser);
    res.json({ token, user: { ...authUser, clientType: user.client_type } });
  });

  app.get('/api/auth/me', requireAuth, (req: AuthedRequest, res) => {
    res.json(req.user);
  });

  // =========================================================================
  // DASHBOARD (Главная панель)
  // =========================================================================
  app.get('/api/dashboard', requireAuth, (req: AuthedRequest, res) => {
    const projects = db.prepare('SELECT * FROM projects WHERE is_deleted = 0 ORDER BY code').all() as Array<Record<string, any>>;
    const forecasts = projects.map((p) => getProjectEarnedValue(db, p.id)).filter(Boolean);
    const freeLimits = db.prepare('SELECT * FROM v_free_limit').all() as Array<Record<string, any>>;
    const cashCalendar = getProjectCashCalendar(db, null, '2026-11-10', 90);
    const pendingRequests = db
      .prepare(
        `SELECT pr.*, c.number AS contract_number, c.counterparty, co.name AS object_name, ci.code AS cost_item_code, ci.name AS cost_item_name
         FROM payment_requests pr
         JOIN contracts c ON c.id = pr.contract_id
         JOIN construction_objects co ON co.id = pr.construction_object_id
         JOIN cost_items ci ON ci.id = pr.cost_item_id
         WHERE pr.is_deleted = 0 AND pr.status IN ('Blocked', 'PendingApproval', 'Approved')
         ORDER BY pr.created_at DESC`
      )
      .all();
    const notifications = db
      .prepare('SELECT * FROM notifications ORDER BY is_read ASC, created_at DESC LIMIT 20')
      .all();

    res.json({
      portfolioSummary: {
        projectCount: projects.length,
        totalContractValue: roundTo(projects.reduce((s, p) => s + Number(p.contract_value), 0), 2),
        totalApprovedBudget: roundTo(freeLimits.reduce((s, f) => s + Number(f.approved_budget), 0), 2),
        totalGrossObligations: roundTo(freeLimits.reduce((s, f) => s + Number(f.gross_obligations), 0), 2),
        totalFreeLimit: roundTo(freeLimits.reduce((s, f) => s + Number(f.free_limit), 0), 2),
        totalReserveP80: roundTo(forecasts.reduce((s, f: any) => s + Number(f.riskSummary.reserveP80), 0), 2),
        gapDaysCount90d: cashCalendar.gapDaysCount,
        maxCashGap90d: cashCalendar.maxCashGap,
      },
      forecasts,
      cashCalendar,
      pendingRequests,
      notifications,
    });
  });

  // =========================================================================
  // PROJECTS, HIERARCHY & BUDGETS (требования 1, 2, 5, 12, 24)
  // =========================================================================
  app.get('/api/projects', requireAuth, (_req, res) => {
    const projects = db.prepare('SELECT * FROM projects WHERE is_deleted = 0 ORDER BY code').all() as Array<Record<string, any>>;
    const objects = db.prepare('SELECT * FROM construction_objects WHERE is_deleted = 0').all() as Array<Record<string, any>>;
    const stages = db.prepare('SELECT * FROM work_stages WHERE is_deleted = 0').all() as Array<Record<string, any>>;
    const costItems = db.prepare('SELECT * FROM cost_items WHERE is_deleted = 0 ORDER BY code').all();
    const freeLimits = db.prepare('SELECT * FROM v_free_limit').all();

    const enriched = projects.map((p) => ({
      ...p,
      forecast: getProjectEarnedValue(db, p.id)?.forecast,
      riskSummary: getProjectEarnedValue(db, p.id)?.riskSummary,
      objects: objects
        .filter((o) => o.project_id === p.id)
        .map((o) => ({
          ...o,
          stages: stages.filter((s) => s.construction_object_id === o.id),
        })),
    }));

    res.json({ projects: enriched, costItems, freeLimits });
  });

  app.post('/api/projects', requireAuth, requireRoles('CEO', 'FinancialManager'), (req: AuthedRequest, res) => {
    const { code, name, contractValue, initialCash, minCashBalance, plannedStartDate, plannedEndDate, objectName, objectAddress } = req.body || {};
    if (!code || !name || contractValue === undefined) {
      res.status(400).json({ error: 'Заполните код, наименование и договорную стоимость проекта.' });
      return;
    }
    const portfolio = db.prepare('SELECT id FROM portfolios LIMIT 1').get() as { id: string };
    const projectId = crypto.randomUUID();
    const objectId = crypto.randomUUID();
    const bvId = crypto.randomUUID();
    const now = new Date().toISOString();

    try {
      db.exec('BEGIN');
      db.prepare(
        'INSERT INTO projects (id, portfolio_id, code, name, contract_value, currency, initial_cash, min_cash_balance, planned_start_date, planned_end_date) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
      ).run(projectId, portfolio.id, String(code).trim(), String(name).trim(), Number(contractValue), 'BYN', Number(initialCash || 0), Number(minCashBalance || 0), plannedStartDate || '2026-11-10', plannedEndDate || '2027-12-31');

      db.prepare('INSERT INTO construction_objects (id, project_id, queue_name, name, address) VALUES (?, ?, ?, ?, ?)').run(
        objectId, projectId, '1-я очередь', objectName || `${name} — основной объект`, objectAddress || 'г. Минск'
      );
      db.prepare('INSERT INTO budget_versions (id, project_id, version_number, status, comment, created_at) VALUES (?, ?, 1, ?, ?, ?)').run(
        bvId, projectId, 'Draft', 'Первоначальный проект бюджета', now
      );
      writeAudit(req.user!.id, req.user!.login, 'Project', projectId, 'CREATE_PROJECT', null, `${code}:${name}`, null);
      db.exec('COMMIT');
      res.status(201).json({ id: projectId, objectId, budgetVersionId: bvId });
    } catch (err: any) {
      db.exec('ROLLBACK');
      res.status(400).json({ error: err.message });
    }
  });

  app.get('/api/budget/versions', requireAuth, (req, res) => {
    const projectId = req.query.projectId as string | undefined;
    const versions = db
      .prepare(
        `SELECT bv.*, p.code AS project_code, p.name AS project_name
         FROM budget_versions bv JOIN projects p ON p.id = bv.project_id
         WHERE bv.is_deleted = 0 ${projectId ? 'AND bv.project_id = ?' : ''}
         ORDER BY p.code, bv.version_number DESC`
      )
      .all(...(projectId ? [projectId] : [])) as Array<Record<string, any>>;

    const limits = db
      .prepare(
        `SELECT bl.*, co.name AS object_name, ci.code AS cost_item_code, ci.name AS cost_item_name
         FROM budget_limits bl
         JOIN construction_objects co ON co.id = bl.construction_object_id
         JOIN cost_items ci ON ci.id = bl.cost_item_id
         WHERE bl.is_deleted = 0`
      )
      .all() as Array<Record<string, any>>;

    res.json(
      versions.map((v) => ({
        ...v,
        limits: limits.filter((l) => l.budget_version_id === v.id),
      }))
    );
  });

  app.post('/api/budget/versions', requireAuth, requireRoles('FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { projectId, comment } = req.body || {};
    if (!projectId) {
      res.status(400).json({ error: 'Укажите projectId.' });
      return;
    }
    const existing = db
      .prepare('SELECT * FROM budget_versions WHERE project_id = ? AND is_deleted = 0 ORDER BY version_number DESC')
      .all(projectId) as Array<Record<string, any>>;
    const nextNum = (existing[0]?.version_number || 0) + 1;
    const newId = crypto.randomUUID();
    const now = new Date().toISOString();

    db.exec('BEGIN');
    try {
      db.prepare('INSERT INTO budget_versions (id, project_id, version_number, status, comment, created_at) VALUES (?, ?, ?, ?, ?, ?)').run(
        newId, projectId, nextNum, 'Draft', comment || `Корректировка бюджета (версия ${nextNum})`, now
      );
      if (existing[0]) {
        const prevLimits = db.prepare('SELECT * FROM budget_limits WHERE budget_version_id = ? AND is_deleted = 0').all(existing[0].id) as Array<Record<string, any>>;
        const ins = db.prepare('INSERT INTO budget_limits (id, budget_version_id, construction_object_id, work_stage_id, cost_item_id, period_start, period_end, amount) VALUES (?, ?, ?, ?, ?, ?, ?, ?)');
        for (const pl of prevLimits) {
          ins.run(crypto.randomUUID(), newId, pl.construction_object_id, pl.work_stage_id, pl.cost_item_id, pl.period_start, pl.period_end, pl.amount);
        }
      }
      writeAudit(req.user!.id, req.user!.login, 'BudgetVersion', newId, 'CREATE_BUDGET_VERSION', null, `v${nextNum}`, comment || null);
      db.exec('COMMIT');
      res.status(201).json({ id: newId, versionNumber: nextNum });
    } catch (err: any) {
      db.exec('ROLLBACK');
      res.status(400).json({ error: err.message });
    }
  });

  app.put('/api/budget/limits/:id', requireAuth, requireRoles('FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { amount, rowVersion } = req.body || {};
    const limit = db
      .prepare(
        `SELECT bl.*, bv.status AS version_status
         FROM budget_limits bl JOIN budget_versions bv ON bv.id = bl.budget_version_id
         WHERE bl.id = ? AND bl.is_deleted = 0`
      )
      .get(String(req.params.id)) as Record<string, any> | undefined;

    if (!limit) {
      res.status(404).json({ error: 'Лимит не найден.' });
      return;
    }
    if (limit.version_status === 'Approved') {
      res.status(400).json({ error: 'Утверждённая версия бюджета неизменяема. Создайте новую версию (правило 12).' });
      return;
    }
    if (rowVersion !== undefined && Number(rowVersion) !== Number(limit.row_version)) {
      res.status(409).json({
        error: 'Конфликт версий: лимит бюджета был изменён другим пользователем. Перечитайте данные (требование 24).',
        code: 'CONCURRENCY_CONFLICT',
      });
      return;
    }
    const newAmt = Number(amount);
    if (Number.isNaN(newAmt) || newAmt < 0) {
      res.status(400).json({ error: 'Сумма лимита должна быть неотрицательной.' });
      return;
    }
    db.prepare('UPDATE budget_limits SET amount = ?, row_version = row_version + 1 WHERE id = ?').run(newAmt, limit.id);
    writeAudit(req.user!.id, req.user!.login, 'BudgetLimit', limit.id, 'UPDATE_LIMIT', String(limit.amount), String(newAmt), null);
    res.json({ id: limit.id, amount: newAmt, row_version: Number(limit.row_version) + 1 });
  });

  app.post('/api/budget/limits', requireAuth, requireRoles('FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { budgetVersionId, constructionObjectId, costItemId, periodStart, periodEnd, amount } = req.body || {};
    const bv = db.prepare('SELECT * FROM budget_versions WHERE id = ?').get(budgetVersionId) as Record<string, any> | undefined;
    if (!bv) {
      res.status(404).json({ error: 'Версия бюджета не найдена.' });
      return;
    }
    if (bv.status === 'Approved') {
      res.status(400).json({ error: 'Нельзя добавлять лимит в утверждённую версию бюджета. Создайте новую версию.' });
      return;
    }
    const id = crypto.randomUUID();
    db.prepare('INSERT INTO budget_limits (id, budget_version_id, construction_object_id, cost_item_id, period_start, period_end, amount) VALUES (?, ?, ?, ?, ?, ?, ?)').run(
      id, budgetVersionId, constructionObjectId, costItemId, periodStart || '2026-01-01', periodEnd || '2026-12-31', Number(amount)
    );
    writeAudit(req.user!.id, req.user!.login, 'BudgetLimit', id, 'ADD_LIMIT', null, String(amount), null);
    res.status(201).json({ id });
  });

  app.post('/api/budget/versions/:id/approve', requireAuth, requireRoles('CEO'), (req: AuthedRequest, res) => {
    const bv = db.prepare('SELECT * FROM budget_versions WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!bv) {
      res.status(404).json({ error: 'Версия бюджета не найдена.' });
      return;
    }
    if (req.body?.rowVersion !== undefined && Number(req.body.rowVersion) !== Number(bv.row_version)) {
      res.status(409).json({ error: 'Конфликт версий бюджета. Обновите страницу.', code: 'CONCURRENCY_CONFLICT' });
      return;
    }
    const now = new Date().toISOString();
    db.exec('BEGIN');
    try {
      db.prepare("UPDATE budget_versions SET status = 'Archived', row_version = row_version + 1 WHERE project_id = ? AND id != ? AND status = 'Approved'").run(bv.project_id, bv.id);
      db.prepare("UPDATE budget_versions SET status = 'Approved', approved_at = ?, approved_by_user_id = ?, row_version = row_version + 1 WHERE id = ?").run(now, req.user!.id, bv.id);
      writeAudit(req.user!.id, req.user!.login, 'BudgetVersion', bv.id, 'APPROVE_BUDGET_VERSION', bv.status, 'Approved', `v${bv.version_number}`);
      db.exec('COMMIT');
      res.json({ id: bv.id, status: 'Approved' });
    } catch (err: any) {
      db.exec('ROLLBACK');
      res.status(400).json({ error: err.message });
    }
  });

  app.get('/api/budget/free-limits', requireAuth, (req, res) => {
    const objectId = req.query.objectId as string | undefined;
    const costItemId = req.query.costItemId as string | undefined;
    if (objectId && costItemId) {
      res.json(getFreeLimitForObjectItem(objectId, costItemId));
      return;
    }
    res.json(db.prepare('SELECT * FROM v_free_limit').all());
  });

  // =========================================================================
  // CONTRACTS, OBLIGATIONS & ADVANCES (требования 3, 4, 10)
  // =========================================================================
  app.get('/api/contracts', requireAuth, (_req, res) => {
    const contracts = db
      .prepare('SELECT c.*, p.code AS project_code, p.name AS project_name FROM contracts c JOIN projects p ON p.id = c.project_id WHERE c.is_deleted = 0 ORDER BY c.number')
      .all() as Array<Record<string, any>>;
    const supplements = db.prepare('SELECT * FROM contract_supplements WHERE is_deleted = 0').all() as Array<Record<string, any>>;
    const schedules = db.prepare('SELECT * FROM contract_schedule_items WHERE is_deleted = 0 ORDER BY planned_date').all() as Array<Record<string, any>>;
    const obligations = db
      .prepare(
        `SELECT o.*, co.name AS object_name, ci.code AS cost_item_code, ci.name AS cost_item_name
         FROM obligations o
         JOIN construction_objects co ON co.id = o.construction_object_id
         JOIN cost_items ci ON ci.id = o.cost_item_id
         WHERE o.is_deleted = 0`
      )
      .all() as Array<Record<string, any>>;
    const advances = db.prepare('SELECT * FROM advances WHERE is_deleted = 0').all() as Array<Record<string, any>>;

    res.json(
      contracts.map((c) => {
        const sups = supplements.filter((s) => s.contract_id === c.id);
        const effectiveTotal = Number(c.total_amount) + sups.reduce((sum, s) => sum + Number(s.amount_delta), 0);
        const obls = obligations.filter((o) => o.contract_id === c.id);
        const advs = advances.filter((a) => a.contract_id === c.id).map((a) => ({
          ...a,
          remainder: roundTo(Number(a.amount) - Number(a.used_amount), 2),
          utilizationPercent: roundTo((Number(a.used_amount) / Number(a.amount)) * 100, 1),
        }));
        const sch = schedules.filter((s) => s.contract_id === c.id);
        return {
          ...c,
          effectiveTotal,
          supplements: sups,
          obligations: obls,
          advances: advs,
          schedule: sch,
        };
      })
    );
  });

  app.post('/api/contracts', requireAuth, requireRoles('ProcurementSpecialist', 'FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { projectId, number, counterparty, counterpartyUnp, direction, totalAmount, vatIncluded, currency, startDate, endDate, retentionPercent, constructionObjectId, costItemId, initialObligationAmount } = req.body || {};
    if (!projectId || !number || !counterparty || !totalAmount) {
      res.status(400).json({ error: 'Заполните проект, номер договора, контрагента и сумму.' });
      return;
    }
    const id = crypto.randomUUID();
    db.exec('BEGIN');
    try {
      db.prepare(
        'INSERT INTO contracts (id, project_id, number, counterparty, counterparty_unp, direction, total_amount, vat_included, vat_rate, currency, start_date, end_date, retention_percent) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 20, ?, ?, ?, ?)'
      ).run(id, projectId, number.trim(), counterparty.trim(), counterpartyUnp || '190000000', direction || 'SupplierSubcontractorOutgoing', Number(totalAmount), vatIncluded === false ? 0 : 1, (currency || 'BYN').toUpperCase(), startDate || '2026-11-10', endDate || '2027-06-30', Number(retentionPercent ?? 5));

      if (constructionObjectId && costItemId && Number(initialObligationAmount) > 0) {
        if (Number(initialObligationAmount) > Number(totalAmount)) {
          throw new Error('Обязательство не может превышать сумму договора (правило 5).');
        }
        db.prepare('INSERT INTO obligations (id, contract_id, construction_object_id, cost_item_id, gross_amount, paid_amount, status) VALUES (?, ?, ?, ?, ?, 0, ?)').run(
          crypto.randomUUID(), id, constructionObjectId, costItemId, Number(initialObligationAmount), 'Open'
        );
      }

      writeAudit(req.user!.id, req.user!.login, 'Contract', id, 'CREATE_CONTRACT', null, `${number}:${totalAmount} ${currency || 'BYN'}`, null);
      db.exec('COMMIT');
      res.status(201).json({ id });
    } catch (err: any) {
      db.exec('ROLLBACK');
      res.status(400).json({ error: err.message });
    }
  });

  app.post('/api/contracts/:id/supplements', requireAuth, requireRoles('ProcurementSpecialist', 'FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { number, amountDelta, signedDate, reason } = req.body || {};
    const contract = db.prepare('SELECT * FROM contracts WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!contract) {
      res.status(404).json({ error: 'Договор не найден.' });
      return;
    }
    const id = crypto.randomUUID();
    try {
      db.prepare('INSERT INTO contract_supplements (id, contract_id, number, amount_delta, signed_date, reason) VALUES (?, ?, ?, ?, ?, ?)').run(
        id, contract.id, number || `ДС-${Date.now().toString().slice(-4)}`, Number(amountDelta || 0), signedDate || '2026-11-10', reason || 'Корректировка объёмов'
      );
      writeAudit(req.user!.id, req.user!.login, 'ContractSupplement', id, 'ADD_SUPPLEMENT', null, String(amountDelta), reason || null);
      res.status(201).json({ id });
    } catch (err: any) {
      res.status(400).json({ error: err.message });
    }
  });

  app.get('/api/advances', requireAuth, (_req, res) => {
    const rows = db
      .prepare(
        `SELECT a.*, c.number AS contract_number, c.counterparty, co.name AS object_name, ci.code AS cost_item_code
         FROM advances a
         JOIN contracts c ON c.id = a.contract_id
         JOIN construction_objects co ON co.id = a.construction_object_id
         JOIN cost_items ci ON ci.id = a.cost_item_id
         WHERE a.is_deleted = 0
         ORDER BY a.due_date`
      )
      .all() as Array<Record<string, any>>;

    res.json(
      rows.map((a) => ({
        ...a,
        remainder: roundTo(Number(a.amount) - Number(a.used_amount), 2),
        utilizationPercent: roundTo((Number(a.used_amount) / Number(a.amount)) * 100, 1),
      }))
    );
  });

  app.post('/api/advances/:id/offset', requireAuth, requireRoles('Accountant', 'FinancialManager'), (req: AuthedRequest, res) => {
    const { offsetAmount } = req.body || {};
    const adv = db.prepare('SELECT * FROM advances WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!adv) {
      res.status(404).json({ error: 'Аванс не найден.' });
      return;
    }
    const add = Number(offsetAmount);
    if (add <= 0 || Number(adv.used_amount) + add > Number(adv.amount)) {
      res.status(400).json({ error: 'Сумма зачёта превышает остаток аванса (инвариант 2).' });
      return;
    }
    const nextUsed = roundTo(Number(adv.used_amount) + add, 4);
    const nextStatus = nextUsed >= Number(adv.amount) ? 'Closed' : adv.status;
    db.prepare('UPDATE advances SET used_amount = ?, status = ?, row_version = row_version + 1 WHERE id = ?').run(nextUsed, nextStatus, adv.id);
    writeAudit(req.user!.id, req.user!.login, 'Advance', adv.id, 'OFFSET_ADVANCE', String(adv.used_amount), String(nextUsed), null);
    res.json({ id: adv.id, used_amount: nextUsed, status: nextStatus });
  });

  // =========================================================================
  // PAYMENT REQUESTS WORKFLOW (UC01, UC04, табл. 1.10, табл. 1.11, правила 4–16)
  // =========================================================================
  app.get('/api/payment-requests', requireAuth, (req, res) => {
    const statusFilter = req.query.status as string | undefined;
    const rows = db
      .prepare(
        `SELECT pr.*, p.code AS project_code, p.name AS project_name,
                co.name AS object_name, c.number AS contract_number, c.counterparty,
                ci.code AS cost_item_code, ci.name AS cost_item_name,
                u.full_name AS author_name
         FROM payment_requests pr
         JOIN projects p ON p.id = pr.project_id
         JOIN construction_objects co ON co.id = pr.construction_object_id
         JOIN contracts c ON c.id = pr.contract_id
         JOIN cost_items ci ON ci.id = pr.cost_item_id
         JOIN users u ON u.id = pr.author_user_id
         WHERE pr.is_deleted = 0 ${statusFilter ? 'AND pr.status = ?' : ''}
         ORDER BY pr.created_at DESC`
      )
      .all(...(statusFilter ? [statusFilter] : [])) as Array<Record<string, any>>;

    const docs = db.prepare('SELECT * FROM documents WHERE is_deleted = 0').all() as Array<Record<string, any>>;
    const payments = db.prepare('SELECT * FROM payments WHERE is_deleted = 0').all() as Array<Record<string, any>>;

    res.json(
      rows.map((r) => ({
        ...r,
        freeLimitSnapshot: getFreeLimitForObjectItem(r.construction_object_id, r.cost_item_id),
        documents: docs.filter((d) => d.payment_request_id === r.id),
        payments: payments.filter((p) => p.payment_request_id === r.id),
      }))
    );
  });

  app.post(
    '/api/payment-requests',
    requireAuth,
    requireRoles('ProjectManager', 'ProcurementSpecialist', 'FinancialManager', 'CEO'),
    (req: AuthedRequest, res) => {
      const {
        projectId,
        constructionObjectId,
        contractId,
        costItemId,
        amount,
        actAmount,
        advanceOffset,
        retentionAmount,
        vatIncluded,
        currency,
        type,
        plannedPaymentDate,
        purpose,
        documentType,
        documentNumber,
        documentDate,
        photoEvidencePaths,
        idempotencyKey,
        submitImmediately,
      } = req.body || {};

      if (!projectId || !constructionObjectId || !contractId || !costItemId || !amount || !documentNumber || !documentDate) {
        res.status(400).json({ error: 'Заполните все обязательные реквизиты заявки (требование 6): объект, договор, статью, сумму, тип и документ-основание.' });
        return;
      }

      if (idempotencyKey) {
        const existing = db.prepare('SELECT * FROM payment_requests WHERE idempotency_key = ? AND is_deleted = 0').get(idempotencyKey) as Record<string, any> | undefined;
        if (existing) {
          res.json({ request: existing, idempotentReplay: true });
          return;
        }
      }

      const dType = documentType || 'ActS2';
      const dNum = String(documentNumber).trim();
      const dDate = String(documentDate).slice(0, 10);

      // Контроль дублей (правило 16, требование 23)
      const duplicate = db
        .prepare('SELECT * FROM documents WHERE type = ? AND LOWER(number) = LOWER(?) AND date = ? AND is_deleted = 0')
        .get(dType, dNum, dDate) as Record<string, any> | undefined;
      if (duplicate) {
        res.status(409).json({
          error: `Контроль дублей: документ ${dType} № ${dNum} от ${dDate} уже оплачен или привязан к заявке!`,
          code: 'DUPLICATE_DETECTED',
        });
        return;
      }

      const contract = db.prepare('SELECT * FROM contracts WHERE id = ? AND is_deleted = 0').get(contractId) as Record<string, any> | undefined;
      const obj = db.prepare('SELECT * FROM construction_objects WHERE id = ? AND is_deleted = 0').get(constructionObjectId) as Record<string, any> | undefined;
      if (!contract || contract.project_id !== projectId || !obj || obj.project_id !== projectId) {
        res.status(400).json({ error: 'Договор или объект строительства не принадлежит выбранному проекту (правило 1, 3).' });
        return;
      }

      const sups = db.prepare('SELECT COALESCE(SUM(amount_delta), 0) AS d FROM contract_supplements WHERE contract_id = ? AND is_deleted = 0').get(contractId) as { d: number };
      const effectiveContract = Number(contract.total_amount) + Number(sups.d);
      const paidOnContract = db
        .prepare("SELECT COALESCE(SUM(payable_amount), 0) AS p FROM payment_requests WHERE contract_id = ? AND is_deleted = 0 AND status NOT IN ('Rejected')")
        .get(contractId) as { p: number };

      const reqAmount = Number(amount);
      const actAmt = Number(actAmount || reqAmount);
      const advOff = Number(advanceOffset || 0);
      const retAmt = Number(retentionAmount || 0);

      let payable: ReturnType<typeof calculateActPayable>;
      try {
        payable = calculateActPayable(actAmt, advOff, retAmt);
      } catch (e: any) {
        res.status(400).json({ error: e.message });
        return;
      }

      const curr = String(currency || 'BYN').toUpperCase();
      const rate = getRateToByn(curr);
      const vatInfo = normalizeVat(reqAmount, vatIncluded !== false, 20);
      const amountInByn = roundTo(payable.payableAmount * rate, 4);
      const reqType = type === 'Advance' ? 'Advance' : 'CurrentPayment';
      const planDate = (plannedPaymentDate || '2026-11-20').slice(0, 10);

      // 4 Автоматические проверки (UC01, табл. 1.10)
      const messages: string[] = [];
      let contractDocsValid = true;
      if (paidOnContract.p + payable.payableAmount > effectiveContract + 0.01) {
        contractDocsValid = false;
        messages.push(`Сумма платежей по договору ${contract.number} превышает лимит договора с доп. соглашениями (${effectiveContract} ${contract.currency}).`);
      }

      const freeLimitRes = getFreeLimitForObjectItem(constructionObjectId, costItemId);
      const freeLimitOk = freeLimitRes.freeLimit >= amountInByn;
      if (!freeLimitOk) {
        messages.push(`Недостаточно свободного лимита: Lдост = ${freeLimitRes.freeLimit.toFixed(2)} тыс. р., заявка = ${amountInByn.toFixed(2)} тыс. р. (Δ = ${freeLimitRes.deviation.toFixed(2)}).`);
      }

      const overdueAdvances = db
        .prepare("SELECT * FROM advances WHERE contract_id = ? AND is_deleted = 0 AND status != 'Closed' AND (status = 'Overdue' OR due_date < '2026-11-10') AND used_amount < amount")
        .all(contractId) as Array<Record<string, any>>;
      const hasOverdueAdvance = overdueAdvances.length > 0;
      const advanceBlocked = hasOverdueAdvance && reqType === 'Advance';
      if (advanceBlocked) {
        messages.push('Блокировка: по договору имеется просроченный аванс — выдача нового аванса запрещена (правило 10).');
      } else if (hasOverdueAdvance) {
        messages.push('Предупреждение: по договору имеется просроченный аванс; текущий платёж по акту пропущен с уведомлением.');
      }

      const cal = getProjectCashCalendar(db, projectId, '2026-11-10', 30);
      const dayMatch = cal.days.find((d) => d.date === planDate);
      const projectedClosing = dayMatch ? dayMatch.closingBalance - amountInByn : cal.initialCash - amountInByn;
      const cashGapAmount = Math.max(0, roundTo(cal.minCashBalance - projectedClosing, 2));
      if (cashGapAmount > 0) {
        messages.push(`Кассовый прогноз: на дату ${planDate} возникает кассовый разрыв ${cashGapAmount} тыс. р.`);
      }

      let nextStatus = 'Draft';
      let blockReason: string | null = null;
      let returnReason: string | null = null;
      const now = new Date().toISOString();
      let submittedAt: string | null = null;
      let blockedAt: string | null = null;

      if (submitImmediately !== false) {
        submittedAt = now;
        if (!contractDocsValid) {
          nextStatus = 'Returned';
          returnReason = messages.join(' ');
        } else if (advanceBlocked || !freeLimitOk) {
          nextStatus = 'Blocked';
          blockedAt = now;
          blockReason = messages.join(' ');
        } else {
          nextStatus = 'PendingApproval';
        }
      }

      const reqId = crypto.randomUUID();
      const docId = crypto.randomUUID();
      const reqNum = `ЗП-20261110-${Math.floor(1000 + Math.random() * 8999)}`;

      db.exec('BEGIN');
      try {
        db.prepare(
          `INSERT INTO payment_requests (
            id, project_id, construction_object_id, contract_id, cost_item_id, author_user_id, number,
            amount, act_amount, advance_offset, retention_amount, payable_amount, vat_included, vat_rate, vat_amount,
            currency, exchange_rate, amount_in_byn, type, status, planned_payment_date, purpose, block_reason,
            return_or_reject_reason, has_overdue_advance_warning, idempotency_key, submitted_at, blocked_at, created_at, updated_at
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 20, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
        ).run(
          reqId, projectId, constructionObjectId, contractId, costItemId, req.user!.id, reqNum,
          reqAmount, actAmt, advOff, retAmt, payable.payableAmount, vatIncluded === false ? 0 : 1, vatInfo.vatAmount,
          curr, rate, amountInByn, reqType, nextStatus, planDate, purpose || `Оплата по документу ${dNum}`,
          blockReason, returnReason, hasOverdueAdvance ? 1 : 0, idempotencyKey || null, submittedAt, blockedAt, now, now
        );

        db.prepare(
          `INSERT INTO documents (
            id, payment_request_id, contract_id, construction_object_id, type, number, date,
            gross_amount, net_amount, vat_included, currency, advance_offset, retention_amount,
            photo_evidence_paths, is_verified_instruction53, created_at
          ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
        ).run(
          docId, reqId, contractId, constructionObjectId, dType, dNum, dDate,
          vatInfo.grossAmount, vatInfo.netAmount, vatIncluded === false ? 0 : 1, curr, advOff, retAmt,
          photoEvidencePaths || null, dType === 'ActS2' || dType === 'CertificateS3' ? 1 : 0, now
        );

        if (nextStatus === 'Blocked') {
          publishNotification('CEO', `Заявка ${reqNum} заблокирована`, blockReason || 'Требуется решение руководства', 'Critical', 'ApprovalDue', '/requests');
        } else if (nextStatus === 'PendingApproval') {
          publishNotification('FinancialManager', `Новая заявка на согласование (${reqNum})`, `К оплате: ${payable.payableAmount} ${curr}`, cashGapAmount > 0 ? 'Warning' : 'Info', cashGapAmount > 0 ? 'CashGap' : 'ApprovalDue', '/requests');
        }

        writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', reqId, 'CREATE_REQUEST', null, `${reqNum}:${nextStatus}:${payable.payableAmount} ${curr}`, purpose || null);
        db.exec('COMMIT');

        const created = db.prepare('SELECT * FROM payment_requests WHERE id = ?').get(reqId);
        res.status(201).json({
          request: created,
          checks: {
            contractAndDocumentsValid: contractDocsValid,
            freeLimitSufficient: freeLimitOk,
            freeLimit: freeLimitRes.freeLimit,
            delta: freeLimitRes.deviation,
            advanceCheckPassed: !advanceBlocked,
            hasOverdueAdvanceWarning: hasOverdueAdvance,
            hasCashGapOnPlannedDate: cashGapAmount > 0,
            cashGapAmount,
            messages,
          },
        });
      } catch (err: any) {
        db.exec('ROLLBACK');
        if (String(err.message).includes('UNIQUE')) {
          res.status(409).json({ error: 'Контроль дублей: такой документ уже зарегистрирован.', code: 'DUPLICATE_DETECTED' });
          return;
        }
        res.status(400).json({ error: err.message });
      }
    }
  );

  // Исправление возвращённой заявки (табл. 1.11: Returned -> UnderReview -> PendingApproval / Blocked)
  app.put(
    '/api/payment-requests/:id',
    requireAuth,
    requireRoles('ProjectManager', 'ProcurementSpecialist', 'FinancialManager'),
    (req: AuthedRequest, res) => {
      const existing = db.prepare('SELECT * FROM payment_requests WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
      if (!existing) {
        res.status(404).json({ error: 'Заявка не найдена.' });
        return;
      }
      if (req.body?.rowVersion !== undefined && Number(req.body.rowVersion) !== Number(existing.row_version)) {
        res.status(409).json({ error: 'Конфликт версий: заявка была изменена другим пользователем.', code: 'CONCURRENCY_CONFLICT' });
        return;
      }
      if (!['Draft', 'Returned'].includes(existing.status)) {
        res.status(400).json({ error: 'Редактирование возможно только в статусах «Черновик» или «Возвращена на доработку».' });
        return;
      }

      const amt = Number(req.body.amount ?? existing.amount);
      const actAmt = Number(req.body.actAmount ?? amt);
      const advOff = Number(req.body.advanceOffset ?? existing.advance_offset);
      const retAmt = Number(req.body.retentionAmount ?? existing.retention_amount);
      const payable = calculateActPayable(actAmt, advOff, retAmt);
      const rate = getRateToByn(existing.currency);
      const amountInByn = roundTo(payable.payableAmount * rate, 4);
      const freeLimit = getFreeLimitForObjectItem(existing.construction_object_id, existing.cost_item_id);
      const newStatus = freeLimit.freeLimit < amountInByn ? 'Blocked' : 'PendingApproval';
      const now = new Date().toISOString();

      db.prepare(
        `UPDATE payment_requests
         SET amount = ?, act_amount = ?, advance_offset = ?, retention_amount = ?, payable_amount = ?,
             amount_in_byn = ?, purpose = ?, planned_payment_date = ?, status = ?,
             block_reason = ?, return_or_reject_reason = NULL, submitted_at = ?, updated_at = ?, row_version = row_version + 1
         WHERE id = ?`
      ).run(
        amt, actAmt, advOff, retAmt, payable.payableAmount, amountInByn,
        req.body.purpose ?? existing.purpose, req.body.plannedPaymentDate ?? existing.planned_payment_date,
        newStatus, newStatus === 'Blocked' ? `Недостаточно свободного лимита (${freeLimit.freeLimit})` : null,
        now, now, existing.id
      );

      writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', existing.id, 'RESUBMIT_REQUEST', existing.status, newStatus, null);
      res.json(db.prepare('SELECT * FROM payment_requests WHERE id = ?').get(existing.id));
    }
  );

  // UC04: Согласование перерасхода руководителем организации
  app.post('/api/payment-requests/:id/approve-overrun', requireAuth, requireRoles('CEO'), (req: AuthedRequest, res) => {
    const { approve, reason, rowVersion } = req.body || {};
    const existing = db.prepare('SELECT * FROM payment_requests WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!existing) {
      res.status(404).json({ error: 'Заявка не найдена.' });
      return;
    }
    if (rowVersion !== undefined && Number(rowVersion) !== Number(existing.row_version)) {
      res.status(409).json({ error: 'Конфликт версий: заявка изменена другим пользователем. Перечитайте данные.', code: 'CONCURRENCY_CONFLICT' });
      return;
    }
    if (existing.status !== 'Blocked') {
      res.status(400).json({ error: 'Согласование перерасхода выполняется только для заблокированных заявок (UC04).' });
      return;
    }

    const now = new Date().toISOString();
    if (!approve) {
      db.prepare("UPDATE payment_requests SET status = 'Rejected', return_or_reject_reason = ?, updated_at = ?, row_version = row_version + 1 WHERE id = ?").run(
        reason || 'Отказ руководителя организации в перерасходе', now, existing.id
      );
      writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', existing.id, 'REJECT_OVERRUN', 'Blocked', 'Rejected', reason || null);
      res.json(db.prepare('SELECT * FROM payment_requests WHERE id = ?').get(existing.id));
      return;
    }

    if (!reason || !String(reason).trim()) {
      res.status(400).json({ error: 'Необходимо ввести обоснование согласования перерасхода (UC04).' });
      return;
    }

    // Шаг 5 табл. 1.10 / шаг 6 UC04: повторная проверка авансов при согласовании перерасхода
    const overdueAdvances = db
      .prepare("SELECT * FROM advances WHERE contract_id = ? AND is_deleted = 0 AND status != 'Closed' AND (status = 'Overdue' OR due_date < '2026-11-10') AND used_amount < amount")
      .all(existing.contract_id) as Array<Record<string, any>>;
    if (overdueAdvances.length > 0 && existing.type === 'Advance') {
      res.status(400).json({ error: 'Повторная проверка: по договору имеется просроченный аванс — выдача нового аванса невозможна.' });
      return;
    }

    db.prepare(
      `UPDATE payment_requests
       SET status = 'PendingApproval', is_overrun_approved = 1, overrun_justification = ?,
           has_overdue_advance_warning = ?, block_reason = NULL, updated_at = ?, row_version = row_version + 1
       WHERE id = ?`
    ).run(String(reason).trim(), overdueAdvances.length > 0 ? 1 : 0, now, existing.id);

    publishNotification('FinancialManager', `Перерасход по ${existing.number} согласован`, `Обоснование: ${reason}. Заявка переведена на согласование.`, 'Info', 'ApprovalDue', '/requests');
    writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', existing.id, 'APPROVE_OVERRUN', 'Blocked', 'PendingApproval', String(reason));
    res.json(db.prepare('SELECT * FROM payment_requests WHERE id = ?').get(existing.id));
  });

  // Согласование финансовым менеджером / руководителем (с поддержкой ЭЦП — требование 25)
  app.post('/api/payment-requests/:id/approve', requireAuth, requireRoles('FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { edsCertificateThumbprint, rowVersion } = req.body || {};
    const existing = db.prepare('SELECT * FROM payment_requests WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!existing) {
      res.status(404).json({ error: 'Заявка не найдена.' });
      return;
    }
    if (rowVersion !== undefined && Number(rowVersion) !== Number(existing.row_version)) {
      res.status(409).json({ error: 'Конфликт версий: заявка обновлена другим пользователем.', code: 'CONCURRENCY_CONFLICT' });
      return;
    }
    if (!['PendingApproval', 'ApprovedByManagement'].includes(existing.status)) {
      res.status(400).json({ error: `Нельзя согласовать заявку в статусе ${existing.status}.` });
      return;
    }
    const now = new Date().toISOString();
    const edsThumb = edsCertificateThumbprint || `BY-UCA-${crypto.randomBytes(5).toString('hex').toUpperCase()}`;

    db.prepare(
      "UPDATE payment_requests SET status = 'Approved', approved_at = ?, eds_signature_thumbprint = ?, eds_signed_at = ?, updated_at = ?, row_version = row_version + 1 WHERE id = ?"
    ).run(now, edsThumb, now, now, existing.id);

    publishNotification('Accountant', `Заявка ${existing.number} согласована к оплате`, `Сумма: ${existing.payable_amount} ${existing.currency} (ЭЦП: ${edsThumb})`, 'Info', 'ApprovalDue', '/requests');
    writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', existing.id, 'APPROVE_REQUEST', existing.status, 'Approved', `ЭЦП: ${edsThumb}`);
    res.json(db.prepare('SELECT * FROM payment_requests WHERE id = ?').get(existing.id));
  });

  app.post('/api/payment-requests/:id/return-or-reject', requireAuth, requireRoles('FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { rejectPermanently, reason, rowVersion } = req.body || {};
    const existing = db.prepare('SELECT * FROM payment_requests WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!existing) {
      res.status(404).json({ error: 'Заявка не найдена.' });
      return;
    }
    if (rowVersion !== undefined && Number(rowVersion) !== Number(existing.row_version)) {
      res.status(409).json({ error: 'Конфликт версий.', code: 'CONCURRENCY_CONFLICT' });
      return;
    }
    const nextStatus = rejectPermanently ? 'Rejected' : 'Returned';
    const now = new Date().toISOString();
    db.prepare('UPDATE payment_requests SET status = ?, return_or_reject_reason = ?, updated_at = ?, row_version = row_version + 1 WHERE id = ?').run(
      nextStatus, reason || 'Требуется уточнение документов', now, existing.id
    );
    writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', existing.id, rejectPermanently ? 'REJECT_REQUEST' : 'RETURN_REQUEST', existing.status, nextStatus, reason || null);
    res.json(db.prepare('SELECT * FROM payment_requests WHERE id = ?').get(existing.id));
  });

  // Исполнение платежа бухгалтером (переход Approved -> Executed + учёт расхода по факту)
  app.post('/api/payment-requests/:id/execute', requireAuth, requireRoles('Accountant'), (req: AuthedRequest, res) => {
    const { paymentOrderNumber, rowVersion } = req.body || {};
    const existing = db.prepare('SELECT * FROM payment_requests WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!existing) {
      res.status(404).json({ error: 'Заявка не найдена.' });
      return;
    }
    if (rowVersion !== undefined && Number(rowVersion) !== Number(existing.row_version)) {
      res.status(409).json({ error: 'Конфликт версий.', code: 'CONCURRENCY_CONFLICT' });
      return;
    }
    if (existing.status !== 'Approved') {
      res.status(400).json({ error: 'Исполнение платежа возможно только из состояния «Согласована» (инвариант 3).' });
      return;
    }

    const now = new Date().toISOString();
    const payId = crypto.randomUUID();
    const orderNum = paymentOrderNumber || `ПП-${now.slice(0, 10).replace(/-/g, '')}-${existing.number.slice(-4)}`;
    const bankOpId = `OP-${Date.now()}-${existing.number}`;
    const rate = getRateToByn(existing.currency);
    const execByn = roundTo(Number(existing.payable_amount) * rate, 4);
    const fxDiff = roundTo(execByn - Number(existing.amount_in_byn), 4);

    db.exec('BEGIN');
    try {
      db.prepare("UPDATE payment_requests SET status = 'Executed', executed_at = ?, updated_at = ?, row_version = row_version + 1 WHERE id = ?").run(now, now, existing.id);
      db.prepare(
        `INSERT INTO payments (id, payment_request_id, bank_operation_id, payment_date, amount, currency, exchange_rate, amount_in_byn, fx_difference, status, payment_order_number, created_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'Executed', ?, ?)`
      ).run(payId, existing.id, bankOpId, now.slice(0, 10), existing.payable_amount, existing.currency, rate, execByn, fxDiff, orderNum, now);

      // Зачёт аванса при текущем платеже по акту С-2
      if (Number(existing.advance_offset) > 0) {
        const adv = db.prepare("SELECT * FROM advances WHERE contract_id = ? AND status != 'Closed' AND is_deleted = 0 ORDER BY due_date LIMIT 1").get(existing.contract_id) as Record<string, any> | undefined;
        if (adv) {
          const add = Math.min(Number(adv.amount) - Number(adv.used_amount), Number(existing.advance_offset));
          if (add > 0) {
            const newUsed = roundTo(Number(adv.used_amount) + add, 4);
            db.prepare('UPDATE advances SET used_amount = ?, status = ? WHERE id = ?').run(newUsed, newUsed >= Number(adv.amount) ? 'Closed' : adv.status, adv.id);
          }
        }
      }

      // Если новый аванс — регистрируем выданный аванс на 60 дней
      if (existing.type === 'Advance') {
        const due = new Date(Date.now() + 60 * 86400000).toISOString().slice(0, 10);
        db.prepare(
          'INSERT INTO advances (id, contract_id, construction_object_id, cost_item_id, amount, used_amount, issued_date, due_date, status, funding_source) VALUES (?, ?, ?, ?, ?, 0, ?, ?, ?, ?)'
        ).run(crypto.randomUUID(), existing.contract_id, existing.construction_object_id, existing.cost_item_id, execByn, now.slice(0, 10), due, 'Active', 'Собственные средства');
      }

      writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', existing.id, 'EXECUTE_PAYMENT', 'Approved', 'Executed', `ПП ${orderNum}`);
      db.exec('COMMIT');
      res.json({ paymentId: payId, paymentOrderNumber: orderNum, bankOperationId: bankOpId });
    } catch (err: any) {
      db.exec('ROLLBACK');
      res.status(400).json({ error: err.message });
    }
  });

  // Автоматическое отклонение заблокированных заявок старше 30 дней (табл. 1.11, UC04 A2)
  app.post('/api/payment-requests/expire-stale', requireAuth, requireRoles('Admin', 'FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const asOf = req.body?.asOfDate ? new Date(req.body.asOfDate) : new Date();
    const blocked = db.prepare("SELECT * FROM payment_requests WHERE status = 'Blocked' AND blocked_at IS NOT NULL AND is_deleted = 0").all() as Array<Record<string, any>>;
    let expired = 0;
    for (const r of blocked) {
      const diffDays = (asOf.getTime() - new Date(r.blocked_at).getTime()) / 86400000;
      if (diffDays >= 30) {
        db.prepare("UPDATE payment_requests SET status = 'Rejected', return_or_reject_reason = ?, updated_at = ?, row_version = row_version + 1 WHERE id = ?").run(
          'Истёк срок согласования перерасхода (30 календарных дней)', asOf.toISOString(), r.id
        );
        writeAudit(req.user!.id, req.user!.login, 'PaymentRequest', r.id, 'AUTO_EXPIRE_BLOCKED', 'Blocked', 'Rejected', '30 days elapsed');
        expired++;
      }
    }
    res.json({ expiredCount: expired });
  });

  // =========================================================================
  // АКТЫ С-2, СПРАВКИ С-3 И ФОТОФИКСАЦИЯ (требование 27, UC15, UC16)
  // =========================================================================
  app.get('/api/acts', requireAuth, (_req, res) => {
    const acts = db
      .prepare(
        `SELECT d.*, c.number AS contract_number, c.counterparty, co.name AS object_name
         FROM documents d
         LEFT JOIN contracts c ON c.id = d.contract_id
         LEFT JOIN construction_objects co ON co.id = d.construction_object_id
         WHERE d.is_deleted = 0 AND d.type IN ('ActS2', 'CertificateS3')
         ORDER BY d.date DESC`
      )
      .all();
    res.json(acts);
  });

  app.post('/api/acts', requireAuth, requireRoles('ProjectManager', 'SiteWorker', 'Accountant', 'ProcurementSpecialist'), (req: AuthedRequest, res) => {
    const { contractId, constructionObjectId, type, number, date, grossAmount, advanceOffset, retentionPercent, actWorkDescription, contractorSigner, customerSigner, photoEvidencePaths } = req.body || {};
    if (!contractId || !constructionObjectId || !number || !date || !grossAmount || !actWorkDescription) {
      res.status(400).json({ error: 'Инструкция № 53 и Постановление № 29: заполните договор, объект, номер, дату, сумму и описание работ.' });
      return;
    }
    const gross = Number(grossAmount);
    const adv = Number(advanceOffset || 0);
    const vatInfo = normalizeVat(gross, true, 20);
    const ret = roundTo(vatInfo.netAmount * (Number(retentionPercent ?? 5) / 100), 2);
    const payable = calculateActPayable(gross, adv, ret);
    const id = crypto.randomUUID();
    const now = new Date().toISOString();

    try {
      db.prepare(
        `INSERT INTO documents (
          id, contract_id, construction_object_id, type, number, date, gross_amount, net_amount, vat_included, currency,
          advance_offset, retention_amount, photo_evidence_paths, act_work_description, contractor_signer, customer_signer,
          is_verified_instruction53, created_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, 'BYN', ?, ?, ?, ?, ?, ?, 1, ?)`
      ).run(id, contractId, constructionObjectId, type === 'CertificateS3' ? 'CertificateS3' : 'ActS2', number.trim(), date.slice(0, 10), gross, vatInfo.netAmount, adv, ret, photoEvidencePaths || null, actWorkDescription, contractorSigner || 'Подрядчик', customerSigner || req.user!.fullName, now);

      writeAudit(req.user!.id, req.user!.login, 'Document', id, 'CREATE_ACT_S2_S3', null, `${number}:${payable.payableAmount}`, actWorkDescription);
      res.status(201).json({ id, payable });
    } catch (err: any) {
      if (String(err.message).includes('UNIQUE')) {
        res.status(409).json({ error: `Акт ${number} от ${date} уже зарегистрирован (контроль дублей).` });
        return;
      }
      res.status(400).json({ error: err.message });
    }
  });

  // =========================================================================
  // CASH CALENDAR 90 DAYS (требование 9, формулы 1.8–1.9)
  // =========================================================================
  app.get('/api/cash-calendar', requireAuth, (req, res) => {
    const projectId = (req.query.projectId as string | undefined) || null;
    const horizon = Number(req.query.horizon || 90);
    res.json(getProjectCashCalendar(db, projectId, '2026-11-10', horizon));
  });

  app.post('/api/cash-calendar/reschedule', requireAuth, requireRoles('FinancialManager', 'CEO'), (req: AuthedRequest, res) => {
    const { itemType, itemId, newDate } = req.body || {};
    if (!itemId || !newDate) {
      res.status(400).json({ error: 'Укажите элемент и новую дату платежа.' });
      return;
    }
    if (itemType === 'request') {
      db.prepare('UPDATE payment_requests SET planned_payment_date = ?, row_version = row_version + 1 WHERE id = ?').run(newDate.slice(0, 10), itemId);
    } else {
      db.prepare('UPDATE contract_schedule_items SET planned_date = ?, row_version = row_version + 1 WHERE id = ?').run(newDate.slice(0, 10), itemId);
    }
    writeAudit(req.user!.id, req.user!.login, 'CashCalendar', itemId, 'RESCHEDULE_PAYMENT', null, newDate, itemType || 'schedule');
    res.json({ updated: true, newDate });
  });

  // =========================================================================
  // RISKS, P80 RESERVE & SENSITIVITY (требования 13, 14, 17)
  // =========================================================================
  app.get('/api/risks', requireAuth, (req, res) => {
    const projectId = (req.query.projectId as string | undefined) || '30000000-0000-0000-0000-000000000001';
    const risks = db
      .prepare('SELECT * FROM risks WHERE project_id = ? AND is_deleted = 0 ORDER BY emv DESC, integral_score DESC')
      .all(projectId) as Array<Record<string, any>>;

    const summary = calculateRiskPortfolio(
      risks.map((r) => ({
        probability: r.probability,
        monetaryImpact: r.monetary_impact,
        residualProbability: r.residual_probability,
        integralScore: r.integral_score,
      }))
    );

    const assessments = db
      .prepare('SELECT ra.*, u.full_name AS assessor_name FROM risk_assessments ra JOIN users u ON u.id = ra.assessed_by_user_id ORDER BY ra.version DESC')
      .all() as Array<Record<string, any>>;

    const sensitivity = calculateSensitivityFactors();
    res.json({
      risks: risks.map((r) => ({
        ...r,
        assessments: assessments.filter((a) => a.risk_id === r.id),
      })),
      summary,
      sensitivity,
    });
  });

  app.post('/api/risks', requireAuth, requireRoles('RiskSpecialist', 'ProjectManager', 'CEO'), (req: AuthedRequest, res) => {
    const { projectId, code, event, cause, consequence, probability, monetaryImpact, ownerRole, mitigation, earlyIndicator, residualProbability } = req.body || {};
    if (!projectId || !code || !event || probability === undefined || monetaryImpact === undefined) {
      res.status(400).json({ error: 'Заполните проект, код, событие, вероятность и денежную оценку риска.' });
      return;
    }
    const p = Number(probability);
    const c = Number(monetaryImpact);
    const pScore = getProbabilityScore(p);
    const iScore = getImpactScore(c);
    const integral = pScore * iScore;
    const emv = roundTo(p * c, 4);
    const resP = residualProbability !== undefined && residualProbability !== null ? Number(residualProbability) : null;
    const resEmv = resP !== null ? roundTo(resP * c, 4) : null;
    const id = crypto.randomUUID();
    const status = integral >= 10 ? 'Treating' : 'Monitoring';

    try {
      db.prepare(
        `INSERT INTO risks (
          id, project_id, code, event, cause, consequence, probability, monetary_impact,
          probability_score, impact_score, integral_score, emv, owner_role, mitigation,
          early_indicator, residual_probability, residual_emv, status
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).run(id, projectId, code.trim(), event.trim(), cause || '', consequence || '', p, c, pScore, iScore, integral, emv, ownerRole || 'Руководитель проекта', mitigation || 'Плановый контроль', earlyIndicator || 'Отклонение от плана', resP, resEmv, status);

      writeAudit(req.user!.id, req.user!.login, 'Risk', id, 'CREATE_RISK', null, `${code}:EMV=${emv}`, event);
      res.status(201).json(db.prepare('SELECT * FROM risks WHERE id = ?').get(id));
    } catch (err: any) {
      res.status(400).json({ error: err.message });
    }
  });

  app.put('/api/risks/:id/assessment', requireAuth, requireRoles('RiskSpecialist', 'ProjectManager', 'CEO'), (req: AuthedRequest, res) => {
    const { probability, monetaryImpact, residualProbability, status, triggerFired, comment, rowVersion } = req.body || {};
    const existing = db.prepare('SELECT * FROM risks WHERE id = ? AND is_deleted = 0').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!existing) {
      res.status(404).json({ error: 'Риск не найден.' });
      return;
    }
    if (rowVersion !== undefined && Number(rowVersion) !== Number(existing.row_version)) {
      res.status(409).json({ error: 'Конфликт версий оценки риска.', code: 'CONCURRENCY_CONFLICT' });
      return;
    }

    const p = Number(probability ?? existing.probability);
    const c = Number(monetaryImpact ?? existing.monetary_impact);
    const pScore = getProbabilityScore(p);
    const iScore = getImpactScore(c);
    const integral = pScore * iScore;
    const emv = roundTo(p * c, 4);
    const resP = residualProbability !== undefined && residualProbability !== null ? Number(residualProbability) : existing.residual_probability;
    const resEmv = resP !== null ? roundTo(resP * c, 4) : null;
    const nextVersion = Number(existing.current_assessment_version) + 1;
    const nextStatus = status || (triggerFired ? 'Realized' : integral >= 10 ? 'Treating' : 'Monitoring');

    db.exec('BEGIN');
    try {
      db.prepare(
        `UPDATE risks SET probability = ?, monetary_impact = ?, probability_score = ?, impact_score = ?,
         integral_score = ?, emv = ?, residual_probability = ?, residual_emv = ?, status = ?,
         trigger_fired = ?, current_assessment_version = ?, row_version = row_version + 1
         WHERE id = ?`
      ).run(p, c, pScore, iScore, integral, emv, resP, resEmv, nextStatus, triggerFired ? 1 : Number(existing.trigger_fired), nextVersion, existing.id);

      const allRisks = db.prepare('SELECT probability, monetary_impact, residual_probability, integral_score FROM risks WHERE project_id = ? AND is_deleted = 0').all(existing.project_id) as Array<any>;
      const summary = calculateRiskPortfolio(allRisks.map((r) => ({
        probability: r.probability, monetaryImpact: r.monetary_impact, residualProbability: r.residual_probability, integralScore: r.integral_score,
      })));

      db.prepare(
        `INSERT INTO risk_assessments (id, risk_id, version, probability, monetary_impact, emv, residual_probability, residual_emv, project_reserve_p80_snapshot, assessed_at, assessed_by_user_id, comment)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`
      ).run(crypto.randomUUID(), existing.id, nextVersion, p, c, emv, resP, resEmv, summary.reserveP80, new Date().toISOString(), req.user!.id, comment || 'Переоценка риска');

      if (integral >= 10 || triggerFired) {
        publishNotification('CEO', `Риск ${existing.code} (R=${integral})`, `${existing.event}: EMV=${emv} тыс. р., резерв P80=${summary.reserveP80} тыс. р.`, integral >= 17 || triggerFired ? 'Critical' : 'Warning', 'RiskTrigger', '/risks');
      }

      writeAudit(req.user!.id, req.user!.login, 'Risk', existing.id, 'UPDATE_RISK_ASSESSMENT', `v${existing.current_assessment_version}:EMV=${existing.emv}`, `v${nextVersion}:EMV=${emv}`, comment || null);
      db.exec('COMMIT');
      res.json({ risk: db.prepare('SELECT * FROM risks WHERE id = ?').get(existing.id), summary });
    } catch (err: any) {
      db.exec('ROLLBACK');
      res.status(400).json({ error: err.message });
    }
  });

  // =========================================================================
  // INTEGRATIONS: BANK MT940/CSV, 1C ACCOUNTING, ESTIMATE XML/CSV (треб. 20–22)
  // =========================================================================
  app.post('/api/integrations/bank-statement', requireAuth, requireRoles('Accountant', 'Admin'), (req: AuthedRequest, res) => {
    const { fileName, content } = req.body || {};
    if (!content) {
      res.status(400).json({ error: 'Передайте содержимое банковской выписки (MT940 или CSV).' });
      return;
    }

    const ops = parseBankStatement(String(content), String(fileName || 'statement.csv'));
    let matchedCount = 0;
    let duplicateSkippedCount = 0;
    const unmatchedOperations: any[] = [];
    const warnings: string[] = [];

    db.exec('BEGIN');
    try {
      for (const op of ops) {
        const dup = db.prepare('SELECT id FROM payments WHERE bank_operation_id = ?').get(op.bankOperationId);
        if (dup) {
          duplicateSkippedCount++;
          warnings.push(`Операция ${op.bankOperationId} уже загружена ранее — повторно пропущена (контроль дублей).`);
          continue;
        }

        const matchedReq = db
          .prepare(
            `SELECT * FROM payment_requests
             WHERE is_deleted = 0
               AND ((? IS NOT NULL AND UPPER(number) = UPPER(?))
                    OR (status = 'Executed' AND ABS(payable_amount - ?) < 0.01))
             LIMIT 1`
          )
          .get(op.paymentRequestNumber, op.paymentRequestNumber, op.amount) as Record<string, any> | undefined;

        if (!matchedReq) {
          unmatchedOperations.push(op);
          warnings.push(`Операция ${op.bankOperationId} (${op.amount} ${op.currency}) не сопоставлена с заявкой.`);
          continue;
        }

        const now = new Date().toISOString();
        if (['Approved', 'Executed'].includes(matchedReq.status)) {
          db.prepare("UPDATE payment_requests SET status = 'Closed', closed_at = ?, updated_at = ?, row_version = row_version + 1 WHERE id = ?").run(now, now, matchedReq.id);
        }

        const rate = getRateToByn(op.currency);
        const amountInByn = roundTo(op.amount * rate, 4);

        db.prepare(
          `INSERT INTO payments (id, payment_request_id, bank_operation_id, payment_date, amount, currency, exchange_rate, amount_in_byn, fx_difference, status, payment_order_number, created_at)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, 0, 'Reconciled', ?, ?)`
        ).run(crypto.randomUUID(), matchedReq.id, op.bankOperationId, op.operationDate, op.amount, op.currency, rate, amountInByn, op.paymentOrderNumber || op.bankOperationId, now);

        const obl = db
          .prepare("SELECT * FROM obligations WHERE contract_id = ? AND construction_object_id = ? AND cost_item_id = ? AND status != 'Closed' AND is_deleted = 0 LIMIT 1")
          .get(matchedReq.contract_id, matchedReq.construction_object_id, matchedReq.cost_item_id) as Record<string, any> | undefined;

        if (obl) {
          const toPay = Math.min(Number(obl.gross_amount) - Number(obl.paid_amount), amountInByn);
          if (toPay > 0) {
            const newPaid = roundTo(Number(obl.paid_amount) + toPay, 4);
            const newOblStatus = newPaid >= Number(obl.gross_amount) ? 'Closed' : 'PartiallyPaid';
            db.prepare('UPDATE obligations SET paid_amount = ?, status = ?, row_version = row_version + 1 WHERE id = ?').run(newPaid, newOblStatus, obl.id);
          }
        }
        matchedCount++;
      }

      writeAudit(req.user!.id, req.user!.login, 'BankStatement', fileName || 'upload', 'IMPORT_BANK_STATEMENT', null, `Matched=${matchedCount},Dup=${duplicateSkippedCount},Unmatched=${unmatchedOperations.length}`, null);
      db.exec('COMMIT');

      res.json({
        totalOperations: ops.length,
        matchedCount,
        duplicateSkippedCount,
        unmatchedCount: unmatchedOperations.length,
        warnings,
        unmatchedOperations,
      });
    } catch (err: any) {
      db.exec('ROLLBACK');
      res.status(400).json({ error: err.message });
    }
  });

  app.get('/api/integrations/accounting-export', requireAuth, requireRoles('Accountant', 'FinancialManager', 'Admin'), (_req, res) => {
    const payments = db
      .prepare(
        `SELECT p.*, pr.number AS request_number, pr.cost_item_id, co.name AS object_name, ci.code AS cost_item_code
         FROM payments p
         JOIN payment_requests pr ON pr.id = p.payment_request_id
         JOIN construction_objects co ON co.id = pr.construction_object_id
         JOIN cost_items ci ON ci.id = pr.cost_item_id
         WHERE p.is_deleted = 0 ORDER BY p.payment_date DESC`
      )
      .all() as Array<Record<string, any>>;

    const lines = ['BankOperationId;PaymentDate;RequestNumber;ObjectName;CostItemCode;Amount;Currency;AmountInByn;Status'];
    for (const p of payments) {
      lines.push([p.bank_operation_id, p.payment_date, p.request_number, p.object_name, p.cost_item_code, p.amount, p.currency, p.amount_in_byn, p.status].join(';'));
    }
    res.setHeader('Content-Type', 'text/csv; charset=utf-8');
    res.setHeader('Content-Disposition', 'attachment; filename="1c_accounting_export.csv"');
    res.send('\uFEFF' + lines.join('\n'));
  });

  app.post('/api/integrations/estimate-import', requireAuth, requireRoles('FinancialManager', 'ProjectManager', 'ProcurementSpecialist', 'Admin'), (req, res) => {
    const { fileName, content } = req.body || {};
    if (!content) {
      res.status(400).json({ error: 'Передайте содержимое сметы (CSV или XML).' });
      return;
    }
    const catalog = db.prepare('SELECT id, code, name, category FROM cost_items WHERE is_deleted = 0').all() as Array<any>;
    res.json(importEstimateContent(String(content), String(fileName || 'estimate.csv'), catalog));
  });

  // =========================================================================
  // REPORTS (5 отчётов: предпросмотр JSON + экспорт XLSX и PDF — требование 18)
  // =========================================================================
  app.get('/api/reports/:type', requireAuth, (req, res) => {
    const projectId = (req.query.projectId as string | undefined) || null;
    res.json(buildReportModel(db, String(req.params.type), projectId));
  });

  app.get('/api/reports/:type/export', requireAuth, async (req, res) => {
    const format = String(req.query.format || 'xlsx').toLowerCase();
    const projectId = (req.query.projectId as string | undefined) || null;
    const model = buildReportModel(db, String(req.params.type), projectId);

    if (format === 'pdf') {
      const buf = await generatePdfReportBuffer(model);
      res.setHeader('Content-Type', 'application/pdf');
      res.setHeader('Content-Disposition', `attachment; filename="report_${String(req.params.type)}.pdf"`);
      res.send(buf);
      return;
    }

    const buf = await generateExcelReportBuffer(model);
    res.setHeader('Content-Type', 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
    res.setHeader('Content-Disposition', `attachment; filename="report_${String(req.params.type)}.xlsx"`);
    res.send(buf);
  });

  // =========================================================================
  // ADMIN, NORM RULES, COST ITEMS, AUDIT & NOTIFICATIONS (треб. 15, 16, 19)
  // =========================================================================
  app.get('/api/admin/roles', requireAuth, (_req, res) => {
    res.json(db.prepare('SELECT * FROM roles ORDER BY code').all());
  });

  app.get('/api/admin/users', requireAuth, requireRoles('Admin', 'CEO'), (_req, res) => {
    res.json(
      db
        .prepare(
          `SELECT u.id, u.login, u.full_name, u.email, u.is_active, u.failed_login_count, u.locked_until,
                  u.two_factor_enabled, u.personal_data_consent, u.consent_date, u.created_at,
                  r.id AS role_id, r.code AS role_code, r.name AS role_name, r.client_type
           FROM users u JOIN roles r ON r.id = u.role_id ORDER BY u.login`
        )
        .all()
    );
  });

  app.post('/api/admin/users', requireAuth, requireRoles('Admin'), (req: AuthedRequest, res) => {
    const { login, password, fullName, email, roleId, twoFactorEnabled } = req.body || {};
    if (!login || !password || !fullName || !roleId) {
      res.status(400).json({ error: 'Заполните логин, пароль, ФИО и роль.' });
      return;
    }
    if (String(password).length < 12) {
      res.status(400).json({ error: 'Пароль должен содержать не менее 12 символов (НФТ-7).' });
      return;
    }
    const id = crypto.randomUUID();
    const now = new Date().toISOString();
    try {
      db.prepare(
        'INSERT INTO users (id, role_id, login, password_hash, full_name, email, two_factor_enabled, two_factor_secret, personal_data_consent, consent_date, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)'
      ).run(id, roleId, String(login).trim(), hashPassword(String(password)), String(fullName).trim(), email || `${login}@stroyfin.by`, twoFactorEnabled ? 1 : 0, twoFactorEnabled ? '246810' : null, now, now);

      writeAudit(req.user!.id, req.user!.login, 'User', id, 'CREATE_USER', null, `${login}:${fullName}`, null);
      res.status(201).json({ id });
    } catch (err: any) {
      res.status(400).json({ error: err.message });
    }
  });

  app.put('/api/admin/users/:id/toggle-active', requireAuth, requireRoles('Admin'), (req: AuthedRequest, res) => {
    const user = db.prepare('SELECT * FROM users WHERE id = ?').get(String(req.params.id)) as Record<string, any> | undefined;
    if (!user) {
      res.status(404).json({ error: 'Пользователь не найден.' });
      return;
    }
    const nextActive = Number(user.is_active) === 1 ? 0 : 1;
    db.prepare('UPDATE users SET is_active = ?, failed_login_count = 0, locked_until = NULL WHERE id = ?').run(nextActive, user.id);
    writeAudit(req.user!.id, req.user!.login, 'User', user.id, nextActive ? 'UNBLOCK_USER' : 'BLOCK_USER', String(user.is_active), String(nextActive), user.login);
    res.json({ id: user.id, is_active: nextActive });
  });

  app.get('/api/admin/norm-rules', requireAuth, (req, res) => {
    const asOfDate = req.query.asOfDate as string | undefined;
    if (asOfDate) {
      res.json(
        db
          .prepare('SELECT * FROM norm_rules WHERE valid_from <= ? AND (valid_to IS NULL OR valid_to >= ?) ORDER BY code, revision DESC')
          .all(asOfDate, asOfDate)
      );
      return;
    }
    res.json(db.prepare('SELECT * FROM norm_rules ORDER BY code, revision DESC').all());
  });

  app.post('/api/admin/norm-rules', requireAuth, requireRoles('Admin', 'FinancialManager'), (req: AuthedRequest, res) => {
    const { code, name, documentSource, valueText, numericValue, validFrom, validTo, responsibleRole } = req.body || {};
    if (!code || !name || !valueText || !validFrom) {
      res.status(400).json({ error: 'Укажите код, название, значение и дату вступления в силу.' });
      return;
    }
    const prev = db.prepare('SELECT MAX(revision) AS max_rev FROM norm_rules WHERE code = ?').get(code) as { max_rev: number | null };
    const rev = (prev?.max_rev || 0) + 1;
    const id = crypto.randomUUID();
    db.prepare(
      'INSERT INTO norm_rules (id, code, name, document_source, value_text, numeric_value, valid_from, valid_to, responsible_role, revision) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
    ).run(id, code.trim(), name.trim(), documentSource || 'Локальный регламент', valueText.trim(), numericValue ?? null, validFrom.slice(0, 10), validTo ? validTo.slice(0, 10) : null, responsibleRole || req.user!.roleName, rev);

    writeAudit(req.user!.id, req.user!.login, 'NormRule', id, 'ADD_NORM_RULE_REVISION', null, `${code} rev.${rev}: ${valueText}`, documentSource || null);
    res.status(201).json({ id, revision: rev });
  });

  app.get('/api/admin/cost-items', requireAuth, (_req, res) => {
    res.json(db.prepare('SELECT * FROM cost_items WHERE is_deleted = 0 ORDER BY code').all());
  });

  app.get('/api/admin/currency-rates', requireAuth, (_req, res) => {
    res.json(db.prepare('SELECT * FROM currency_rates ORDER BY rate_date DESC, currency').all());
  });

  app.get('/api/audit-logs', requireAuth, requireRoles('Admin', 'CEO', 'FinancialManager'), (req, res) => {
    const objectFilter = req.query.objectName as string | undefined;
    const actionFilter = req.query.action as string | undefined;
    let sql = 'SELECT * FROM audit_logs WHERE 1=1';
    const params: any[] = [];
    if (objectFilter) {
      sql += ' AND object_name = ?';
      params.push(objectFilter);
    }
    if (actionFilter) {
      sql += ' AND action LIKE ?';
      params.push(`%${actionFilter}%`);
    }
    sql += ' ORDER BY id DESC LIMIT 200';
    res.json(db.prepare(sql).all(...params));
  });

  app.get('/api/notifications', requireAuth, (_req, res) => {
    res.json(db.prepare('SELECT * FROM notifications ORDER BY is_read ASC, created_at DESC LIMIT 50').all());
  });

  app.post('/api/notifications/:id/read', requireAuth, (req, res) => {
    db.prepare('UPDATE notifications SET is_read = 1 WHERE id = ?').run(String(req.params.id));
    res.json({ ok: true });
  });

  app.post('/api/notifications/read-all', requireAuth, (_req, res) => {
    db.prepare('UPDATE notifications SET is_read = 1').run();
    res.json({ ok: true });
  });

  return app;
}
