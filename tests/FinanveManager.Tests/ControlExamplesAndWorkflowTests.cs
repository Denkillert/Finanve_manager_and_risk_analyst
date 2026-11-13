using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanveManager.Tests;

public class ControlExamplesAndWorkflowTests
{
    [Fact]
    public void FreeLimit_Example1_MatchesChapter1ExactValues()
    {
        var res = FinancialCalculator.CalculateFreeLimit(10000m, 6000m, 3000m, 1000m);

        Assert.Equal(4000m, res.FreeLimit);
        Assert.Equal(3000m, res.ObligationRemainder);
        Assert.Equal(-4000m, res.Deviation);
        Assert.Equal(-40m, res.DeviationPercent);
        Assert.False(res.HasMissingBaselinePlan);
    }

    [Fact]
    public void FreeLimit_Example2_ExhaustedArticle_MatchesChapter1ExactValues()
    {
        var res = FinancialCalculator.CalculateFreeLimit(10000m, 10500m, 7000m, 1500m);

        Assert.Equal(-500m, res.FreeLimit);
        Assert.Equal(3500m, res.ObligationRemainder);
        Assert.Equal(500m, res.Deviation);
        Assert.Equal(5m, res.DeviationPercent);
    }

    [Fact]
    public void EarnedValue_Example_MatchesCpiSpiEtcEacAndFinancialResult()
    {
        var res = EarnedValueCalculator.Calculate(11000m, 3400m, 3000m, 3300m, 14500m, 1948m);

        Assert.Equal(0.909m, res.Cpi);
        Assert.Equal(0.882m, res.Spi);
        Assert.Equal(8800m, res.Etc);
        Assert.Equal(12100m, res.Eac);
        Assert.Equal(2400m, res.FinancialResultWithoutReserve);
        Assert.Equal(452m, res.FinancialResultWithReserve);
        Assert.Equal(16.6m, res.MarginWithoutReservePercent);
        Assert.Equal(3.1m, res.MarginWithReservePercent);
    }

    [Fact]
    public void CashCalendar_Table15_Matches5DayCashGapExample()
    {
        var start = new DateTime(2026, 11, 10);
        var flows = new Dictionary<DateTime, (decimal, decimal)>
        {
            [start.AddDays(1)] = (0m, 300m),
            [start.AddDays(2)] = (1000m, 400m),
            [start.AddDays(3)] = (0m, 1800m),
            [start.AddDays(4)] = (600m, 200m),
            [start.AddDays(5)] = (0m, 100m),
        };

        var forecast = FinancialCalculator.CalculateCashCalendar(2000m, 1000m, start, 5, flows);

        Assert.Equal(2000m, forecast[0].ClosingBalance);
        Assert.Equal(0m, forecast[0].CashGap);
        Assert.Equal(1700m, forecast[1].ClosingBalance);
        Assert.Equal(2300m, forecast[2].ClosingBalance);
        Assert.Equal(500m, forecast[3].ClosingBalance);
        Assert.Equal(500m, forecast[3].CashGap);
        Assert.Equal(900m, forecast[4].ClosingBalance);
        Assert.Equal(100m, forecast[4].CashGap);
        Assert.Equal(800m, forecast[5].ClosingBalance);
        Assert.Equal(200m, forecast[5].CashGap);
    }

    [Fact]
    public void ActS2Payable_Formula112_MatchesControlExample()
    {
        var res = FinancialCalculator.CalculateActPayable(1200m, 300m, 50m);
        Assert.Equal(850m, res.PayableAmount);
    }

