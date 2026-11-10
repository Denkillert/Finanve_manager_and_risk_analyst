using System.Security.Claims;
using FinanveManager.Application.Interfaces;
using FinanveManager.Infrastructure.Persistence;
using FinanveManager.Infrastructure.Security;
using FinanveManager.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Web.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;

    public AccountController(AppDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken ct = default)
    {
        ViewBag.ReturnUrl = returnUrl;
        var model = new LoginViewModel
        {
            DemoUsers = await LoadDemoUsersAsync(ct)
        };
        return View(model);
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null, CancellationToken ct = default)
    {
        model.DemoUsers = await LoadDemoUsersAsync(ct);
        ViewBag.ReturnUrl = returnUrl;

        var login = model.Login?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(model.Password))
        {
            model.ErrorMessage = "Укажите логин и пароль.";
            return View(model);
        }

        var user = await _db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Login == login && !u.IsDeleted, ct);

        if (user == null || !user.IsActive)
        {
            model.ErrorMessage = "Неверный логин или учётная запись отключена.";
            return View(model);
        }

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
        {
            model.ErrorMessage = $"Учётная запись заблокирована до {user.LockedUntil.Value.ToLocalTime():HH:mm:ss} после 5 неудачных попыток (НФТ-7).";
            return View(model);
        }

        if (!PasswordHasher.VerifyPassword(model.Password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            if (user.FailedLoginCount >= 5)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            }
            await _audit.LogAsync(user.Id, user.Login, "Auth", user.Login, "LOGIN_FAILED", null,
                $"Attempt {user.FailedLoginCount}", user.LockedUntil.HasValue ? "ACCOUNT_LOCKED" : null, ct);
            await _db.SaveChangesAsync(ct);

            model.ErrorMessage = user.LockedUntil.HasValue
                ? "Превышено 5 неудачных попыток. Учётная запись заблокирована на 15 минут (НФТ-7)."
                : $"Неверный пароль (попытка {user.FailedLoginCount} из 5).";
            return View(model);
        }

        if (user.TwoFactorEnabled && (model.TwoFactorCode?.Trim() ?? string.Empty) != (user.TwoFactorSecret ?? "246810"))
        {
            model.RequiresTwoFactor = true;
            model.ErrorMessage = "Для администратора требуется код двухфакторной аутентификации (2FA: 246810).";
            return View(model);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await _audit.LogAsync(user.Id, user.Login, "Auth", user.Login, "LOGIN_SUCCESS", null, user.Role?.Code, "Web ASP.NET", ct);
        await _db.SaveChangesAsync(ct);

        await SignInUserAsync(user);
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);
        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickLogin(string login, CancellationToken ct = default)
    {
        var user = await _db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Login == login && !u.IsDeleted && u.IsActive, ct);

        if (user == null)
            return RedirectToAction(nameof(Login));

        if (user.TwoFactorEnabled)
        {
            var vm = new LoginViewModel
            {
                Login = user.Login,
                Password = "StroyFin2026!",
                TwoFactorCode = "246810",
                RequiresTwoFactor = true,
                DemoUsers = await LoadDemoUsersAsync(ct)
            };
            return View("Login", vm);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await _audit.LogAsync(user.Id, user.Login, "Auth", user.Login, "QUICK_LOGIN", null, user.Role?.Code, "Web ASP.NET", ct);
        await _db.SaveChangesAsync(ct);

        await SignInUserAsync(user);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied() => View();

    private async Task SignInUserAsync(FinanveManager.Domain.Entities.User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Login),
            new(ClaimTypes.Role, user.Role?.Code ?? "SiteWorker"),
            new("FullName", user.FullName),
            new("RoleName", user.Role?.Name ?? "Пользователь")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    private async Task<List<DemoAccountOption>> LoadDemoUsersAsync(CancellationToken ct)
    {
        return await _db.Users.Include(u => u.Role)
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.Login)
            .Select(u => new DemoAccountOption(
                u.Login,
                u.FullName,
                u.Role != null ? u.Role.Code : "SiteWorker",
                u.Role != null ? u.Role.Name : "",
                u.Role != null ? u.Role.ClientType : "Web",
                u.TwoFactorEnabled))
            .ToListAsync(ct);
    }
}
