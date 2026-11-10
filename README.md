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
│   ├── FinanveManager.Infrastructure/     # EF Core 9 (AppDbContext, SqlServer + InMemory fallback), MT940/CSV, 1С, сметы, PBKDF2-SHA256
│   ├── FinanveManager.Web/                # Веб-приложение ASP.NET Core 9 MVC + Razor Views (C#, запуск из Visual Studio по F5) + JWT /api/*
│   ├── FinanveManager.Api/                # ASP.NET Core 9 Web API (TLS 1.3 Kestrel, JWT Bearer, OpenAPI 9, контроллеры)
│   ├── FinanveManager.WpfClient/          # Настольный WPF-клиент (.NET 9 Windows, MVVM) для desktop-ролей
│   └── WebClient/                         # Дополнительный SPA-клиент React 18 + TypeScript + Tailwind CSS (опционально с demo-server)
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

## Быстрый запуск в веб-режиме (Node.js 22.5+ / Linux & Windows)

> **Важно:** для встроенного модуля `node:sqlite` требуется **Node.js 22.5 или выше** (`node -v`). Папка `src/WebClient/dist` исключена из Git, поэтому после клонирования сначала соберите веб-клиент или запустите dev-сервер Vite.

### Вариант А. Запуск всего веб-приложения на одном порту (`http://localhost:5050`)

```bash
# 1. Собрать production-бандл веб-клиента (src/WebClient/dist)
cd src/WebClient
npm install
npm run build

# 2. Запустить REST API и раздачу веб-интерфейса
cd ../../demo-server
npm install
npm run start   # Открыть в браузере: http://localhost:5050
```

### Вариант Б. Режим разработки с hot-reload (два терминала)

1. **Терминал 1 (`demo-server` на порту `5050`):**
   ```bash
   cd demo-server
   npm install
   npm run typecheck
   npm test        # 13 модульных и интеграционных тестов (Vitest + Supertest)
   npm run dev     # API на http://localhost:5050
   ```
2. **Терминал 2 (`src/WebClient` на порту `5173`):**
   ```bash
   cd src/WebClient
   npm install
   npm run dev     # Открыть в браузере: http://localhost:5173 (прокси /api -> 127.0.0.1:5050)
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

## Сборка и запуск решения .NET 9.0 в Visual Studio (ASP.NET Core Web + WPF + MS SQL Server 2022)

1. *(Опционально для MS SQL Server 2022)* Выполнить SQL-скрипты развёртывания БД в SSMS или `sqlcmd`:
   ```bash
   sqlcmd -S localhost -i database/01_schemas_and_tables.sql
   sqlcmd -S localhost -i database/02_seed_data.sql
   ```
   > **Автономный режим (`InMemory`):** если MS SQL Server недоступен или база ещё не развёрнута, проекты **`FinanveManager.Web`** и **`FinanveManager.Api`** автоматически переключаются на встроенную БД `EF Core InMemory` и заполняют её всеми контрольными данными (`DbInitializer.SeedIfEmptyAsync`).
2. **Запуск веб-приложения на C# (`FinanveManager.Web`) из Visual Studio:**
   - Откройте `Finanve_manager_and_risk_analyst.sln` в Visual Studio 2022 (17.12+ / .NET 9 SDK).
   - В обозревателе решений щёлкните правой кнопкой по **`FinanveManager.Web`** → **Назначить запускаемым проектом (Set as Startup Project)** и нажмите **F5** (или выполните `dotnet run --project src/FinanveManager.Web`).
   - Браузер откроет страницу входа `https://localhost:5051/Account/Login` (или `http://localhost:5050`) с быстрым входом по всем 8 ролям (`StroyFin2026!`, для `admin` — 2FA `246810`).
   - Веб-приложение `FinanveManager.Web` также предоставляет JWT REST-эндпоинты `/api/auth/login` и `/api/payment-requests`, поэтому к нему можно подключаться и из настольного **`FinanveManager.WpfClient`**.
3. **Запуск API и/или WPF-клиента из командной строки или Visual Studio:**
   ```bash
   dotnet restore Finanve_manager_and_risk_analyst.sln
   dotnet build Finanve_manager_and_risk_analyst.sln -c Release
   dotnet test tests/FinanveManager.Tests/FinanveManager.Tests.csproj
   dotnet run --project src/FinanveManager.Web     # Веб-приложение ASP.NET Core 9 MVC (https://localhost:5051)
   dotnet run --project src/FinanveManager.Api     # Отдельный Web API (https://localhost:5001)
   dotnet run --project src/FinanveManager.WpfClient # WPF-клиент (Windows; работает как с сервером, так и автономно)
   ```
