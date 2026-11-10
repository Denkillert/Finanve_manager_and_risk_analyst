import { describe, expect, it, beforeEach } from 'vitest';
import request from 'supertest';
import { createDatabase, IDS } from '../src/db/database.js';
import { createApp } from '../src/app.js';
import {
  calculateActPayable,
  calculateCashCalendar,
  calculateEarnedValue,
  calculateFreeLimit,
  calculatePenalty,
  calculateRiskPortfolio,
  calculateSensitivityFactors,
} from '../src/domain/calculators.js';

describe('Глава 1: Контрольные математические примеры предметной области', () => {
  it('Пример 1 (п. 1.3.1): статья имеет запас (B=10000, O=6000, F=3000, Q=1000)', () => {
    const res = calculateFreeLimit(10000, 6000, 3000, 1000);
    expect(res.freeLimit).toBe(4000);
    expect(res.obligationRemainder).toBe(3000);
    expect(res.deviation).toBe(-4000);
    expect(res.deviationPercent).toBe(-40);
    expect(res.hasMissingBaselinePlan).toBe(false);
  });

  it('Пример 2 (п. 1.3.1): статья исчерпана (B=10000, O=10500, F=7000, Q=1500)', () => {
    const res = calculateFreeLimit(10000, 10500, 7000, 1500);
    expect(res.freeLimit).toBe(-500);
    expect(res.obligationRemainder).toBe(3500);
    expect(res.deviation).toBe(500);
    expect(res.deviationPercent).toBe(5);
  });

  it('Пример п. 1.3.2: освоенный объём (CPI=0.909, SPI=0.882, ETC=8800, EAC=12100, Rфин=452)', () => {
    const res = calculateEarnedValue(11000, 3400, 3000, 3300, 14500, 1948);
    expect(res.cpi).toBe(0.909);
    expect(res.spi).toBe(0.882);
    expect(res.etc).toBe(8800);
    expect(res.eac).toBe(12100);
    expect(res.financialResultWithoutReserve).toBe(2400);
    expect(res.financialResultWithReserve).toBe(452);
    expect(res.marginWithoutReservePercent).toBe(16.6);
    expect(res.marginWithReservePercent).toBe(3.1);
  });

  it('Таблица 1.5 (п. 1.3.3): остаток и кассовый разрыв за 5 дней', () => {
    const flows = new Map<string, { inflows: number; outflows: number }>([
      ['2026-11-11', { inflows: 0, outflows: 300 }],
      ['2026-11-12', { inflows: 1000, outflows: 400 }],
      ['2026-11-13', { inflows: 0, outflows: 1800 }],
      ['2026-11-14', { inflows: 600, outflows: 200 }],
      ['2026-11-15', { inflows: 0, outflows: 100 }],
    ]);
    const days = calculateCashCalendar(2000, 1000, '2026-11-10', 5, flows);
    expect(days.map((d) => [d.closingBalance, d.cashGap])).toEqual([
      [2000, 0],
      [1700, 0],
      [2300, 0],
      [500, 500],
      [900, 100],
      [800, 200],
    ]);
  });

  it('Формула (1.12): сумма к оплате по акту С-2 (1200 - 300 - 50 = 850)', () => {
    const res = calculateActPayable(1200, 300, 50);
    expect(res.payableAmount).toBe(850);
  });

  it('Таблицы 1.15–1.17: 12 рисков (ΣEMV=1340, σ=723.22, P80=1947.51, остаток=715, снижение=625)', () => {
    const specs: Array<[number, number, number]> = [
      [0.30, 600, 0.15], [0.46, 450, 0.21], [0.25, 400, 0.15], [0.35, 380, 0.20],
      [0.30, 400, 0.20], [0.15, 600, 0.08], [0.25, 320, 0.15], [0.20, 700, 0.10],
      [0.10, 800, 0.05], [0.30, 150, 0.15], [0.15, 700, 0.08], [0.10, 600, 0.05],
    ];
    const sum = calculateRiskPortfolio(specs.map(([p, c, rp]) => ({ probability: p, monetaryImpact: c, residualProbability: rp })));
    expect(sum.totalEmv).toBe(1340);
    expect(sum.sigma).toBeCloseTo(723.22, 1);
    expect(Math.round(sum.reserveP80)).toBe(1948);
    expect(sum.totalResidualEmv).toBe(715);
    expect(sum.emvReduction).toBe(625);
  });

  it('Таблица 1.18 и п. 1.3.4: чувствительность EAC и пеня 0.1%/день (макс. 10%)', () => {
    const sens = calculateSensitivityFactors();
    expect(sens[0].factorName).toBe('Цена материалов');
    expect(sens[0].impactOnEac).toBe(450);
    expect(calculatePenalty(1000, 15)).toBe(15);
    expect(calculatePenalty(1000, 150)).toBe(100);
  });
});

