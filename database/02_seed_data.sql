-- ============================================================================
-- Начальное наполнение БД (контрольные примеры главы 1 и модельные данные)
-- Организация профиля крупного панельного домостроения (УНП, проекты, объекты)
-- Все суммы указаны в тыс. бел. руб. (если не указана иная валюта договора)
-- ============================================================================

SET NOCOUNT ON;
GO

-- Фиксированные идентификаторы для воспроизводимости ссылок и автотестов
DECLARE @OrgId UNIQUEIDENTIFIER = '10000000-0000-0000-0000-000000000001';
DECLARE @PortfolioId UNIQUEIDENTIFIER = '20000000-0000-0000-0000-000000000001';
DECLARE @Project1Id UNIQUEIDENTIFIER = '30000000-0000-0000-0000-000000000001';
DECLARE @Project2Id UNIQUEIDENTIFIER = '30000000-0000-0000-0000-000000000002';

DECLARE @Obj1Id UNIQUEIDENTIFIER = '40000000-0000-0000-0000-000000000001';
DECLARE @Obj2Id UNIQUEIDENTIFIER = '40000000-0000-0000-0000-000000000002';
DECLARE @Obj3Id UNIQUEIDENTIFIER = '40000000-0000-0000-0000-000000000003';

DECLARE @ItemMatId UNIQUEIDENTIFIER = '50000000-0000-0000-0000-000000000001';
DECLARE @ItemSubId UNIQUEIDENTIFIER = '50000000-0000-0000-0000-000000000002';
DECLARE @ItemEqId UNIQUEIDENTIFIER = '50000000-0000-0000-0000-000000000003';
DECLARE @ItemOvrId UNIQUEIDENTIFIER = '50000000-0000-0000-0000-000000000004';

DECLARE @RoleCeo UNIQUEIDENTIFIER = '60000000-0000-0000-0000-000000000001';
DECLARE @RoleFin UNIQUEIDENTIFIER = '60000000-0000-0000-0000-000000000002';
DECLARE @RoleAcc UNIQUEIDENTIFIER = '60000000-0000-0000-0000-000000000003';
DECLARE @RoleProc UNIQUEIDENTIFIER = '60000000-0000-0000-0000-000000000004';
DECLARE @RolePm UNIQUEIDENTIFIER = '60000000-0000-0000-0000-000000000005';
DECLARE @RoleRisk UNIQUEIDENTIFIER = '60000000-0000-0000-0000-000000000006';
DECLARE @RoleAdmin UNIQUEIDENTIFIER = '60000000-0000-0000-0000-000000000007';

DECLARE @UserCeo UNIQUEIDENTIFIER = '70000000-0000-0000-0000-000000000001';
DECLARE @UserFin UNIQUEIDENTIFIER = '70000000-0000-0000-0000-000000000002';
DECLARE @UserAcc UNIQUEIDENTIFIER = '70000000-0000-0000-0000-000000000003';
DECLARE @UserProc UNIQUEIDENTIFIER = '70000000-0000-0000-0000-000000000004';
DECLARE @UserPm UNIQUEIDENTIFIER = '70000000-0000-0000-0000-000000000005';
DECLARE @UserRisk UNIQUEIDENTIFIER = '70000000-0000-0000-0000-000000000006';
DECLARE @UserAdmin UNIQUEIDENTIFIER = '70000000-0000-0000-0000-000000000007';

-- 1. Роли (7 ролей по таблице 1.8 и разд. 2.1.3 / НФТ-8)
INSERT INTO security.Roles (Id, Code, Name, ClientType, Description) VALUES
(@RoleCeo, N'CEO', N'Руководитель организации', N'WPF', N'Портфель, ликвидность, утверждение бюджетов и перерасхода (UC04), критические риски'),
(@RoleFin, N'FinancialManager', N'Финансовый менеджер', N'Web', N'Ведение бюджетов, платёжный календарь, согласование заявок, контроль авансов'),
(@RoleAcc, N'Accountant', N'Бухгалтер', N'WPF', N'Исполнение платежей, загрузка выписок, учёт факта и закрытие обязательств'),
(@RoleProc, N'ProcurementSpecialist', N'Специалист по снабжению', N'WPF', N'Ведение договоров, спецификаций, заявок по поставкам и контроль сроков'),
(@RolePm, N'ProjectManager', N'Руководитель проекта', N'Web', N'Оформление заявок, подготовка актов С-2/С-3 с фотофиксацией, прогноз EAC, риски'),
(@RoleRisk, N'RiskSpecialist', N'Специалист по рискам', N'Web', N'Реестр рисков, оценка EMV и резерва P80, триггеры, остаточный риск'),
(@RoleAdmin, N'Admin', N'Администратор системы', N'WPF', N'Учётные записи, роли, справочники, интеграции, журнал аудита');

