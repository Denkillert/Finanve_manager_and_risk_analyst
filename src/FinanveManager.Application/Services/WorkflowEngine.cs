using FinanveManager.Application.Interfaces;
using FinanveManager.Domain.Common;
using FinanveManager.Domain.Entities;
using FinanveManager.Domain.Enums;

namespace FinanveManager.Application.Services;

/// <summary>
/// Конечный автомат состояний платёжной заявки (таблица 1.11, рис. 2.18).
/// </summary>
public class WorkflowEngine : IWorkflowEngine
{
    public Task SubmitForApprovalAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.Draft or RequestStatus.UnderReview or RequestStatus.Returned or RequestStatus.Blocked or RequestStatus.ApprovedByManagement))
            throw new DomainException($"Недопустимый переход в состояние «На согласовании» из {request.Status}.");

        request.Status = RequestStatus.PendingApproval;
        request.SubmittedAt ??= DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task BlockAsync(PaymentRequest request, string reason, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.Draft or RequestStatus.UnderReview or RequestStatus.PendingApproval or RequestStatus.Returned))
            throw new DomainException($"Недопустимая блокировка из состояния {request.Status}.");

        request.Status = RequestStatus.Blocked;
        request.BlockReason = reason;
        request.BlockedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task ApproveAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.PendingApproval or RequestStatus.ApprovedByManagement))
            throw new DomainException($"Согласование невозможно из состояния {request.Status}.");

        request.Status = RequestStatus.Approved;
        request.ApprovedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task ReturnForRevisionAsync(PaymentRequest request, string reason, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.UnderReview or RequestStatus.PendingApproval or RequestStatus.Draft))
            throw new DomainException($"Возврат на доработку невозможен из состояния {request.Status}.");

        request.Status = RequestStatus.Returned;
        request.ReturnOrRejectReason = reason;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task RejectAsync(PaymentRequest request, string reason, CancellationToken ct = default)
    {
        if (request.Status is not (RequestStatus.Blocked or RequestStatus.PendingApproval or RequestStatus.UnderReview))
            throw new DomainException($"Отклонение невозможно из состояния {request.Status}.");

        request.Status = RequestStatus.Rejected;
        request.ReturnOrRejectReason = reason;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task MarkExecutedAsync(PaymentRequest request, CancellationToken ct = default)
    {
        request.EnsureCanExecute();
        request.Status = RequestStatus.Executed;
        request.ExecutedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }

    public Task CloseByStatementAsync(PaymentRequest request, CancellationToken ct = default)
    {
        if (request.Status != RequestStatus.Executed)
            throw new DomainException($"Закрытие по выписке возможно только из состояния «Исполнена» (текущее: {request.Status}).");

        request.Status = RequestStatus.Closed;
        request.ClosedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;
        return Task.CompletedTask;
    }
}
