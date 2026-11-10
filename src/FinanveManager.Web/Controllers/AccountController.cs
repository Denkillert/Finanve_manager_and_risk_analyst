using System.Security.Claims;
using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
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
    private readonly AuthService _auth;

    public AccountController(AppDbContext db, AuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken ct = default)
    {
        if (User.Identity?.IsAuthenticated == true && string.IsNullOrWhiteSpace(returnUrl))
        {
            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            var (ctrl, act) = RolePermissions.GetLandingRoute(role);
            return RedirectToAction(act, ctrl);
        }

        ViewBag.ReturnUrl = returnUrl;
        return View(new LoginViewModel { DemoUsers = await LoadDemoUsersAsync(ct) });
    }

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null, CancellationToken ct = default)
    {
        model.DemoUsers = await LoadDemoUsersAsync(ct);
        ViewBag.ReturnUrl = returnUrl;

        var res = await _auth.LoginAsync(model.Login, model.Password, model.TwoFactorCode, "Web ASP.NET", ClientApp.Web, ct);
        if (!res.Success || res.Session == null)
        {
            model.ErrorMessage = res.ErrorMessage;
            model.RequiresTwoFactor = res.RequiresTwoFactor;
            return View(model);
        }

        await SignInSessionAsync(res.Session);
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        var (ctrl, act) = RolePermissions.GetLandingRoute(res.Session.RoleCode);
        return RedirectToAction(act, ctrl);
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

    private async Task SignInSessionAsync(AuthenticatedSession session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.UserId.ToString()),
            new(ClaimTypes.Name, session.Login),
            new(ClaimTypes.Role, session.RoleCode),
            new("FullName", session.FullName),
            new("RoleName", session.RoleName)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }

    private async Task<List<DemoAccountOption>> LoadDemoUsersAsync(CancellationToken ct)
    {
        var all = await _db.Users.Include(u => u.Role)
            .Where(u => !u.IsDeleted && u.IsActive)
            .OrderBy(u => u.Login)
            .Select(u => new DemoAccountOption(
                u.Login,
                u.FullName,
                u.Role != null ? u.Role.Code : "SiteWorker",
                u.Role != null ? u.Role.Name : "",
                u.TwoFactorEnabled))
            .ToListAsync(ct);

        return all.Where(u => RolePermissions.IsClientAllowed(u.RoleCode, ClientApp.Web)).ToList();
    }
}
