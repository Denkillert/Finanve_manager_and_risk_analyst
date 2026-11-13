using FinanveManager.Data.Models;
using FinanveManager.Data.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinanveManager.Data.Services;

public record CreateMaterialRequestDto(
    Guid ProjectId,
    Guid ConstructionObjectId,
    Guid? CostItemId,
    string MaterialName,
    string Unit,
    decimal Quantity,
    DateTime RequiredDate,
    string? Purpose,
    string? SpecificationNotes,
    bool SubmitImmediately);

public record SupplyMaterialRequestDto(
    Guid? ContractId,
    string? SupplierName,
    decimal? AgreedUnitPrice,
    string? Comment,
    Guid? SupplierOfferId = null);

public interface IMaterialRequestService
{
    Task<MaterialRequest> CreateAsync(
        CreateMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default);

    Task<MaterialRequest> UpdateAsync(
        Guid id, CreateMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default);

    Task<MaterialRequest> SubmitAsync(
        Guid id, Guid userId, string login, CancellationToken ct = default);

    Task<MaterialRequest> ApproveAsync(
        Guid id, SupplyMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default);

    Task<MaterialRequest> RejectAsync(
        Guid id, string reason, Guid userId, string login, CancellationToken ct = default);

    Task<MaterialRequest> MarkOrderedAsync(
        Guid id, SupplyMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default);

    Task<MaterialRequest> MarkDeliveredAsync(
        Guid id, Guid userId, string login, CancellationToken ct = default);

    Task<MaterialRequest> CreatePaymentRequestAsync(
        Guid id, Guid userId, string login, CancellationToken ct = default);
}