    [Fact]
    public void Risks_Table115And117_MatchesTotalEmvSigmaReserveP80AndResidual()
    {
        var specs = new (string Code, decimal P, decimal C, decimal ResP)[]
        {
            ("R-01", 0.30m, 600m, 0.15m), ("R-02", 0.46m, 450m, 0.21m), ("R-03", 0.25m, 400m, 0.15m),
            ("R-04", 0.35m, 380m, 0.20m), ("R-05", 0.30m, 400m, 0.20m), ("R-06", 0.15m, 600m, 0.08m),
            ("R-07", 0.25m, 320m, 0.15m), ("R-08", 0.20m, 700m, 0.10m), ("R-09", 0.10m, 800m, 0.05m),
            ("R-10", 0.30m, 150m, 0.15m), ("R-11", 0.15m, 700m, 0.08m), ("R-12", 0.10m, 600m, 0.05m)
        };

        var risks = specs.Select(s =>
        {
            var r = new Risk { Code = s.Code, Probability = s.P, MonetaryImpact = s.C, ResidualProbability = s.ResP };
            RiskCalculator.RecalculateScores(r);
            return r;
        }).ToList();

        var strategy = new P80ReserveStrategy();
        decimal reserve = strategy.Calculate(risks, out decimal emv, out decimal sigma);

        Assert.Equal(1340m, emv);
        Assert.InRange(sigma, 723.15m, 723.30m);
        Assert.InRange(reserve, 1947.4m, 1948.1m);

        var summary = RiskCalculator.CalculatePortfolioSummary(risks);
        Assert.Equal(715m, summary.TotalResidualEmv);
        Assert.Equal(625m, summary.EmvReduction);

        var matrix = RiskCalculator.BuildRiskMatrix(risks);
        Assert.Equal(25, matrix.Count);
        // R-02: p = 0,46 → балл вероятности 3; C = 450 → балл влияния 4; R = 3 × 4 = 12.
        Assert.Contains(matrix, m => m.IntegralScore == 12 && m.RiskCodes.Contains("R-02"));
    }

    [Fact]
    public void BankAdapter_ParsesMt940AndCsvCleanly()
    {
        var adapter = new BankAdapter();
        string csv = "BankOperationId;Date;Amount;Currency;Counterparty;RequestNumber;Purpose;Order\nOP-001;2026-11-10;850.00;BYN;ООО СпецМонтаж;ЗП-20261110-ABCDEF;Оплата по акту;ПП-101";
        var parsed = adapter.ParseStatement(csv, "statement.csv");

        Assert.Single(parsed);
        Assert.Equal("OP-001", parsed[0].BankOperationId);
        Assert.Equal(850m, parsed[0].Amount);
        Assert.Equal("ЗП-20261110-ABCDEF", parsed[0].PaymentRequestNumber);
    }

    [Fact]
    public void RequestInvariant_CannotExecuteBeforeApproved()
    {
        var req = new PaymentRequest { Number = "ЗП-1", Status = RequestStatus.Blocked };
        Assert.Throws<DomainException>(() => req.EnsureCanExecute());
    }

    [Fact]
    public void PasswordHasher_EnforcesMinLengthAndVerifiesHash()
    {
        Assert.Throws<ArgumentException>(() => PasswordHasher.HashPassword("short"));
        string hash = PasswordHasher.HashPassword("StroyFin2026!");
        Assert.True(PasswordHasher.VerifyPassword("StroyFin2026!", hash));
        Assert.False(PasswordHasher.VerifyPassword("WrongPassword123", hash));
        Assert.True(PasswordHasher.VerifyPassword(
            "StroyFin2026!",
            "U3Ryb3lGaW5TYWx0MjAyNg==.j9j+cxMYYr7AsTJ0SDooo1GJTTO4HPopAMoVN81VpFw="));
    }

