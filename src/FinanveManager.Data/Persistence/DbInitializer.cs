using FinanveManager.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Data.Persistence;

public static class SeedIds
{
    public static readonly Guid Org = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Portfolio = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid Project1 = Guid.Parse("30000000-0000-0000-0000-000000000001");
    public static readonly Guid Project2 = Guid.Parse("30000000-0000-0000-0000-000000000002");

    public static readonly Guid Obj1 = Guid.Parse("40000000-0000-0000-0000-000000000001");
    public static readonly Guid Obj2 = Guid.Parse("40000000-0000-0000-0000-000000000002");
    public static readonly Guid Obj3 = Guid.Parse("40000000-0000-0000-0000-000000000003");

    public static readonly Guid ItemMat = Guid.Parse("50000000-0000-0000-0000-000000000001");
    public static readonly Guid ItemSub = Guid.Parse("50000000-0000-0000-0000-000000000002");
    public static readonly Guid ItemEq = Guid.Parse("50000000-0000-0000-0000-000000000003");
    public static readonly Guid ItemOvr = Guid.Parse("50000000-0000-0000-0000-000000000004");

    public static readonly Guid RoleCeo = Guid.Parse("60000000-0000-0000-0000-000000000001");
    public static readonly Guid RoleFin = Guid.Parse("60000000-0000-0000-0000-000000000002");
    public static readonly Guid RoleAcc = Guid.Parse("60000000-0000-0000-0000-000000000003");
    public static readonly Guid RoleProc = Guid.Parse("60000000-0000-0000-0000-000000000004");
    public static readonly Guid RolePm = Guid.Parse("60000000-0000-0000-0000-000000000005");
    public static readonly Guid RoleRisk = Guid.Parse("60000000-0000-0000-0000-000000000006");
    public static readonly Guid RoleAdmin = Guid.Parse("60000000-0000-0000-0000-000000000007");
    public static readonly Guid RoleSite = Guid.Parse("60000000-0000-0000-0000-000000000008");

    public static readonly Guid UserCeo = Guid.Parse("70000000-0000-0000-0000-000000000001");
    public static readonly Guid UserFin = Guid.Parse("70000000-0000-0000-0000-000000000002");
    public static readonly Guid UserAcc = Guid.Parse("70000000-0000-0000-0000-000000000003");
    public static readonly Guid UserProc = Guid.Parse("70000000-0000-0000-0000-000000000004");
    public static readonly Guid UserPm = Guid.Parse("70000000-0000-0000-0000-000000000005");
    public static readonly Guid UserRisk = Guid.Parse("70000000-0000-0000-0000-000000000006");
    public static readonly Guid UserAdmin = Guid.Parse("70000000-0000-0000-0000-000000000007");
    public static readonly Guid UserSite = Guid.Parse("70000000-0000-0000-0000-000000000008");

    public static readonly Guid Bv1 = Guid.Parse("80000000-0000-0000-0000-000000000001");
    public static readonly Guid Bv2 = Guid.Parse("80000000-0000-0000-0000-000000000002");

    public static readonly Guid ContractCust = Guid.Parse("90000000-0000-0000-0000-000000000001");
    public static readonly Guid ContractMat = Guid.Parse("90000000-0000-0000-0000-000000000002");
    public static readonly Guid ContractSub = Guid.Parse("90000000-0000-0000-0000-000000000003");
    public static readonly Guid ContractDdu = Guid.Parse("90000000-0000-0000-0000-000000000004");

    public const string DefaultDemoPasswordHash = "U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw=";
}

public sealed class AppDatabaseFactory
{
    public bool UsesSqlServer { get; }
    public string ProviderLabel => UsesSqlServer ? "MS SQL Server 2022" : "InMemory (Локальная БД)";
    private readonly DbContextOptions<AppDbContext> _options;

    public AppDatabaseFactory(string? connectionString, string inMemoryName = "FinanveManagerSharedDb")
    {
        UsesSqlServer = DbInitializer.CanUseSqlServer(connectionString);
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (UsesSqlServer && !string.IsNullOrWhiteSpace(connectionString))
            builder.UseSqlServer(connectionString);
        else
            builder.UseInMemoryDatabase(inMemoryName);
        _options = builder.Options;
    }

    public AppDbContext CreateContext() => new(_options);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var db = CreateContext();
        await DbInitializer.SeedIfEmptyAsync(db, ct);
    }
}