/// <summary>
/// Заявки прораба на материал. Прораб только описывает потребность (без выбора поставщика),
/// снабженец подбирает договор, поставщика и согласованную цену.
/// </summary>
public class MaterialRequestService : IMaterialRequestService
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IPaymentRequestService _paymentRequests;

    public MaterialRequestService(AppDbContext db, IAuditService audit, IPaymentRequestService paymentRequests)
    {
        _db = db;
        _audit = audit;
        _paymentRequests = paymentRequests;
    }

    public async Task<MaterialRequest> CreateAsync(
        CreateMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default)
    {
        var obj = await _db.ConstructionObjects
            .FirstOrDefaultAsync(o => !o.IsDeleted && o.Id == dto.ConstructionObjectId, ct)
            ?? throw new DomainException("Объект строительства не найден.");
        if (obj.ProjectId != dto.ProjectId)
            throw new DomainException("Объект не принадлежит выбранному проекту.");
        if (string.IsNullOrWhiteSpace(dto.MaterialName))
            throw new DomainException("Укажите наименование материала.");
        if (dto.Quantity <= 0)
            throw new DomainException("Количество материала должно быть положительным.");

        var request = new MaterialRequest
        {
            ProjectId = dto.ProjectId,
            ConstructionObjectId = dto.ConstructionObjectId,
            AuthorUserId = userId,
            CostItemId = dto.CostItemId,
            Number = await NextNumberAsync(ct),
            MaterialName = dto.MaterialName.Trim(),
            Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "шт." : dto.Unit.Trim(),
            Quantity = dto.Quantity,
            EstimatedUnitPrice = 0m,
            RequiredDate = dto.RequiredDate == default ? DateTime.UtcNow.Date.AddDays(7) : dto.RequiredDate.Date,
            Purpose = dto.Purpose?.Trim(),
            SpecificationNotes = dto.SpecificationNotes?.Trim(),
            Status = dto.SubmitImmediately ? MaterialRequestStatus.Submitted : MaterialRequestStatus.Draft,
            SubmittedAt = dto.SubmitImmediately ? DateTime.UtcNow : null
        };
        request.RecalculateTotals();
        await _db.MaterialRequests.AddAsync(request, ct);
        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number, "CREATE_MATERIAL_REQUEST",
            null, $"{request.MaterialName}; Qty={request.Quantity}; Est={request.EstimatedTotal:N2}", request.Purpose, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<MaterialRequest> UpdateAsync(
        Guid id, CreateMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default)
    {
        var request = await GetEditableAsync(id, userId, ct);
        if (string.IsNullOrWhiteSpace(dto.MaterialName))
            throw new DomainException("Укажите наименование материала.");
        if (dto.Quantity <= 0)
            throw new DomainException("Количество материала должно быть положительным.");

        request.MaterialName = dto.MaterialName.Trim();
        request.Unit = string.IsNullOrWhiteSpace(dto.Unit) ? "шт." : dto.Unit.Trim();
        request.Quantity = dto.Quantity;
        request.RequiredDate = dto.RequiredDate == default ? request.RequiredDate : dto.RequiredDate.Date;
        request.Purpose = dto.Purpose?.Trim();
        request.SpecificationNotes = dto.SpecificationNotes?.Trim();
        if (dto.CostItemId.HasValue) request.CostItemId = dto.CostItemId;
        if (dto.SubmitImmediately)
        {
            request.Status = MaterialRequestStatus.Submitted;
            request.SubmittedAt = DateTime.UtcNow;
            request.RejectedAt = null;
        }
        request.RecalculateTotals();

        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number, "UPDATE_MATERIAL_REQUEST",
            null, $"{request.MaterialName}; Qty={request.Quantity}; Est={request.EstimatedTotal:N2}", null, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<MaterialRequest> SubmitAsync(
        Guid id, Guid userId, string login, CancellationToken ct = default)
    {
        var request = await GetEditableAsync(id, userId, ct);
        request.Status = MaterialRequestStatus.Submitted;
        request.SubmittedAt = DateTime.UtcNow;
        request.RejectedAt = null;
        request.UpdatedAt = DateTime.UtcNow;
        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number, "SUBMIT_MATERIAL_REQUEST",
            null, "Submitted", null, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<MaterialRequest> ApproveAsync(
        Guid id, SupplyMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default)
    {
        var request = await _db.MaterialRequests.FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
            ?? throw new DomainException("Заявка на материал не найдена.");
        if (request.Status is MaterialRequestStatus.Delivered or MaterialRequestStatus.Rejected)
            throw new DomainException("Нельзя согласовать заявку в текущем статусе.");

        await ApplySupplyAsync(request, dto, ct);
        request.Status = MaterialRequestStatus.Approved;
        request.ApprovedAt = DateTime.UtcNow;
        request.RejectedAt = null;
        request.UpdatedAt = DateTime.UtcNow;

        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number, "APPROVE_MATERIAL_REQUEST",
            null, $"Supplier={request.SupplierName}; Agreed={request.AgreedTotal:N2}", request.SupplyComment, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<MaterialRequest> RejectAsync(
        Guid id, string reason, Guid userId, string login, CancellationToken ct = default)
    {
        var request = await _db.MaterialRequests.FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
            ?? throw new DomainException("Заявка на материал не найдена.");
        if (request.Status is MaterialRequestStatus.Delivered)
            throw new DomainException("Поставленная заявка не может быть отклонена.");

        request.Status = MaterialRequestStatus.Rejected;
        request.SupplyComment = string.IsNullOrWhiteSpace(reason) ? "Требуется уточнение потребности" : reason.Trim();
        request.RejectedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number, "REJECT_MATERIAL_REQUEST",
            null, request.SupplyComment, null, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<MaterialRequest> MarkOrderedAsync(
        Guid id, SupplyMaterialRequestDto dto, Guid userId, string login, CancellationToken ct = default)
    {
        var request = await _db.MaterialRequests.FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
            ?? throw new DomainException("Заявка на материал не найдена.");
        if (request.Status is not (MaterialRequestStatus.Approved or MaterialRequestStatus.Ordered))
            throw new DomainException("Заказать можно только согласованную заявку.");

        await ApplySupplyAsync(request, dto, ct);
        request.Status = MaterialRequestStatus.Ordered;
        request.OrderedAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number, "ORDER_MATERIAL_REQUEST",
            null, $"Supplier={request.SupplierName}; Agreed={request.AgreedTotal:N2}", null, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<MaterialRequest> MarkDeliveredAsync(
        Guid id, Guid userId, string login, CancellationToken ct = default)
    {
        var request = await _db.MaterialRequests.FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
            ?? throw new DomainException("Заявка на материал не найдена.");
        if (request.Status != MaterialRequestStatus.Ordered)
            throw new DomainException("Отметить поставку можно только для заказанной заявки.");

        request.Status = MaterialRequestStatus.Delivered;
        request.DeliveredAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number, "DELIVER_MATERIAL_REQUEST",
            null, "Delivered", null, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<MaterialRequest> CreatePaymentRequestAsync(
        Guid id, Guid userId, string login, CancellationToken ct = default)
    {
        var request = await _db.MaterialRequests
            .FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
            ?? throw new DomainException("Заявка на материал не найдена.");

        if (request.Status is not (MaterialRequestStatus.Approved or MaterialRequestStatus.Ordered or MaterialRequestStatus.Delivered))
            throw new DomainException("Платёжную заявку можно сформировать только после согласования заявки снабженцем.");
        if (request.PaymentRequestId.HasValue)
            throw new DomainException("По этой заявке платёжная заявка уже сформирована.");
        if (!request.AgreedUnitPrice.HasValue || request.AgreedTotal is null || request.AgreedTotal.Value <= 0)
            throw new DomainException("Сначала выберите предложение поставщика и согласуйте цену.");

        Guid? contractId = request.ContractId;
        if (!contractId.HasValue || contractId.Value == Guid.Empty)
        {
            var contract = await _db.Contracts
                .Where(c => !c.IsDeleted && c.Direction == ContractDirection.SupplierSubcontractorOutgoing)
                .FirstOrDefaultAsync(c => c.Counterparty == request.SupplierName, ct);
            contractId = contract?.Id;
        }
        if (!contractId.HasValue || contractId.Value == Guid.Empty)
            throw new DomainException("Для оплаты зарегистрируйте договор с выбранным поставщиком.");

        Guid? costItemId = request.CostItemId;
        if (!costItemId.HasValue)
        {
            costItemId = await _db.CostItems
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.Code)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
        }
        if (!costItemId.HasValue)
            throw new DomainException("В справочнике нет статьи затрат для оплаты материала.");

        var plannedDate = request.RequiredDate.Date < DateTime.UtcNow.Date
            ? DateTime.UtcNow.Date
            : request.RequiredDate.Date;

        var dto = new CreatePaymentRequestDto(
            request.ProjectId,
            request.ConstructionObjectId,
            contractId.Value,
            costItemId.Value,
            request.AgreedTotal.Value,
            request.AgreedTotal.Value,
            0m,
            0m,
            VatIncluded: true,
            Currency: "BYN",
            RequestType.CurrentPayment,
            plannedDate,
            $"Оплата материала «{request.MaterialName}» по заявке {request.Number} (поставщик: {request.SupplierName})",
            DocumentType.Invoice,
            $"МЗ-{request.Number}",
            DateTime.UtcNow.Date,
            null,
            $"MATERIAL-{request.Id}",
            SubmitImmediately: true);

        var (payment, _) = await _paymentRequests.CreateAsync(dto, userId, login, ct);

        request.PaymentRequestId = payment.Id;
        request.UpdatedAt = DateTime.UtcNow;
        await _audit.LogAsync(userId, login, nameof(MaterialRequest), request.Number,
            "CREATE_PAYMENT_REQUEST_FROM_MATERIAL", null,
            $"Payment={payment.Number}; Amount={payment.PayableAmount:N2}", null, ct);
        await _db.SaveChangesAsync(ct);
        return request;
    }

    private async Task ApplySupplyAsync(MaterialRequest request, SupplyMaterialRequestDto dto, CancellationToken ct)
    {
        if (dto.SupplierOfferId.HasValue && dto.SupplierOfferId.Value != Guid.Empty)
        {
            var offer = await _db.SupplierOffers
                .Include(o => o.Supplier)
                .FirstOrDefaultAsync(o => !o.IsDeleted && o.IsActive && o.Id == dto.SupplierOfferId.Value, ct)
                ?? throw new DomainException("Выбранное предложение поставщика не найдено.");

            request.SupplierOfferId = offer.Id;
            request.SupplierId = offer.SupplierId;
            request.SupplierName = offer.Supplier?.Name ?? dto.SupplierName?.Trim();
            request.AgreedUnitPrice = offer.UnitPrice;
        }
        else
        {
            if (dto.ContractId.HasValue && dto.ContractId.Value != Guid.Empty)
            {
                var contract = await _db.Contracts
                    .FirstOrDefaultAsync(c => !c.IsDeleted && c.Id == dto.ContractId.Value, ct)
                    ?? throw new DomainException("Выбранный договор поставки не найден.");
                request.ContractId = contract.Id;
                request.SupplierName = string.IsNullOrWhiteSpace(dto.SupplierName)
                    ? contract.Counterparty
                    : dto.SupplierName.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(dto.SupplierName))
            {
                request.SupplierName = dto.SupplierName.Trim();
            }

            if (dto.AgreedUnitPrice.HasValue)
            {
                if (dto.AgreedUnitPrice.Value < 0)
                    throw new DomainException("Согласованная цена не может быть отрицательной.");
                request.AgreedUnitPrice = dto.AgreedUnitPrice.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(request.SupplierName))
            throw new DomainException("Выберите предложение поставщика или укажите поставщика вручную.");

        // Если снабженец не выбрал договор, подбираем действующий договор с этим поставщиком.
        if (!request.ContractId.HasValue && dto.ContractId.HasValue && dto.ContractId.Value != Guid.Empty)
            request.ContractId = dto.ContractId.Value;
        if (!request.ContractId.HasValue)
        {
            var supplierContract = await _db.Contracts
                .Where(c => !c.IsDeleted && c.Direction == ContractDirection.SupplierSubcontractorOutgoing)
                .FirstOrDefaultAsync(c => c.Counterparty == request.SupplierName, ct);
            request.ContractId = supplierContract?.Id;
        }

        if (!string.IsNullOrWhiteSpace(dto.Comment))
            request.SupplyComment = dto.Comment.Trim();

        request.RecalculateTotals();
    }

    private async Task<MaterialRequest> GetEditableAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var request = await _db.MaterialRequests.FirstOrDefaultAsync(r => !r.IsDeleted && r.Id == id, ct)
            ?? throw new DomainException("Заявка на материал не найдена.");
        if (request.AuthorUserId != userId)
            throw new DomainException("Дорабатывать заявку может только её автор.");
        if (request.Status is MaterialRequestStatus.Ordered or MaterialRequestStatus.Delivered or MaterialRequestStatus.Approved)
            throw new DomainException("Заявка уже согласована снабженцем и не подлежит доработке.");
        return request;
    }

    private async Task<string> NextNumberAsync(CancellationToken ct)
    {
        string prefix = $"МЗ-{DateTime.UtcNow:yyyyMMdd}-";
        var numbers = await _db.MaterialRequests
            .Where(r => r.Number.StartsWith(prefix))
            .Select(r => r.Number)
            .ToListAsync(ct);

        int max = 0;
        foreach (var number in numbers)
        {
            var tail = number[prefix.Length..];
            if (int.TryParse(tail, out var value) && value > max)
                max = value;
        }
        return prefix + (max + 1).ToString("D4");
    }
}
