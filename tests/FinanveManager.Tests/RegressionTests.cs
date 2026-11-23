using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanveManager.Tests;

public class RegressionTests
{
    [Fact]
    public void Calendar_IncludesDueAndOverdueItemsAndFirstFiveDays()
    {
        var today = new DateTime(2026, 12, 1);
        var schedule = new[]
        {
            new ContractScheduleItem { PlannedDate = today.AddDays(-1), Amount = 20, IsIncoming = true },
            new ContractScheduleItem { PlannedDate = today, Amount = 5, IsIncoming = false },
            new ContractScheduleItem { PlannedDate = today.AddDays(2), Amount = 999, IsPaid = true }
        };
        var requests = new[]
        {
            new PaymentRequest { PlannedPaymentDate = today, AmountInByn = 10, Status = RequestStatus.Approved },
            new PaymentRequest { PlannedPaymentDate = today.AddDays(3), AmountInByn = 30, Status = RequestStatus.PendingApproval },
            new PaymentRequest { PlannedPaymentDate = today.AddDays(3), AmountInByn = 999, Status = RequestStatus.Rejected }
        };
        var flows = FinancialCalculator.BuildCashFlows(today, schedule, requests);
        var days = FinancialCalculator.CalculateCashCalendar(100, 0, today, 5, flows);
        Assert.Equal(100m, days[0].ClosingBalance);
        Assert.Equal(105m, days[1].ClosingBalance);
        Assert.Equal(75m, days[3].ClosingBalance);
        Assert.Equal(75m, days[5].ClosingBalance);
        Assert.Throws<ArgumentOutOfRangeException>(() => FinancialCalculator.CalculateCashCalendar(0, 0, today, -1, flows));
    }

    [Fact]
    public void RiskRecalculation_ClearsResidualAndRejectsInvalidProbability()
    {
        var risk = new Risk { Probability = 0.5m, MonetaryImpact = 100, ResidualProbability = 0.2m };
        RiskCalculator.RecalculateScores(risk);
        Assert.Equal(20m, risk.ResidualEmv);
        risk.ResidualProbability = null;
        RiskCalculator.RecalculateScores(risk);
        Assert.Null(risk.ResidualEmv);
        risk.ResidualProbability = 1.1m;
        Assert.Throws<DomainException>(() => RiskCalculator.RecalculateScores(risk));
        Assert.Throws<DomainException>(() => FinancialCalculator.NormalizeVat(100, true, -100));
    }

    [Fact]
    public void PasswordHasher_RejectsMalformedHashes()
    {
        Assert.False(PasswordHasher.VerifyPassword("password", "not-base64.invalid"));
        Assert.False(PasswordHasher.VerifyPassword("password", "."));
    }

    [Fact]
    public async Task CurrencyRate_RejectsUnknownCurrencyInsteadOfAssumingParity()
    {
        var provider = new NbrbCurrencyRateProvider();
        Assert.Equal(1m, await provider.GetRateToBynAsync("BYN", DateTime.Today));
        await Assert.ThrowsAsync<DomainException>(() => provider.GetRateToBynAsync("XXX", DateTime.Today));
    }

