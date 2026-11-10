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
            return reportType == "project-evm-forecast" ? "project-evm-forecast" : "risks-reserve-p80";
        return string.IsNullOrWhiteSpace(reportType) ? "budget-plan-fact" : reportType;
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

    private static string EscapeCsv(string? v) => $"\"{(v ?? string.Empty).Replace("\"", "\"\"")}\"";
}
