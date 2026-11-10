using System.Security.Authentication;
using System.Text;
using FinanveManager.Application.Interfaces;
using FinanveManager.Application.Services;
using FinanveManager.Application.Strategies;
using FinanveManager.Domain.Common;
using FinanveManager.Infrastructure.Adapters;
using FinanveManager.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// НФТ-6: Запрет протоколов ниже TLS 1.3 для продакшн-конечных точек HTTPS
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls13;
    });
});

var connStr = builder.Configuration.GetConnectionString("DefaultConnection");
bool useSqlServer = DbInitializer.CanUseSqlServer(connStr);

builder.Services.AddDbContext<AppDbContext>(opt =>
{
    if (useSqlServer)
        opt.UseSqlServer(connStr);
    else
        opt.UseInMemoryDatabase("FinanveManagerApiDb");
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
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.SeedIfEmptyAsync(db);
}

app.UseExceptionHandler(errApp =>
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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => Results.Content(
    """
    <!doctype html><html lang="ru"><head><meta charset="utf-8"><title>FinanveManager.Api</title></head>
    <body style="font-family:Segoe UI,sans-serif;max-width:720px;margin:40px auto;line-height:1.6">
      <h2>FinanveManager.Api (.NET 9.0) запущен</h2>
      <p>REST API системы «СтройФинРиск» активен.</p>
      <ul>
        <li><a href="/api/health">/api/health</a> — статус сервиса</li>
        <li><a href="/openapi/v1.json">/openapi/v1.json</a> — спецификация OpenAPI (в режиме Development)</li>
      </ul>
      <p>Для интерактивного веб-интерфейса на C# ASP.NET запустите проект <strong>FinanveManager.Web</strong>.</p>
    </body></html>
    """,
    "text/html; charset=utf-8"));

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "FinanveManager.Api",
    database = useSqlServer ? "SqlServer" : "InMemory",
    runtime = Environment.Version.ToString(),
    timestamp = DateTime.UtcNow
}));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
