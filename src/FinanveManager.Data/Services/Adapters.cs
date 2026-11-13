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
    private static readonly string[] LineSeparators = ["\r\n", "\n", "\r"];

    [GeneratedRegex(@"(ЗП-\d+-[A-Z0-9]+)")]
    private static partial Regex RequestNumberRegex();

    public List<BankStatementOperationDto> ParseStatement(string content, string fileName)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        content = content.TrimStart('\uFEFF', '\u200B', ' ', '\t');
        if (fileName.EndsWith(".mt940", StringComparison.OrdinalIgnoreCase)
            || content.Contains(":61:", StringComparison.Ordinal))
            return ParseMt940(content);

        return ParseCsv(content);
    }

    // ---------------------------------------------------------------- CSV

    private static List<BankStatementOperationDto> ParseCsv(string content)
    {
        var result = new List<BankStatementOperationDto>();
        var lines = content.Split(LineSeparators, StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        if (lines.Count == 0)
            return result;

        char delimiter = DetectDelimiter(lines[0]);
        var header = SplitLine(lines[0], delimiter);

        var map = BuildColumnMap(header);
        int start = map.IsHeader ? 1 : 0;
        if (!map.IsHeader)
            map = ColumnMap.Positional;

        for (int i = start; i < lines.Count; i++)
        {
            var cols = SplitLine(lines[i], delimiter);
            if (cols.Count < 3)
                continue;

            string opId = map.Get(cols, ColumnKind.OperationId);
            if (string.IsNullOrWhiteSpace(opId))
                opId = $"OP-{i:D4}";

            var opDate = ParseDate(map.Get(cols, ColumnKind.Date));
            var rawAmount = map.Get(cols, ColumnKind.Amount);
            if (!TryParseAmount(rawAmount, out var amount))
                continue;
            if (amount == 0m)
                continue;

            string currency = map.Get(cols, ColumnKind.Currency);
            if (string.IsNullOrWhiteSpace(currency))
                currency = "BYN";
            currency = NormalizeCurrency(currency);

            string counterparty = map.Get(cols, ColumnKind.Counterparty);
            string purpose = map.Get(cols, ColumnKind.Purpose);
            string? requestNumber = NullIfBlank(map.Get(cols, ColumnKind.RequestNumber));
            string? order = NullIfBlank(map.Get(cols, ColumnKind.Order));

            if (requestNumber == null && !string.IsNullOrWhiteSpace(purpose))
            {
                var match = RequestNumberRegex().Match(purpose);
                if (match.Success)
                    requestNumber = match.Groups[1].Value;
            }

            result.Add(new BankStatementOperationDto(
                opId.Trim(),
                opDate,
                Math.Abs(amount),
                currency,
                counterparty.Trim(),
                requestNumber,
                purpose.Trim(),
                order));
        }
        return result;
    }

    private static char DetectDelimiter(string firstLine)
    {
        char[] candidates = [';', '\t', '|', ','];
        char best = ';';
        int bestCount = -1;
        foreach (var candidate in candidates)
        {
            int count = CountOutsideQuotes(firstLine, candidate);
            if (count > bestCount)
            {
                bestCount = count;
                best = candidate;
            }
        }
        return best;
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        int count = 0;
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == delimiter && !inQuotes) count++;
        }
        return count;
    }

    private static List<string> SplitLine(string line, char delimiter)
    {
        var values = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        values.Add(current.ToString());
        return values;
    }

    private enum ColumnKind
    {
        OperationId,
        Date,
        Amount,
        Currency,
        Counterparty,
        RequestNumber,
        Purpose,
        Order
    }

    private sealed class ColumnMap
    {
        public bool IsHeader { get; init; }
        public Dictionary<ColumnKind, int> Indexes { get; init; } = new();

        public static ColumnMap Positional => new()
        {
            IsHeader = false,
            Indexes =
            {
                [ColumnKind.OperationId] = 0,
                [ColumnKind.Date] = 1,
                [ColumnKind.Amount] = 2,
                [ColumnKind.Currency] = 3,
                [ColumnKind.Counterparty] = 4,
                [ColumnKind.RequestNumber] = 5,
                [ColumnKind.Purpose] = 6,
                [ColumnKind.Order] = 7
            }
        };

        public string Get(IReadOnlyList<string> cols, ColumnKind kind)
        {
            if (!Indexes.TryGetValue(kind, out var index) || index < 0 || index >= cols.Count)
                return string.Empty;
            return cols[index];
        }
    }

    private static ColumnMap BuildColumnMap(IReadOnlyList<string> header)
    {
        var map = new ColumnMap { IsHeader = false };
        for (int i = 0; i < header.Count; i++)
        {
            var key = NormalizeHeader(header[i]);
            if (key.Length == 0) continue;

            ColumnKind? kind = key switch
            {
                var k when k.Contains("bankoperationid") || k == "operationid" || k == "id"
                    || k.Contains("номероперации") || k.Contains("номердокумента") => ColumnKind.OperationId,
                var k when k == "date" || k.Contains("дата") || k.Contains("operationdate") => ColumnKind.Date,
                var k when k.Contains("amount") || k.Contains("сумма") || k.Contains("summa")
                    || k.Contains("оборот") => ColumnKind.Amount,
                var k when k.Contains("currency") || k.Contains("валюта") => ColumnKind.Currency,
                var k when k.Contains("counterparty") || k.Contains("контрагент")
                    || k.Contains("плательщик") || k.Contains("получатель") => ColumnKind.Counterparty,
                var k when k.Contains("requestnumber") || k.Contains("номерзаявки")
                    || k == "заявка" => ColumnKind.RequestNumber,
                var k when k.Contains("purpose") || k.Contains("назначение") => ColumnKind.Purpose,
                var k when k == "order" || k.Contains("paymentorder") || k.Contains("orderno")
                    || k.Contains("номерпп") => ColumnKind.Order,
                _ => (ColumnKind?)null
            };

            if (kind.HasValue && !map.Indexes.ContainsKey(kind.Value))
                map.Indexes[kind.Value] = i;
        }

        // Заголовок считаем распознанным, если найдены хотя бы дата и сумма.
        bool isHeader = map.Indexes.ContainsKey(ColumnKind.Date) && map.Indexes.ContainsKey(ColumnKind.Amount);
        if (!isHeader)
            return ColumnMap.Positional;

        if (!map.Indexes.ContainsKey(ColumnKind.OperationId)) map.Indexes[ColumnKind.OperationId] = 0;
        return new ColumnMap { IsHeader = true, Indexes = map.Indexes };
    }

    private static string NormalizeHeader(string value) =>
        new string(value.Trim().Trim('"').ToLowerInvariant()
            .Where(char.IsLetterOrDigit).ToArray());

    private static string NormalizeCurrency(string value)
    {
        string cleaned = new string(value.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return string.IsNullOrWhiteSpace(cleaned) ? "BYN" : cleaned;
    }

    private static DateTime ParseDate(string value)
    {
        value = value.Trim().Trim('"');
        string[] formats =
        [
            "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
            "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yyyy HH:mm:ss",
            "dd/MM/yyyy", "d/M/yyyy", "yyyyMMdd", "yyyy/MM/dd",
            "MM/dd/yyyy"
        ];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var date))
            return date.Date;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return date.Date;
        return DateTime.UtcNow.Date;
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        amount = 0m;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string cleaned = value.Trim().Trim('"')
            .Replace("\u00A0", string.Empty)
            .Replace(" ", string.Empty)
            .Replace("BYN", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("RUB", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("USD", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("EUR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("р.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("руб", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (cleaned.Length == 0)
            return false;

        bool negative = cleaned.StartsWith('(') && cleaned.EndsWith(')');
        cleaned = cleaned.Trim('(', ')');

        if (cleaned.Contains(',') && cleaned.Contains('.'))
        {
            // 1.234,56 → 1234.56 ; 1,234.56 → 1234.56
            int lastComma = cleaned.LastIndexOf(',');
            int lastDot = cleaned.LastIndexOf('.');
            if (lastComma > lastDot)
                cleaned = cleaned.Replace(".", string.Empty).Replace(',', '.');
            else
                cleaned = cleaned.Replace(",", string.Empty);
        }
        else if (cleaned.Contains(','))
        {
            cleaned = cleaned.Replace(',', '.');
        }

        if (!decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out amount))
            return false;

        if (negative) amount = -amount;
        return true;
    }

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // -------------------------------------------------------------- MT940

    private static List<BankStatementOperationDto> ParseMt940(string content)
    {
        var result = new List<BankStatementOperationDto>();
        var lines = content.Split(LineSeparators, StringSplitOptions.None);

        string? current61 = null;
        var current86 = new System.Text.StringBuilder();

        void Flush()
        {
            if (current61 == null)
                return;

            string line61 = current61.Trim();
            string rest = line61.StartsWith(":61:", StringComparison.Ordinal)
                ? line61[4..].Trim()
                : line61;

            // Формат: YYMMDD[MMDD]C/D[REV]AmountN...//reference
            if (rest.Length < 8)
            {
                current61 = null;
                current86.Clear();
                return;
            }

            string datePart = rest[..6];
            DateTime date;
            if (!DateTime.TryParseExact("20" + datePart, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out date))
                date = DateTime.UtcNow.Date;

            int idx = 6;
            // Необязательная дата валютирования MMDD
            if (rest.Length > 10 && rest[6..10].All(char.IsDigit))
                idx = 10;

            bool negative = false;
            if (idx < rest.Length && (rest[idx] == 'C' || rest[idx] == 'D'))
            {
                negative = rest[idx] == 'D';
                idx++;
            }
            if (idx + 1 < rest.Length && rest[idx] == 'R' && rest[idx + 1] == 'D')
            {
                negative = !negative;
                idx += 2;
            }

            int amountStart = idx;
            while (idx < rest.Length && (char.IsDigit(rest[idx]) || rest[idx] is ',' or '.'))
                idx++;
            string amountText = rest[amountStart..idx];

            string reference = string.Empty;
            int refIndex = rest.IndexOf("//", idx, StringComparison.Ordinal);
            if (refIndex >= 0)
            {
                reference = rest[(refIndex + 2)..].Trim();
                int nextTag = reference.IndexOf(':');
                if (nextTag > 0) reference = reference[..nextTag].Trim();
            }

            decimal amount = 0m;
            if (TryParseAmount(amountText, out var parsed))
                amount = negative ? -parsed : parsed;

            string purpose = current86.ToString().Trim().Replace("\r", " ").Replace("\n", " ");
            if (purpose.Length == 0)
            {
                int purposeIndex = rest.IndexOf("//", idx, StringComparison.Ordinal);
                if (purposeIndex >= 0)
                    purpose = rest[(purposeIndex + 2)..].Trim();
            }

            var reqMatch = RequestNumberRegex().Match(purpose);
            string opId = string.IsNullOrWhiteSpace(reference) ? $"MT940-{result.Count + 1:D4}" : reference;

            if (amount != 0m)
            {
                result.Add(new BankStatementOperationDto(
                    opId,
                    date,
                    Math.Abs(amount),
                    "BYN",
                    string.Empty,
                    reqMatch.Success ? reqMatch.Groups[1].Value : null,
                    purpose,
                    reference.Length == 0 ? null : reference));
            }

            current61 = null;
            current86.Clear();
        }

        foreach (var raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith(":61:", StringComparison.Ordinal))
            {
                Flush();
                current61 = line;
            }
            else if (line.StartsWith(":86:", StringComparison.Ordinal))
            {
                current86.Append(line.Length > 4 ? line[4..] : string.Empty).Append(' ');
            }
            else if (line.StartsWith(':'))
            {
                Flush();
            }
            else if (current86.Length > 0)
            {
                current86.Append(line.Trim()).Append(' ');
            }
        }
        Flush();
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

    public byte[] ExportEntriesXlsx(IEnumerable<Payment> payments)
    {
        var list = payments.ToList();
        var ctx = new AccountingReportContext
        {
            PeriodStart = list.Count == 0 ? DateTime.UtcNow.Date.AddMonths(-1) : list.Min(p => p.PaymentDate),
            PeriodEnd = list.Count == 0 ? DateTime.UtcNow.Date : list.Max(p => p.PaymentDate),
            Payments = list
        };
        return AccountingReports.BuildPaymentsRegister(ctx);
    }

    public byte[] ExportBalanceSheet(AccountingReportContext context) =>
        AccountingReports.BuildBalanceSheet(context);

    public byte[] ExportProfitAndLoss(AccountingReportContext context) =>
        AccountingReports.BuildProfitAndLoss(context);

    public byte[] ExportVatRegister(AccountingReportContext context) =>
        AccountingReports.BuildVatRegister(context);

    public byte[] ExportPrimaryDocumentsRegister(AccountingReportContext context) =>
        AccountingReports.BuildPrimaryDocumentsRegister(context);

    public byte[] ExportReceivablesPayables(AccountingReportContext context) =>
        AccountingReports.BuildReceivablesPayables(context);

    public byte[] Export1CClientBankExchange(AccountingReportContext context) =>
        AccountingReports.Build1CClientBankExchange(context);
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
        this IServiceCollection services, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Строка подключения к MS SQL Server не задана (ConnectionStrings:DefaultConnection).");

        services.AddDbContext<AppDbContext>(opt => opt.UseSqlServer(connectionString));

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<INotificationPublisher, InMemoryNotificationPublisher>();
        services.AddScoped<ICurrencyRateProvider, NbrbCurrencyRateProvider>();
        services.AddScoped<IEdsSignatureService, MockEdsSignatureService>();
        services.AddScoped<IBankAdapter, BankAdapter>();
        services.AddScoped<IAccountingAdapter, AccountingAdapter>();
        services.AddScoped<IReserveStrategy, P80ReserveStrategy>();
        services.AddScoped<IWorkflowEngine, WorkflowEngine>();
        services.AddScoped<IAdvancePolicy, AdvancePolicy>();
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IPaymentRequestService, PaymentRequestService>();
        services.AddScoped<IRiskService, RiskService>();
        services.AddScoped<IProjectManagementService, ProjectManagementService>();
        services.AddScoped<IMaterialRequestService, MaterialRequestService>();
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
    public IProjectManagementService ProjectMgmt { get; }
    public StatementImportService StatementImport { get; }
    public IAccountingAdapter Accounting { get; }
    public IMaterialRequestService MaterialRequests { get; }

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
        ProjectMgmt = new ProjectManagementService(db, pub, Audit);
        StatementImport = new StatementImportService(
            bank,
            new EfRepository<Payment>(db),
            new EfRepository<PaymentRequest>(db),
            new EfRepository<Obligation>(db),
            wf, Audit, uow);
        Accounting = new AccountingAdapter();
        MaterialRequests = new MaterialRequestService(db, Audit);
    }

    public void Dispose() => Db.Dispose();
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}
