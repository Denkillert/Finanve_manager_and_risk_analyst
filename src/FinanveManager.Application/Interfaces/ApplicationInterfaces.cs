using FinanveManager.Application.DTOs;
using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;
using FinanveManager.Domain.Services;

namespace FinanveManager.Application.Interfaces;

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
    Task<PaymentRequest> ResubmitAsync(
        Guid requestId, CreatePaymentRequestDto dto, byte[] rowVersion, Guid currentUserId, string currentLogin, CancellationToken ct = default);
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
    Task<decimal> CalculateObligationRemainderAsync(Guid constructionObjectId, Guid costItemId, CancellationToken ct = default);
    Task<BudgetVersion> CreateNewVersionAsync(Guid projectId, string comment, Guid userId, string userLogin, CancellationToken ct = default);
    Task<BudgetVersion> ApproveVersionAsync(Guid versionId, byte[] rowVersion, Guid userId, string userLogin, CancellationToken ct = default);
}

public interface IAdvancePolicy
{
    Task<AdvanceCheckResultDto> CheckAsync(Guid contractId, RequestType requestType, DateTime asOfDate, CancellationToken ct = default);
    bool IsOverdue(Advance advance, DateTime asOfDate);
    bool BlockNewAdvance(IEnumerable<Advance> advances, RequestType requestType, DateTime asOfDate);
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
    string ExportEntriesCsv(IEnumerable<Payment> payments, IEnumerable<Document> documents);
    Task<int> ImportEntriesAsync(string content, CancellationToken ct = default);
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

public interface IReportGenerator
{
    byte[] GenerateExcelReport(string reportType, object model);
    byte[] GeneratePdfReport(string reportType, object model);
}
