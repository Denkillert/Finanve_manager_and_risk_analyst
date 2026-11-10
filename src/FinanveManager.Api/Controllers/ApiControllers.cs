using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FinanveManager.Application.DTOs;
using FinanveManager.Application.Interfaces;
using FinanveManager.Application.Services;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;
using FinanveManager.Domain.Services;
using FinanveManager.Infrastructure.Persistence;
using FinanveManager.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace FinanveManager.Api.Controllers;

public record LoginRequest(string Login, string Password, string? TwoFactorCode);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IAuditService _audit;

    public AuthController(AppDbContext db, IConfiguration config, IAuditService audit)
    {
        _db = db;
        _config = config;
        _audit = audit;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var user = await _db.Users.Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Login == req.Login && !u.IsDeleted, ct);

        if (user == null || !user.IsActive)
            return Unauthorized(new { error = "Неверный логин или пароль." });

        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
            return StatusCode(423, new { error = $"Учётная запись заблокирована до {user.LockedUntil:HH:mm}." });

        if (!PasswordHasher.VerifyPassword(req.Password, user.PasswordHash))
        {
            user.FailedLoginCount += 1;
            if (user.FailedLoginCount >= 5)
            {
                user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
            }
            await _db.SaveChangesAsync(ct);
            await _audit.LogAsync(user.Id, user.Login, "Auth", user.Login, "LOGIN_FAILED", null, $"Attempt {user.FailedLoginCount}", null, ct);
            await _db.SaveChangesAsync(ct);
            return Unauthorized(new { error = "Неверный логин или пароль." });
        }

        if (user.TwoFactorEnabled && req.TwoFactorCode != "246810")
            return Unauthorized(new { error = "Требуется код двухфакторной аутентификации администратора.", requiresTwoFactor = true });

        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        await _audit.LogAsync(user.Id, user.Login, "Auth", user.Login, "LOGIN_SUCCESS", null, user.Role?.Code, null, ct);
        await _db.SaveChangesAsync(ct);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SigningKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Login),
            new Claim(ClaimTypes.Role, user.Role?.Code ?? "SiteWorker")
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: creds);

        return Ok(new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            user = new { user.Id, user.Login, user.FullName, Role = user.Role?.Code, RoleName = user.Role?.Name }
        });
    }
}

[ApiController]
[Route("api/payment-requests")]
[Authorize]
public class PaymentRequestsController : ControllerBase
{
    private readonly IPaymentRequestService _service;
    private readonly IRepository<PaymentRequest> _repo;

    public PaymentRequestsController(IPaymentRequestService service, IRepository<PaymentRequest> repo)
    {
        _service = service;
        _repo = repo;
    }

    private (Guid UserId, string Login) CurrentUser() =>
        (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty,
         User.Identity?.Name ?? "unknown");

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct) =>
        Ok(await _repo.GetAllAsync(ct));

    [HttpPost]
    [Authorize(Roles = "ProjectManager,ProcurementSpecialist,FinancialManager,CEO")]
    public async Task<IActionResult> Create([FromBody] CreatePaymentRequestDto dto, CancellationToken ct)
    {
        var (uid, login) = CurrentUser();
        var (req, checks) = await _service.CreateAsync(dto, uid, login, ct);
        return Ok(new { request = req, checks });
    }

    [HttpPost("{id:guid}/approve-overrun")]
    [Authorize(Roles = "CEO")]
    public async Task<IActionResult> ApproveOverrun(Guid id, [FromBody] OverrunDecisionDto dto, CancellationToken ct)
    {
        var (uid, login) = CurrentUser();
        return Ok(await _service.ApproveOverrunAsync(id, dto.Approve, dto.Reason, dto.RowVersion, uid, login, ct));
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "FinancialManager,CEO")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApproveRequestDto dto, CancellationToken ct)
    {
        var (uid, login) = CurrentUser();
        return Ok(await _service.ApproveAsync(id, dto.EdsCertificateThumbprint, dto.RowVersion, uid, login, ct));
    }

    [HttpPost("{id:guid}/return-or-reject")]
    [Authorize(Roles = "FinancialManager,CEO")]
    public async Task<IActionResult> ReturnOrReject(Guid id, [FromBody] ReturnRejectDto dto, CancellationToken ct)
    {
        var (uid, login) = CurrentUser();
        return Ok(await _service.ReturnOrRejectAsync(id, dto.RejectPermanently, dto.Reason, dto.RowVersion, uid, login, ct));
    }

    [HttpPost("{id:guid}/execute")]
    [Authorize(Roles = "Accountant")]
    public async Task<IActionResult> Execute(Guid id, [FromBody] ExecutePaymentDto dto, CancellationToken ct)
    {
        var (uid, login) = CurrentUser();
        return Ok(await _service.ExecuteAsync(id, dto.PaymentOrderNumber, dto.RowVersion, uid, login, ct));
    }
}

