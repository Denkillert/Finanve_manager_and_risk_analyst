using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ConstructionObject> ConstructionObjects => Set<ConstructionObject>();
    public DbSet<WorkStage> WorkStages => Set<WorkStage>();
    public DbSet<CostItem> CostItems => Set<CostItem>();
    public DbSet<BudgetVersion> BudgetVersions => Set<BudgetVersion>();
    public DbSet<BudgetLimit> BudgetLimits => Set<BudgetLimit>();
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractSupplement> ContractSupplements => Set<ContractSupplement>();
    public DbSet<ContractScheduleItem> ContractScheduleItems => Set<ContractScheduleItem>();
    public DbSet<Obligation> Obligations => Set<Obligation>();
    public DbSet<Advance> Advances => Set<Advance>();
    public DbSet<PaymentRequest> PaymentRequests => Set<PaymentRequest>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<CurrencyRate> CurrencyRates => Set<CurrencyRate>();
    public DbSet<NormRule> NormRules => Set<NormRule>();
    public DbSet<Risk> Risks => Set<Risk>();
    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Organization>().ToTable("Organizations", "finance");
        modelBuilder.Entity<Portfolio>().ToTable("Portfolios", "finance");
        modelBuilder.Entity<Project>().ToTable("Projects", "finance")
            .HasIndex(p => new { p.PortfolioId, p.Code }).IsUnique();
        modelBuilder.Entity<ConstructionObject>().ToTable("ConstructionObjects", "finance");
        modelBuilder.Entity<WorkStage>().ToTable("WorkStages", "finance");
        modelBuilder.Entity<CostItem>().ToTable("CostItems", "finance")
            .HasIndex(c => c.Code).IsUnique();
        modelBuilder.Entity<BudgetVersion>().ToTable("BudgetVersions", "finance")
            .HasIndex(v => new { v.ProjectId, v.VersionNumber }).IsUnique();
        modelBuilder.Entity<BudgetLimit>().ToTable("BudgetLimits", "finance");
        modelBuilder.Entity<Contract>().ToTable("Contracts", "finance")
            .HasIndex(c => new { c.ProjectId, c.Number }).IsUnique();
        modelBuilder.Entity<ContractSupplement>().ToTable("ContractSupplements", "finance")
            .HasIndex(s => new { s.ContractId, s.Number }).IsUnique();
        modelBuilder.Entity<ContractScheduleItem>().ToTable("ContractScheduleItems", "finance");
        modelBuilder.Entity<Obligation>().ToTable("Obligations", "finance");
        modelBuilder.Entity<Advance>().ToTable("Advances", "finance");
        modelBuilder.Entity<PaymentRequest>().ToTable("PaymentRequests", "finance")
            .HasIndex(r => r.Number).IsUnique();
        modelBuilder.Entity<Document>().ToTable("Documents", "finance")
            .HasIndex(d => new { d.Type, d.Number, d.Date }).IsUnique();
        modelBuilder.Entity<Payment>().ToTable("Payments", "finance")
            .HasIndex(p => p.BankOperationId).IsUnique();
        modelBuilder.Entity<CurrencyRate>().ToTable("CurrencyRates", "finance")
            .HasIndex(r => new { r.Currency, r.RateDate }).IsUnique();
        modelBuilder.Entity<NormRule>().ToTable("NormRules", "finance")
            .HasIndex(r => new { r.Code, r.Revision }).IsUnique();

        modelBuilder.Entity<Risk>().ToTable("Risks", "risk")
            .HasIndex(r => new { r.ProjectId, r.Code }).IsUnique();
        modelBuilder.Entity<RiskAssessment>().ToTable("RiskAssessments", "risk")
            .HasIndex(a => new { a.RiskId, a.Version }).IsUnique();

        modelBuilder.Entity<Role>().ToTable("Roles", "security")
            .HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<User>().ToTable("Users", "security")
            .HasIndex(u => u.Login).IsUnique();

        modelBuilder.Entity<AuditLog>().ToTable("AuditLogs", "audit");
        modelBuilder.Entity<Notification>().ToTable("Notifications", "audit");

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.RowVersion))
                    .IsRowVersion();

                foreach (var prop in entityType.GetProperties().Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
                {
                    prop.SetPrecision(18);
                    prop.SetScale(4);
                }
            }
        }
    }
}

public class EfRepository<T> : IRepository<T> where T : BaseEntity
{
    private readonly AppDbContext _context;

    public EfRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _context.Set<T>().FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);

    public async Task<List<T>> GetAllAsync(CancellationToken ct = default) =>
        await _context.Set<T>().Where(e => !e.IsDeleted).ToListAsync(ct);

    public async Task AddAsync(T entity, CancellationToken ct = default) =>
        await _context.Set<T>().AddAsync(entity, ct);

    public Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        _context.Set<T>().Update(entity);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;
        _context.Set<T>().Update(entity);
        return Task.CompletedTask;
    }
}

public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException();
        }
    }
}
