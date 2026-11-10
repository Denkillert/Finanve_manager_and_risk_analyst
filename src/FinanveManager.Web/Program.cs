using System.Text;
using System.Text.Json.Serialization;
using FinanveManager.Application.Interfaces;
using FinanveManager.Application.Services;
using FinanveManager.Application.Strategies;
using FinanveManager.Domain.Common;
using FinanveManager.Infrastructure.Adapters;
using FinanveManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
bool useSqlServer = DbInitializer.CanUseSqlServer(connStr);

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    if (useSqlServer)
        opt.UseSqlServer(connStr);
    else
        opt.UseInMemoryDatabase("FinanveManagerWebDb");
});

builder.Services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<INotificationPublisher, InMemoryNotificationPublisher>();
builder.Services.AddScoped<ICurrencyRateProvider, NbrbCurrencyRateProvider>();
builder.Services.AddScoped<IEdsSignatureService, MockEdsSignatureService>();
builder.Services.AddScoped<IBankAdapter, BankAdapter>();
builder.Services.AddScoped<IAccountingAdapter, AccountingAdapter>();
builder.Services.AddScoped<IEstimateImporter, EstimateImporter>();
builder.Services.AddScoped<IReserveStrategy, P80ReserveStrategy>();
builder.Services.AddScoped<IWorkflowEngine, WorkflowEngine>();
builder.Services.AddScoped<IAdvancePolicy, AdvancePolicy>();
builder.Services.AddScoped<IBudgetService, BudgetService>();
builder.Services.AddScoped<IPaymentRequestService, PaymentRequestService>();
builder.Services.AddScoped<IRiskService, RiskService>();
builder.Services.AddScoped<StatementImportService>();

var signingKey = builder.Configuration["Jwt:SigningKey"] ?? "DEV_FALLBACK_KEY_32_CHARS_MINIMUM!";
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews()
    .AddJsonOptions(opt => opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.SeedIfEmptyAsync(db);
    app.Logger.LogInformation("FinanveManager.Web database ready ({Provider}).", useSqlServer ? "SqlServer" : "InMemory");
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else
{
    app.UseHsts();
}

app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments("/api"), apiBranch =>
{
    apiBranch.UseExceptionHandler(errApp =>
    {
        errApp.Run(async context =>
        {
            var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = ex switch
            {
                ConcurrencyConflictException => StatusCodes.Status409Conflict,
                DuplicateEntityException => StatusCodes.Status409Conflict,
                DomainException => StatusCodes.Status400BadRequest,
                UnauthorizedAccessException => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status500InternalServerError
            };
            await context.Response.WriteAsJsonAsync(new
            {
                error = ex?.Message ?? "Внутренняя ошибка сервера",
                code = (ex as DomainException)?.ErrorCode ?? "SERVER_ERROR"
            });
        });
    });
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "FinanveManager.Web",
    database = useSqlServer ? "SqlServer" : "InMemory",
    runtime = Environment.Version.ToString(),
    timestamp = DateTime.UtcNow
}));

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
