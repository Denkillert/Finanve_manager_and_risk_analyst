# Сетевое программное средство управления финансами проектов и анализа рисков в строительной компании («СтройФинРиск»)

Реализация программного комплекса в соответствии со спецификацией курсового проекта (`Наработка общ.docx`, гл. 1–2): многослойное решение на **.NET 9.0 (C# 13 / ASP.NET Core 9 / EF Core 9 / WPF MVVM / xUnit)**, скрипты развёртывания **MS SQL Server 2022**, а также адаптивный **веб-клиент (React + TypeScript + Vite)** и **демонстрационный REST-сервер (Node.js + TypeScript + SQLite)** для запуска, интеграционного тестирования и живого предпросмотра.

---

## Структура решения и репозитория

```text
Finanve_manager_and_risk_analyst.sln
├── database/
│   ├── 01_schemas_and_tables.sql          # DDL MS SQL Server 2022 (схемы finance, risk, security, audit; vFreeLimit)
│   └── 02_seed_data.sql                   # Стартовые данные, 8 ролей RBAC, контрольные примеры гл. 1 и 12 рисков (R-01..R-12)
├── src/
│   ├── FinanveManager.Domain/             # Сущности предметной области, Enums, калькуляторы (Lдост, EVM, Cashflow, P80, С-2/С-3)
│   ├── FinanveManager.Application/        # DTO, интерфейсы, WorkflowEngine, BudgetService, PaymentRequestService, RiskService
│   ├── FinanveManager.Infrastructure/     # EF Core 9 (AppDbContext), MT940/CSV, 1С, сметы XML/CSV, ЭЦП, PBKDF2-SHA256
│   ├── FinanveManager.Api/                # ASP.NET Core 9 Web API (TLS 1.3 Kestrel, JWT Bearer, OpenAPI 9, контроллеры)
│   ├── FinanveManager.WpfClient/          # Настольный WPF-клиент (.NET 9 Windows, MVVM) для desktop-ролей
│   └── WebClient/                         # Адаптивный веб-клиент React 18 + TypeScript + Tailwind CSS (от 360 px)
├── tests/
│   └── FinanveManager.Tests/              # Модульные и интеграционные тесты xUnit (.NET 9.0)
└── demo-server/                           # Локальный Node.js/TypeScript + SQLite REST-сервер (XLSX/PDF, MT940, Vitest)
```

---

## Реализованный функционал и контрольные ориентиры

- **Иерархия и бюджетирование**: `Организация → Портфель → Проект → Очередь → Объект → Этап (PV, EV, AC)`; версионирование бюджетов (`Draft → Approved → Archived`) с неизменяемостью утверждённых версий и представлением `vFreeLimit` (`Lдост = B − O`, `Oост = O − F`, неисполненные заявки `Q` не вычитаются повторно).
- **Платёжные заявки и документооборот (УРП / С-2 / С-3)**: автоматический контроль лимита `Lдост`, контроль авансов (блокировка нового аванса при просроченном; предупреждение по текущему платежу), защита от дублей по `(Type, Number, Date)`, оптимистическая блокировка (`RowVersion`), UC04 (согласование перерасхода руководителем организации), автоматическое отклонение заявок в `Blocked` старше 30 дней, ЭЦП при утверждении, расчёт актов по формуле `Sопл = Sакт − Aзач − Uуд` с прикреплением фотофиксации.
- **Платёжный календарь и интеграции**: 90-дневный расчёт `Cash_t = Cash_0 + ΣIn − ΣOut` и кассовых разрывов `Gap_t = max(0, Cash_min − Cash_t)`, предупреждение о просрочке заказчика `> 10` дней (R-08), перенос даты платежа, идемпотентный импорт банковских выписок `MT940` и `CSV` (по `BankOperationId`), импорт смет `XML/CSV`, экспорт проводок `1С (CSV)` и выгрузка 5 отчётов в `.xlsx` и `.pdf`.
- **Анализ рисков и чувствительности**: матрица `5×5` (`R = p_б · I_б`, порог `R ≤ 9`), `EMV = p · I`, расчёт резерва `P80 = ΣEMV + 0.84 · σ` (`1948 тыс. р.` для `R-01..R-12`), остаточный `EMV` (`715 тыс. р.`), 4-факторный анализ чувствительности EAC («Торнадо»).
- **Безопасность (RBAC — 8 ролей)**: `Руководитель организации`, `Финансовый менеджер`, `Бухгалтер`, `Специалист по снабжению`, `Руководитель проекта`, `Специалист по рискам`, `Администратор системы` (с обязательным 2FA, без прав финансовых операций), `Работник на объекте`; PBKDF2-SHA256 (≥ 12 символов), блокировка после 5 неудачных попыток, неизменяемый `AuditLogs`.

---

## Быстрый запуск в веб-режиме (Node.js 22+ / Linux & Windows)

### 1. Запуск REST API и тестов (`demo-server`)

```bash
cd demo-server
npm install
npm run typecheck
npm test        # 13 модульных и интеграционных тестов (Vitest + Supertest)
npm run dev     # Запуск сервера на http://0.0.0.0:5050 (раздаёт собранный WebClient/dist)
```

### 2. Запуск веб-клиента в режиме разработки (`src/WebClient`)

```bash
cd src/WebClient
npm install
npm run build   # Сборка production bundle в src/WebClient/dist
npm run dev     # Vite dev-сервер на http://0.0.0.0:5173 с прокси /api -> http://127.0.0.1:5050
```

### Демонстрационные учётные записи (пароль для всех: `StroyFin2026!`)

| Логин | Роль | Клиент | Примечание |
| :--- | :--- | :--- | :--- |
| `finmanager` | Финансовый менеджер | Веб | Лимиты, согласование с ЭЦП, отчётность |
| `director` | Руководитель организации | WPF / Веб | Утверждение бюджета и перерасхода (UC04) |
| `pm` | Руководитель проекта | Веб | Заявки, акты С-2/С-3, прогноз EAC |
| `risk` | Специалист по рискам | Веб | Матрица 5×5, переоценка, P80 |
| `accountant` | Бухгалтер | WPF / Веб | Исполнение платежей, импорт MT940/CSV, 1С |
| `supply` | Специалист по снабжению | WPF / Веб | Договоры, авансы, заявки на материалы |
| `siteworker` | Работник на объекте | Веб (360 px) | Подготовка С-2 и фотофиксация работ |
| `admin` | Администратор системы | WPF / Веб | Пользователи, правила, аудит (код 2FA: `246810`) |

---

## Сборка и запуск решения .NET 9.0 и MS SQL Server 2022 (Windows / SDK .NET 9.0)

1. Выполнить SQL-скрипты развёртывания БД в MS SQL Server 2022 (в SSMS или `sqlcmd`):
   ```bash
   sqlcmd -S localhost -i database/01_schemas_and_tables.sql
   sqlcmd -S localhost -i database/02_seed_data.sql
   ```
2. Настроить строку подключения в `src/FinanveManager.Api/appsettings.json`.
3. Выполнить восстановление, сборку и прогон модульных тестов xUnit:
   ```bash
   dotnet restore Finanve_manager_and_risk_analyst.sln
   dotnet build Finanve_manager_and_risk_analyst.sln -c Release
   dotnet test tests/FinanveManager.Tests/FinanveManager.Tests.csproj
   ```
4. Запустить API (`dotnet run --project src/FinanveManager.Api`, спецификация OpenAPI доступна в режиме Development по пути `/openapi/v1.json`) и, при необходимости на Windows, настольный WPF-клиент (`dotnet run --project src/FinanveManager.WpfClient`).
