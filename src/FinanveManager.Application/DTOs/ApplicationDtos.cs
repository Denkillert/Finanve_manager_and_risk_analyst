using FinanveManager.Domain.Enums;

namespace FinanveManager.Application.DTOs;

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