-- Пароль для всех 7 демо-учёток: StroyFin2026! (PBKDF2-SHA256, 100 000 итераций, совместим с PasswordHasher.cs)
INSERT INTO security.Users (Id, RoleId, Login, PasswordHash, FullName, Email, IsActive, TwoFactorEnabled, PersonalDataConsentGiven, ConsentDate) VALUES
(@UserCeo, @RoleCeo, N'director', N'U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=', N'Ковалёв Андрей Сергеевич', N'director@stroyfin.by', 1, 0, 1, SYSUTCDATETIME()),
(@UserFin, @RoleFin, N'finmanager', N'U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=', N'Петрова Елена Викторовна', N'finmanager@stroyfin.by', 1, 0, 1, SYSUTCDATETIME()),
(@UserAcc, @RoleAcc, N'accountant', N'U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=', N'Сидорова Наталья Ивановна', N'accountant@stroyfin.by', 1, 0, 1, SYSUTCDATETIME()),
(@UserProc, @RoleProc, N'supply', N'U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=', N'Новиков Дмитрий Олегович', N'supply@stroyfin.by', 1, 0, 1, SYSUTCDATETIME()),
(@UserPm, @RolePm, N'pm', N'U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=', N'Громов Алексей Николаевич', N'pm@stroyfin.by', 1, 0, 1, SYSUTCDATETIME()),
(@UserRisk, @RoleRisk, N'risk', N'U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=', N'Волкова Марина Петровна', N'risk@stroyfin.by', 1, 0, 1, SYSUTCDATETIME()),
(@UserAdmin, @RoleAdmin, N'admin', N'U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=', N'Титов Сергей Владимирович', N'admin@stroyfin.by', 1, 1, 1, SYSUTCDATETIME());

-- 2. Иерархия организации и проектов (контрольные примеры 1.3.1, 1.3.2, 1.3.3)
INSERT INTO finance.Organizations (Id, Name, Unp) VALUES
(@OrgId, N'ОАО «МинскСтройКомплекс» (модельная организация)', N'100123456');

INSERT INTO finance.Portfolios (Id, OrganizationId, Name) VALUES
(@PortfolioId, @OrgId, N'Жилищное и гражданское строительство 2026');

-- Проект 1: V = 14 500 тыс. р., BAC = 11 000 тыс. р., EV = 3 000, AC = 3 300, PV = 3 400 (пример п. 1.3.2)
INSERT INTO finance.Projects (Id, PortfolioId, Code, Name, ContractValue, Currency, InitialCash, MinCashBalance, PlannedStartDate, PlannedEndDate) VALUES
(@Project1Id, @PortfolioId, N'PRJ-2026-01', N'ЖК «Лошицкий квартал», жилые дома № 4 и № 5', 14500.0000, 'BYN', 2000.0000, 1000.0000, '2026-01-15', '2026-12-30'),
(@Project2Id, @PortfolioId, N'PRJ-2026-02', N'Детский сад на 230 мест в м-не Каменная Горка', 8900.0000, 'BYN', 1200.0000, 500.0000, '2026-03-01', '2027-06-30');

INSERT INTO finance.ConstructionObjects (Id, ProjectId, QueueName, Name, Address) VALUES
(@Obj1Id, @Project1Id, N'1-я очередь', N'Жилой дом № 4 (серия М464-У1)', N'г. Минск, ул. Игуменский тракт, 42'),
(@Obj2Id, @Project1Id, N'2-я очередь', N'Жилой дом № 5 и инженерные сети', N'г. Минск, ул. Игуменский тракт, 44'),
(@Obj3Id, @Project2Id, N'1-я очередь', N'Здание ДДУ с бассейном', N'г. Минск, ул. Налибокская, 18');