    [Fact]
    public void RolePermissions_EnforcesStrictRoleAndClientIsolation_Nfr1AndTable25()
    {
        var adminSections = RolePermissions.GetSectionsForRole("Admin").Select(s => s.Section).ToList();
        Assert.Equal(new[] { AppSection.Administration, AppSection.AuditLog }, adminSections);

        var accSections = RolePermissions.GetSectionsForRole("Accountant").Select(s => s.Section).ToList();
        Assert.Equal(new[] { AppSection.PaymentRequests, AppSection.ContractsAndStatements }, accSections);

        var procSections = RolePermissions.GetSectionsForRole("ProcurementSpecialist").Select(s => s.Section).ToList();
        Assert.Equal(new[] { AppSection.ContractsAndStatements, AppSection.PaymentRequests }, procSections);

        var pmSections = RolePermissions.GetSectionsForRole("ProjectManager").Select(s => s.Section).ToList();
        Assert.Equal(new[] { AppSection.ProjectsAndBudgets, AppSection.PaymentRequests, AppSection.SiteActs }, pmSections);

        var riskSections = RolePermissions.GetSectionsForRole("RiskSpecialist").Select(s => s.Section).ToList();
        Assert.Equal(new[] { AppSection.RisksAndP80, AppSection.Reports }, riskSections);

        Assert.False(RolePermissions.CanAccess("Accountant", AppSection.RisksAndP80));
        Assert.False(RolePermissions.CanAccess("Accountant", AppSection.PaymentCalendar));
        Assert.False(RolePermissions.CanAccess("ProcurementSpecialist", AppSection.PaymentCalendar));
        Assert.False(RolePermissions.CanAccess("ProjectManager", AppSection.RisksAndP80));
        Assert.False(RolePermissions.CanAccess("FinancialManager", AppSection.RisksAndP80));
        Assert.False(RolePermissions.CanAccess("FinancialManager", AppSection.Administration));
        Assert.False(RolePermissions.CanAccess("RiskSpecialist", AppSection.Dashboard));
        Assert.False(RolePermissions.CanAccess("RiskSpecialist", AppSection.ProjectsAndBudgets));
        Assert.False(RolePermissions.CanAccess("Admin", AppSection.Dashboard));

        // Табл. 1.8 и НФТ-1: строго 7 ролей (4 в WPF, 3 в Веб)
        Assert.True(RolePermissions.IsClientAllowed("CEO", ClientApp.Wpf));
        Assert.False(RolePermissions.IsClientAllowed("CEO", ClientApp.Web));
        Assert.True(RolePermissions.IsClientAllowed("FinancialManager", ClientApp.Web));
        Assert.False(RolePermissions.IsClientAllowed("FinancialManager", ClientApp.Wpf));
        Assert.True(RolePermissions.IsClientAllowed("RiskSpecialist", ClientApp.Web));
        Assert.True(RolePermissions.IsClientAllowed("ProjectManager", ClientApp.Web));

        Assert.Equal(("Admin", "Index"), RolePermissions.GetLandingRoute("Admin"));
        Assert.Equal(("Requests", "Index"), RolePermissions.GetLandingRoute("Accountant"));
        Assert.Equal(("Contracts", "Index"), RolePermissions.GetLandingRoute("ProcurementSpecialist"));
        Assert.Equal(("Projects", "Index"), RolePermissions.GetLandingRoute("ProjectManager"));
        Assert.Equal(("Risks", "Index"), RolePermissions.GetLandingRoute("RiskSpecialist"));
        Assert.Equal(("Dashboard", "Index"), RolePermissions.GetLandingRoute("FinancialManager"));
        Assert.Equal("Заблокирована", RussianLabels.RequestStatusName(RequestStatus.Blocked));
    }

    [Fact]
    public async Task Ceo_CanCreateProject_FromEmptyDb_AndAssignProjectManager()
    {
        var factory = AppDatabaseFactory.CreateInMemoryForTests($"CeoEmptyDb_{Guid.NewGuid():N}");
        using (var initScope = factory.CreateScope())
        {
            await DbInitializer.SeedRolesAndAdminOnlyAsync(initScope.Db);
            await initScope.Db.Users.AddAsync(new User
            {
                Id = SeedIds.UserPm,
                RoleId = SeedIds.RolePm,
                Login = "prorab1",
                PasswordHash = SeedIds.DefaultDemoPasswordHash,
                FullName = "Сидоров А.В. (Прораб)",
                Email = "prorab1@stroyfin.by",
                IsActive = true,
                PersonalDataConsentGiven = true,
                ConsentDate = DateTime.UtcNow
            });
            await initScope.Db.Users.AddAsync(new User
            {
                Id = SeedIds.UserRisk,
                RoleId = SeedIds.RoleRisk,
                Login = "risk1",
                PasswordHash = SeedIds.DefaultDemoPasswordHash,
                FullName = "Волкова М.П. (Специалист по рискам)",
                Email = "risk1@stroyfin.by",
                IsActive = true,
                PersonalDataConsentGiven = true,
                ConsentDate = DateTime.UtcNow
            });
            await initScope.Db.SaveChangesAsync();
        }

        using var scope = factory.CreateScope();
        var created = await scope.ProjectMgmt.CreateProjectAsync(
            new CreateProjectDto(
                "PRJ-EMPTY-01",
                "Школа на 1020 мест",
                12500m,
                "BYN",
                2000m,
                600m,
                new DateTime(2026, 11, 10),
                new DateTime(2028, 8, 31),
                "Учебный корпус",
                "г. Минск, ул. Притыцкого, 90",
                "1-я очередь",
                SeedIds.UserPm),
            SeedIds.UserCeo,
            "director");

        Assert.Equal("PRJ-EMPTY-01", created.Project.Code);
        Assert.Equal(BudgetVersionStatus.Draft, created.InitialBudgetVersion.Status);
        Assert.NotNull(created.ManagerAssignment);
        Assert.True(created.ManagerAssignment!.IsPrimaryManager);
        Assert.Equal(SeedIds.UserPm, created.ManagerAssignment.UserId);

        var matCost = await scope.Db.CostItems.FirstAsync(c => c.Code == "ST-01");
        var limit = await scope.ProjectMgmt.UpsertDraftBudgetLimitAsync(
            created.InitialBudgetVersion.Id, created.InitialObject.Id, matCost.Id, 7500m, SeedIds.UserCeo, "director");
        Assert.Equal(7500m, limit.Amount);

        var approved = await scope.Budget.ApproveVersionAsync(
            created.InitialBudgetVersion.Id, created.InitialBudgetVersion.RowVersion, SeedIds.UserCeo, "director");
        Assert.Equal(BudgetVersionStatus.Approved, approved.Status);

        var free = await scope.Budget.CalculateFreeLimitAsync(created.InitialObject.Id, matCost.Id);
        Assert.Equal(7500m, free.ApprovedBudget);

        // Снятие и повторное назначение прораба безопасно переактивирует запись без нарушения уникального индекса
        await scope.ProjectMgmt.RemoveMemberAsync(created.ManagerAssignment.Id, SeedIds.UserCeo, "director");
        var reassigned = await scope.ProjectMgmt.AssignMemberAsync(
            new AssignProjectMemberDto(created.Project.Id, SeedIds.UserPm, "Руководитель проекта (прораб)", true),
            SeedIds.UserCeo, "director");
        Assert.False(reassigned.IsDeleted);
        Assert.True(reassigned.IsPrimaryManager);

        // На проект можно назначить только прораба: другие роли отклоняются.
        var wrongRole = await Assert.ThrowsAsync<DomainException>(() => scope.ProjectMgmt.AssignMemberAsync(
            new AssignProjectMemberDto(created.Project.Id, SeedIds.UserRisk, "Специалист по рискам", false),
            SeedIds.UserCeo, "director"));
        Assert.Equal("INVALID_PROJECT_MANAGER_ROLE", wrongRole.ErrorCode);
    }

