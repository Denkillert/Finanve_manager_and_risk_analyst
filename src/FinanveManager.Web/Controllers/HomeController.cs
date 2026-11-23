using System.Security.Claims;
using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Models;
using FinanveManager.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

/// <summary>Главное меню (хаб): плитки только тех разделов, что разрешены роли.</summary>
[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _db;

    public HomeController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var roleCode = User.FindFirstValue(ClaimTypes.Role) ?? "ProjectManager";
        var roleName = User.FindFirst("RoleName")?.Value ?? roleCode;
        var descriptors = RolePermissions.GetSectionsForRole(roleCode);
        var counts = await LoadCountsAsync(ct);
        var lastRateDate = await _db.CurrencyRates
            .OrderByDescending(r => r.RateDate)
            .Select(r => (DateTime?)r.RateDate)
            .FirstOrDefaultAsync(ct);
        var now = DateTime.Now;

        var tiles = descriptors
            .Select(d => BuildTile(d, counts, roleCode))
            .ToList();

        var vm = new HubViewModel
        {
            Greeting = now.Hour switch
            {
                < 6 => "Доброй ночи",
                < 12 => "Доброе утро",
                < 18 => "Добрый день",
                _ => "Добрый вечер"
            },
            Scope = ScopeFor(roleName),
            ReferenceDate = lastRateDate.HasValue
                ? $"Курсы НБРБ на {lastRateDate.Value:dd.MM.yyyy}"
                : "Курсы НБРБ не загружены",
            UnreadNotifications = counts.UnreadNotifications,
            Tiles = tiles,
            Attention = tiles
                .Where(t => !string.IsNullOrWhiteSpace(t.AttentionText))
                .OrderBy(t => t.CountTone == "hot" ? 0 : t.CountTone == "warm" ? 1 : 2)
                .Take(4)
                .ToList()
        };

        ViewData["Title"] = "Главное меню";
        ViewData["Subtitle"] = "Все разделы системы, доступные вашей роли";
        ViewData["Hub"] = true;
        ViewData["UnreadNotifications"] = counts.UnreadNotifications;
        return View(vm);
    }

    private sealed record HubCounts(
        int UnreadNotifications,
        int Projects,
        int PendingRequests,
        int PendingMaterials,
        int ActiveContracts,
        int HighRisks,
        int Acts,
        int Users,
        int AuditToday);

    private async Task<HubCounts> LoadCountsAsync(CancellationToken ct)
    {
        var today = DateTime.Today;
        var now = DateTime.Now;

        return new HubCounts(
            UnreadNotifications: await _db.Notifications.CountAsync(n => !n.IsRead, ct),
            Projects: await _db.Projects.CountAsync(p => !p.IsDeleted, ct),
            PendingRequests: await _db.PaymentRequests.CountAsync(r => !r.IsDeleted
                && (r.Status == RequestStatus.PendingApproval || r.Status == RequestStatus.Blocked), ct),
            PendingMaterials: await _db.MaterialRequests.CountAsync(m => !m.IsDeleted
                && (m.Status == MaterialRequestStatus.Submitted || m.Status == MaterialRequestStatus.Draft), ct),
            ActiveContracts: await _db.Contracts.CountAsync(c => !c.IsDeleted
                && c.StartDate <= now && c.EndDate >= now, ct),
            HighRisks: await _db.Risks.CountAsync(r => !r.IsDeleted
                && r.IntegralScore >= 15
                && r.Status != RiskStatus.Closed && r.Status != RiskStatus.Realized, ct),
            Acts: await _db.Documents.CountAsync(d => !d.IsDeleted && d.Type == DocumentType.ActS2, ct),
            Users: await _db.Users.CountAsync(u => !u.IsDeleted && u.IsActive, ct),
            AuditToday: await _db.AuditLogs.CountAsync(a => a.Timestamp >= today, ct));
    }

    private static HubTileViewModel BuildTile(SectionDescriptor descriptor, HubCounts counts, string roleCode)
    {
        var section = descriptor.Section;
        var (description, accent) = Describe(section);

        string? countText = null;
        string tone = "soft";
        string? attention = null;

        switch (section)
        {
            case AppSection.Dashboard:
                countText = counts.UnreadNotifications > 0 ? $"{counts.UnreadNotifications} уведомл." : null;
                tone = counts.UnreadNotifications > 0 ? "warm" : "soft";
                break;
            case AppSection.ProjectsAndBudgets:
                countText = counts.Projects > 0
                    ? $"{counts.Projects} {Plural(counts.Projects, "проект", "проекта", "проектов")}"
                    : null;
                break;
            case AppSection.PaymentRequests:
                countText = counts.PendingRequests > 0 ? $"{counts.PendingRequests} · ждёт" : null;
                tone = counts.PendingRequests > 0 ? "hot" : "soft";
                if (counts.PendingRequests > 0)
                    attention = $"Платёжные заявки: {counts.PendingRequests}";
                break;
            case AppSection.ContractsAndStatements:
                countText = counts.ActiveContracts > 0 ? $"{counts.ActiveContracts} действ." : null;
                break;
            case AppSection.RisksAndP80:
                countText = counts.HighRisks > 0 ? $"{counts.HighRisks} · высокий" : null;
                tone = counts.HighRisks > 0 ? "hot" : "soft";
                if (counts.HighRisks > 0)
                    attention = $"Высокие риски: {counts.HighRisks}";
                break;
            case AppSection.SiteActs:
                countText = counts.Acts > 0
                    ? $"{counts.Acts} {Plural(counts.Acts, "акт", "акта", "актов")}"
                    : null;
                break;
            case AppSection.Administration:
                countText = counts.Users > 0 ? $"{counts.Users} учёт." : null;
                break;
            case AppSection.AuditLog:
                countText = counts.AuditToday > 0 ? $"{counts.AuditToday} за сутки" : null;
                break;
        }

        // Заявки на материал живут в разделе «Платёжные заявки» (вкладка прораба и снабженца).
        if (section == AppSection.PaymentRequests && counts.PendingMaterials > 0)
        {
            countText = $"{counts.PendingRequests + counts.PendingMaterials} · ждёт";
            tone = "hot";
            attention ??= $"Заявки на материал: {counts.PendingMaterials}";
        }

        return new HubTileViewModel
        {
            Section = section,
            Title = descriptor.Title,
            Description = description,
            Controller = descriptor.Controller,
            Action = SectionIcons.Action(section),
            Accent = accent,
            CountText = countText,
            CountTone = tone,
            AttentionText = attention
        };
    }

    private static (string Description, string Accent) Describe(AppSection section) => section switch
    {
        AppSection.Dashboard => ("Портфель, ликвидность, EVM и кассовые разрывы", "green"),
        AppSection.ProjectsAndBudgets => ("Проекты, объекты, этапы, версии бюджета и лимиты", "green"),
        AppSection.PaymentRequests => ("Согласование, ЭЦП, исполнение и контроль лимита", "amber"),
        AppSection.PaymentCalendar => ("Платежи по дням, баланс и кассовые разрывы", "amber"),
        AppSection.ContractsAndStatements => ("Договоры, авансы, выписки CSV/MT940, экспорт 1С", "violet"),
        AppSection.RisksAndP80 => ("Матрица 5×5, EMV, σ и портфельный резерв P80", "amber"),
        AppSection.Reports => ("Финансовые отчёты, реестр рисков, печатные формы", "violet"),
        AppSection.SiteActs => ("Акты выполненных работ, удержания, фотофиксация", "green"),
        AppSection.Administration => ("Пользователи, роли, НСИ, курсы валют", "violet"),
        AppSection.AuditLog => ("Неизменяемый журнал действий и попыток входа", "violet"),
        _ => ("Раздел системы", "green")
    };

    private static string Plural(int count, string one, string few, string many)
    {
        int mod100 = Math.Abs(count) % 100;
        int mod10 = mod100 % 10;
        if (mod100 is >= 11 and <= 14) return many;
        return mod10 switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }

    private static string ScopeFor(string roleName) => roleName switch
    {
        var r when r.Contains("Руководитель организации", StringComparison.OrdinalIgnoreCase)
            || r.Contains("Директор", StringComparison.OrdinalIgnoreCase)
            => "Портфель всей организации · утверждение бюджетов и перерасхода",
        var r when r.Contains("Финансовый", StringComparison.OrdinalIgnoreCase)
            => "Оперативный контур · планирование бюджетов и акцепт (ЭЦП)",
        var r when r.Contains("Бухгалтер", StringComparison.OrdinalIgnoreCase)
            => "Фактическое исполнение платежей, выписки и экспорт в 1С",
        var r when r.Contains("снабжен", StringComparison.OrdinalIgnoreCase)
            => "Закупки: реестр поставщиков, цены, договоры, заявки на материал",
        var r when r.Contains("прораб", StringComparison.OrdinalIgnoreCase)
            => "Строго назначенные проекты · заявки на материал и акты С-2",
        var r when r.Contains("риск", StringComparison.OrdinalIgnoreCase)
            => "Реестр рисков, матрица 5×5 и расчёт резерва P80",
        var r when r.Contains("Администратор", StringComparison.OrdinalIgnoreCase)
            => "Учётные записи, НСИ, курсы валют и журнал аудита",
        _ => "Финансовый контур строительной компании"
    };
}
