using FinanveManager.Application.DTOs;
using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;

namespace FinanveManager.Application.Services;

/// <summary>
/// Сервис импорта банковских выписок MT940/CSV с идемпотентным сопоставлением платежей и закрытием обязательств (рис. 2.15).
/// </summary>
public class StatementImportService
{
    private readonly IBankAdapter _bankAdapter;
    private readonly IRepository<Payment> _paymentRepo;
    private readonly IRepository<PaymentRequest> _requestRepo;
    private readonly IRepository<Obligation> _obligationRepo;
    private readonly IWorkflowEngine _workflow;
    private readonly IAuditService _auditService;
    private readonly IUnitOfWork _unitOfWork;

    public StatementImportService(
        IBankAdapter bankAdapter,
        IRepository<Payment> paymentRepo,
        IRepository<PaymentRequest> requestRepo,
        IRepository<Obligation> obligationRepo,
        IWorkflowEngine workflow,
        IAuditService auditService,
        IUnitOfWork unitOfWork)
    {
        _bankAdapter = bankAdapter;
        _paymentRepo = paymentRepo;
        _requestRepo = requestRepo;
        _obligationRepo = obligationRepo;
        _workflow = workflow;
        _auditService = auditService;
        _unitOfWork = unitOfWork;
    }

    public async Task<StatementImportReportDto> ImportAsync(
        string fileContent, string fileName, Guid userId, string userLogin, CancellationToken ct = default)
    {
        var parsedOps = _bankAdapter.ParseStatement(fileContent, fileName);
        var existingPayments = await _paymentRepo.GetAllAsync(ct);
        var requests = await _requestRepo.GetAllAsync(ct);
        var obligations = await _obligationRepo.GetAllAsync(ct);

        int matched = 0;
        int duplicates = 0;
        var unmatched = new List<BankStatementOperationDto>();
        var warnings = new List<string>();

        foreach (var op in parsedOps)
        {
            // Идемпотентность: уникальный идентификатор банковской операции не создаёт дубль (правило 16)
            if (existingPayments.Any(p => !p.IsDeleted && string.Equals(p.BankOperationId, op.BankOperationId, StringComparison.OrdinalIgnoreCase)))
            {
                duplicates++;
                warnings.Add($"Операция {op.BankOperationId} уже загружена ранее — повторная запись пропущена (контроль дублей).");
                continue;
            }

            var req = requests.FirstOrDefault(r =>
                !r.IsDeleted
                && ((op.PaymentRequestNumber != null && r.Number.Equals(op.PaymentRequestNumber, StringComparison.OrdinalIgnoreCase))
                    || (r.Status == RequestStatus.Executed && Math.Abs(r.PayableAmount - op.Amount) < 0.01m)));

            if (req == null)
            {
                unmatched.Add(op);
                warnings.Add($"Платёж {op.BankOperationId} ({op.Amount:N2} {op.Currency}) не сопоставлен с заявкой.");
                continue;
            }

            if (req.Status == RequestStatus.Approved)
            {
                await _workflow.MarkExecutedAsync(req, ct);
            }

            if (req.Status == RequestStatus.Executed)
            {
                await _workflow.CloseByStatementAsync(req, ct);
                await _requestRepo.UpdateAsync(req, ct);
            }

            var payment = new Payment
            {
                PaymentRequestId = req.Id,
                BankOperationId = op.BankOperationId,
                PaymentDate = op.OperationDate.Date,
                Amount = op.Amount,
                Currency = op.Currency,
                ExchangeRate = req.ExchangeRate,
                AmountInByn = Math.Round(op.Amount * req.ExchangeRate, 4),
                Status = "Reconciled",
                PaymentOrderNumber = op.PaymentOrderNumber
            };
            await _paymentRepo.AddAsync(payment, ct);
            existingPayments.Add(payment);

            // Закрытие обязательства (шаг 8 табл. 1.10, рис. 2.15)
            var obl = obligations.FirstOrDefault(o =>
                !o.IsDeleted && o.ContractId == req.ContractId
                && o.ConstructionObjectId == req.ConstructionObjectId
                && o.CostItemId == req.CostItemId && o.Status != ObligationStatus.Closed);
            if (obl != null)
            {
                decimal toApply = Math.Min(obl.Remainder, payment.AmountInByn);
                if (toApply > 0)
                {
                    obl.RegisterPayment(toApply);
                    await _obligationRepo.UpdateAsync(obl, ct);
                }
            }

            matched++;
        }

        await _auditService.LogAsync(userId, userLogin, "BankStatement", fileName, "IMPORT_BANK_STATEMENT",
            null, $"Matched={matched};Duplicates={duplicates};Unmatched={unmatched.Count}", null, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new StatementImportReportDto(
            TotalOperations: parsedOps.Count,
            MatchedCount: matched,
            DuplicateSkippedCount: duplicates,
            UnmatchedCount: unmatched.Count,
            Warnings: warnings,
            UnmatchedOperations: unmatched);
    }
}
