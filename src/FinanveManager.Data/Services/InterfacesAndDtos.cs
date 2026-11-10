using FinanveManager.Data.Models;

namespace FinanveManager.Data.Services;

public record CreatePaymentRequestDto(
    Guid ProjectId,
    Guid ConstructionObjectId,
    Guid ContractId,
    Guid CostItemId,
    decimal Amount,
    decimal ActAmount,
    decimal AdvanceOffset,
    decimal RetentionAmount,
    bool VatIncluded,
    string Currency,
    RequestType Type,
    DateTime PlannedPaymentDate,
    string Purpose,
    DocumentType DocumentType,
    string DocumentNumber,
    DateTime DocumentDate,
    string? FilePath,
    string? IdempotencyKey,
    bool SubmitImmediately);

public record AdvanceCheckResultDto(
    bool HasOverdueAdvance,
    bool ShouldBlockRequest,
    string? WarningMessage,
    decimal ContractOverdueAmount);

public record RequestCheckSummaryDto(
    bool ContractAndDocumentsValid,
    bool FreeLimitSufficient,
    decimal FreeLimit,
    decimal Delta,
    bool AdvanceCheckPassed,
    bool HasOverdueAdvanceWarning,
    bool HasCashGapOnPlannedDate,
    decimal CashGapAmount,
    DateTime? SuggestedPaymentDate,
    List<string> Messages);

public record BankStatementOperationDto(
    string BankOperationId,
    DateTime OperationDate,
    decimal Amount,
    string Currency,
    string CounterpartyName,
    string? PaymentRequestNumber,
    string Purpose,
    string? PaymentOrderNumber);

public record StatementImportReportDto(
    int TotalOperations,
    int MatchedCount,
    int DuplicateSkippedCount,
    int UnmatchedCount,
    List<string> Warnings,
    List<BankStatementOperationDto> UnmatchedOperations);

public record EstimateImportLineDto(
    string CostItemCode,
    string Description,
    decimal Amount,
    bool Matched);

public record EstimateImportResultDto(
    int TotalLines,
    int MatchedLines,
    decimal MatchPercent,
    List<EstimateImportLineDto> Lines);

public record EdsSignResultDto(
    bool IsValid,
    string CertificateThumbprint,
    string SignerName,
    DateTime SignedAt);

public interface IRepository<T> where T : BaseEntity
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    Task UpdateAsync(T entity, CancellationToken ct = default);
    Task DeleteAsync(T entity, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IPaymentRequestService
{
    Task<(PaymentRequest Request, RequestCheckSummaryDto Checks)> CreateAsync(
        CreatePaymentRequestDto dto, Guid currentUserId, string currentLogin, CancellationToken ct = default);
    Task<PaymentRequest> ApproveOverrunAsync(
        Guid requestId, bool approve, string justificationOrReason, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default);
    Task<PaymentRequest> ApproveAsync(
        Guid requestId, string? edsCertificateThumbprint, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default);
    Task<PaymentRequest> ReturnOrRejectAsync(
        Guid requestId, bool rejectPermanently, string reason, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default);
    Task<Payment> ExecuteAsync(
        Guid requestId, string? paymentOrderNumber, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default);
    Task<int> ExpireStaleBlockedRequestsAsync(DateTime nowUtc, CancellationToken ct = default);
}

public interface IBudgetService
{
    Task<FreeLimitResult> CalculateFreeLimitAsync(Guid constructionObjectId, Guid costItemId, CancellationToken ct = default);
    Task<BudgetVersion> CreateNewVersionAsync(Guid projectId, string comment, Guid userId, string userLogin, CancellationToken ct = default);
    Task<BudgetVersion> ApproveVersionAsync(Guid versionId, byte[] rowVersion, Guid userId, string userLogin, CancellationToken ct = default);
}

public interface IAdvancePolicy
{
    Task<AdvanceCheckResultDto> CheckAsync(Guid contractId, RequestType requestType, DateTime asOfDate, CancellationToken ct = default);
}

public interface IWorkflowEngine
{
    Task SubmitForApprovalAsync(PaymentRequest request, CancellationToken ct = default);
    Task BlockAsync(PaymentRequest request, string reason, CancellationToken ct = default);
    Task ApproveAsync(PaymentRequest request, CancellationToken ct = default);
    Task ReturnForRevisionAsync(PaymentRequest request, string reason, CancellationToken ct = default);
    Task RejectAsync(PaymentRequest request, string reason, CancellationToken ct = default);
    Task MarkExecutedAsync(PaymentRequest request, CancellationToken ct = default);
    Task CloseByStatementAsync(PaymentRequest request, CancellationToken ct = default);
}

public interface IReserveStrategy
{
    decimal Z { get; }
    decimal Calculate(IEnumerable<Risk> risks, out decimal totalEmv, out decimal sigma);
}

public interface IRiskService
{
    Task<decimal> CalculateEmvAsync(Guid projectId, CancellationToken ct = default);
    Task<RiskPortfolioSummary> CalculateReserveP80Async(Guid projectId, CancellationToken ct = default);
    Task<Risk> UpdateAssessmentAsync(
        Guid riskId, decimal probability, decimal monetaryImpact, decimal? residualProbability,
        string comment, byte[] rowVersion, Guid userId, string userLogin, CancellationToken ct = default);
}

public interface INotificationPublisher
{
    Task PublishAsync(Notification notification, CancellationToken ct = default);
}

public interface IAuditService
{
    Task LogAsync(Guid? userId, string userLogin, string objectName, string objectId, string action,
        string? oldValue = null, string? newValue = null, string? details = null, CancellationToken ct = default);
}

public interface IBankAdapter
{
    List<BankStatementOperationDto> ParseStatement(string content, string fileName);
}

public interface IAccountingAdapter
{
    string ExportEntriesCsv(IEnumerable<Payment> payments);
}

public interface IEstimateImporter
{
    EstimateImportResultDto ImportFromExcelOrXml(string content, string fileName, IReadOnlyList<CostItem> catalog);
}

public interface IEdsSignatureService
{
    EdsSignResultDto SignApproval(string requestNumber, string signerName, string? providedThumbprint);
}

public interface ICurrencyRateProvider
{
    Task<decimal> GetRateToBynAsync(string currency, DateTime date, CancellationToken ct = default);
}
