import { createRequire } from 'node:module';
import type { DatabaseSync as DatabaseSyncType } from 'node:sqlite';
import fs from 'node:fs';
import path from 'node:path';
import { hashPassword } from '../security/auth.js';
import { getImpactScore, getProbabilityScore, roundTo } from '../domain/calculators.js';

const require = createRequire(import.meta.url);
const { DatabaseSync } = require('node:sqlite') as typeof import('node:sqlite');

export const IDS = {
  org: '10000000-0000-0000-0000-000000000001',
  portfolio: '20000000-0000-0000-0000-000000000001',
  project1: '30000000-0000-0000-0000-000000000001',
  project2: '30000000-0000-0000-0000-000000000002',
  obj1: '40000000-0000-0000-0000-000000000001',
  obj2: '40000000-0000-0000-0000-000000000002',
  obj3: '40000000-0000-0000-0000-000000000003',
  itemMat: '50000000-0000-0000-0000-000000000001',
  itemSub: '50000000-0000-0000-0000-000000000002',
  itemEq: '50000000-0000-0000-0000-000000000003',
  itemOvr: '50000000-0000-0000-0000-000000000004',
  roleCeo: '60000000-0000-0000-0000-000000000001',
  roleFin: '60000000-0000-0000-0000-000000000002',
  roleAcc: '60000000-0000-0000-0000-000000000003',
  roleProc: '60000000-0000-0000-0000-000000000004',
  rolePm: '60000000-0000-0000-0000-000000000005',
  roleRisk: '60000000-0000-0000-0000-000000000006',
  roleAdmin: '60000000-0000-0000-0000-000000000007',
  roleSite: '60000000-0000-0000-0000-000000000008',
  userCeo: '70000000-0000-0000-0000-000000000001',
  userFin: '70000000-0000-0000-0000-000000000002',
  userAcc: '70000000-0000-0000-0000-000000000003',
  userProc: '70000000-0000-0000-0000-000000000004',
  userPm: '70000000-0000-0000-0000-000000000005',
  userRisk: '70000000-0000-0000-0000-000000000006',
  userAdmin: '70000000-0000-0000-0000-000000000007',
  userSite: '70000000-0000-0000-0000-000000000008',
  bv1: '80000000-0000-0000-0000-000000000001',
  bv2: '80000000-0000-0000-0000-000000000002',
  contractCust: '90000000-0000-0000-0000-000000000001',
  contractMat: '90000000-0000-0000-0000-000000000002',
  contractSub: '90000000-0000-0000-0000-000000000003',
  contractDdu: '90000000-0000-0000-0000-000000000004',
};

export function createDatabase(dbPath: string): DatabaseSyncType {
  if (dbPath !== ':memory:') {
    fs.mkdirSync(path.dirname(dbPath), { recursive: true });
  }
  const db = new DatabaseSync(dbPath);
  db.exec('PRAGMA foreign_keys = ON;');
  initSchema(db);
  seedIfEmpty(db);
  return db;
}