INSERT INTO finance.WorkStages (ConstructionObjectId, Name, PlannedStartDate, PlannedEndDate, PlannedValuePV, EarnedValueEV, ActualCostAC) VALUES
(@Obj1Id, N'Нулевой цикл и фундамент', '2026-01-15', '2026-05-30', 1800.0000, 1750.0000, 1850.0000),
(@Obj1Id, N'Монтаж железобетонных панелей', '2026-06-01', '2026-10-30', 1600.0000, 1250.0000, 1450.0000),
(@Obj2Id, N'Устройство наружных инженерных сетей', '2026-07-01', '2026-12-20', 0.0000, 0.0000, 0.0000),
(@Obj3Id, N'Земляные работы и фундамент ДДУ', '2026-03-01', '2026-09-30', 2100.0000, 2000.0000, 1950.0000);

INSERT INTO finance.CostItems (Id, Code, Name, Category) VALUES
(@ItemMatId, N'ST-01', N'Строительные материалы и конструкции (ЖБИ, арматура, цемент)', N'Материалы'),
(@ItemSubId, N'ST-02', N'Субподрядные монтажные и специальные работы', N'Субподряд'),
(@ItemEqId, N'ST-03', N'Инженерное и технологическое оборудование', N'Оборудование'),
(@ItemOvrId, N'ST-04', N'Накладные расходы, транспорт и механизмы', N'Накладные');

-- 3. Бюджет версии 1 (утверждённый) с контрольными лимитами:
-- Статья ST-01 (Материалы): B = 10 000 (пример 1 п. 1.3.1 — запас: O=6000, F=3000, Q=1000 -> Lдост = 4000)
-- Статья ST-02 (Субподряд): B = 10 000? По BAC=11 000 на объект 1 (5000+5000+1000) и для демонстрации примера 2 (B=10000, O=10500) на Объекте 2
DECLARE @Bv1Id UNIQUEIDENTIFIER = '80000000-0000-0000-0000-000000000001';
INSERT INTO finance.BudgetVersions (Id, ProjectId, VersionNumber, Status, ApprovedAt, ApprovedByUserId, Comment) VALUES
(@Bv1Id, @Project1Id, 1, N'Approved', '2026-01-10', @UserCeo, N'Базовый утверждённый бюджет проекта (версия 1)');

INSERT INTO finance.BudgetLimits (BudgetVersionId, ConstructionObjectId, CostItemId, PeriodStart, PeriodEnd, Amount) VALUES
(@Bv1Id, @Obj1Id, @ItemMatId, '2026-01-01', '2026-12-31', 10000.0000),
(@Bv1Id, @Obj1Id, @ItemSubId, '2026-01-01', '2026-12-31', 1000.0000),
(@Bv1Id, @Obj2Id, @ItemSubId, '2026-01-01', '2026-12-31', 10000.0000);

-- 4. Договоры, обязательства и авансы
DECLARE @ContractCustId UNIQUEIDENTIFIER = '90000000-0000-0000-0000-000000000001';
DECLARE @ContractMatId UNIQUEIDENTIFIER = '90000000-0000-0000-0000-000000000002';
DECLARE @ContractSubId UNIQUEIDENTIFIER = '90000000-0000-0000-0000-000000000003';

INSERT INTO finance.Contracts (Id, ProjectId, Number, Counterparty, CounterpartyUnp, Direction, TotalAmount, VatIncluded, VatRate, Currency, StartDate, EndDate, RetentionPercent) VALUES
(@ContractCustId, @Project1Id, N'ГП-2026/01', N'УКС Мингорисполкома', N'100234567', N'CustomerIncoming', 14500.0000, 1, 20.00, 'BYN', '2026-01-10', '2026-12-31', 5.00),
(@ContractMatId, @Project1Id, N'П-2026/14', N'ОАО «Белорусский цементный завод»', N'700012345', N'SupplierSubcontractorOutgoing', 6500.0000, 1, 20.00, 'BYN', '2026-01-20', '2026-12-31', 0.00),
(@ContractSubId, @Project1Id, N'СП-2026/09', N'ООО «СпецМонтажСтрой»', N'191234567', N'SupplierSubcontractorOutgoing', 11000.0000, 1, 20.00, 'BYN', '2026-02-01', '2026-12-31', 5.00);

