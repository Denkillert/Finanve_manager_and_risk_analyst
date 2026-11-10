# Сетевое программное средство управления финансами проектов и анализа рисков в строительной компании («СтройФинРиск»)

Реализация программного комплекса в соответствии со спецификацией курсового проекта (`Наработка общ.docx`, гл. 1–2) на чистом **.NET 9.0 (C# 13 / ASP.NET Core 9 MVC / WPF / EF Core 9 / xUnit)** и **MS SQL Server 2022**.

---

## Архитектура решения

```text
Finanve_manager_and_risk_analyst.sln
├── database/
│   ├── 01_schemas_and_tables.sql          # DDL MS SQL Server 2022 (схемы finance, risk, security, audit; vFreeLimit)
│   └── 02_seed_data.sql                   # Стартовые данные, 8 ролей RBAC, контрольные примеры и 12 рисков (R-01..R-12)
├── src/
│   ├── FinanveManager.Data/               # Единая библиотека классов (.NET 9.0): модели, БД (EF Core 9 + SqlServer/InMemory),
│   │                                      # расчётные модули (Lдост, EVM, Cashflow, P80, С-2/С-3), сервисы, RBAC и AuthService
│   ├── FinanveManager.Web/                # Веб-клиент (ASP.NET Core 9 MVC + Razor Views + Cookie RBAC)
│   └── FinanveManager.WpfClient/          # Настольный клиент (WPF .NET 9 Windows + обязательная форма входа LoginWindow)
└── tests/
    └── FinanveManager.Tests/              # Модульные и интеграционные тесты xUnit (.NET 9.0)
```

Оба клиентских приложения (**`FinanveManager.Web`** и **`FinanveManager.WpfClient`**) подключаются напрямую к библиотеке **`FinanveManager.Data`** без промежуточного API-сервера:
- при наличии MS SQL Server 2022 используется подключение из `ConnectionStrings:DefaultConnection` (`Server=.;Database=FinanveManagerDb;...`);
- при отсутствии MS SQL Server библиотека автоматически переключается на встроенную базу `EF Core InMemory` с начальным заполнением (`DbInitializer.SeedIfEmptyAsync`).

---

## Авторизация и ролевая изоляция (RBAC, табл. 1.8 и НФТ-1)

Оба клиента требуют обязательной авторизации до входа в программу и разделяют роли в соответствии с **НФТ-1** (4 роли в настольном **WPF-клиенте**, 4 роли в **Веб-клиенте**) и **таблицей 2.5**:

| Логин | Роль | Клиент (табл. 1.8) | Доступные разделы интерфейса | Примечание |
| :--- | :--- | :--- | :--- | :--- |
| `director` | Руководитель организации (`CEO`) | **WPF** | Главная панель, Проекты/Бюджеты (утверждение), Заявки (UC04), Календарь, Риски и матрица 5×5, Отчёты | Утверждение версий и перерасхода |
| `accountant` | Бухгалтер (`Accountant`) | **WPF** | Главная панель (огранич.), Платёжные заявки (исполнение ПП), Календарь, Договоры/Авансы (MT940/CSV, 1С) | Без доступа к рискам |
| `supply` | Специалист по снабжению (`ProcurementSpecialist`) | **WPF** | Главная панель (огранич.), Договоры и закупки (регистрация, сметы), Платёжные заявки (создание) | Контроль остатков договоров |
| `admin` | Администратор системы (`Admin`) | **WPF** | Администрирование (пользователи, НСИ, курсы), Журнал аудита (`AuditLogs`) | Код 2FA: `246810`, без доступа к финансам (НФТ-8) |
| `finmanager` | Финансовый менеджер (`FinancialManager`) | **Web** | Главная панель, Проекты/Бюджеты (`Draft`), Заявки (ЭЦП), Календарь (перенос), Договоры и авансы, Отчёты | Корректировка планов |
| `pm` | Руководитель проекта (`ProjectManager`) | **Web** | Главная панель, Проекты/Бюджеты, Заявки, Акты С-2 с фотофиксацией, Риски и P80 | Мониторинг EAC и этапов |
| `risk` | Специалист по рискам (`RiskSpecialist`) | **Web** | Главная панель (огранич.), Риски (матрица 5×5, P80, торнадо), Отчёты (по рискам и EVM) | Переоценка и триггеры |
| `siteworker` | Работник на объекте (`SiteWorker`) | **Web (360 px)** | Этапы строительства и оформление актов С-2 с фотофиксацией | Без финансовых модулей |

Демонстрационный пароль для всех учётных записей: **`StroyFin2026!`** (хэш PBKDF2-SHA256).

> **Примечание по NuGet (.NET 9.0):** в `FinanveManager.Data.csproj` используются пакеты `Microsoft.EntityFrameworkCore.InMemory` и `Microsoft.EntityFrameworkCore.SqlServer` версии **`9.0.0`** — не обновляйте их до версий `10.x`/`11.x` при целевом фреймворке `net9.0`.

---

## Запуск в Visual Studio и из командной строки

1. *(Опционально для MS SQL Server 2022)* Выполните скрипты `database/01_schemas_and_tables.sql` и `database/02_seed_data.sql` в SSMS или `sqlcmd`.
2. Откройте `Finanve_manager_and_risk_analyst.sln` в Visual Studio 2022 (17.12+, `.NET 9 SDK`):
   - **Веб-приложение**: назначьте стартовым **`FinanveManager.Web`** и нажмите **F5** (`https://localhost:5051/Account/Login`).
   - **WPF-приложение**: назначьте стартовым **`FinanveManager.WpfClient`** и нажмите **F5** (откроется окно входа `LoginWindow`).
3. Сборка и тестирование из CLI:
   ```bash
   dotnet restore Finanve_manager_and_risk_analyst.sln
   dotnet build Finanve_manager_and_risk_analyst.sln -c Release
   dotnet test tests/FinanveManager.Tests/FinanveManager.Tests.csproj
   ```
