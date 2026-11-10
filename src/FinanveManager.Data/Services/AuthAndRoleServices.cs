using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Data.Services;

public enum ClientApp
{
    Any = 0,
    Wpf = 1,
    Web = 2
}

public enum AppSection
{
    Dashboard,
    ProjectsAndBudgets,
    PaymentRequests,
    PaymentCalendar,
    ContractsAndStatements,
    RisksAndP80,
    Reports,
    SiteActs,
    Administration,
    AuditLog
}

public record SectionDescriptor(AppSection Section, string Title, string Controller);

public record AuthenticatedSession(
    Guid UserId,
    string Login,
    string FullName,
    string RoleCode,
    string RoleName);

public record AuthLoginResult(
    bool Success,
    AuthenticatedSession? Session,
    string? ErrorMessage,
    bool RequiresTwoFactor = false);

public static class RolePermissions
{
    private static readonly Dictionary<string, ClientApp> RoleClients = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CEO"] = ClientApp.Wpf,
        ["Accountant"] = ClientApp.Wpf,
        ["ProcurementSpecialist"] = ClientApp.Wpf,
        ["Admin"] = ClientApp.Wpf,
        ["FinancialManager"] = ClientApp.Web,
        ["ProjectManager"] = ClientApp.Web,
        ["RiskSpecialist"] = ClientApp.Web
    };

    private static readonly Dictionary<string, AppSection[]> AllowedSections = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CEO"] =
        [
            AppSection.Dashboard,
            AppSection.ProjectsAndBudgets,
            AppSection.PaymentRequests,
            AppSection.PaymentCalendar,
            AppSection.RisksAndP80,
            AppSection.Reports
        ],
        ["FinancialManager"] =
        [
            AppSection.Dashboard,
            AppSection.ProjectsAndBudgets,
            AppSection.PaymentRequests,
            AppSection.PaymentCalendar,
            AppSection.ContractsAndStatements,
            AppSection.Reports
        ],
        ["Accountant"] =
        [
            AppSection.Dashboard,
            AppSection.PaymentRequests,
            AppSection.PaymentCalendar,
            AppSection.ContractsAndStatements
        ],
        ["ProcurementSpecialist"] =
        [
            AppSection.Dashboard,
            AppSection.ContractsAndStatements,
            AppSection.PaymentRequests
        ],
        ["ProjectManager"] =
        [
            AppSection.Dashboard,
            AppSection.ProjectsAndBudgets,
            AppSection.PaymentRequests,
            AppSection.SiteActs,
            AppSection.RisksAndP80
        ],
        ["RiskSpecialist"] =
        [
            AppSection.Dashboard,
            AppSection.RisksAndP80,
            AppSection.Reports
        ],
        ["Admin"] =
        [
            AppSection.Administration,
            AppSection.AuditLog
        ]
    };

    private static readonly Dictionary<AppSection, (string Title, string Controller)> Meta = new()
    {
        [AppSection.Dashboard] = ("Главная панель", "Dashboard"),
        [AppSection.ProjectsAndBudgets] = ("Проекты и бюджеты", "Projects"),
        [AppSection.PaymentRequests] = ("Платёжные заявки", "Requests"),
        [AppSection.PaymentCalendar] = ("Платёжный календарь", "Calendar"),
        [AppSection.ContractsAndStatements] = ("Договоры и выписки", "Contracts"),
        [AppSection.RisksAndP80] = ("Риски и резерв P80", "Risks"),
        [AppSection.Reports] = ("Отчёты", "Reports"),
        [AppSection.SiteActs] = ("Акты С-2 (объект)", "Requests"),
        [AppSection.Administration] = ("Администрирование", "Admin"),
        [AppSection.AuditLog] = ("Журнал аудита", "Admin")
    };

    public static ClientApp GetTargetClient(string roleCode) =>
        RoleClients.TryGetValue(roleCode, out var client) ? client : ClientApp.Web;

    public static bool IsClientAllowed(string roleCode, ClientApp currentClient)
    {
        if (currentClient == ClientApp.Any) return true;
        return GetTargetClient(roleCode) == currentClient;
    }

    public static IReadOnlyList<SectionDescriptor> GetSectionsForRole(string roleCode)
    {
        if (!AllowedSections.TryGetValue(roleCode, out var list))
            list = [AppSection.Dashboard];

        return list.Select(s => new SectionDescriptor(s, Meta[s].Title, Meta[s].Controller)).ToList();
    }

    public static bool CanAccess(string roleCode, AppSection section)
    {
        return AllowedSections.TryGetValue(roleCode, out var list) && list.Contains(section);
    }

    public static (string Controller, string Action) GetLandingRoute(string roleCode) => roleCode switch
    {
        "Admin" => ("Admin", "Index"),
        _ => ("Dashboard", "Index")
    };
}

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public AuthService(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<AuthLoginResult> LoginAsync(
        string login,
        string password,
        string? twoFactorCode,
        string sourceClient,
        CancellationToken ct = default)
        => await LoginAsync(login, password, twoFactorCode, sourceClient, ClientApp.Any, ct);

    public async Task<AuthLoginResult> LoginAsync(
        string login,
        string password,
        string? twoFactorCode,
        string sourceClient,
        ClientApp requiredClient,
        CancellationToken ct = default)
    {
        string normalized = (login ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(password))
            return new AuthLoginResult(false, null, "Введите логин и пароль.");

        var user = await _db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Login == normalized && !u.IsDeleted, ct);

        if (user == null || !user.IsActive)
            return new AuthLoginResult(false, null, "Неверный логин или учётная запись отключена.");

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
            return new AuthLoginResult(false, null,
                $"Учётная запись заблокирована до {user.LockedUntil.Value.ToLocalTime():HH:mm:ss} после 5 неудачных попыток (НФТ-7).");

        if (!PasswordHasher.VerifyPassword(password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            if (user.FailedLoginCount >= 5)
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);

            await _audit.LogAsync(user.Id, user.Login, "Auth", user.Login, "LOGIN_FAILED", null,
                $"Попытка {user.FailedLoginCount}", sourceClient, ct);
            await _db.SaveChangesAsync(ct);

            string err = user.LockedUntil.HasValue
                ? "Превышено 5 неудачных попыток. Учётная запись заблокирована на 15 минут (НФТ-7)."
                : $"Неверный пароль (попытка {user.FailedLoginCount} из 5).";
            return new AuthLoginResult(false, null, err);
        }

        string roleCode = user.Role?.Code ?? "ProjectManager";
        if (!RolePermissions.IsClientAllowed(roleCode, requiredClient))
        {
            string targetClientName = RolePermissions.GetTargetClient(roleCode) == ClientApp.Wpf
                ? "настольного WPF-клиента"
                : "веб-клиента";
            return new AuthLoginResult(false, null,
                $"Роль «{user.Role?.Name ?? roleCode}» предназначена для {targetClientName} согласно НФТ-1 (табл. 1.8).");
        }

        if (user.TwoFactorEnabled && (twoFactorCode?.Trim() ?? string.Empty) != (user.TwoFactorSecret ?? "246810"))
        {
            return new AuthLoginResult(false, null,
                "Для администратора требуется код 2FA (по умолчанию: 246810).", RequiresTwoFactor: true);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await _audit.LogAsync(user.Id, user.Login, "Auth", user.Login, "LOGIN_SUCCESS", null,
            roleCode, sourceClient, ct);
        await _db.SaveChangesAsync(ct);

        var session = new AuthenticatedSession(
            user.Id,
            user.Login,
            user.FullName,
            roleCode,
            user.Role?.Name ?? "Пользователь");

        return new AuthLoginResult(true, session, null);
    }
}