describe('API Интеграционные тесты: RBAC, Workflow, Concurrency, Дубли, MT940, Отчёты', () => {
  let app: ReturnType<typeof createApp>;

  beforeEach(() => {
    const db = createDatabase(':memory:');
    app = createApp(db);
  });

  async function loginAs(login: string, twoFactorCode?: string): Promise<string> {
    const res = await request(app).post('/api/auth/login').send({ login, password: 'StroyFin2026!', twoFactorCode });
    expect(res.status).toBe(200);
    return res.body.token;
  }

  it('Аутентификация: блокировка после 5 неудачных попыток и 2FA для администратора', async () => {
    const no2fa = await request(app).post('/api/auth/login').send({ login: 'admin', password: 'StroyFin2026!' });
    expect(no2fa.status).toBe(401);
    expect(no2fa.body.requiresTwoFactor).toBe(true);

    const ok2fa = await request(app).post('/api/auth/login').send({ login: 'admin', password: 'StroyFin2026!', twoFactorCode: '246810' });
    expect(ok2fa.status).toBe(200);

    for (let i = 0; i < 5; i++) {
      await request(app).post('/api/auth/login').send({ login: 'pm', password: 'WrongPassword123' });
    }
    const locked = await request(app).post('/api/auth/login').send({ login: 'pm', password: 'StroyFin2026!' });
    expect(locked.status).toBe(423);
  });

  it('RBAC (НФТ-8): администратор не может согласовывать или исполнять финансовые платежи', async () => {
    const adminToken = await loginAs('admin', '246810');
    const pmToken = await loginAs('pm');

    const resAdminPay = await request(app)
      .post('/api/payment-requests/95000000-0000-0000-0000-000000000003/execute')
      .set('Authorization', `Bearer ${adminToken}`)
      .send({ rowVersion: 1 });
    expect(resAdminPay.status).toBe(403);

    const resPmOverrun = await request(app)
      .post('/api/payment-requests/95000000-0000-0000-0000-000000000002/approve-overrun')
      .set('Authorization', `Bearer ${pmToken}`)
      .send({ approve: true, reason: 'Попытка без прав', rowVersion: 1 });
    expect(resPmOverrun.status).toBe(403);
  });

  it('Бизнес-правило 10 и 16: блокировка нового аванса при просроченном, предупреждение по акту и защита от дублей', async () => {
    const pmToken = await loginAs('pm');

    // Новый аванс по договору СП-2026/09 (имеет просроченный аванс) -> Blocked!
    const advReq = await request(app)
      .post('/api/payment-requests')
      .set('Authorization', `Bearer ${pmToken}`)
      .send({
        projectId: IDS.project1,
        constructionObjectId: IDS.obj1,
        contractId: IDS.contractSub,
        costItemId: IDS.itemMat,
        amount: 200,
        vatIncluded: true,
        currency: 'BYN',
        type: 'Advance',
        plannedPaymentDate: '2026-11-22',
        purpose: 'Новый аванс субподрядчику',
        documentType: 'Invoice',
        documentNumber: 'СЧ-777',
        documentDate: '2026-11-10',
        submitImmediately: true,
      });
    expect(advReq.status).toBe(201);
    expect(advReq.body.request.status).toBe('Blocked');
    expect(advReq.body.checks.advanceCheckPassed).toBe(false);

    // Повторная подача с тем же документом-основанием -> 409 Дубль!
    const dupReq = await request(app)
      .post('/api/payment-requests')
      .set('Authorization', `Bearer ${pmToken}`)
      .send({
        projectId: IDS.project1,
        constructionObjectId: IDS.obj1,
        contractId: IDS.contractSub,
        costItemId: IDS.itemMat,
        amount: 200,
        vatIncluded: true,
        currency: 'BYN',
        type: 'CurrentPayment',
        plannedPaymentDate: '2026-11-22',
        purpose: 'Повторная оплата',
        documentType: 'Invoice',
        documentNumber: 'СЧ-777',
        documentDate: '2026-11-10',
      });
    expect(dupReq.status).toBe(409);

    // Текущий платёж по другому документу при просроченном авансе -> пропускается с предупреждением!
    const curReq = await request(app)
      .post('/api/payment-requests')
      .set('Authorization', `Bearer ${pmToken}`)
      .send({
        projectId: IDS.project1,
        constructionObjectId: IDS.obj1,
        contractId: IDS.contractSub,
        costItemId: IDS.itemMat,
        amount: 250,
        vatIncluded: true,
        currency: 'BYN',
        type: 'CurrentPayment',
        plannedPaymentDate: '2026-11-25',
        purpose: 'Текущий платёж по акту С-2',
        documentType: 'ActS2',
        documentNumber: 'С2-888',
        documentDate: '2026-11-10',
      });
    expect(curReq.status).toBe(201);
    expect(curReq.body.request.status).toBe('PendingApproval');
    expect(curReq.body.request.has_overdue_advance_warning).toBe(1);
  });

  it('Полный цикл UC04 + Согласование ЭЦП + Исполнение + Импорт MT940 (идемпотентность) + Оптимистическая блокировка', async () => {
    const ceoToken = await loginAs('director');
    const finToken = await loginAs('finmanager');
    const accToken = await loginAs('accountant');

    // Заявка ЗП-20261110-0002 заблокирована по перерасходу (rowVersion = 1)
    const staleRes = await request(app)
      .post('/api/payment-requests/95000000-0000-0000-0000-000000000002/approve-overrun')
      .set('Authorization', `Bearer ${ceoToken}`)
      .send({ approve: true, reason: 'Устаревшая версия', rowVersion: 99 });
    expect(staleRes.status).toBe(409);

    const overrunOk = await request(app)
      .post('/api/payment-requests/95000000-0000-0000-0000-000000000002/approve-overrun')
      .set('Authorization', `Bearer ${ceoToken}`)
      .send({ approve: true, reason: 'Критично для закрытия контура кровли до заморозков', rowVersion: 1 });
    expect(overrunOk.status).toBe(200);
    expect(overrunOk.body.status).toBe('PendingApproval');

    const finApprove = await request(app)
      .post('/api/payment-requests/95000000-0000-0000-0000-000000000002/approve')
      .set('Authorization', `Bearer ${finToken}`)
      .send({ edsCertificateThumbprint: 'BY-AVEST-2026-9911', rowVersion: overrunOk.body.row_version });
    expect(finApprove.status).toBe(200);
    expect(finApprove.body.status).toBe('Approved');

    const execPay = await request(app)
      .post('/api/payment-requests/95000000-0000-0000-0000-000000000002/execute')
      .set('Authorization', `Bearer ${accToken}`)
      .send({ paymentOrderNumber: 'ПП-555', rowVersion: finApprove.body.row_version });
    expect(execPay.status).toBe(200);

    // Импорт банковской выписки CSV (и повторный импорт — проверка идемпотентности)
    const csv = `BankOperationId;Date;Amount;Currency;Counterparty;RequestNumber;Purpose\nBNK-2026-0099;2026-11-10;1500;BYN;СтройТрест 7;ЗП-20261110-0002;Оплата кровли`;
    const imp1 = await request(app)
      .post('/api/integrations/bank-statement')
      .set('Authorization', `Bearer ${accToken}`)
      .send({ fileName: 'bank.csv', content: csv });
    expect(imp1.status).toBe(200);
    expect(imp1.body.matchedCount).toBe(1);

    const imp2 = await request(app)
      .post('/api/integrations/bank-statement')
      .set('Authorization', `Bearer ${accToken}`)
      .send({ fileName: 'bank.csv', content: csv });
    expect(imp2.status).toBe(200);
    expect(imp2.body.matchedCount).toBe(0);
    expect(imp2.body.duplicateSkippedCount).toBe(1);
  });

  it('Генерация отчётов XLSX и PDF + импорт сметы XML', async () => {
    const finToken = await loginAs('finmanager');
    const binaryParser = (res: any, cb: (err: Error | null, body: Buffer) => void) => {
      const data: Buffer[] = [];
      res.on('data', (chunk: Buffer) => data.push(chunk));
      res.on('end', () => cb(null, Buffer.concat(data)));
    };

    const xlsxRes = await request(app)
      .get('/api/reports/risks/export?format=xlsx')
      .set('Authorization', `Bearer ${finToken}`)
      .buffer(true)
      .parse(binaryParser);
    expect(xlsxRes.status).toBe(200);
    expect((xlsxRes.body as Buffer).subarray(0, 2).toString()).toBe('PK');

    const pdfRes = await request(app)
      .get('/api/reports/budget-fact/export?format=pdf')
      .set('Authorization', `Bearer ${finToken}`)
      .buffer(true)
      .parse(binaryParser);
    expect(pdfRes.status).toBe(200);
    expect((pdfRes.body as Buffer).subarray(0, 5).toString()).toBe('%PDF-');

    const estRes = await request(app)
      .post('/api/integrations/estimate-import')
      .set('Authorization', `Bearer ${finToken}`)
      .send({
        fileName: 'smeta.xml',
        content: `<Estimate><Item Code="ST-01" Name="Бетон и арматура" Amount="4500"/><Item Code="ST-02" Name="Субподрядные монтажные работы" Amount="3200"/></Estimate>`,
      });
    expect(estRes.status).toBe(200);
    expect(estRes.body.matchPercent).toBe(100);
  });

  it('Представление v_free_limit, версионирование бюджета, MT940 и истечение 30 дней блокировки', async () => {
    const ceoToken = await loginAs('director');
    const finToken = await loginAs('finmanager');
    const accToken = await loginAs('accountant');

    // Проверка SQL-представления v_free_limit на стартовых данных
    const flRes = await request(app).get('/api/budget/free-limits').set('Authorization', `Bearer ${finToken}`);
    expect(flRes.status).toBe(200);
    const ex1 = flRes.body.find((r: any) => r.cost_item_code === 'ST-01' && r.project_code === 'PRJ-2026-01');
    const ex2 = flRes.body.find((r: any) => r.cost_item_code === 'ST-02' && r.project_code === 'PRJ-2026-02');
    expect(ex1.free_limit).toBe(4000);
    expect(ex1.obligation_remainder).toBe(3000);
    expect(ex1.submitted_requests).toBe(1000);
    expect(ex2.free_limit).toBe(-500);
    expect(ex2.obligation_remainder).toBe(3500);

    // Проверка новой версии бюджета и блокировки правки утверждённой версии
    const bvRes = await request(app)
      .post('/api/budget/versions')
      .set('Authorization', `Bearer ${finToken}`)
      .send({ projectId: IDS.project1, comment: 'Корректировка на зимний период' });
    expect(bvRes.status).toBe(201);
    expect(bvRes.body.versionNumber).toBe(2);

    // Исполнение согласованной заявки ЗП-20261110-0003 (850 тыс. р.) и закрытие через MT940
    const exec3 = await request(app)
      .post('/api/payment-requests/95000000-0000-0000-0000-000000000003/execute')
      .set('Authorization', `Bearer ${accToken}`)
      .send({ paymentOrderNumber: 'ПП-850', rowVersion: 1 });
    expect(exec3.status).toBe(200);

    const mt940 = `:20:STMT20261110\n:60F:C261110BYN2000,00\n:61:261110D850,00NTRFNONREF//MT940-OP-850\n:86:Оплата по заявке ЗП-20261110-0003 акт С-2`;
    const impMt = await request(app)
      .post('/api/integrations/bank-statement')
      .set('Authorization', `Bearer ${accToken}`)
      .send({ fileName: 'bank.mt940', content: mt940 });
    expect(impMt.status).toBe(200);
    expect(impMt.body.matchedCount).toBe(1);

    // Истечение 30 дней по заблокированной заявке
    const expRes = await request(app)
      .post('/api/payment-requests/expire-stale')
      .set('Authorization', `Bearer ${ceoToken}`)
      .send({ asOfDate: '2026-12-15T12:00:00Z' });
    expect(expRes.status).toBe(200);
    expect(expRes.body.expiredCount).toBeGreaterThanOrEqual(1);
  });
});