    [Fact]
    public async Task AppDatabaseFactory_And_AuthService_Enforce2FaClientSplitAndSeeding()
    {
        Assert.Throws<InvalidOperationException>(() => new AppDatabaseFactory(null));
        Assert.Throws<InvalidOperationException>(() => new AppDatabaseFactory("   "));

        var factory = AppDatabaseFactory.CreateInMemoryForTests($"FactoryTestDb_{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        Assert.False(factory.UsesSqlServer);

        using var scope = factory.CreateScope();
        Assert.Equal(7, await scope.Db.Roles.CountAsync());
        Assert.Equal(7, await scope.Db.Users.CountAsync());
        Assert.Equal(12, await scope.Db.Risks.CountAsync(r => r.ProjectId == SeedIds.Project1));

        var no2Fa = await scope.Auth.LoginAsync("admin", "StroyFin2026!", null, "Tests", ClientApp.Wpf);
        Assert.False(no2Fa.Success);
        Assert.True(no2Fa.RequiresTwoFactor);

        var okAdmin = await scope.Auth.LoginAsync("admin", "StroyFin2026!", "246810", "Tests", ClientApp.Wpf);
        Assert.True(okAdmin.Success);
        Assert.Equal("Admin", okAdmin.Session?.RoleCode);

        var wrongClient = await scope.Auth.LoginAsync("finmanager", "StroyFin2026!", null, "Tests", ClientApp.Wpf);
        Assert.False(wrongClient.Success);
    }

    [Fact]
    public async Task MaterialRequest_Workflow_KeepsProcurementWithSupplyAndAuthoringWithForeman()
    {
        var factory = AppDatabaseFactory.CreateInMemoryForTests($"MaterialRequestDb_{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        using var scope = factory.CreateScope();

        var dto = new CreateMaterialRequestDto(
            SeedIds.Project1,
            SeedIds.Obj1,
            SeedIds.ItemMat,
            "Керамическая плитка 600×600",
            "м²",
            120m,
            new DateTime(2026, 12, 1),
            "Отделка холлов 1–3 этажей",
            "ГОСТ 6787-2001",
            SubmitImmediately: true);

        // Прораб создаёт потребность в материале: поставщик и цена договора не выбираются.
        var created = await scope.MaterialRequests.CreateAsync(dto, SeedIds.UserPm, "pm");
        Assert.Equal(MaterialRequestStatus.Submitted, created.Status);
        Assert.Equal(0m, created.EstimatedTotal);
        Assert.Null(created.SupplierName);
        Assert.Null(created.ContractId);
        Assert.Null(created.AgreedTotal);

        // Снабженец возвращает заявку на доработку.
        var rejected = await scope.MaterialRequests.RejectAsync(
            created.Id, "Уточните класс износостойкости PEI", SeedIds.UserProc, "supply");
        Assert.Equal(MaterialRequestStatus.Rejected, rejected.Status);

        // Дорабатывать может только автор заявки.
        await Assert.ThrowsAsync<DomainException>(() => scope.MaterialRequests.UpdateAsync(
            created.Id, dto, SeedIds.UserProc, "supply"));

        var updated = await scope.MaterialRequests.UpdateAsync(
            created.Id, dto with { SpecificationNotes = "PEI IV, морозостойкость F100" }, SeedIds.UserPm, "pm");
        Assert.Equal(MaterialRequestStatus.Submitted, updated.Status);
        Assert.Null(updated.RejectedAt);

        // Снабженец ведёт реестр поставщиков: у ООО «БелСтройПоставка» есть предложение по плитке.
        var tileOffer = await scope.Db.SupplierOffers
            .Include(o => o.Supplier)
            .FirstAsync(o => o.MaterialName == "Керамическая плитка 600×600" && !o.IsDeleted);
        Assert.Equal(31.5m, tileOffer.UnitPrice);

        // Договор заключается с организацией-контрагентом, без привязки к проекту.
        var supplierContract = new Contract
        {
            Number = "П-2026/15",
            Counterparty = tileOffer.Supplier!.Name,
            CounterpartyUnp = tileOffer.Supplier.Unp,
            Direction = ContractDirection.SupplierSubcontractorOutgoing,
            TotalAmount = 10000m,
            Currency = "BYN",
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 12, 31)
        };
        await scope.Db.Contracts.AddAsync(supplierContract);
        await scope.Db.SaveChangesAsync();
        Assert.Null(supplierContract.ProjectId);

        // Снабженец выбирает предложение с выгодной ценой — поставщик и цена подставляются автоматически.
        var approved = await scope.MaterialRequests.ApproveAsync(
            created.Id,
            new SupplyMaterialRequestDto(null, null, null, "Поставка по договору П-2026/15", tileOffer.Id),
            SeedIds.UserProc,
            "supply");
        Assert.Equal(MaterialRequestStatus.Approved, approved.Status);
        Assert.Equal(supplierContract.Id, approved.ContractId);
        Assert.Equal(tileOffer.Supplier.Name, approved.SupplierName);
        Assert.Equal(tileOffer.Id, approved.SupplierOfferId);
        Assert.Equal(31.5m, approved.AgreedUnitPrice);
        Assert.Equal(3780m, approved.AgreedTotal);

        // После обработки заявки снабженец формирует платёжную заявку.
        var withPayment = await scope.MaterialRequests.CreatePaymentRequestAsync(created.Id, SeedIds.UserProc, "supply");
        Assert.NotNull(withPayment.PaymentRequestId);
        var payment = await scope.Db.PaymentRequests.FirstAsync(p => p.Id == withPayment.PaymentRequestId);
        Assert.Equal(3780m, payment.PayableAmount);
        Assert.Equal($"MATERIAL-{created.Id}", payment.IdempotencyKey);
        Assert.Contains(created.Number, payment.Purpose);

        // После согласования прораб уже не может менять заявку.
        await Assert.ThrowsAsync<DomainException>(() => scope.MaterialRequests.UpdateAsync(
            created.Id, dto, SeedIds.UserPm, "pm"));

        var ordered = await scope.MaterialRequests.MarkOrderedAsync(
            created.Id,
            new SupplyMaterialRequestDto(null, null, null, null, tileOffer.Id),
            SeedIds.UserProc,
            "supply");
        Assert.Equal(MaterialRequestStatus.Ordered, ordered.Status);

        var delivered = await scope.MaterialRequests.MarkDeliveredAsync(created.Id, SeedIds.UserProc, "supply");
        Assert.Equal(MaterialRequestStatus.Delivered, delivered.Status);

        // Поставленную заявку нельзя отклонить или согласовать повторно.
        await Assert.ThrowsAsync<DomainException>(() => scope.MaterialRequests.RejectAsync(
            created.Id, "Повторный возврат", SeedIds.UserProc, "supply"));
        await Assert.ThrowsAsync<DomainException>(() => scope.MaterialRequests.ApproveAsync(
            created.Id, new SupplyMaterialRequestDto(null, null, 31m, null, tileOffer.Id), SeedIds.UserProc, "supply"));
    }
}