function initSchema(db: DatabaseSyncType) {
  db.exec(`
    CREATE TABLE IF NOT EXISTS roles (
      id TEXT PRIMARY KEY,
      code TEXT NOT NULL UNIQUE,
      name TEXT NOT NULL,
      client_type TEXT NOT NULL CHECK (client_type IN ('WPF', 'Web', 'Both')),
      description TEXT,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS users (
      id TEXT PRIMARY KEY,
      role_id TEXT NOT NULL REFERENCES roles(id),
      login TEXT NOT NULL UNIQUE,
      password_hash TEXT NOT NULL,
      full_name TEXT NOT NULL,
      email TEXT NOT NULL,
      is_active INTEGER NOT NULL DEFAULT 1,
      failed_login_count INTEGER NOT NULL DEFAULT 0,
      locked_until TEXT,
      two_factor_enabled INTEGER NOT NULL DEFAULT 0,
      two_factor_secret TEXT,
      personal_data_consent INTEGER NOT NULL DEFAULT 1,
      consent_date TEXT,
      created_at TEXT NOT NULL,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS organizations (
      id TEXT PRIMARY KEY,
      name TEXT NOT NULL,
      unp TEXT NOT NULL UNIQUE,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS portfolios (
      id TEXT PRIMARY KEY,
      organization_id TEXT NOT NULL REFERENCES organizations(id),
      name TEXT NOT NULL,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS projects (
      id TEXT PRIMARY KEY,
      portfolio_id TEXT NOT NULL REFERENCES portfolios(id),
      code TEXT NOT NULL,
      name TEXT NOT NULL,
      contract_value REAL NOT NULL CHECK (contract_value >= 0),
      currency TEXT NOT NULL DEFAULT 'BYN',
      initial_cash REAL NOT NULL DEFAULT 0 CHECK (initial_cash >= 0),
      min_cash_balance REAL NOT NULL DEFAULT 0 CHECK (min_cash_balance >= 0),
      planned_start_date TEXT NOT NULL,
      planned_end_date TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      UNIQUE(portfolio_id, code)
    );

    CREATE TABLE IF NOT EXISTS construction_objects (
      id TEXT PRIMARY KEY,
      project_id TEXT NOT NULL REFERENCES projects(id),
      queue_name TEXT NOT NULL,
      name TEXT NOT NULL,
      address TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS work_stages (
      id TEXT PRIMARY KEY,
      construction_object_id TEXT NOT NULL REFERENCES construction_objects(id),
      name TEXT NOT NULL,
      planned_start_date TEXT NOT NULL,
      planned_end_date TEXT NOT NULL,
      planned_value_pv REAL NOT NULL DEFAULT 0,
      earned_value_ev REAL NOT NULL DEFAULT 0,
      actual_cost_ac REAL NOT NULL DEFAULT 0,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS cost_items (
      id TEXT PRIMARY KEY,
      code TEXT NOT NULL UNIQUE,
      name TEXT NOT NULL,
      category TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS budget_versions (
      id TEXT PRIMARY KEY,
      project_id TEXT NOT NULL REFERENCES projects(id),
      version_number INTEGER NOT NULL,
      status TEXT NOT NULL CHECK (status IN ('Draft', 'Approved', 'Archived')),
      approved_at TEXT,
      approved_by_user_id TEXT REFERENCES users(id),
      comment TEXT,
      created_at TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      UNIQUE(project_id, version_number)
    );

    CREATE TABLE IF NOT EXISTS budget_limits (
      id TEXT PRIMARY KEY,
      budget_version_id TEXT NOT NULL REFERENCES budget_versions(id),
      construction_object_id TEXT NOT NULL REFERENCES construction_objects(id),
      work_stage_id TEXT REFERENCES work_stages(id),
      cost_item_id TEXT NOT NULL REFERENCES cost_items(id),
      period_start TEXT NOT NULL,
      period_end TEXT NOT NULL,
      amount REAL NOT NULL CHECK (amount >= 0),
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS contracts (
      id TEXT PRIMARY KEY,
      project_id TEXT NOT NULL REFERENCES projects(id),
      number TEXT NOT NULL,
      counterparty TEXT NOT NULL,
      counterparty_unp TEXT NOT NULL,
      direction TEXT NOT NULL CHECK (direction IN ('CustomerIncoming', 'SupplierSubcontractorOutgoing')),
      total_amount REAL NOT NULL CHECK (total_amount >= 0),
      vat_included INTEGER NOT NULL DEFAULT 1,
      vat_rate REAL NOT NULL DEFAULT 20,
      currency TEXT NOT NULL DEFAULT 'BYN',
      start_date TEXT NOT NULL,
      end_date TEXT NOT NULL,
      retention_percent REAL NOT NULL DEFAULT 5,
      penalty_rate_per_day REAL NOT NULL DEFAULT 0.1,
      max_penalty_percent REAL NOT NULL DEFAULT 10,
      specification_notes TEXT,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      UNIQUE(project_id, number)
    );

    CREATE TABLE IF NOT EXISTS contract_supplements (
      id TEXT PRIMARY KEY,
      contract_id TEXT NOT NULL REFERENCES contracts(id),
      number TEXT NOT NULL,
      amount_delta REAL NOT NULL,
      signed_date TEXT NOT NULL,
      reason TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      UNIQUE(contract_id, number)
    );

    CREATE TABLE IF NOT EXISTS contract_schedule_items (
      id TEXT PRIMARY KEY,
      contract_id TEXT NOT NULL REFERENCES contracts(id),
      planned_date TEXT NOT NULL,
      amount REAL NOT NULL CHECK (amount >= 0),
      is_incoming INTEGER NOT NULL,
      is_paid INTEGER NOT NULL DEFAULT 0,
      description TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS obligations (
      id TEXT PRIMARY KEY,
      contract_id TEXT NOT NULL REFERENCES contracts(id),
      construction_object_id TEXT NOT NULL REFERENCES construction_objects(id),
      cost_item_id TEXT NOT NULL REFERENCES cost_items(id),
      gross_amount REAL NOT NULL CHECK (gross_amount >= 0),
      paid_amount REAL NOT NULL DEFAULT 0 CHECK (paid_amount >= 0),
      status TEXT NOT NULL CHECK (status IN ('Open', 'PartiallyPaid', 'Closed')),
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      CHECK (paid_amount <= gross_amount)
    );

    CREATE TABLE IF NOT EXISTS advances (
      id TEXT PRIMARY KEY,
      contract_id TEXT NOT NULL REFERENCES contracts(id),
      construction_object_id TEXT NOT NULL REFERENCES construction_objects(id),
      cost_item_id TEXT NOT NULL REFERENCES cost_items(id),
      amount REAL NOT NULL CHECK (amount > 0),
      used_amount REAL NOT NULL DEFAULT 0 CHECK (used_amount >= 0),
      issued_date TEXT NOT NULL,
      due_date TEXT NOT NULL,
      status TEXT NOT NULL CHECK (status IN ('Active', 'Overdue', 'Closed')),
      funding_source TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      CHECK (used_amount <= amount)
    );

    CREATE TABLE IF NOT EXISTS payment_requests (
      id TEXT PRIMARY KEY,
      project_id TEXT NOT NULL REFERENCES projects(id),
      construction_object_id TEXT NOT NULL REFERENCES construction_objects(id),
      contract_id TEXT NOT NULL REFERENCES contracts(id),
      cost_item_id TEXT NOT NULL REFERENCES cost_items(id),
      budget_version_id TEXT REFERENCES budget_versions(id),
      author_user_id TEXT NOT NULL REFERENCES users(id),
      number TEXT NOT NULL UNIQUE,
      amount REAL NOT NULL CHECK (amount > 0),
      act_amount REAL NOT NULL DEFAULT 0,
      advance_offset REAL NOT NULL DEFAULT 0,
      retention_amount REAL NOT NULL DEFAULT 0,
      payable_amount REAL NOT NULL,
      vat_included INTEGER NOT NULL DEFAULT 1,
      vat_rate REAL NOT NULL DEFAULT 20,
      vat_amount REAL NOT NULL DEFAULT 0,
      currency TEXT NOT NULL DEFAULT 'BYN',
      exchange_rate REAL NOT NULL DEFAULT 1,
      amount_in_byn REAL NOT NULL,
      type TEXT NOT NULL CHECK (type IN ('Advance', 'CurrentPayment')),
      status TEXT NOT NULL CHECK (status IN ('Draft', 'UnderReview', 'Returned', 'Blocked', 'PendingApproval', 'ApprovedByManagement', 'Approved', 'Executed', 'Closed', 'Rejected')),
      planned_payment_date TEXT NOT NULL,
      purpose TEXT,
      block_reason TEXT,
      return_or_reject_reason TEXT,
      overrun_justification TEXT,
      has_overdue_advance_warning INTEGER NOT NULL DEFAULT 0,
      is_overrun_approved INTEGER NOT NULL DEFAULT 0,
      eds_signature_thumbprint TEXT,
      eds_signed_at TEXT,
      idempotency_key TEXT,
      submitted_at TEXT,
      blocked_at TEXT,
      approved_at TEXT,
      executed_at TEXT,
      closed_at TEXT,
      created_at TEXT NOT NULL,
      updated_at TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      CHECK (advance_offset + retention_amount <= amount + 0.0001)
    );

    CREATE TABLE IF NOT EXISTS documents (
      id TEXT PRIMARY KEY,
      payment_request_id TEXT REFERENCES payment_requests(id),
      contract_id TEXT REFERENCES contracts(id),
      construction_object_id TEXT REFERENCES construction_objects(id),
      type TEXT NOT NULL CHECK (type IN ('Contract', 'ActS2', 'CertificateS3', 'Invoice', 'BankStatement', 'Specification')),
      number TEXT NOT NULL,
      date TEXT NOT NULL,
      gross_amount REAL NOT NULL DEFAULT 0,
      net_amount REAL NOT NULL DEFAULT 0,
      vat_included INTEGER NOT NULL DEFAULT 1,
      currency TEXT NOT NULL DEFAULT 'BYN',
      advance_offset REAL NOT NULL DEFAULT 0,
      retention_amount REAL NOT NULL DEFAULT 0,
      file_path TEXT,
      photo_evidence_paths TEXT,
      act_work_description TEXT,
      contractor_signer TEXT,
      customer_signer TEXT,
      is_verified_instruction53 INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      UNIQUE(type, number, date)
    );

    CREATE TABLE IF NOT EXISTS payments (
      id TEXT PRIMARY KEY,
      payment_request_id TEXT NOT NULL REFERENCES payment_requests(id),
      bank_operation_id TEXT NOT NULL UNIQUE,
      payment_date TEXT NOT NULL,
      amount REAL NOT NULL CHECK (amount > 0),
      currency TEXT NOT NULL DEFAULT 'BYN',
      exchange_rate REAL NOT NULL DEFAULT 1,
      amount_in_byn REAL NOT NULL,
      fx_difference REAL NOT NULL DEFAULT 0,
      status TEXT NOT NULL CHECK (status IN ('Executed', 'Reconciled')),
      payment_order_number TEXT,
      counterparty_account TEXT,
      created_at TEXT NOT NULL,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1
    );

    CREATE TABLE IF NOT EXISTS currency_rates (
      id TEXT PRIMARY KEY,
      currency TEXT NOT NULL,
      rate_date TEXT NOT NULL,
      rate_to_byn REAL NOT NULL CHECK (rate_to_byn > 0),
      UNIQUE(currency, rate_date)
    );

    CREATE TABLE IF NOT EXISTS norm_rules (
      id TEXT PRIMARY KEY,
      code TEXT NOT NULL,
      name TEXT NOT NULL,
      document_source TEXT NOT NULL,
      value_text TEXT NOT NULL,
      numeric_value REAL,
      valid_from TEXT NOT NULL,
      valid_to TEXT,
      responsible_role TEXT NOT NULL,
      revision INTEGER NOT NULL DEFAULT 1,
      row_version INTEGER NOT NULL DEFAULT 1,
      UNIQUE(code, revision)
    );

    CREATE TABLE IF NOT EXISTS risks (
      id TEXT PRIMARY KEY,
      project_id TEXT NOT NULL REFERENCES projects(id),
      code TEXT NOT NULL,
      event TEXT NOT NULL,
      cause TEXT NOT NULL,
      consequence TEXT NOT NULL,
      probability REAL NOT NULL CHECK (probability >= 0 AND probability <= 1),
      monetary_impact REAL NOT NULL CHECK (monetary_impact >= 0),
      probability_score INTEGER NOT NULL CHECK (probability_score BETWEEN 1 AND 5),
      impact_score INTEGER NOT NULL CHECK (impact_score BETWEEN 1 AND 5),
      integral_score INTEGER NOT NULL CHECK (integral_score BETWEEN 1 AND 25),
      emv REAL NOT NULL CHECK (emv >= 0),
      owner_role TEXT NOT NULL,
      mitigation TEXT NOT NULL,
      early_indicator TEXT NOT NULL,
      trigger_fired INTEGER NOT NULL DEFAULT 0,
      residual_probability REAL,
      residual_emv REAL,
      status TEXT NOT NULL CHECK (status IN ('Identified', 'Analyzed', 'Treating', 'Monitoring', 'Realized', 'Closed')),
      linked_contract_id TEXT REFERENCES contracts(id),
      linked_cost_item_id TEXT REFERENCES cost_items(id),
      current_assessment_version INTEGER NOT NULL DEFAULT 1,
      is_deleted INTEGER NOT NULL DEFAULT 0,
      row_version INTEGER NOT NULL DEFAULT 1,
      UNIQUE(project_id, code)
    );

    CREATE TABLE IF NOT EXISTS risk_assessments (
      id TEXT PRIMARY KEY,
      risk_id TEXT NOT NULL REFERENCES risks(id),
      version INTEGER NOT NULL,
      probability REAL NOT NULL,
      monetary_impact REAL NOT NULL,
      emv REAL NOT NULL,
      residual_probability REAL,
      residual_emv REAL,
      project_reserve_p80_snapshot REAL NOT NULL,
      assessed_at TEXT NOT NULL,
      assessed_by_user_id TEXT NOT NULL REFERENCES users(id),
      comment TEXT,
      UNIQUE(risk_id, version)
    );

    CREATE TABLE IF NOT EXISTS audit_logs (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      user_id TEXT,
      user_login TEXT NOT NULL,
      timestamp TEXT NOT NULL,
      object_name TEXT NOT NULL,
      object_id TEXT NOT NULL,
      action TEXT NOT NULL,
      old_value TEXT,
      new_value TEXT,
      details TEXT
    );

    CREATE TABLE IF NOT EXISTS notifications (
      id TEXT PRIMARY KEY,
      target_user_id TEXT,
      target_role_code TEXT,
      title TEXT NOT NULL,
      message TEXT NOT NULL,
      severity TEXT NOT NULL CHECK (severity IN ('Info', 'Warning', 'Critical')),
      category TEXT NOT NULL CHECK (category IN ('RiskTrigger', 'CashGap', 'ApprovalDue', 'OverdueAdvance', 'System')),
      is_read INTEGER NOT NULL DEFAULT 0,
      created_at TEXT NOT NULL,
      entity_link TEXT
    );

    DROP VIEW IF EXISTS v_free_limit;
    CREATE VIEW v_free_limit AS
    SELECT
      p.id AS project_id,
      p.code AS project_code,
      p.name AS project_name,
      co.id AS construction_object_id,
      co.name AS construction_object_name,
      ci.id AS cost_item_id,
      ci.code AS cost_item_code,
      ci.name AS cost_item_name,
      bv.id AS budget_version_id,
      bv.version_number,
      COALESCE(lim.budget_amount, 0) AS approved_budget,
      COALESCE(obl.gross_obligations, 0) AS gross_obligations,
      COALESCE(obl.paid_fact, 0) AS paid_fact,
      COALESCE(req.submitted_unpaid, 0) AS submitted_requests,
      COALESCE(lim.budget_amount, 0) - COALESCE(obl.gross_obligations, 0) AS free_limit,
      COALESCE(obl.gross_obligations, 0) - COALESCE(obl.paid_fact, 0) AS obligation_remainder,
      COALESCE(obl.gross_obligations, 0) - COALESCE(lim.budget_amount, 0) AS deviation,
      CASE
        WHEN COALESCE(lim.budget_amount, 0) = 0 THEN NULL
        ELSE ROUND(((COALESCE(obl.gross_obligations, 0) - lim.budget_amount) / lim.budget_amount) * 100.0, 2)
      END AS deviation_percent
    FROM projects p
    JOIN budget_versions bv ON bv.project_id = p.id AND bv.status = 'Approved' AND bv.is_deleted = 0
    JOIN construction_objects co ON co.project_id = p.id AND co.is_deleted = 0
    CROSS JOIN cost_items ci
    LEFT JOIN (
      SELECT budget_version_id, construction_object_id, cost_item_id, SUM(amount) AS budget_amount
      FROM budget_limits WHERE is_deleted = 0
      GROUP BY budget_version_id, construction_object_id, cost_item_id
    ) lim ON lim.budget_version_id = bv.id AND lim.construction_object_id = co.id AND lim.cost_item_id = ci.id
    LEFT JOIN (
      SELECT construction_object_id, cost_item_id, SUM(gross_amount) AS gross_obligations, SUM(paid_amount) AS paid_fact
      FROM obligations WHERE is_deleted = 0
      GROUP BY construction_object_id, cost_item_id
    ) obl ON obl.construction_object_id = co.id AND obl.cost_item_id = ci.id
    LEFT JOIN (
      SELECT construction_object_id, cost_item_id, SUM(amount_in_byn) AS submitted_unpaid
      FROM payment_requests
      WHERE is_deleted = 0 AND status IN ('UnderReview', 'Blocked', 'PendingApproval', 'ApprovedByManagement', 'Approved')
      GROUP BY construction_object_id, cost_item_id
    ) req ON req.construction_object_id = co.id AND req.cost_item_id = ci.id
    WHERE p.is_deleted = 0 AND ci.is_deleted = 0 AND (lim.budget_amount IS NOT NULL OR obl.gross_obligations IS NOT NULL);
  `);
}