-- Обязательства для воспроизведения Примера 1 (Lдост=4000, Oост=3000) и Примера 2 (Lдост=-500, Oост=3500)
INSERT INTO finance.Obligations (ContractId, ConstructionObjectId, CostItemId, GrossAmount, PaidAmount, Status) VALUES
(@ContractMatId, @Obj1Id, @ItemMatId, 6000.0000, 3000.0000, N'PartiallyPaid'),
(@ContractSubId, @Obj2Id, @ItemSubId, 10500.0000, 7000.0000, N'PartiallyPaid');

-- Авансы (включая просроченный аванс по договору СП-2026/09 для демонстрации правила 10)
INSERT INTO finance.Advances (ContractId, ConstructionObjectId, CostItemId, Amount, UsedAmount, IssuedDate, DueDate, Status, FundingSource) VALUES
(@ContractMatId, @Obj1Id, @ItemMatId, 1200.0000, 900.0000, '2026-09-01', '2026-12-15', N'Active', N'Собственные средства'),
(@ContractSubId, @Obj2Id, @ItemSubId, 1500.0000, 1000.0000, '2026-08-01', '2026-10-15', N'Overdue', N'Кредитная линия');

-- 5. Нормативные правила с датами действия (требование 16, таблица 1.13)
INSERT INTO finance.NormRules (Code, Name, DocumentSource, ValueText, NumericValue, ValidFrom, ValidTo, ResponsibleRole, Revision) VALUES
(N'VAT_RATE', N'Базовая ставка НДС', N'Налоговый кодекс РБ', N'20 %', 20.000000, '2024-01-01', NULL, N'Бухгалтер', 1),
(N'RETENTION_DEFAULT', N'Гарантийное удержание по договору подряда', N'Кодекс № 289-З / договор подряда', N'5 % от суммы акта', 5.000000, '2024-07-23', NULL, N'Финансовый менеджер', 1),
(N'PENALTY_DAILY', N'Типовая пеня за просрочку обязательства', N'Договор / п. 1.3.4', N'0,1 % в день, макс. 10 %', 0.100000, '2024-07-23', NULL, N'Финансовый менеджер', 1),
(N'RESERVE_Z_P80', N'Квантиль обеспеченности резерва на риски P80', N'СТБ ISO 31000-2020 / Методика п. 1.9', N'z = 0,84 (уровень 80 %)', 0.840000, '2024-01-01', NULL, N'Специалист по рискам', 1),
(N'COST_RULE_53', N'Отнесение фактических затрат строго на объект и статью', N'Постановление МАиС РБ № 53 от 30.05.2024', N'Обязательная аналитика: Объект + Статья', 1.000000, '2024-05-30', NULL, N'Бухгалтер', 1),
(N'ACT_FORMS_29', N'Применение форм актов С-2 и справок С-3', N'Постановление МАиС РБ № 29 от 20.07.2018 (ред. 29.10.2026)', N'Формы С-2а, С-2б, С-3а', 1.000000, '2018-07-20', NULL, N'Руководитель проекта', 1);

