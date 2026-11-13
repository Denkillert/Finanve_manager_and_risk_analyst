using System.Security.Claims;
using System.Text;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanveManager.Web.Controllers;

[Authorize(Roles = "CEO,FinancialManager,RiskSpecialist")]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IBudgetService _budgetService;

    public ReportsController(AppDbContext db, IBudgetService budgetService)
    {
        _db = db;
        _budgetService = budgetService;
    }

    private string NormalizeReportForRole(string? reportType)
    {
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        if (role == "RiskSpecialist")
            return "risks-reserve-p80";

        if (role == "FinancialManager")
        {
            return reportType switch
            {
                "contracts-advances" => "contracts-advances",
                "payment-requests-violations" => "payment-requests-violations",
                "cash-flow-forecast" => "cash-flow-forecast",
                "budget-plan-fact" => "budget-plan-fact",
                _ => "cash-flow-forecast"
            };
        }

        return reportType switch
        {
            "budget-plan-fact" => "budget-plan-fact",
            "project-evm-forecast" => "project-evm-forecast",
            "ceo-portfolio-dashboard" => "ceo-portfolio-dashboard",
            "ceo-liquidity" => "ceo-liquidity",
            "ceo-critical-risks" => "ceo-critical-risks",
            "cash-flow-forecast" => "cash-flow-forecast",
            "contracts-advances" => "contracts-advances",
            "payment-requests-violations" => "payment-requests-violations",
            _ => "ceo-portfolio-dashboard"
        };
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? reportType = null, CancellationToken ct = default)
    {
        var model = await WebPortfolioHelper.BuildReportAsync(_db, _budgetService, NormalizeReportForRole(reportType), ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Print(string? reportType = null, CancellationToken ct = default)
    {
        var model = await WebPortfolioHelper.BuildReportAsync(_db, _budgetService, NormalizeReportForRole(reportType), ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string? reportType = null, CancellationToken ct = default)
    {
        var model = await WebPortfolioHelper.BuildReportAsync(_db, _budgetService, NormalizeReportForRole(reportType), ct);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', model.Headers.Select(EscapeCsv)));
        foreach (var row in model.Rows)
            sb.AppendLine(string.Join(';', row.Select(EscapeCsv)));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"{model.ReportType}-report.csv");
    }

    /// <summary>
    /// Экспорт отчёта в Excel с форматированием (шапка, денежные форматы, итоги).
    /// Для риск-менеджера это основной формат вместо CSV.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportXlsx(string? reportType = null, CancellationToken ct = default)
    {
        var model = await WebPortfolioHelper.BuildReportAsync(_db, _budgetService, NormalizeReportForRole(reportType), ct);

        var wb = new XlsxWorkbook();
        var sheet = wb.AddSheet("Отчёт");
        sheet.SetWidths(model.Headers.Select((_, i) => i == 0 ? 14d : i == 1 ? 42d : 22d).ToArray());
        sheet.FreezeRows(4);

        sheet.AddTitle(model.Title, model.Headers.Count);
        sheet.AddSubtitle(model.Subtitle);
        sheet.AddSubtitle($"Сформировано: {DateTime.Now:dd.MM.yyyy HH:mm}. Роль: {User.FindFirstValue(ClaimTypes.Role)}.");
        sheet.AddHeader(model.Headers.Cast<object?>().ToArray());

        foreach (var row in model.Rows)
        {
            bool isTotal = row.Count > 0 && row[0].Contains("ИТОГО", StringComparison.OrdinalIgnoreCase);
            var cells = row.Select(cell => ParseCell(cell)).ToArray();
            if (isTotal)
                sheet.AddTotalRow(cells);
            else
                sheet.AddRow(cells);
        }

        var bytes = wb.Build();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"{model.ReportType}-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    private static object? ParseCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;
        string trimmed = value.Trim();
        string normalized = trimmed.Replace(" ", string.Empty).Replace("\u00A0", string.Empty);
        if (normalized.Contains(',') && !normalized.Contains('.'))
            normalized = normalized.Replace(',', '.');
        if (decimal.TryParse(normalized, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
            return number;
        return trimmed;
    }

    private static string EscapeCsv(string? v) => $"\"{(v ?? string.Empty).Replace("\"", "\"\"")}\"";
}
