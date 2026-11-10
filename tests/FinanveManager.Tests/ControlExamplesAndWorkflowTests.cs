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
        Assert.Contains(matrix, m => m.IntegralScore == 16 && m.RiskCodes.Contains("R-02"));
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

        var siteSections = RolePermissions.GetSectionsForRole("SiteWorker").Select(s => s.Section).ToList();
        Assert.Equal(new[] { AppSection.SiteActs }, siteSections);

        var riskSections = RolePermissions.GetSectionsForRole("RiskSpecialist").Select(s => s.Section).ToList();
        Assert.Equal(new[] { AppSection.Dashboard, AppSection.RisksAndP80, AppSection.Reports }, riskSections);

        Assert.False(RolePermissions.CanAccess("Accountant", AppSection.RisksAndP80));
        Assert.False(RolePermissions.CanAccess("FinancialManager", AppSection.Administration));
        Assert.False(RolePermissions.CanAccess("Admin", AppSection.Dashboard));

        // Табл. 1.8 и НФТ-1: 4 роли в WPF, 4 роли в Веб
        Assert.True(RolePermissions.IsClientAllowed("CEO", ClientApp.Wpf));
        Assert.False(RolePermissions.IsClientAllowed("CEO", ClientApp.Web));
        Assert.True(RolePermissions.IsClientAllowed("FinancialManager", ClientApp.Web));
        Assert.False(RolePermissions.IsClientAllowed("FinancialManager", ClientApp.Wpf));

        Assert.Equal(("Admin", "Index"), RolePermissions.GetLandingRoute("Admin"));
        Assert.Equal(("Requests", "Acts"), RolePermissions.GetLandingRoute("SiteWorker"));
        Assert.Equal("Заблокирована", RussianLabels.RequestStatusName(RequestStatus.Blocked));
    }

    [Fact]
    public async Task AppDatabaseFactory_And_AuthService_Enforce2FaClientSplitAndSeeding()
    {
        var factory = new AppDatabaseFactory(null, $"FactoryTestDb_{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        Assert.False(factory.UsesSqlServer);

        using var scope = factory.CreateScope();
        Assert.Equal(8, await scope.Db.Roles.CountAsync());
        Assert.Equal(8, await scope.Db.Users.CountAsync());
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
}