-- 6. Реестр 12 рисков (таблицы 1.14–1.17: ΣEMV = 1340, σ = 723.22, P80 = 1948, остаток = 715)
INSERT INTO risk.Risks (ProjectId, Code, Event, Cause, Consequence, Probability, MonetaryImpact, ProbabilityScore, ImpactScore, IntegralScore, Emv, OwnerRole, Mitigation, EarlyIndicator, TriggerFired, ResidualProbability, ResidualEmv, Status) VALUES
(@Project1Id, N'R-01', N'Задержка финансирования', N'Сезонность поступлений, несогласованный график', N'Кассовый разрыв, остановка работ', 0.30, 600.0000, 3, 5, 15, 180.0000, N'Финансовый менеджер', N'Резервный кредитный лимит, приоритизация платежей', N'Остаток ниже минимального', 0, 0.15, 90.0000, N'Treating'),
(@Project1Id, N'R-02', N'Рост цен на материалы', N'Инфляция, отказ поставщика от цены', N'Превышение бюджета', 0.46, 450.0000, 3, 4, 12, 207.0000, N'Специалист по снабжению', N'Фиксация цены, закупка партии', N'Рост цены более 5 % к прайсу', 0, 0.21, 94.5000, N'Treating'),
(@Project1Id, N'R-03', N'Срыв поставки', N'Отсутствие товара, транспортные ограничения', N'Простой, удорожание работ', 0.25, 400.0000, 3, 4, 12, 100.0000, N'Специалист по снабжению', N'Альтернативный поставщик, страховой запас', N'Просрочка поставки более 3 дней', 0, 0.15, 60.0000, N'Treating'),
(@Project1Id, N'R-04', N'Просроченный аванс', N'Непредставление актов, нарушение подрядчиком', N'Иммобилизация средств', 0.35, 380.0000, 3, 3, 9, 133.0000, N'Финансовый менеджер', N'Удержание, претензионная работа', N'Kисп ниже 100 % при истёкшем сроке', 1, 0.20, 76.0000, N'Monitoring'),
(@Project1Id, N'R-05', N'Отставание от графика', N'Недостаток ресурсов, ошибки планирования', N'Рост накладных расходов', 0.30, 400.0000, 3, 4, 12, 120.0000, N'Руководитель проекта', N'Перепланирование, дополнительные ресурсы', N'Отставание более 5 % по графику', 0, 0.20, 80.0000, N'Treating'),
(@Project1Id, N'R-06', N'Ошибка проектного решения', N'Неполные исходные данные', N'Переделки, дополнительные затраты', 0.15, 600.0000, 2, 5, 10, 90.0000, N'Руководитель проекта', N'Экспертиза решений до закупки', N'Замечания к проектной документации', 0, 0.08, 48.0000, N'Treating'),
(@Project1Id, N'R-07', N'Изменение требований заказчика', N'Уточнение объёмов, смена решений', N'Дополнительные затраты, сдвиг сроков', 0.25, 320.0000, 3, 3, 9, 80.0000, N'Руководитель проекта', N'Фиксация объёма, дополнительное соглашение', N'Изменение задания заказчиком', 0, 0.15, 48.0000, N'Monitoring'),
(@Project1Id, N'R-08', N'Неплатёж заказчика', N'Финансовые трудности заказчика', N'Нехватка финансирования', 0.20, 700.0000, 2, 5, 10, 140.0000, N'Финансовый менеджер', N'График авансирования, банковская гарантия', N'Просрочка поступления более 10 дней', 0, 0.10, 70.0000, N'Treating'),
(@Project1Id, N'R-09', N'Авария, форс-мажор', N'Природные условия, отказ оборудования', N'Перенос сроков, восстановление', 0.10, 800.0000, 1, 5, 5, 80.0000, N'Руководитель организации', N'Страхование, резерв времени', N'Страховое событие', 0, 0.05, 40.0000, N'Monitoring'),
(@Project1Id, N'R-10', N'Ошибка классификации затрат', N'Расхождение справочников', N'Искажение себестоимости, замечания', 0.30, 150.0000, 3, 2, 6, 45.0000, N'Бухгалтер', N'Справочник статей, проверка классификации', N'Расхождение с Инструкцией № 53', 0, 0.15, 22.5000, N'Monitoring'),
(@Project1Id, N'R-11', N'Утечка данных, компрометация учётной записи', N'Слабая аутентификация, ошибка прав доступа', N'Ущерб, блокировка платежей', 0.15, 700.0000, 2, 5, 10, 105.0000, N'Администратор системы', N'Двухфакторная аутентификация, аудит доступа', N'Неудачные входы, аномальные права', 0, 0.08, 56.0000, N'Treating'),
(@Project1Id, N'R-12', N'Отказ ИТ-инфраструктуры, потеря данных', N'Сбой сервера, ошибка резервного копирования', N'Простой, потеря платежей', 0.10, 600.0000, 1, 5, 5, 60.0000, N'Администратор системы', N'Резервное копирование, контроль восстановления', N'Сбой репликации, ошибка backup', 0, 0.05, 30.0000, N'Monitoring');
GO