public record OverrunDecisionDto(bool Approve, string Reason, byte[] RowVersion);
public record ApproveRequestDto(string? EdsCertificateThumbprint, byte[] RowVersion);
public record ReturnRejectDto(bool RejectPermanently, string Reason, byte[] RowVersion);
public record ExecutePaymentDto(string? PaymentOrderNumber, byte[] RowVersion);

[ApiController]
[Route("api/budget")]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly IBudgetService _budgetService;
    public BudgetController(IBudgetService budgetService) => _budgetService = budgetService;

    [HttpGet("free-limit")]
    public async Task<IActionResult> GetFreeLimit([FromQuery] Guid objectId, [FromQuery] Guid costItemId, CancellationToken ct) =>
        Ok(await _budgetService.CalculateFreeLimitAsync(objectId, costItemId, ct));

    [HttpPost("versions")]
    [Authorize(Roles = "FinancialManager,CEO")]
    public async Task<IActionResult> CreateVersion([FromQuery] Guid projectId, [FromQuery] string comment, CancellationToken ct)
    {
        var uid = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        return Ok(await _budgetService.CreateNewVersionAsync(projectId, comment, uid, User.Identity?.Name ?? "", ct));
    }
}

[ApiController]
[Route("api/risks")]
[Authorize]
public class RiskController : ControllerBase
{
    private readonly IRiskService _riskService;
    private readonly IRepository<Risk> _riskRepo;

    public RiskController(IRiskService riskService, IRepository<Risk> riskRepo)
    {
        _riskService = riskService;
        _riskRepo = riskRepo;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid projectId, CancellationToken ct)
    {
        var risks = (await _riskRepo.GetAllAsync(ct))
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.Emv)
            .ThenByDescending(r => r.IntegralScore)
            .ToList();
        var summary = await _riskService.CalculateReserveP80Async(projectId, ct);
        var sensitivity = RiskCalculator.CalculateEacSensitivity();
        return Ok(new { risks, summary, sensitivity });
    }

    [HttpPut("{id:guid}/assessment")]
    [Authorize(Roles = "RiskSpecialist,ProjectManager,CEO")]
    public async Task<IActionResult> UpdateAssessment(Guid id, [FromBody] UpdateRiskDto dto, CancellationToken ct)
    {
        var uid = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed) ? parsed : Guid.Empty;
        return Ok(await _riskService.UpdateAssessmentAsync(
            id, dto.Probability, dto.MonetaryImpact, dto.ResidualProbability, dto.Comment, dto.RowVersion,
            uid, User.Identity?.Name ?? "", ct));
    }
}

public record UpdateRiskDto(decimal Probability, decimal MonetaryImpact, decimal? ResidualProbability, string Comment, byte[] RowVersion);

[ApiController]
[Route("api/integrations")]
[Authorize]
public class IntegrationsController : ControllerBase
{
    private readonly StatementImportService _statementService;
    private readonly IEstimateImporter _estimateImporter;
    private readonly IRepository<CostItem> _costItemRepo;

    public IntegrationsController(
        StatementImportService statementService,
        IEstimateImporter estimateImporter,
        IRepository<CostItem> costItemRepo)
    {
        _statementService = statementService;
        _estimateImporter = estimateImporter;
        _costItemRepo = costItemRepo;
    }

    [HttpPost("bank-statement")]
    [Authorize(Roles = "Accountant,Admin")]
    public async Task<IActionResult> ImportStatement([FromBody] TextFilePayload payload, CancellationToken ct)
    {
        var uid = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
        return Ok(await _statementService.ImportAsync(payload.Content, payload.FileName, uid, User.Identity?.Name ?? "", ct));
    }

    [HttpPost("estimate-import")]
    [Authorize(Roles = "FinancialManager,ProjectManager,ProcurementSpecialist")]
    public async Task<IActionResult> ImportEstimate([FromBody] TextFilePayload payload, CancellationToken ct)
    {
        var catalog = await _costItemRepo.GetAllAsync(ct);
        return Ok(_estimateImporter.ImportFromExcelOrXml(payload.Content, payload.FileName, catalog));
    }
}

public record TextFilePayload(string FileName, string Content);
