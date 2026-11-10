using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinanveManager.Data.Services;

public partial class BankAdapter : IBankAdapter
{
    private static readonly string[] LineSeparators = ["\r\n", "\n"];

    [GeneratedRegex(@":61:(\d{6})([CD])([\d,\.]+)N\w+//([^\r\n]+)(?:\r?\n:86:([^\r\n:]+))?")]
    private static partial Regex Mt940OperationRegex();

    [GeneratedRegex(@"(ЗП-\d+-[A-Z0-9]+)")]
    private static partial Regex RequestNumberRegex();

    public List<BankStatementOperationDto> ParseStatement(string content, string fileName)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        if (fileName.EndsWith(".mt940", StringComparison.OrdinalIgnoreCase) || content.Contains(":61:"))
            return ParseMt940(content);

        return ParseCsv(content);
    }

    private static List<BankStatementOperationDto> ParseCsv(string content)
    {
        var result = new List<BankStatementOperationDto>();
        var lines = content.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines.Skip(1))
        {
            var sep = rawLine.Contains(';') ? ';' : ',';
            var cols = rawLine.Split(sep);
            if (cols.Length < 4) continue;

            if (!DateTime.TryParse(cols[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var opDate))
                opDate = DateTime.UtcNow.Date;
            if (!decimal.TryParse(cols[2].Trim().Replace(" ", "").Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
                continue;

            result.Add(new BankStatementOperationDto(
                cols[0].Trim(),
                opDate,
                amount,
                cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3]) ? cols[3].Trim().ToUpperInvariant() : "BYN",
                cols.Length > 4 ? cols[4].Trim() : string.Empty,
                cols.Length > 5 && !string.IsNullOrWhiteSpace(cols[5]) ? cols[5].Trim() : null,
                cols.Length > 6 ? cols[6].Trim() : string.Empty,
                cols.Length > 7 ? cols[7].Trim() : null));
        }
        return result;
    }

    private static List<BankStatementOperationDto> ParseMt940(string content)
    {
        var result = new List<BankStatementOperationDto>();
        foreach (Match m in Mt940OperationRegex().Matches(content))
        {
            var date = DateTime.ParseExact("20" + m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture);
            decimal amount = decimal.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            string opId = m.Groups[4].Value.Trim();
            string purpose = m.Groups[5].Success ? m.Groups[5].Value.Trim() : string.Empty;
            var reqMatch = RequestNumberRegex().Match(purpose);

            result.Add(new BankStatementOperationDto(
                opId, date, amount, "BYN", string.Empty,
                reqMatch.Success ? reqMatch.Groups[1].Value : null,
                purpose, opId));
        }
        return result;
    }
}

public class AccountingAdapter : IAccountingAdapter
{
    public string ExportEntriesCsv(IEnumerable<Payment> payments)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BankOperationId;PaymentDate;Amount;Currency;AmountInByn;FxDifference;PaymentOrderNumber;Status");
        foreach (var p in payments)
        {
            sb.AppendLine(string.Join(";",
                p.BankOperationId,
                p.PaymentDate.ToString("yyyy-MM-dd"),
                p.Amount.ToString(CultureInfo.InvariantCulture),
                p.Currency,
                p.AmountInByn.ToString(CultureInfo.InvariantCulture),
                p.FxDifference.ToString(CultureInfo.InvariantCulture),
                p.PaymentOrderNumber ?? "",
                p.Status));
        }
        return sb.ToString();
    }
}

public class EstimateImporter : IEstimateImporter
{
    public EstimateImportResultDto ImportFromExcelOrXml(string content, string fileName, IReadOnlyList<CostItem> catalog)
    {
        var lines = new List<EstimateImportLineDto>();
        if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) || content.TrimStart().StartsWith("<"))
        {
            var doc = XDocument.Parse(content);
            foreach (var elem in doc.Descendants("Item"))
            {
                string code = elem.Attribute("Code")?.Value ?? elem.Element("Code")?.Value ?? "";
                string desc = elem.Attribute("Name")?.Value ?? elem.Element("Name")?.Value ?? "";
                decimal.TryParse(elem.Attribute("Amount")?.Value ?? elem.Element("Amount")?.Value ?? "0",
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var amt);

                bool matched = catalog.Any(c =>
                    c.Code.Equals(code, StringComparison.OrdinalIgnoreCase)
                    || desc.Contains(c.Category, StringComparison.OrdinalIgnoreCase));
                lines.Add(new EstimateImportLineDto(code, desc, amt, matched));
            }
        }
        else
        {
            string[] separators = ["\r\n", "\n"];
            foreach (var r in content.Split(separators, StringSplitOptions.RemoveEmptyEntries).Skip(1))
            {
                var cols = r.Split(r.Contains(';') ? ';' : ',');
                if (cols.Length < 3) continue;
                string code = cols[0].Trim();
                string desc = cols[1].Trim();
                decimal.TryParse(cols[2].Trim().Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt);
                bool matched = catalog.Any(c =>
                    c.Code.Equals(code, StringComparison.OrdinalIgnoreCase)
                    || desc.Contains(c.Category, StringComparison.OrdinalIgnoreCase));
                lines.Add(new EstimateImportLineDto(code, desc, amt, matched));
            }
        }

        int matchedCount = lines.Count(l => l.Matched);
        decimal pct = lines.Count == 0 ? 0m : Math.Round((decimal)matchedCount / lines.Count * 100m, 1);
        return new EstimateImportResultDto(lines.Count, matchedCount, pct, lines);
    }
}