    [Fact]
    public async Task Authentication_LocksIncorrectSecondFactorAndResetsAfterExpiry()
    {
        var factory = AppDatabaseFactory.CreateInMemoryForTests($"AuthRegression_{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        using var scope = factory.CreateScope();
        var user = await scope.Db.Users.SingleAsync(u => u.Login == "admin");
        for (int i = 0; i < 5; i++)
            Assert.False((await scope.Auth.LoginAsync("admin", "StroyFin2026!", "wrong", "Tests", ClientApp.Wpf)).Success);
        Assert.NotNull(user.LockedUntil);
        Assert.False((await scope.Auth.LoginAsync("admin", "StroyFin2026!", "246810", "Tests", ClientApp.Wpf)).Success);
        user.LockedUntil = DateTime.UtcNow.AddMinutes(-1);
        await scope.Db.SaveChangesAsync();
        Assert.False((await scope.Auth.LoginAsync("admin", "wrong", "246810", "Tests", ClientApp.Wpf)).Success);
        Assert.Equal(1, user.FailedLoginCount);
        Assert.Null(user.LockedUntil);
        Assert.True((await scope.Auth.LoginAsync("admin", "StroyFin2026!", "246810", "Tests", ClientApp.Wpf)).Success);
        user.TwoFactorSecret = null;
        await scope.Db.SaveChangesAsync();
        Assert.False((await scope.Auth.LoginAsync("admin", "StroyFin2026!", "246810", "Tests", ClientApp.Wpf)).Success);
    }

    [Fact]
    public async Task MaterialRequest_SupplierChangeDoesNotKeepAnotherSuppliersContract()
    {
        var factory = AppDatabaseFactory.CreateInMemoryForTests($"MaterialRegression_{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        using var scope = factory.CreateScope();
        var dto = new CreateMaterialRequestDto(SeedIds.Project1, SeedIds.Obj1, SeedIds.ItemMat,
            "Тестовый материал", "шт.", 2, DateTime.Today.AddDays(1), null, null, true);
        var request = await scope.MaterialRequests.CreateAsync(dto, SeedIds.UserPm, "pm");
        var contractA = new Contract { Number = "REG-A", Counterparty = "Поставщик A", Direction = ContractDirection.SupplierSubcontractorOutgoing };
        var contractB = new Contract { Number = "REG-B", Counterparty = "Поставщик B", Direction = ContractDirection.SupplierSubcontractorOutgoing };
        await scope.Db.Contracts.AddRangeAsync(contractA, contractB);
        await scope.Db.SaveChangesAsync();
        await scope.MaterialRequests.ApproveAsync(request.Id, new SupplyMaterialRequestDto(contractA.Id, null, 10, null), SeedIds.UserProc, "supply");
        Assert.Equal(contractA.Id, request.ContractId);
        await scope.MaterialRequests.ApproveAsync(request.Id, new SupplyMaterialRequestDto(null, "Поставщик B", 15, null), SeedIds.UserProc, "supply");
        Assert.Equal(contractB.Id, request.ContractId);
        Assert.Equal(30m, request.AgreedTotal);
        await Assert.ThrowsAsync<DomainException>(() => scope.MaterialRequests.ApproveAsync(request.Id,
            new SupplyMaterialRequestDto(contractA.Id, "Поставщик B", 15, null), SeedIds.UserProc, "supply"));
        Assert.Equal(contractB.Id, request.ContractId);
    }

    [Fact]
    public async Task PaymentRequest_RejectsObjectFromAnotherProjectBeforeSaving()
    {
        var factory = AppDatabaseFactory.CreateInMemoryForTests($"PaymentRegression_{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        using var scope = factory.CreateScope();
        var project = new Project { Code = "OTHER", Name = "Другой проект" };
        await scope.Db.Projects.AddAsync(project);
        await scope.Db.SaveChangesAsync();
        var contract = await scope.Db.Contracts.FirstAsync();
        var dto = new CreatePaymentRequestDto(project.Id, SeedIds.Obj1, contract.Id, SeedIds.ItemMat,
            100m, 100m, 0m, 0m, true, "BYN", RequestType.CurrentPayment,
            DateTime.Today, "Тест", DocumentType.ActS2, "TEST-123", DateTime.Today, null, null, false);
        var before = await scope.Db.PaymentRequests.CountAsync();
        await Assert.ThrowsAsync<DomainException>(() => scope.Requests.CreateAsync(dto, SeedIds.UserPm, "pm"));
        Assert.Equal(before, await scope.Db.PaymentRequests.CountAsync());
    }

    [Fact]
    public async Task RiskAssessment_InvalidResidualDoesNotChangeTrackedRisk()
    {
        var factory = AppDatabaseFactory.CreateInMemoryForTests($"RiskRegression_{Guid.NewGuid():N}");
        await factory.InitializeAsync();
        using var scope = factory.CreateScope();
        var risk = await scope.Db.Risks.FirstAsync();
        var oldProbability = risk.Probability;
        await Assert.ThrowsAsync<DomainException>(() => scope.Risks.UpdateAssessmentAsync(
            risk.Id, 0.2m, 100m, 2m, "Тест", Array.Empty<byte>(), SeedIds.UserRisk, "risk"));
        Assert.Equal(oldProbability, risk.Probability);
    }

    [Fact]
    public async Task BlockedRequest_CanRemainBlockedWhenReasonChanges()
    {
        var blockedAt = DateTime.UtcNow.AddDays(-20);
        var request = new PaymentRequest { Status = RequestStatus.Blocked, BlockedAt = blockedAt };
        await new WorkflowEngine().BlockAsync(request, "Просроченный аванс");
        Assert.Equal(RequestStatus.Blocked, request.Status);
        Assert.Equal("Просроченный аванс", request.BlockReason);
        Assert.Equal(blockedAt, request.BlockedAt);
    }
}
