using FinanveManager.Data.Persistence;
using FinanveManager.Data.Services;
using FinanveManager.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddFinanveManagerData(connStr);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnValidatePrincipal = async context =>
        {
            var idText = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = context.Principal?.FindFirstValue(ClaimTypes.Role);
            var login = context.Principal?.Identity?.Name;
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var user = Guid.TryParse(idText, out var id)
                ? await db.Users.AsNoTracking().Include(u => u.Role)
                    .FirstOrDefaultAsync(u => u.Id == id, context.HttpContext.RequestAborted)
                : null;
            if (user == null || user.IsDeleted || !user.IsActive || user.Role == null ||
                !string.Equals(user.Login, login, StringComparison.Ordinal) ||
                !string.Equals(user.Role.Code, role, StringComparison.Ordinal) ||
                !RolePermissions.IsClientAllowed(user.Role.Code, ClientApp.Web))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new FlexibleDecimalModelBinderProvider());
});

var app = builder.Build();

// Проверка подключения к MS SQL Server (ошибка при недоступности; если БД отсутствует — создаётся пустой)
await DbInitializer.EnsureSqlServerDatabaseAsync(connStr);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