public class MockEdsSignatureService : IEdsSignatureService
{
    public EdsSignResultDto SignApproval(string requestNumber, string signerName, string? providedThumbprint)
    {
        string thumb = string.IsNullOrWhiteSpace(providedThumbprint)
            ? $"BY-UCA-{Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()}"
            : providedThumbprint.Trim();
        return new EdsSignResultDto(true, thumb, signerName, DateTime.UtcNow);
    }
}

public class NbrbCurrencyRateProvider : ICurrencyRateProvider
{
    private static readonly Dictionary<string, decimal> DefaultRates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BYN"] = 1.0000m,
        ["USD"] = 3.2500m,
        ["EUR"] = 3.5200m,
        ["RUB"] = 0.0345m,
        ["CNY"] = 0.4500m
    };

    public Task<decimal> GetRateToBynAsync(string currency, DateTime date, CancellationToken ct = default) =>
        Task.FromResult(DefaultRates.TryGetValue(currency, out var r) ? r : 1.0000m);
}

public class AuditService : IAuditService
{
    private readonly AppDbContext _db;
    public AuditService(AppDbContext db) => _db = db;

    public async Task LogAsync(Guid? userId, string userLogin, string objectName, string objectId, string action,
        string? oldValue = null, string? newValue = null, string? details = null, CancellationToken ct = default)
    {
        await _db.AuditLogs.AddAsync(new AuditLog
        {
            UserId = userId,
            UserLogin = userLogin,
            Timestamp = DateTime.UtcNow,
            ObjectName = objectName,
            ObjectId = objectId,
            Action = action,
            OldValue = oldValue,
            NewValue = newValue,
            Details = details
        }, ct);
    }
}

public class InMemoryNotificationPublisher : INotificationPublisher
{
    private readonly AppDbContext _db;
    public InMemoryNotificationPublisher(AppDbContext db) => _db = db;

    public async Task PublishAsync(Notification notification, CancellationToken ct = default) =>
        await _db.Notifications.AddAsync(notification, ct);
}

/// <summary>
/// Единая регистрация библиотеки работы с БД и сервисов предметной области (для Web и тестов).
/// </summary>
public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddFinanveManagerData(
        this IServiceCollection services, string? connectionString, string inMemoryName = "FinanveManagerWebDb")
    {
        bool useSqlServer = DbInitializer.CanUseSqlServer(connectionString);
        services.AddDbContext<AppDbContext>(opt =>
        {
            if (useSqlServer && !string.IsNullOrWhiteSpace(connectionString))
                opt.UseSqlServer(connectionString);
            else
                opt.UseInMemoryDatabase(inMemoryName);
        });

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<INotificationPublisher, InMemoryNotificationPublisher>();
        services.AddScoped<ICurrencyRateProvider, NbrbCurrencyRateProvider>();
        services.AddScoped<IEdsSignatureService, MockEdsSignatureService>();
        services.AddScoped<IBankAdapter, BankAdapter>();
        services.AddScoped<IAccountingAdapter, AccountingAdapter>();
        services.AddScoped<IEstimateImporter, EstimateImporter>();
        services.AddScoped<IReserveStrategy, P80ReserveStrategy>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IAdvancePolicy, AdvancePolicy>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IPaymentRequestService, PaymentRequestService>();
        services.AddScoped<IRiskService, RiskService>();
        services.AddScoped<StatementImportService>();
        services.AddScoped<AuthService>();
        return services;
    }

    public static DataScope CreateScope(this AppDatabaseFactory factory) => new(factory.CreateContext());
}

public sealed class DataScope : IAsyncDisposable, IDisposable
{
    public AppDbContext Db { get; }
    public IAuditService Audit { get; }
    public AuthService Auth { get; }
    public IBudgetService Budget { get; }
    public IPaymentRequestService Requests { get; }
    public IRiskService Risks { get; }
    public StatementImportService StatementImport { get; }
    public IAccountingAdapter Accounting { get; }
    public IEstimateImporter EstimateImporter { get; }

    public DataScope(AppDbContext db)
    {
        Db = db;
        var uow = new UnitOfWork(db);
        Audit = new AuditService(db);
        var pub = new InMemoryNotificationPublisher(db);
        var rate = new NbrbCurrencyRateProvider();
        var eds = new MockEdsSignatureService();
        var bank = new BankAdapter();
        var wf = new WorkflowEngine();
        var advPolicy = new AdvancePolicy(new EfRepository<Advance>(db));

        Auth = new AuthService(db, Audit);
        Budget = new BudgetService(
            new EfRepository<BudgetVersion>(db),
            new EfRepository<BudgetLimit>(db),
            new EfRepository<Obligation>(db),
            new EfRepository<PaymentRequest>(db),
            new EfRepository<ConstructionObject>(db),
            Audit, uow);
        Requests = new PaymentRequestService(
            new EfRepository<PaymentRequest>(db),
            new EfRepository<Contract>(db),
            new EfRepository<Document>(db),
            new EfRepository<Project>(db),
            new EfRepository<Payment>(db),
            Budget, advPolicy, wf, rate, eds, pub, Audit, uow);
        Risks = new RiskService(
            new EfRepository<Risk>(db),
            new EfRepository<RiskAssessment>(db),
            new P80ReserveStrategy(),
            pub, Audit, uow);
        StatementImport = new StatementImportService(
            bank,
            new EfRepository<Payment>(db),
            new EfRepository<PaymentRequest>(db),
            new EfRepository<Obligation>(db),
            wf, Audit, uow);
        Accounting = new AccountingAdapter();
        EstimateImporter = new EstimateImporter();
    }

    public void Dispose() => Db.Dispose();
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
