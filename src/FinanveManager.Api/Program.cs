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

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

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

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "FinanveManager.Api",
    runtime = Environment.Version.ToString(),
    timestamp = DateTime.UtcNow
}));

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
