using System.Text;
using FinanveManager.Application.Interfaces;
using FinanveManager.Infrastructure.Persistence;
using FinanveManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanveManager.Web.Controllers;

[Authorize]
public class ReportsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IBudgetService _budgetService;

    public ReportsController(AppDbContext db, IBudgetService budgetService)
    {
        _db = db;
        _budgetService = budgetService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string reportType = "budget-plan-fact", CancellationToken ct = default)
    {
        var model = await WebPortfolioHelper.BuildReportAsync(_db, _budgetService, reportType, ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Print(string reportType = "budget-plan-fact", CancellationToken ct = default)
    {
        var model = await WebPortfolioHelper.BuildReportAsync(_db, _budgetService, reportType, ct);
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string reportType = "budget-plan-fact", CancellationToken ct = default)
    {
        var model = await WebPortfolioHelper.BuildReportAsync(_db, _budgetService, reportType, ct);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', model.Headers.Select(EscapeCsv)));
        foreach (var row in model.Rows)
            sb.AppendLine(string.Join(';', row.Select(EscapeCsv)));

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", $"{model.ReportType}-report.csv");
    }

    private static string EscapeCsv(string? v)
    {
        var s = (v ?? string.Empty).Replace("\"", "\"\"");
        return $"\"{s}\"";
    }
}
