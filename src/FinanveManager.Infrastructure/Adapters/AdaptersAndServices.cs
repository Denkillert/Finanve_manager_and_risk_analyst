using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FinanveManager.Application.DTOs;
using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Entities;
using FinanveManager.Infrastructure.Persistence;

namespace FinanveManager.Infrastructure.Adapters;

/// <summary>
/// Адаптер банка (рис. 2.13, 2.15, требование 20): разбор выписок MT940 и CSV.
/// </summary>
public class BankAdapter : IBankAdapter
{
    public List<BankStatementOperationDto> ParseStatement(string content, string fileName)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new List<BankStatementOperationDto>();

        if (fileName.EndsWith(".mt940", StringComparison.OrdinalIgnoreCase) || content.Contains(":61:"))
            return ParseMt940(content);

        return ParseCsv(content);
    }

    private static List<BankStatementOperationDto> ParseCsv(string content)
    {
        var result = new List<BankStatementOperationDto>();
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
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
                BankOperationId: cols[0].Trim(),
                OperationDate: opDate,
                Amount: amount,
                Currency: cols.Length > 3 && !string.IsNullOrWhiteSpace(cols[3]) ? cols[3].Trim().ToUpperInvariant() : "BYN",
                CounterpartyName: cols.Length > 4 ? cols[4].Trim() : string.Empty,
                PaymentRequestNumber: cols.Length > 5 && !string.IsNullOrWhiteSpace(cols[5]) ? cols[5].Trim() : null,
                Purpose: cols.Length > 6 ? cols[6].Trim() : string.Empty,
                PaymentOrderNumber: cols.Length > 7 ? cols[7].Trim() : null));
        }
        return result;
    }

    private static List<BankStatementOperationDto> ParseMt940(string content)
    {
        var result = new List<BankStatementOperationDto>();
        var matches = Regex.Matches(content, @":61:(\d{6})([CD])([\d,\.]+)N\w+//([^\r\n]+)(?:\r?\n:86:([^\r\n:]+))?");
        foreach (Match m in matches)
        {
            string yymmdd = m.Groups[1].Value;
            var date = DateTime.ParseExact("20" + yymmdd, "yyyyMMdd", CultureInfo.InvariantCulture);
            decimal amount = decimal.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            string opId = m.Groups[4].Value.Trim();
            string purpose = m.Groups[5].Success ? m.Groups[5].Value.Trim() : string.Empty;

            var reqMatch = Regex.Match(purpose, @"(ЗП-\d+-[A-Z0-9]+)");
            result.Add(new BankStatementOperationDto(
                BankOperationId: opId,
                OperationDate: date,
                Amount: amount,
                Currency: "BYN",
                CounterpartyName: string.Empty,
                PaymentRequestNumber: reqMatch.Success ? reqMatch.Groups[1].Value : null,
                Purpose: purpose,
                PaymentOrderNumber: opId));
        }
        return result;
    }
}

public class AccountingAdapter : IAccountingAdapter
{
    public string ExportEntriesCsv(IEnumerable<Payment> payments, IEnumerable<Document> documents)
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

    public Task<int> ImportEntriesAsync(string content, CancellationToken ct = default)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult(Math.Max(0, lines.Length - 1));
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
            var rows = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var r in rows.Skip(1))
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

    public Task<decimal> GetRateToBynAsync(string currency, DateTime date, CancellationToken ct = default)
    {
        return Task.FromResult(DefaultRates.TryGetValue(currency, out var r) ? r : 1.0000m);
    }
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

    public async Task PublishAsync(Notification notification, CancellationToken ct = default)
    {
        await _db.Notifications.AddAsync(notification, ct);
    }
}
