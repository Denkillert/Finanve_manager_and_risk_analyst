using System.Security.Claims;
using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Entities;
using FinanveManager.Infrastructure.Persistence;
using FinanveManager.Infrastructure.Security;
using FinanveManager.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DomainUser = FinanveManager.Domain.Entities.User;

namespace FinanveManager.Web.Controllers;

[Authorize(Roles = "Admin,CEO,FinancialManager")]
public class AdminController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public AdminController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private (Guid UserId, string Login) CurrentUser() =>
        (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
         User.Identity?.Name ?? "unknown");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var vm = new AdminViewModel
        {
            Roles = await _db.Roles.Where(r => !r.IsDeleted).OrderBy(r => r.Code).ToListAsync(ct),
            Users = await _db.Users.Include(u => u.Role).Where(u => !u.IsDeleted).OrderBy(u => u.Login).ToListAsync(ct),
            NormRules = await _db.NormRules.Where(n => !n.IsDeleted).OrderBy(n => n.Code).ToListAsync(ct),
            CurrencyRates = await _db.CurrencyRates.Where(c => !c.IsDeleted).OrderBy(c => c.Currency).ToListAsync(ct),
            AuditLogs = await _db.AuditLogs.OrderByDescending(a => a.Timestamp).Take(100).ToListAsync(ct)
        };
        return View(vm);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(string login, string fullName, string email, Guid roleId, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            TempData["Error"] = "Пароль должен содержать минимум 12 символов (НФТ-7), а логин не может быть пустым.";
            return RedirectToAction(nameof(Index));
        }

        string normLogin = login.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Login == normLogin, ct))
        {
            TempData["Error"] = $"Пользователь с логином «{normLogin}» уже существует.";
            return RedirectToAction(nameof(Index));
        }

        var entity = new DomainUser
        {
            RoleId = roleId,
            Login = normLogin,
            FullName = string.IsNullOrWhiteSpace(fullName) ? normLogin : fullName.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? $"{normLogin}@stroyfinrisk.by" : email.Trim(),
            PasswordHash = PasswordHasher.HashPassword(password),
            IsActive = true,
            PersonalDataConsentGiven = true,
            ConsentDate = DateTime.UtcNow
        };
        await _db.Users.AddAsync(entity, ct);

        var (uid, actor) = CurrentUser();
        await _audit.LogAsync(uid, actor, nameof(DomainUser), normLogin, "CREATE_USER", null, entity.FullName, null, ct);
        await _db.SaveChangesAsync(ct);
        TempData["Success"] = $"Создан пользователь {entity.Login} ({entity.FullName}).";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUserActive(Guid userId, CancellationToken ct)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted, ct);
        if (user == null)
        {
            TempData["Error"] = "Пользователь не найден.";
            return RedirectToAction(nameof(Index));
        }
        if (user.Login == "admin")
        {
            TempData["Error"] = "Системную учётную запись admin нельзя отключить.";
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = !user.IsActive;
        user.LockedUntil = null;
        user.FailedLoginCount = 0;

        var (uid, actor) = CurrentUser();
        await _audit.LogAsync(uid, actor, nameof(DomainUser), user.Login, "TOGGLE_USER_ACTIVE", null, user.IsActive.ToString(), null, ct);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Пользователь {user.Login}: IsActive = {user.IsActive}.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = "Admin,FinancialManager")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNormRule(string code, string name, string documentSource, string valueText, decimal? numericValue, string responsibleRole, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            TempData["Error"] = "Укажите код и наименование нормативного правила.";
            return RedirectToAction(nameof(Index));
        }

        var rule = new NormRule
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            DocumentSource = string.IsNullOrWhiteSpace(documentSource) ? "Внутренний стандарт" : documentSource.Trim(),
            ValueText = valueText?.Trim() ?? "",
            NumericValue = numericValue,
            ValidFrom = new DateTime(2026, 11, 10),
            ResponsibleRole = string.IsNullOrWhiteSpace(responsibleRole) ? "FinManager" : responsibleRole.Trim(),
            Revision = 1
        };
        await _db.NormRules.AddAsync(rule, ct);
        var (uid, actor) = CurrentUser();
        await _audit.LogAsync(uid, actor, nameof(NormRule), rule.Code, "ADD_NORM_RULE", null, rule.ValueText, rule.DocumentSource, ct);
        await _db.SaveChangesAsync(ct);

        TempData["Success"] = $"Нормативное правило {rule.Code} добавлено.";
        return RedirectToAction(nameof(Index));
    }
}