function seedIfEmpty(db: DatabaseSyncType) {
  const row = db.prepare('SELECT COUNT(*) AS cnt FROM roles').get() as { cnt: number };
  if (row.cnt > 0) return;

  const now = '2026-11-10T09:00:00.000Z';
  const defaultHash = hashPassword('StroyFin2026!');

  const roles = [
    [IDS.roleCeo, 'CEO', 'Руководитель организации', 'Both', 'Портфель, ликвидность, утверждение бюджетов и перерасхода, критические риски'],
    [IDS.roleFin, 'FinancialManager', 'Финансовый менеджер', 'Web', 'Бюджеты, платёжный календарь, согласование заявок, контроль авансов и разрывов'],
    [IDS.roleAcc, 'Accountant', 'Бухгалтер', 'WPF', 'Исполнение платежей, загрузка банковских выписок, учёт факта и закрытие обязательств'],
    [IDS.roleProc, 'ProcurementSpecialist', 'Специалист по снабжению', 'WPF', 'Договоры, спецификации, заявки на закупки, контроль поставок'],
    [IDS.rolePm, 'ProjectManager', 'Руководитель проекта', 'Web', 'Создание заявок, акты С-2/С-3 с фотофиксацией, прогноз EAC, регистрация рисков'],
    [IDS.roleRisk, 'RiskSpecialist', 'Специалист по рискам', 'Web', 'Реестр рисков, оценка EMV, σ, резерв P80, триггеры, анализ чувствительности'],
    [IDS.roleAdmin, 'Admin', 'Администратор системы', 'WPF', 'Пользователи, 8 ролей, справочники, нормативные правила, интеграции, аудит'],
    [IDS.roleSite, 'SiteWorker', 'Работник на объекте', 'Web', 'Просмотр этапов строительства и загрузка фотофиксации к актам С-2'],
  ];
  const insRole = db.prepare('INSERT INTO roles (id, code, name, client_type, description) VALUES (?, ?, ?, ?, ?)');
  for (const r of roles) insRole.run(...r);

  const users = [
    [IDS.userCeo, IDS.roleCeo, 'director', defaultHash, 'Ковалёв Андрей Сергеевич', 'director@stroyfin.by', 0, null, now],
    [IDS.userFin, IDS.roleFin, 'finmanager', defaultHash, 'Петрова Елена Викторовна', 'finmanager@stroyfin.by', 0, null, now],
    [IDS.userAcc, IDS.roleAcc, 'accountant', defaultHash, 'Сидорова Наталья Ивановна', 'accountant@stroyfin.by', 0, null, now],
    [IDS.userProc, IDS.roleProc, 'supply', defaultHash, 'Новиков Дмитрий Олегович', 'supply@stroyfin.by', 0, null, now],
    [IDS.userPm, IDS.rolePm, 'pm', defaultHash, 'Громов Алексей Николаевич', 'pm@stroyfin.by', 0, null, now],
    [IDS.userRisk, IDS.roleRisk, 'risk', defaultHash, 'Волкова Марина Петровна', 'risk@stroyfin.by', 0, null, now],
    [IDS.userAdmin, IDS.roleAdmin, 'admin', defaultHash, 'Титов Сергей Владимирович', 'admin@stroyfin.by', 1, '246810', now],
    [IDS.userSite, IDS.roleSite, 'siteworker', defaultHash, 'Орлов Максим Игоревич', 'site@stroyfin.by', 0, null, now],
  ];
  const insUser = db.prepare(
    'INSERT INTO users (id, role_id, login, password_hash, full_name, email, two_factor_enabled, two_factor_secret, consent_date, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
  );
  for (const u of users) insUser.run(u[0], u[1], u[2], u[3], u[4], u[5], u[6], u[7], u[8], u[8]);

  db.prepare('INSERT INTO organizations (id, name, unp) VALUES (?, ?, ?)').run(
    IDS.org, 'ОАО «МинскСтройКомплекс» (модельная организация)', '100123456'
  );
  db.prepare('INSERT INTO portfolios (id, organization_id, name) VALUES (?, ?, ?)').run(
    IDS.portfolio, IDS.org, 'Жилищное и гражданское строительство 2026'
  );

  // Проект 1 воспроизводит контрольный пример п. 1.3.2 (V=14500, BAC=11000, InitialCash=2000, MinCash=1000)
  db.prepare(
    'INSERT INTO projects (id, portfolio_id, code, name, contract_value, currency, initial_cash, min_cash_balance, planned_start_date, planned_end_date) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
  ).run(IDS.project1, IDS.portfolio, 'PRJ-2026-01', 'ЖК «Лошицкий квартал», жилые дома № 4 и № 5', 14500, 'BYN', 2000, 1000, '2026-01-15', '2026-12-30');
  db.prepare(
    'INSERT INTO projects (id, portfolio_id, code, name, contract_value, currency, initial_cash, min_cash_balance, planned_start_date, planned_end_date) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
  ).run(IDS.project2, IDS.portfolio, 'PRJ-2026-02', 'Детский сад на 230 мест в м-не Каменная Горка', 8900, 'BYN', 1200, 500, '2026-03-01', '2027-06-30');

  db.prepare('INSERT INTO construction_objects (id, project_id, queue_name, name, address) VALUES (?, ?, ?, ?, ?)').run(
    IDS.obj1, IDS.project1, '1-я очередь', 'Жилой дом № 4 (серия М464-У1)', 'г. Минск, ул. Игуменский тракт, 42'
  );
  db.prepare('INSERT INTO construction_objects (id, project_id, queue_name, name, address) VALUES (?, ?, ?, ?, ?)').run(
    IDS.obj2, IDS.project1, '2-я очередь', 'Жилой дом № 5 и инженерные сети', 'г. Минск, ул. Игуменский тракт, 44'
  );
  db.prepare('INSERT INTO construction_objects (id, project_id, queue_name, name, address) VALUES (?, ?, ?, ?, ?)').run(
    IDS.obj3, IDS.project2, '1-я очередь', 'Здание ДДУ с бассейном', 'г. Минск, ул. Налибокская, 18'
  );

  // Этапы Проекта 1 суммарно дают PV=3400, EV=3000, AC=3300 (пример п. 1.3.2 -> CPI=0.909, SPI=0.882, ETC=8800, EAC=12100)
  const stages = [
    ['41000000-0000-0000-0000-000000000001', IDS.obj1, 'Нулевой цикл и фундамент', '2026-01-15', '2026-05-30', 1800, 1750, 1850],
    ['41000000-0000-0000-0000-000000000002', IDS.obj1, 'Монтаж железобетонных панелей', '2026-06-01', '2026-10-30', 1600, 1250, 1450],
    ['41000000-0000-0000-0000-000000000003', IDS.obj2, 'Наружные инженерные сети', '2026-07-01', '2026-12-20', 0, 0, 0],
    ['41000000-0000-0000-0000-000000000004', IDS.obj3, 'Земляные работы и фундамент ДДУ', '2026-03-01', '2026-09-30', 2100, 2000, 1950],
  ];
  const insStage = db.prepare(
    'INSERT INTO work_stages (id, construction_object_id, name, planned_start_date, planned_end_date, planned_value_pv, earned_value_ev, actual_cost_ac) VALUES (?, ?, ?, ?, ?, ?, ?, ?)'
  );
  for (const s of stages) insStage.run(...s);

  const items = [
    [IDS.itemMat, 'ST-01', 'Строительные материалы и конструкции (ЖБИ, арматура, цемент)', 'Материалы'],
    [IDS.itemSub, 'ST-02', 'Субподрядные монтажные и специальные работы', 'Субподряд'],
    [IDS.itemEq, 'ST-03', 'Инженерное и технологическое оборудование', 'Оборудование'],
    [IDS.itemOvr, 'ST-04', 'Накладные расходы, транспорт и механизмы', 'Накладные'],
  ];
  const insItem = db.prepare('INSERT INTO cost_items (id, code, name, category) VALUES (?, ?, ?, ?)');
  for (const i of items) insItem.run(...i);

  // Бюджет v1 Проекта 1 (BAC=11000 по объекту 1 для примера 1.3.2, либо 10000 по статьям примеров 1 и 2)
  // Чтобы BAC Проекта 1 был ровно 11000 и одновременно на Объекте 1 ST-01 (B=10000, Пример 1) + ST-04 (B=1000),
  // а Пример 2 (B=10000, O=10500) жил в Проекте 2 (Объект 3 ST-02) или через отдельный лимит?
  // Проверим! Если в Проекте 1: Obj1+ST-01 = 10000, Obj1+ST-04 = 1000 (итого BAC Проекта 1 = 11 000!!),
  // а в Проекте 2: Obj3+ST-02 = 10000 (и O = 10500 -> Пример 2 Lдост = -500, Oост = 3500!!),
  // тогда КАЖДЫЙ контрольный пример (и Пример 1, и Пример 2, и BAC=11000 в п. 1.3.2) сходится ОДНОВРЕМЕННО без противоречий!
  db.prepare(
    'INSERT INTO budget_versions (id, project_id, version_number, status, approved_at, approved_by_user_id, comment, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)'
  ).run(IDS.bv1, IDS.project1, 1, 'Approved', '2026-01-10', IDS.userCeo, 'Утверждённый бюджет проекта (BAC = 11 000 тыс. р.)', now);
  db.prepare(
    'INSERT INTO budget_versions (id, project_id, version_number, status, approved_at, approved_by_user_id, comment, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)'
  ).run(IDS.bv2, IDS.project2, 1, 'Approved', '2026-03-01', IDS.userCeo, 'Утверждённый бюджет ДДУ (статья ST-02 = 10 000 тыс. р.)', now);

  const insLimit = db.prepare(
    'INSERT INTO budget_limits (id, budget_version_id, construction_object_id, cost_item_id, period_start, period_end, amount) VALUES (?, ?, ?, ?, ?, ?, ?)'
  );
  // Пример 1 (п. 1.3.1): Obj1 + ST-01 -> B = 10 000
  insLimit.run('81000000-0000-0000-0000-000000000001', IDS.bv1, IDS.obj1, IDS.itemMat, '2026-01-01', '2026-12-31', 10000);
  // Дополнение до BAC = 11 000 в Проекте 1 (п. 1.3.2): Obj1 + ST-04 -> B = 1 000
  insLimit.run('81000000-0000-0000-0000-000000000002', IDS.bv1, IDS.obj1, IDS.itemOvr, '2026-01-01', '2026-12-31', 1000);
  // Пример 2 (п. 1.3.1): Obj3 + ST-02 -> B = 10 000
  insLimit.run('81000000-0000-0000-0000-000000000003', IDS.bv2, IDS.obj3, IDS.itemSub, '2026-03-01', '2027-06-30', 10000);

  // Договоры
  const insContract = db.prepare(
    'INSERT INTO contracts (id, project_id, number, counterparty, counterparty_unp, direction, total_amount, vat_included, vat_rate, currency, start_date, end_date, retention_percent, specification_notes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
  );
  insContract.run(IDS.contractCust, IDS.project1, 'ГП-2026/01', 'УКС Мингорисполкома', '100234567', 'CustomerIncoming', 14500, 1, 20, 'BYN', '2026-01-10', '2026-12-31', 5, 'Генподрядный договор ЖК «Лошицкий квартал»');
  insContract.run(IDS.contractMat, IDS.project1, 'П-2026/14', 'ОАО «Белорусский цементный завод»', '700012345', 'SupplierSubcontractorOutgoing', 9500, 1, 20, 'BYN', '2026-01-20', '2026-12-31', 0, 'Поставка ЖБИ, цемента и товарного бетона');
  insContract.run(IDS.contractSub, IDS.project1, 'СП-2026/09', 'ООО «СпецМонтажСтрой»', '191234567', 'SupplierSubcontractorOutgoing', 3500, 1, 20, 'BYN', '2026-02-01', '2026-12-31', 5, 'Монтаж сетей и спецработы (USD/BYN эквивалент)');
  insContract.run(IDS.contractDdu, IDS.project2, 'СП-2026/22', 'ОАО «СтройТрест № 7»', '100998877', 'SupplierSubcontractorOutgoing', 12000, 1, 20, 'BYN', '2026-03-05', '2027-06-30', 5, 'Субподряд по строительству ДДУ');

  // Доп. соглашение
  db.prepare('INSERT INTO contract_supplements (id, contract_id, number, amount_delta, signed_date, reason) VALUES (?, ?, ?, ?, ?, ?)').run(
    '91000000-0000-0000-0000-000000000001', IDS.contractMat, 'ДС-1', 500, '2026-06-15', 'Дополнительная поставка железобетонных плит перекрытия'
  );

  // Обязательства (Пример 1: O=6000, F=3000; Пример 2: O=10500, F=7000)
  const insObl = db.prepare(
    'INSERT INTO obligations (id, contract_id, construction_object_id, cost_item_id, gross_amount, paid_amount, status) VALUES (?, ?, ?, ?, ?, ?, ?)'
  );
  insObl.run('92000000-0000-0000-0000-000000000001', IDS.contractMat, IDS.obj1, IDS.itemMat, 6000, 3000, 'PartiallyPaid');
  insObl.run('92000000-0000-0000-0000-000000000002', IDS.contractDdu, IDS.obj3, IDS.itemSub, 10500, 7000, 'PartiallyPaid');

  // Авансы (включая просроченный аванс на СП-2026/09 и СП-2026/22 + активный аванс на П-2026/14)
  const insAdv = db.prepare(
    'INSERT INTO advances (id, contract_id, construction_object_id, cost_item_id, amount, used_amount, issued_date, due_date, status, funding_source) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
  );
  insAdv.run('93000000-0000-0000-0000-000000000001', IDS.contractMat, IDS.obj1, IDS.itemMat, 1200, 900, '2026-09-01', '2026-12-20', 'Active', 'Собственные средства');
  insAdv.run('93000000-0000-0000-0000-000000000002', IDS.contractSub, IDS.obj2, IDS.itemSub, 1500, 1000, '2026-08-01', '2026-10-15', 'Overdue', 'Кредитная линия');

  // Графики платежей (воспроизведение контрольного примера таблицы 1.5 на первые 5 дней от 2026-11-10!)
  const insSched = db.prepare(
    'INSERT INTO contract_schedule_items (id, contract_id, planned_date, amount, is_incoming, is_paid, description) VALUES (?, ?, ?, ?, ?, ?, ?)'
  );
  // Просроченная дебиторка заказчика > 10 дней (индикатор R-08)
  insSched.run('94000000-0000-0000-0000-000000000000', IDS.contractCust, '2026-10-25', 850, 1, 0, 'Оплата заказчиком акта С-2 № 08 (просрочено > 10 дн.)');
  // День 1 (+1 = 2026-11-11): Out 300
  insSched.run('94000000-0000-0000-0000-000000000001', IDS.contractMat, '2026-11-11', 300, 0, 0, 'Поставка цемента (партия № 12)');
  // День 2 (+2 = 2026-11-12): In 1000, Out 400
  insSched.run('94000000-0000-0000-0000-000000000002', IDS.contractCust, '2026-11-12', 1000, 1, 0, 'Поступление от заказчика по С-3 № 10');
  insSched.run('94000000-0000-0000-0000-000000000003', IDS.contractMat, '2026-11-12', 400, 0, 0, 'Оплата арматуры и плит');
  // День 3 (+3 = 2026-11-13): Out 1800 -> Кассовый разрыв 500!
  insSched.run('94000000-0000-0000-0000-000000000004', IDS.contractSub, '2026-11-13', 1800, 0, 0, 'Расчёт с субподрядчиком по этапу монтажа');
  // День 4 (+4 = 2026-11-14): In 600, Out 200 -> Кассовый разрыв 100!
  insSched.run('94000000-0000-0000-0000-000000000005', IDS.contractCust, '2026-11-14', 600, 1, 0, 'Целевой аванс заказчика на оборудование');
  insSched.run('94000000-0000-0000-0000-000000000006', IDS.contractMat, '2026-11-14', 200, 0, 0, 'Транспортные услуги и бетон');
  // День 5 (+5 = 2026-11-15): Out 100 -> Кассовый разрыв 200!
  insSched.run('94000000-0000-0000-0000-000000000007', IDS.contractMat, '2026-11-15', 100, 0, 0, 'Прочие материалы');
  // День 10 (+10 = 2026-11-20): Крупное поступление, закрывающее разрыв
  insSched.run('94000000-0000-0000-0000-000000000008', IDS.contractCust, '2026-11-20', 2200, 1, 0, 'Оплата заказчиком по акту С-2 № 11');

  // Платёжные заявки (демонстрируют разные состояния жизненного цикла табл. 1.11)
  const insReq = db.prepare(`
    INSERT INTO payment_requests (
      id, project_id, construction_object_id, contract_id, cost_item_id, budget_version_id, author_user_id,
      number, amount, act_amount, advance_offset, retention_amount, payable_amount, vat_included, vat_rate, vat_amount,
      currency, exchange_rate, amount_in_byn, type, status, planned_payment_date, purpose, block_reason,
      has_overdue_advance_warning, is_overrun_approved, submitted_at, blocked_at, approved_at, created_at, updated_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  // Заявка 1: Q = 1000 по Примеру 1 (на согласовании, не уменьшает Lдост повторно!)
  insReq.run(
    '95000000-0000-0000-0000-000000000001', IDS.project1, IDS.obj1, IDS.contractMat, IDS.itemMat, IDS.bv1, IDS.userPm,
    'ЗП-20261110-0001', 1000, 1000, 0, 0, 1000, 1, 20, 166.67, 'BYN', 1, 1000, 'CurrentPayment', 'PendingApproval',
    '2026-11-18', 'Оплата поставки плит перекрытия по накладной № 412', null, 0, 0, now, null, null, now, now
  );
  // Заявка 2: Q = 1500 по Примеру 2 (заблокирована из-за исчерпания лимита Lдост = -500!)
  insReq.run(
    '95000000-0000-0000-0000-000000000002', IDS.project2, IDS.obj3, IDS.contractDdu, IDS.itemSub, IDS.bv2, IDS.userPm,
    'ЗП-20261110-0002', 1500, 1500, 0, 0, 1500, 1, 20, 250, 'BYN', 1, 1500, 'CurrentPayment', 'Blocked',
    '2026-11-19', 'Оплата монтажа кровли ДДУ (превышение лимита ST-02)', 'Недостаточно свободного лимита: Lдост = -500.00 тыс. р. (Δ = +500.00)',
    0, 0, now, now, null, now, now
  );
  // Заявка 3: Согласована, готова к исполнению бухгалтером (пример С-2: Sакт=1200, Aзач=300, Uуд=50 -> Sопл=850!)
  insReq.run(
    '95000000-0000-0000-0000-000000000003', IDS.project1, IDS.obj1, IDS.contractMat, IDS.itemOvr, IDS.bv1, IDS.userProc,
    'ЗП-20261110-0003', 1200, 1200, 300, 50, 850, 1, 20, 200, 'BYN', 1, 850, 'CurrentPayment', 'Approved',
    '2026-11-16', 'Оплата по акту С-2 № 18 за вычетом аванса 300 и удержания 50', null,
    0, 0, now, null, now, now, now
  );

  const insDoc = db.prepare(`
    INSERT INTO documents (
      id, payment_request_id, contract_id, construction_object_id, type, number, date,
      gross_amount, net_amount, vat_included, currency, advance_offset, retention_amount,
      act_work_description, contractor_signer, customer_signer, is_verified_instruction53, created_at
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  insDoc.run('96000000-0000-0000-0000-000000000001', '95000000-0000-0000-0000-000000000001', IDS.contractMat, IDS.obj1, 'Invoice', 'ТТН-412', '2026-11-08', 1000, 833.33, 1, 'BYN', 0, 0, 'Поставка плит перекрытия', 'Белорусский цементный завод', 'Громов А.Н.', 1, now);
  insDoc.run('96000000-0000-0000-0000-000000000002', '95000000-0000-0000-0000-000000000002', IDS.contractDdu, IDS.obj3, 'ActS2', 'С2-09/26', '2026-11-09', 1500, 1250, 1, 'BYN', 0, 0, 'Монтажные работы по кровле', 'СтройТрест № 7', 'Громов А.Н.', 1, now);
  insDoc.run('96000000-0000-0000-0000-000000000003', '95000000-0000-0000-0000-000000000003', IDS.contractMat, IDS.obj1, 'ActS2', 'С2-18/26', '2026-11-07', 1200, 1000, 1, 'BYN', 300, 50, 'Сдача-приёмка монтажа панелей 4–6 эт.', 'СпецМонтажСтрой', 'Громов А.Н.', 1, now);

  // Курсы НБ РБ (п. 1.3.4)
  const insRate = db.prepare('INSERT INTO currency_rates (id, currency, rate_date, rate_to_byn) VALUES (?, ?, ?, ?)');
  insRate.run('97000000-0000-0000-0000-000000000001', 'BYN', '2026-11-10', 1.0);
  insRate.run('97000000-0000-0000-0000-000000000002', 'USD', '2026-11-10', 3.25);
  insRate.run('97000000-0000-0000-0000-000000000003', 'EUR', '2026-11-10', 3.52);
  insRate.run('97000000-0000-0000-0000-000000000004', 'RUB', '2026-11-10', 0.0345);
  insRate.run('97000000-0000-0000-0000-000000000005', 'CNY', '2026-11-10', 0.45);

  // Нормативные правила (табл. 1.13, требование 16)
  const insRule = db.prepare(
    'INSERT INTO norm_rules (id, code, name, document_source, value_text, numeric_value, valid_from, valid_to, responsible_role, revision) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)'
  );
  insRule.run('98000000-0000-0000-0000-000000000001', 'VAT_RATE', 'Базовая ставка НДС', 'Налоговый кодекс РБ', '20 %', 20, '2024-01-01', null, 'Бухгалтер', 1);
  insRule.run('98000000-0000-0000-0000-000000000002', 'RETENTION_DEFAULT', 'Гарантийное удержание по акту', 'Кодекс № 289-З / договор подряда', '5 % суммы акта', 5, '2024-07-23', null, 'Финансовый менеджер', 1);
  insRule.run('98000000-0000-0000-0000-000000000003', 'PENALTY_DAILY', 'Пеня за просрочку обязательства', 'Условия договора (п. 1.3.4)', '0,1 %/день, макс. 10 %', 0.1, '2024-07-23', null, 'Финансовый менеджер', 1);
  insRule.run('98000000-0000-0000-0000-000000000004', 'RESERVE_Z_P80', 'Обеспеченность резерва на риски P80', 'СТБ ISO 31000-2020 (п. 1.9)', 'z = 0,84 (80 %)', 0.84, '2024-01-01', null, 'Специалист по рискам', 1);
  insRule.run('98000000-0000-0000-0000-000000000005', 'COST_RULE_53', 'Привязка затрат к объекту и статье', 'Постановление МАиС РБ № 53 от 30.05.2024', 'Обязательна на каждой операции', 1, '2024-05-30', null, 'Бухгалтер', 1);
  insRule.run('98000000-0000-0000-0000-000000000006', 'ACT_FORMS_29', 'Формы С-2 и справки С-3', 'Постановление МАиС РБ № 29 (ред. 29.10.2026)', 'Хранение 5 лет, фотофиксация', 1, '2018-07-20', null, 'Руководитель проекта', 1);

  // Реестр 12 рисков Проекта 1 (таблицы 1.14–1.17)
  const riskData: Array<[string, string, string, string, number, number, string, string, string, number, number, string]> = [
    ['R-01', 'Задержка финансирования', 'Сезонность поступлений, несогласованный график', 'Кассовый разрыв, остановка работ', 0.30, 600, 'Финансовый менеджер', 'Резервный кредитный лимит, приоритизация платежей', 'Остаток ниже минимального', 0, 0.15, 'Treating'],
    ['R-02', 'Рост цен на материалы', 'Инфляция, отказ поставщика от цены', 'Превышение бюджета', 0.46, 450, 'Специалист по снабжению', 'Фиксация цены, закупка партии', 'Рост цены более 5 % к прайсу', 0, 0.21, 'Treating'],
    ['R-03', 'Срыв поставки', 'Отсутствие товара, транспортные ограничения', 'Простой, удорожание работ', 0.25, 400, 'Специалист по снабжению', 'Альтернативный поставщик, страховой запас', 'Просрочка поставки более 3 дней', 0, 0.15, 'Treating'],
    ['R-04', 'Просроченный аванс', 'Непредставление актов, нарушение подрядчиком', 'Иммобилизация средств', 0.35, 380, 'Финансовый менеджер', 'Удержание, претензионная работа', 'Kисп ниже 100 % при истёкшем сроке', 1, 0.20, 'Monitoring'],
    ['R-05', 'Отставание от графика', 'Недостаток ресурсов, ошибки планирования', 'Рост накладных расходов', 0.30, 400, 'Руководитель проекта', 'Перепланирование, дополнительные ресурсы', 'Отставание более 5 % по графику', 0, 0.20, 'Treating'],
    ['R-06', 'Ошибка проектного решения', 'Неполные исходные данные', 'Переделки, дополнительные затраты', 0.15, 600, 'Руководитель проекта', 'Экспертиза решений до закупки', 'Замечания к проектной документации', 0, 0.08, 'Treating'],
    ['R-07', 'Изменение требований заказчика', 'Уточнение объёмов, смена решений', 'Дополнительные затраты, сдвиг сроков', 0.25, 320, 'Руководитель проекта', 'Фиксация объёма, дополнительное соглашение', 'Изменение задания заказчиком', 0, 0.15, 'Monitoring'],
    ['R-08', 'Неплатёж заказчика', 'Финансовые трудности заказчика', 'Нехватка финансирования', 0.20, 700, 'Финансовый менеджер', 'График авансирования, банковская гарантия', 'Просрочка поступления более 10 дней', 1, 0.10, 'Treating'],
    ['R-09', 'Авария, форс-мажор', 'Природные условия, отказ оборудования', 'Перенос сроков, восстановление', 0.10, 800, 'Руководитель организации', 'Страхование, резерв времени', 'Страховое событие', 0, 0.05, 'Monitoring'],
    ['R-10', 'Ошибка классификации затрат', 'Расхождение справочников', 'Искажение себестоимости, замечания', 0.30, 150, 'Бухгалтер', 'Справочник статей, проверка классификации', 'Расхождение с Инструкцией № 53', 0, 0.15, 'Monitoring'],
    ['R-11', 'Утечка данных, компрометация учётной записи', 'Слабая аутентификация, ошибка прав доступа', 'Ущерб, блокировка платежей', 0.15, 700, 'Администратор системы', 'Двухфакторная аутентификация, аудит доступа', 'Неудачные входы, аномальные права', 0, 0.08, 'Treating'],
    ['R-12', 'Отказ ИТ-инфраструктуры, потеря данных', 'Сбой сервера, ошибка резервного копирования', 'Простой, потеря платежей', 0.10, 600, 'Администратор системы', 'Резервное копирование, контроль восстановления', 'Сбой репликации, ошибка backup', 0, 0.05, 'Monitoring'],
  ];
  const insRisk = db.prepare(`
    INSERT INTO risks (
      id, project_id, code, event, cause, consequence, probability, monetary_impact,
      probability_score, impact_score, integral_score, emv, owner_role, mitigation,
      early_indicator, trigger_fired, residual_probability, residual_emv, status
    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
  `);
  const insAssess = db.prepare(`
    INSERT INTO risk_assessments (
      id, risk_id, version, probability, monetary_impact, emv, residual_probability,
      residual_emv, project_reserve_p80_snapshot, assessed_at, assessed_by_user_id, comment
    ) VALUES (?, ?, 1, ?, ?, ?, ?, ?, 1947.51, ?, ?, ?)
  `);

  riskData.forEach((r, idx) => {
    const id = `99000000-0000-0000-0000-${String(idx + 1).padStart(12, '0')}`;
    const pScore = getProbabilityScore(r[4]);
    const iScore = getImpactScore(r[5]);
    const integral = pScore * iScore;
    const emv = roundTo(r[4] * r[5], 4);
    const resEmv = roundTo(r[10] * r[5], 4);
    insRisk.run(id, IDS.project1, r[0], r[1], r[2], r[3], r[4], r[5], pScore, iScore, integral, emv, r[6], r[7], r[8], r[9], r[10], resEmv, r[11]);
    insAssess.run(`99100000-0000-0000-0000-${String(idx + 1).padStart(12, '0')}`, id, r[4], r[5], emv, r[10], resEmv, now, IDS.userRisk, 'Первичная оценка реестра рисков (табл. 1.15)');
  });

  // Уведомления
  const insNotif = db.prepare('INSERT INTO notifications (id, target_role_code, title, message, severity, category, created_at, entity_link) VALUES (?, ?, ?, ?, ?, ?, ?, ?)');
  insNotif.run('9a000000-0000-0000-0000-000000000001', 'CEO', 'Перерасход по заявке ЗП-20261110-0002', 'Свободный лимит по статье ST-02 исчерпан (Lдост = -500 тыс. р.). Требуется решение руководителя.', 'Critical', 'ApprovalDue', now, '/requests');
  insNotif.run('9a000000-0000-0000-0000-000000000002', 'FinancialManager', 'Прогноз кассового разрыва (день 3–5)', 'На 13.11.2026 прогнозируется кассовый разрыв 500 тыс. р. по проекту ЖК «Лошицкий квартал».', 'Critical', 'CashGap', now, '/calendar');
  insNotif.run('9a000000-0000-0000-0000-000000000003', 'FinancialManager', 'Сработал триггер риска R-04 и R-08', 'Просрочен аванс по договору СП-2026/09 и просрочка поступления заказчика > 10 дней.', 'Warning', 'RiskTrigger', now, '/risks');

  // Начальная запись аудита
  db.prepare('INSERT INTO audit_logs (user_id, user_login, timestamp, object_name, object_id, action, new_value, details) VALUES (?, ?, ?, ?, ?, ?, ?, ?)').run(
    IDS.userAdmin, 'admin', now, 'System', 'INIT', 'SEED_DATABASE', 'Completed', 'Загружены контрольные примеры главы 1 и реестр 12 рисков'
  );
}