public static class DbInitializer
{
    public static bool CanUseSqlServer(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            var probed = connectionString.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase)
                ? connectionString
                : $"{connectionString.TrimEnd(';')};Connect Timeout=2";
            var opts = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(probed)
                .Options;
            using var ctx = new AppDbContext(opts);
            if (!ctx.Database.CanConnect()) return false;
            _ = ctx.Roles.Any();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SeedIfEmptyAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Roles.AnyAsync(ct))
            return;

        var baseDate = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

        var roles = new List<Role>
        {
            new() { Id = SeedIds.RoleCeo, Code = "CEO", Name = "Руководитель организации", ClientType = "WPF", Description = "Портфель, ликвидность, утверждение бюджетов и перерасхода (UC04), критические риски" },
            new() { Id = SeedIds.RoleFin, Code = "FinancialManager", Name = "Финансовый менеджер", ClientType = "Web", Description = "Бюджеты, платёжный календарь, согласование заявок с ЭЦП, контроль авансов и разрывов" },
            new() { Id = SeedIds.RoleAcc, Code = "Accountant", Name = "Бухгалтер", ClientType = "WPF", Description = "Исполнение платежей, загрузка банковских выписок MT940/CSV, учёт факта и 1С" },
            new() { Id = SeedIds.RoleProc, Code = "ProcurementSpecialist", Name = "Специалист по снабжению", ClientType = "WPF", Description = "Договоры, спецификации, заявки на закупки, контроль поставок" },
            new() { Id = SeedIds.RolePm, Code = "ProjectManager", Name = "Руководитель проекта", ClientType = "Web", Description = "Создание заявок, акты С-2/С-3 с фотофиксацией, прогноз EAC, регистрация рисков" },
            new() { Id = SeedIds.RoleRisk, Code = "RiskSpecialist", Name = "Специалист по рискам", ClientType = "Web", Description = "Реестр рисков 5×5, оценка EMV, σ и резерва P80, триггеры, чувствительность EAC" },
            new() { Id = SeedIds.RoleAdmin, Code = "Admin", Name = "Администратор системы", ClientType = "WPF", Description = "Пользователи, роли, справочники, нормативные правила, аудит (2FA; фин. операции запрещены)" },
            new() { Id = SeedIds.RoleSite, Code = "SiteWorker", Name = "Работник на объекте", ClientType = "Web", Description = "Просмотр этапов строительства, подготовка актов С-2 и фотофиксация" }
        };
        await db.Roles.AddRangeAsync(roles, ct);

        var users = new List<User>
        {
            new() { Id = SeedIds.UserCeo, RoleId = SeedIds.RoleCeo, Login = "director", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Ковалёв Андрей Сергеевич", Email = "director@stroyfin.by", IsActive = true, TwoFactorEnabled = false, PersonalDataConsentGiven = true, ConsentDate = baseDate },
            new() { Id = SeedIds.UserFin, RoleId = SeedIds.RoleFin, Login = "finmanager", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Петрова Елена Викторовна", Email = "finmanager@stroyfin.by", IsActive = true, TwoFactorEnabled = false, PersonalDataConsentGiven = true, ConsentDate = baseDate },
            new() { Id = SeedIds.UserAcc, RoleId = SeedIds.RoleAcc, Login = "accountant", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Сидорова Наталья Ивановна", Email = "accountant@stroyfin.by", IsActive = true, TwoFactorEnabled = false, PersonalDataConsentGiven = true, ConsentDate = baseDate },
            new() { Id = SeedIds.UserProc, RoleId = SeedIds.RoleProc, Login = "supply", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Новиков Дмитрий Олегович", Email = "supply@stroyfin.by", IsActive = true, TwoFactorEnabled = false, PersonalDataConsentGiven = true, ConsentDate = baseDate },
            new() { Id = SeedIds.UserPm, RoleId = SeedIds.RolePm, Login = "pm", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Громов Алексей Николаевич", Email = "pm@stroyfin.by", IsActive = true, TwoFactorEnabled = false, PersonalDataConsentGiven = true, ConsentDate = baseDate },
            new() { Id = SeedIds.UserRisk, RoleId = SeedIds.RoleRisk, Login = "risk", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Волкова Марина Петровна", Email = "risk@stroyfin.by", IsActive = true, TwoFactorEnabled = false, PersonalDataConsentGiven = true, ConsentDate = baseDate },
            new() { Id = SeedIds.UserAdmin, RoleId = SeedIds.RoleAdmin, Login = "admin", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Титов Сергей Владимирович", Email = "admin@stroyfin.by", IsActive = true, TwoFactorEnabled = true, TwoFactorSecret = "246810", PersonalDataConsentGiven = true, ConsentDate = baseDate },
            new() { Id = SeedIds.UserSite, RoleId = SeedIds.RoleSite, Login = "siteworker", PasswordHash = SeedIds.DefaultDemoPasswordHash, FullName = "Орлов Максим Игоревич", Email = "site@stroyfin.by", IsActive = true, TwoFactorEnabled = false, PersonalDataConsentGiven = true, ConsentDate = baseDate }
        };
        await db.Users.AddRangeAsync(users, ct);

        await db.Organizations.AddAsync(new Organization
        {
            Id = SeedIds.Org,
            Name = "ОАО «МинскСтройКомплекс»",
            Unp = "100123456"
        }, ct);

        await db.Portfolios.AddAsync(new Portfolio
        {
            Id = SeedIds.Portfolio,
            OrganizationId = SeedIds.Org,
            Name = "Жилищное и гражданское строительство 2026"
        }, ct);

        await db.Projects.AddRangeAsync(new List<Project>
        {
            new()
            {
                Id = SeedIds.Project1,
                PortfolioId = SeedIds.Portfolio,
                Code = "PRJ-2026-01",
                Name = "ЖК «Лошицкий квартал», жилые дома № 4 и № 5",
                ContractValue = 14500m,
                Currency = "BYN",
                InitialCash = 2000m,
                MinCashBalance = 1000m,
                PlannedStartDate = new DateTime(2026, 1, 15),
                PlannedEndDate = new DateTime(2026, 12, 30)
            },
            new()
            {
                Id = SeedIds.Project2,
                PortfolioId = SeedIds.Portfolio,
                Code = "PRJ-2026-02",
                Name = "Детский сад на 230 мест в м-не Каменная Горка",
                ContractValue = 8900m,
                Currency = "BYN",
                InitialCash = 1200m,
                MinCashBalance = 500m,
                PlannedStartDate = new DateTime(2026, 3, 1),
                PlannedEndDate = new DateTime(2027, 6, 30)
            }
        }, ct);

        await db.ConstructionObjects.AddRangeAsync(new List<ConstructionObject>
        {
            new() { Id = SeedIds.Obj1, ProjectId = SeedIds.Project1, QueueName = "1-я очередь", Name = "Жилой дом № 4 (серия М464-У1)", Address = "г. Минск, ул. Игуменский тракт, 42" },
            new() { Id = SeedIds.Obj2, ProjectId = SeedIds.Project1, QueueName = "2-я очередь", Name = "Жилой дом № 5 и инженерные сети", Address = "г. Минск, ул. Игуменский тракт, 44" },
            new() { Id = SeedIds.Obj3, ProjectId = SeedIds.Project2, QueueName = "1-я очередь", Name = "Здание ДДУ с бассейном", Address = "г. Минск, ул. Налибокская, 18" }
        }, ct);

        await db.WorkStages.AddRangeAsync(new List<WorkStage>
        {
            new() { Id = Guid.Parse("41000000-0000-0000-0000-000000000001"), ConstructionObjectId = SeedIds.Obj1, Name = "Нулевой цикл и фундамент", PlannedStartDate = new DateTime(2026, 1, 15), PlannedEndDate = new DateTime(2026, 5, 30), PlannedValuePV = 1800m, EarnedValueEV = 1750m, ActualCostAC = 1850m },
            new() { Id = Guid.Parse("41000000-0000-0000-0000-000000000002"), ConstructionObjectId = SeedIds.Obj1, Name = "Монтаж железобетонных панелей", PlannedStartDate = new DateTime(2026, 6, 1), PlannedEndDate = new DateTime(2026, 10, 30), PlannedValuePV = 1600m, EarnedValueEV = 1250m, ActualCostAC = 1450m },
            new() { Id = Guid.Parse("41000000-0000-0000-0000-000000000003"), ConstructionObjectId = SeedIds.Obj2, Name = "Наружные инженерные сети", PlannedStartDate = new DateTime(2026, 7, 1), PlannedEndDate = new DateTime(2026, 12, 20), PlannedValuePV = 0m, EarnedValueEV = 0m, ActualCostAC = 0m },
            new() { Id = Guid.Parse("41000000-0000-0000-0000-000000000004"), ConstructionObjectId = SeedIds.Obj3, Name = "Земляные работы и фундамент ДДУ", PlannedStartDate = new DateTime(2026, 3, 1), PlannedEndDate = new DateTime(2026, 9, 30), PlannedValuePV = 2100m, EarnedValueEV = 2000m, ActualCostAC = 1950m }
        }, ct);

        await db.CostItems.AddRangeAsync(new List<CostItem>
        {
            new() { Id = SeedIds.ItemMat, Code = "ST-01", Name = "Строительные материалы и конструкции (ЖБИ, арматура, цемент)", Category = "Материалы" },
            new() { Id = SeedIds.ItemSub, Code = "ST-02", Name = "Субподрядные монтажные и специальные работы", Category = "Субподряд" },
            new() { Id = SeedIds.ItemEq, Code = "ST-03", Name = "Инженерное и технологическое оборудование", Category = "Оборудование" },
            new() { Id = SeedIds.ItemOvr, Code = "ST-04", Name = "Накладные расходы, транспорт и механизмы", Category = "Накладные" }
        }, ct);

        await db.BudgetVersions.AddRangeAsync(new List<BudgetVersion>
        {
            new() { Id = SeedIds.Bv1, ProjectId = SeedIds.Project1, VersionNumber = 1, Status = BudgetVersionStatus.Approved, ApprovedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), ApprovedByUserId = SeedIds.UserCeo, Comment = "Утверждённый бюджет проекта (BAC = 11 000 тыс. р.)" },
            new() { Id = SeedIds.Bv2, ProjectId = SeedIds.Project2, VersionNumber = 1, Status = BudgetVersionStatus.Approved, ApprovedAt = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), ApprovedByUserId = SeedIds.UserCeo, Comment = "Утверждённый бюджет ДДУ (статья ST-02 = 10 000 тыс. р.)" }
        }, ct);

        await db.BudgetLimits.AddRangeAsync(new List<BudgetLimit>
        {
            new() { Id = Guid.Parse("81000000-0000-0000-0000-000000000001"), BudgetVersionId = SeedIds.Bv1, ConstructionObjectId = SeedIds.Obj1, CostItemId = SeedIds.ItemMat, PeriodStart = new DateTime(2026, 1, 1), PeriodEnd = new DateTime(2026, 12, 31), Amount = 10000m },
            new() { Id = Guid.Parse("81000000-0000-0000-0000-000000000002"), BudgetVersionId = SeedIds.Bv1, ConstructionObjectId = SeedIds.Obj1, CostItemId = SeedIds.ItemOvr, PeriodStart = new DateTime(2026, 1, 1), PeriodEnd = new DateTime(2026, 12, 31), Amount = 1000m },
            new() { Id = Guid.Parse("81000000-0000-0000-0000-000000000003"), BudgetVersionId = SeedIds.Bv2, ConstructionObjectId = SeedIds.Obj3, CostItemId = SeedIds.ItemSub, PeriodStart = new DateTime(2026, 3, 1), PeriodEnd = new DateTime(2027, 6, 30), Amount = 10000m }
        }, ct);

        await db.Contracts.AddRangeAsync(new List<Contract>
        {
            new() { Id = SeedIds.ContractCust, ProjectId = SeedIds.Project1, Number = "ГП-2026/01", Counterparty = "УКС Мингорисполкома", CounterpartyUnp = "100234567", Direction = ContractDirection.CustomerIncoming, TotalAmount = 14500m, VatIncluded = true, VatRate = 20m, Currency = "BYN", StartDate = new DateTime(2026, 1, 10), EndDate = new DateTime(2026, 12, 31), RetentionPercent = 5m, SpecificationNotes = "Генподрядный договор ЖК «Лошицкий квартал»" },
            new() { Id = SeedIds.ContractMat, ProjectId = SeedIds.Project1, Number = "П-2026/14", Counterparty = "ОАО «Белорусский цементный завод»", CounterpartyUnp = "700012345", Direction = ContractDirection.SupplierSubcontractorOutgoing, TotalAmount = 9500m, VatIncluded = true, VatRate = 20m, Currency = "BYN", StartDate = new DateTime(2026, 1, 20), EndDate = new DateTime(2026, 12, 31), RetentionPercent = 0m, SpecificationNotes = "Поставка ЖБИ, цемента и товарного бетона" },
            new() { Id = SeedIds.ContractSub, ProjectId = SeedIds.Project1, Number = "СП-2026/09", Counterparty = "ООО «СпецМонтажСтрой»", CounterpartyUnp = "191234567", Direction = ContractDirection.SupplierSubcontractorOutgoing, TotalAmount = 3500m, VatIncluded = true, VatRate = 20m, Currency = "BYN", StartDate = new DateTime(2026, 2, 1), EndDate = new DateTime(2026, 12, 31), RetentionPercent = 5m, SpecificationNotes = "Монтаж сетей и спецработы" },
            new() { Id = SeedIds.ContractDdu, ProjectId = SeedIds.Project2, Number = "СП-2026/22", Counterparty = "ОАО «СтройТрест № 7»", CounterpartyUnp = "100998877", Direction = ContractDirection.SupplierSubcontractorOutgoing, TotalAmount = 12000m, VatIncluded = true, VatRate = 20m, Currency = "BYN", StartDate = new DateTime(2026, 3, 5), EndDate = new DateTime(2027, 6, 30), RetentionPercent = 5m, SpecificationNotes = "Субподряд по строительству ДДУ" }
        }, ct);

        await db.ContractSupplements.AddAsync(new ContractSupplement
        {
            Id = Guid.Parse("91000000-0000-0000-0000-000000000001"),
            ContractId = SeedIds.ContractMat,
            Number = "ДС-1",
            AmountDelta = 500m,
            SignedDate = new DateTime(2026, 6, 15),
            Reason = "Дополнительная поставка железобетонных плит перекрытия"
        }, ct);

        await db.Obligations.AddRangeAsync(new List<Obligation>
        {
            new() { Id = Guid.Parse("92000000-0000-0000-0000-000000000001"), ContractId = SeedIds.ContractMat, ConstructionObjectId = SeedIds.Obj1, CostItemId = SeedIds.ItemMat, GrossAmount = 6000m, PaidAmount = 3000m, Status = ObligationStatus.PartiallyPaid },
            new() { Id = Guid.Parse("92000000-0000-0000-0000-000000000002"), ContractId = SeedIds.ContractDdu, ConstructionObjectId = SeedIds.Obj3, CostItemId = SeedIds.ItemSub, GrossAmount = 10500m, PaidAmount = 7000m, Status = ObligationStatus.PartiallyPaid }
        }, ct);

        await db.Advances.AddRangeAsync(new List<Advance>
        {
            new() { Id = Guid.Parse("93000000-0000-0000-0000-000000000001"), ContractId = SeedIds.ContractMat, ConstructionObjectId = SeedIds.Obj1, CostItemId = SeedIds.ItemMat, Amount = 1200m, UsedAmount = 900m, IssuedDate = new DateTime(2026, 9, 1), DueDate = new DateTime(2026, 12, 20), Status = AdvanceStatus.Active, FundingSource = "Собственные средства" },
            new() { Id = Guid.Parse("93000000-0000-0000-0000-000000000002"), ContractId = SeedIds.ContractSub, ConstructionObjectId = SeedIds.Obj2, CostItemId = SeedIds.ItemSub, Amount = 1500m, UsedAmount = 1000m, IssuedDate = new DateTime(2026, 8, 1), DueDate = new DateTime(2026, 10, 15), Status = AdvanceStatus.Overdue, FundingSource = "Кредитная линия" }
        }, ct);

        await db.ContractScheduleItems.AddRangeAsync(new List<ContractScheduleItem>
        {
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000000"), ContractId = SeedIds.ContractCust, PlannedDate = new DateTime(2026, 10, 25), Amount = 850m, IsIncoming = true, IsPaid = false, Description = "Оплата заказчиком акта С-2 № 08 (просрочено > 10 дн.)" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000001"), ContractId = SeedIds.ContractMat, PlannedDate = new DateTime(2026, 11, 11), Amount = 300m, IsIncoming = false, IsPaid = false, Description = "Поставка цемента (партия № 12)" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000002"), ContractId = SeedIds.ContractCust, PlannedDate = new DateTime(2026, 11, 12), Amount = 1000m, IsIncoming = true, IsPaid = false, Description = "Поступление от заказчика по С-3 № 10" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000003"), ContractId = SeedIds.ContractMat, PlannedDate = new DateTime(2026, 11, 12), Amount = 400m, IsIncoming = false, IsPaid = false, Description = "Оплата арматуры и плит" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000004"), ContractId = SeedIds.ContractSub, PlannedDate = new DateTime(2026, 11, 13), Amount = 1800m, IsIncoming = false, IsPaid = false, Description = "Расчёт с субподрядчиком по этапу монтажа" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000005"), ContractId = SeedIds.ContractCust, PlannedDate = new DateTime(2026, 11, 14), Amount = 600m, IsIncoming = true, IsPaid = false, Description = "Целевой аванс заказчика на оборудование" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000006"), ContractId = SeedIds.ContractMat, PlannedDate = new DateTime(2026, 11, 14), Amount = 200m, IsIncoming = false, IsPaid = false, Description = "Транспортные услуги и бетон" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000007"), ContractId = SeedIds.ContractMat, PlannedDate = new DateTime(2026, 11, 15), Amount = 100m, IsIncoming = false, IsPaid = false, Description = "Прочие материалы" },
            new() { Id = Guid.Parse("94000000-0000-0000-0000-000000000008"), ContractId = SeedIds.ContractCust, PlannedDate = new DateTime(2026, 11, 20), Amount = 2200m, IsIncoming = true, IsPaid = false, Description = "Оплата заказчиком по акту С-2 № 11" }
        }, ct);

        await db.PaymentRequests.AddRangeAsync(new List<PaymentRequest>
        {
            new()
            {
                Id = Guid.Parse("95000000-0000-0000-0000-000000000001"),
                ProjectId = SeedIds.Project1, ConstructionObjectId = SeedIds.Obj1, ContractId = SeedIds.ContractMat, CostItemId = SeedIds.ItemMat,
                BudgetVersionId = SeedIds.Bv1, AuthorUserId = SeedIds.UserPm, Number = "ЗП-20261110-0001",
                Amount = 1000m, ActAmount = 1000m, AdvanceOffset = 0m, RetentionAmount = 0m, PayableAmount = 1000m,
                VatIncluded = true, VatRate = 20m, VatAmount = 166.67m, Currency = "BYN", ExchangeRate = 1m, AmountInByn = 1000m,
                Type = RequestType.CurrentPayment, Status = RequestStatus.PendingApproval,
                PlannedPaymentDate = new DateTime(2026, 11, 18), Purpose = "Оплата поставки плит перекрытия по накладной № 412",
                SubmittedAt = baseDate, CreatedAt = baseDate, UpdatedAt = baseDate
            },
            new()
            {
                Id = Guid.Parse("95000000-0000-0000-0000-000000000002"),
                ProjectId = SeedIds.Project2, ConstructionObjectId = SeedIds.Obj3, ContractId = SeedIds.ContractDdu, CostItemId = SeedIds.ItemSub,
                BudgetVersionId = SeedIds.Bv2, AuthorUserId = SeedIds.UserPm, Number = "ЗП-20261110-0002",
                Amount = 1500m, ActAmount = 1500m, AdvanceOffset = 0m, RetentionAmount = 0m, PayableAmount = 1500m,
                VatIncluded = true, VatRate = 20m, VatAmount = 250m, Currency = "BYN", ExchangeRate = 1m, AmountInByn = 1500m,
                Type = RequestType.CurrentPayment, Status = RequestStatus.Blocked,
                PlannedPaymentDate = new DateTime(2026, 11, 19), Purpose = "Оплата монтажа кровли ДДУ (превышение лимита ST-02)",
                BlockReason = "Недостаточно свободного лимита: Lдост = -500.00 тыс. р. (Δ = +500.00)",
                SubmittedAt = baseDate, BlockedAt = baseDate, CreatedAt = baseDate, UpdatedAt = baseDate
            },
            new()
            {
                Id = Guid.Parse("95000000-0000-0000-0000-000000000003"),
                ProjectId = SeedIds.Project1, ConstructionObjectId = SeedIds.Obj1, ContractId = SeedIds.ContractMat, CostItemId = SeedIds.ItemOvr,
                BudgetVersionId = SeedIds.Bv1, AuthorUserId = SeedIds.UserProc, Number = "ЗП-20261110-0003",
                Amount = 1200m, ActAmount = 1200m, AdvanceOffset = 300m, RetentionAmount = 50m, PayableAmount = 850m,
                VatIncluded = true, VatRate = 20m, VatAmount = 200m, Currency = "BYN", ExchangeRate = 1m, AmountInByn = 850m,
                Type = RequestType.CurrentPayment, Status = RequestStatus.Approved,
                PlannedPaymentDate = new DateTime(2026, 11, 16), Purpose = "Оплата по акту С-2 № 18 за вычетом аванса 300 и удержания 50",
                SubmittedAt = baseDate, ApprovedAt = baseDate, CreatedAt = baseDate, UpdatedAt = baseDate
            }
        }, ct);

        await db.Documents.AddRangeAsync(new List<Document>
        {
            new() { Id = Guid.Parse("96000000-0000-0000-0000-000000000001"), PaymentRequestId = Guid.Parse("95000000-0000-0000-0000-000000000001"), ContractId = SeedIds.ContractMat, ConstructionObjectId = SeedIds.Obj1, Type = DocumentType.Invoice, Number = "ТТН-412", Date = new DateTime(2026, 11, 8), GrossAmount = 1000m, NetAmount = 833.33m, VatIncluded = true, Currency = "BYN", ActWorkDescription = "Поставка плит перекрытия", ContractorSigner = "Белорусский цементный завод", CustomerSigner = "Громов А.Н.", IsVerifiedByInstruction53 = true },
            new() { Id = Guid.Parse("96000000-0000-0000-0000-000000000002"), PaymentRequestId = Guid.Parse("95000000-0000-0000-0000-000000000002"), ContractId = SeedIds.ContractDdu, ConstructionObjectId = SeedIds.Obj3, Type = DocumentType.ActS2, Number = "С2-09/26", Date = new DateTime(2026, 11, 9), GrossAmount = 1500m, NetAmount = 1250m, VatIncluded = true, Currency = "BYN", ActWorkDescription = "Монтажные работы по кровле", ContractorSigner = "СтройТрест № 7", CustomerSigner = "Громов А.Н.", IsVerifiedByInstruction53 = true },
            new() { Id = Guid.Parse("96000000-0000-0000-0000-000000000003"), PaymentRequestId = Guid.Parse("95000000-0000-0000-0000-000000000003"), ContractId = SeedIds.ContractMat, ConstructionObjectId = SeedIds.Obj1, Type = DocumentType.ActS2, Number = "С2-18/26", Date = new DateTime(2026, 11, 7), GrossAmount = 1200m, NetAmount = 1000m, VatIncluded = true, Currency = "BYN", AdvanceOffset = 300m, RetentionAmount = 50m, ActWorkDescription = "Сдача-приёмка монтажа панелей 4–6 эт.", ContractorSigner = "СпецМонтажСтрой", CustomerSigner = "Громов А.Н.", IsVerifiedByInstruction53 = true }
        }, ct);

        await db.CurrencyRates.AddRangeAsync(new List<CurrencyRate>
        {
            new() { Currency = "BYN", RateDate = new DateTime(2026, 11, 10), RateToByn = 1.0m },
            new() { Currency = "USD", RateDate = new DateTime(2026, 11, 10), RateToByn = 3.25m },
            new() { Currency = "EUR", RateDate = new DateTime(2026, 11, 10), RateToByn = 3.52m },
            new() { Currency = "RUB", RateDate = new DateTime(2026, 11, 10), RateToByn = 0.0345m },
            new() { Currency = "CNY", RateDate = new DateTime(2026, 11, 10), RateToByn = 0.45m }
        }, ct);

        await db.NormRules.AddRangeAsync(new List<NormRule>
        {
            new() { Code = "VAT_RATE", Name = "Базовая ставка НДС", DocumentSource = "Налоговый кодекс РБ", ValueText = "20 %", NumericValue = 20m, ValidFrom = new DateTime(2024, 1, 1), ResponsibleRole = "Бухгалтер", Revision = 1 },
            new() { Code = "RETENTION_DEFAULT", Name = "Гарантийное удержание по акту", DocumentSource = "Кодекс № 289-З / договор подряда", ValueText = "5 % суммы акта", NumericValue = 5m, ValidFrom = new DateTime(2024, 7, 23), ResponsibleRole = "Финансовый менеджер", Revision = 1 },
            new() { Code = "PENALTY_DAILY", Name = "Пеня за просрочку обязательства", DocumentSource = "Условия договора (п. 1.3.4)", ValueText = "0,1 %/день, макс. 10 %", NumericValue = 0.1m, ValidFrom = new DateTime(2024, 7, 23), ResponsibleRole = "Финансовый менеджер", Revision = 1 },
            new() { Code = "RESERVE_Z_P80", Name = "Обеспеченность резерва на риски P80", DocumentSource = "СТБ ISO 31000-2020 (п. 1.9)", ValueText = "z = 0,84 (80 %)", NumericValue = 0.84m, ValidFrom = new DateTime(2024, 1, 1), ResponsibleRole = "Специалист по рискам", Revision = 1 },
            new() { Code = "COST_RULE_53", Name = "Привязка затрат к объекту и статье", DocumentSource = "Постановление МАиС РБ № 53 от 30.05.2024", ValueText = "Обязательна на каждой операции", NumericValue = 1m, ValidFrom = new DateTime(2024, 5, 30), ResponsibleRole = "Бухгалтер", Revision = 1 },
            new() { Code = "ACT_FORMS_29", Name = "Формы С-2 и справки С-3", DocumentSource = "Постановление МАиС РБ № 29", ValueText = "Хранение 5 лет, фотофиксация", NumericValue = 1m, ValidFrom = new DateTime(2018, 7, 20), ResponsibleRole = "Руководитель проекта", Revision = 1 }
        }, ct);

        var riskSpecs = new (string Code, string Event, string Cause, string Consequence, decimal P, decimal C, string Owner, string Mitigation, string Indicator, bool Trigger, decimal ResP, RiskStatus Status)[]
        {
            ("R-01", "Задержка финансирования", "Сезонность поступлений", "Кассовый разрыв", 0.30m, 600m, "Финансовый менеджер", "Резервный кредитный лимит, приоритизация платежей", "Остаток ниже минимального", false, 0.15m, RiskStatus.Treating),
            ("R-02", "Рост цен на материалы", "Инфляция", "Превышение бюджета", 0.46m, 450m, "Специалист по снабжению", "Фиксация цены, закупка партии", "Рост цены более 5 %", false, 0.21m, RiskStatus.Treating),
            ("R-03", "Срыв поставки", "Отсутствие товара", "Простой, удорожание", 0.25m, 400m, "Специалист по снабжению", "Альтернативный поставщик, страховой запас", "Просрочка поставки > 3 дн.", false, 0.15m, RiskStatus.Treating),
            ("R-04", "Просроченный аванс", "Непредставление актов", "Иммобилизация средств", 0.35m, 380m, "Финансовый менеджер", "Удержание, претензионная работа", "Kисп < 100 % при истёкшем сроке", true, 0.20m, RiskStatus.Monitoring),
            ("R-05", "Отставание от графика", "Недостаток ресурсов", "Рост накладных расходов", 0.30m, 400m, "Руководитель проекта", "Перепланирование, дополнительные ресурсы", "Отставание > 5 %", false, 0.20m, RiskStatus.Treating),
            ("R-06", "Ошибка проектного решения", "Неполные исходные данные", "Переделки, доп. затраты", 0.15m, 600m, "Руководитель проекта", "Экспертиза решений до закупки", "Замечания к проектной документации", false, 0.08m, RiskStatus.Treating),
            ("R-07", "Изменение требований заказчика", "Уточнение объёмов", "Доп. затраты, сдвиг сроков", 0.25m, 320m, "Руководитель проекта", "Фиксация объёма, доп. соглашение", "Изменение задания заказчиком", false, 0.15m, RiskStatus.Monitoring),
            ("R-08", "Неплатёж заказчика", "Финансовые трудности заказчика", "Нехватка финансирования", 0.20m, 700m, "Финансовый менеджер", "График авансирования, банковская гарантия", "Просрочка поступления > 10 дн.", true, 0.10m, RiskStatus.Treating),
            ("R-09", "Авария, форс-мажор", "Природные условия, отказ оборудования", "Перенос сроков", 0.10m, 800m, "Руководитель организации", "Страхование, резерв времени", "Страховое событие", false, 0.05m, RiskStatus.Monitoring),
            ("R-10", "Ошибка классификации затрат", "Расхождение справочников", "Искажение себестоимости", 0.30m, 150m, "Бухгалтер", "Справочник статей, проверка классификации", "Расхождение с Инструкцией № 53", false, 0.15m, RiskStatus.Monitoring),
            ("R-11", "Утечка данных, компрометация учётной записи", "Слабая аутентификация", "Ущерб, блокировка платежей", 0.15m, 700m, "Администратор системы", "2FA, аудит доступа", "Неудачные входы", false, 0.08m, RiskStatus.Treating),
            ("R-12", "Отказ ИТ-инфраструктуры, потеря данных", "Сбой сервера", "Простой, потеря платежей", 0.10m, 600m, "Администратор системы", "Резервное копирование, контроль восстановления", "Ошибка backup", false, 0.05m, RiskStatus.Monitoring)
        };

        var risks = new List<Risk>();
        var assessments = new List<RiskAssessment>();
        for (int i = 0; i < riskSpecs.Length; i++)
        {
            var s = riskSpecs[i];
            var risk = new Risk
            {
                Id = Guid.Parse($"99000000-0000-0000-0000-{i + 1:D12}"),
                ProjectId = SeedIds.Project1,
                Code = s.Code,
                Event = s.Event,
                Cause = s.Cause,
                Consequence = s.Consequence,
                Probability = s.P,
                MonetaryImpact = s.C,
                OwnerRole = s.Owner,
                Mitigation = s.Mitigation,
                EarlyIndicator = s.Indicator,
                TriggerFired = s.Trigger,
                ResidualProbability = s.ResP,
                Status = s.Status
            };
            RiskCalculator.RecalculateScores(risk);
            risks.Add(risk);
            assessments.Add(new RiskAssessment
            {
                Id = Guid.Parse($"99100000-0000-0000-0000-{i + 1:D12}"),
                RiskId = risk.Id,
                Version = 1,
                Probability = risk.Probability,
                MonetaryImpact = risk.MonetaryImpact,
                Emv = risk.Emv,
                ResidualProbability = risk.ResidualProbability,
                ResidualEmv = risk.ResidualEmv,
                ProjectReserveP80Snapshot = 1947.51m,
                AssessedAt = baseDate,
                AssessedByUserId = SeedIds.UserRisk,
                Comment = "Первичная оценка реестра рисков (табл. 1.15)"
            });
        }
        await db.Risks.AddRangeAsync(risks, ct);
        await db.RiskAssessments.AddRangeAsync(assessments, ct);

        await db.Notifications.AddRangeAsync(new List<Notification>
        {
            new() { TargetRoleCode = "CEO", Title = "Перерасход по заявке ЗП-20261110-0002", Message = "Свободный лимит по статье ST-02 исчерпан (Lдост = -500 тыс. р.). Требуется решение руководителя.", Severity = NotificationSeverity.Critical, Category = NotificationCategory.ApprovalDue, CreatedAt = baseDate, EntityLink = "/Requests" },
            new() { TargetRoleCode = "FinancialManager", Title = "Прогноз кассового разрыва (день 3–5)", Message = "На 13.11.2026 прогнозируется кассовый разрыв 500 тыс. р. по проекту ЖК «Лошицкий квартал».", Severity = NotificationSeverity.Critical, Category = NotificationCategory.CashGap, CreatedAt = baseDate, EntityLink = "/Calendar" },
            new() { TargetRoleCode = "FinancialManager", Title = "Сработали триггеры рисков R-04 и R-08", Message = "Просрочен аванс по договору СП-2026/09 и просрочка поступления заказчика > 10 дней.", Severity = NotificationSeverity.Warning, Category = NotificationCategory.RiskTrigger, CreatedAt = baseDate, EntityLink = "/Risks" }
        }, ct);

        await db.AuditLogs.AddAsync(new AuditLog
        {
            UserId = SeedIds.UserAdmin,
            UserLogin = "admin",
            Timestamp = baseDate,
            ObjectName = "System",
            ObjectId = "INIT",
            Action = "SEED_DATABASE",
            NewValue = "Completed",
            Details = "Загружены контрольные примеры главы 1 и реестр 12 рисков"
        }, ct);

        await db.SaveChangesAsync(ct);
    }
}
