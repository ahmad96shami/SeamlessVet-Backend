using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Contracts;

/// <summary>
/// Batch (Dawra/Cycle) CRUD (PRD §7.2, M8 task 8). Batch financial configuration — the supervision
/// fee model, the per-batch entitlement override, and the doctor share — is an Admin/Accountant
/// operation (PRD §7), so the endpoints are gated on <c>contracts.activate</c> and writes never reach
/// the device through <c>/sync</c>. The values written here are what M9 reads to compute the
/// responsible doctor's entitlement.
///
/// <para>M30: the doctor's entitlement is no longer computed when a batch is closed — it is computed
/// (and credited to the doctor-partner ledger) when the batch is <b>settled</b> (تصفية) via
/// <c>BatchSettlementService</c>. Closing a batch here is a plain status flip.</para>
/// </summary>
public sealed class BatchesService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public BatchesService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<BatchResponse>> ListAsync(
        Guid? customerId,
        Guid? responsibleDoctorId,
        Guid? contractId,
        string? status,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (status is not null && !BatchStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_batch_status", $"status '{status}' is not valid.");
        }

        var query = _db.Batches.AsNoTracking();

        if (customerId is { } cid) query = query.Where(b => b.CustomerId == cid);
        if (responsibleDoctorId is { } did) query = query.Where(b => b.ResponsibleDoctorId == did);
        if (contractId is { } ctid) query = query.Where(b => b.ContractId == ctid);
        if (status is not null) query = query.Where(b => b.Status == status);

        var rows = await query
            .OrderByDescending(b => b.StartDate)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        // M24 — surface "settled" so the web can route closed-but-unsettled cycles into the settle flow.
        var ids = rows.Select(b => b.Id).ToList();
        var settledAt = await _db.BatchSettlements.AsNoTracking()
            .Where(s => ids.Contains(s.BatchId))
            .ToDictionaryAsync(s => s.BatchId, s => s.SettledAt, cancellationToken);

        return rows
            .Select(b => _mapper.Map<BatchResponse>(b) with
            {
                SettledAt = settledAt.TryGetValue(b.Id, out var at) ? at : null,
            })
            .ToList();
    }

    public async Task<BatchResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await _db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                    ?? throw new NotFoundException("batch", id);

        var settledAt = await _db.BatchSettlements.AsNoTracking()
            .Where(s => s.BatchId == id)
            .Select(s => (DateTimeOffset?)s.SettledAt)
            .FirstOrDefaultAsync(cancellationToken);

        return _mapper.Map<BatchResponse>(batch) with { SettledAt = settledAt };
    }

    public async Task<BatchResponse> CreateAsync(BatchCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        await RequireExistsAsync(_db.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken),
            "customer", request.CustomerId);
        await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == request.ResponsibleDoctorId, cancellationToken),
            "doctor", request.ResponsibleDoctorId);

        if (request.ContractId is { } contractId)
        {
            await RequireExistsAsync(_db.Contracts.AnyAsync(c => c.Id == contractId, cancellationToken),
                "contract", contractId);
        }

        // M15 — the cycle's farm must belong to the batch's customer (customer_id stays a denormalized
        // mirror of farm.customer_id).
        if (request.FarmId is { } reqFarmId)
        {
            await RequireFarmBelongsToCustomerAsync(reqFarmId, request.CustomerId, cancellationToken);
        }

        if (request.Id is { } id && id != Guid.Empty
            && await _db.Batches.IgnoreQueryFilters().AnyAsync(b => b.Id == id, cancellationToken))
        {
            throw new ConflictException("batch_id_collision", $"A batch with id '{id}' already exists.");
        }

        var batch = new Batch
        {
            Id = request.Id ?? Guid.Empty,
            ContractId = request.ContractId,
            CustomerId = request.CustomerId,
            FarmId = request.FarmId,
            ResponsibleDoctorId = request.ResponsibleDoctorId,
            AnimalCount = request.AnimalCount,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            SupervisionFeeModel = request.SupervisionFeeModel,
            SupervisionFeeValue = request.SupervisionFeeValue,
            EntitlementEnabled = request.EntitlementEnabled,
            EntitlementSystem = request.EntitlementSystem,
            Status = request.Status ?? BatchStatus.Open,
        };

        _db.Batches.Add(batch);
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<BatchResponse>(batch);
    }

    public async Task<BatchResponse> UpdateAsync(Guid id, BatchPatchRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                    ?? throw new NotFoundException("batch", id);

        // A settled batch (تصفية) is frozen: its settlement snapshots + ledger adjustments would go
        // stale if its configuration changed underneath them (SCHEMA invariant #11).
        await RequireNotSettledAsync(id, cancellationToken);

        if (request.ContractId is { } contractId)
        {
            await RequireExistsAsync(_db.Contracts.AnyAsync(c => c.Id == contractId, cancellationToken),
                "contract", contractId);
            batch.ContractId = contractId;
        }

        if (request.ResponsibleDoctorId is { } doctorId)
        {
            await RequireExistsAsync(_db.Users.AnyAsync(u => u.Id == doctorId, cancellationToken), "doctor", doctorId);
            batch.ResponsibleDoctorId = doctorId;
        }

        // M15 — (re)attribute the cycle to a farm of its customer (customer_id is unchanged here).
        if (request.FarmId is { } patchFarmId)
        {
            await RequireFarmBelongsToCustomerAsync(patchFarmId, batch.CustomerId, cancellationToken);
            batch.FarmId = patchFarmId;
        }

        if (request.AnimalCount.HasValue) batch.AnimalCount = request.AnimalCount.Value;
        if (request.StartDate.HasValue) batch.StartDate = request.StartDate.Value;
        if (request.EndDate.HasValue) batch.EndDate = request.EndDate;
        if (request.SupervisionFeeModel is not null) batch.SupervisionFeeModel = request.SupervisionFeeModel;
        if (request.SupervisionFeeValue.HasValue) batch.SupervisionFeeValue = request.SupervisionFeeValue.Value;
        if (request.EntitlementEnabled.HasValue) batch.EntitlementEnabled = request.EntitlementEnabled;
        if (request.EntitlementSystem is not null) batch.EntitlementSystem = request.EntitlementSystem;

        // M30 — closing a batch is a plain status flip; the doctor's entitlement is computed and
        // credited to their partner ledger only when the batch is settled (تصفية), not here.
        if (request.Status is not null) batch.Status = request.Status;
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<BatchResponse>(batch);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                    ?? throw new NotFoundException("batch", id);

        // A settled batch is frozen — deleting it would orphan its settlement + ledger adjustments.
        await RequireNotSettledAsync(id, cancellationToken);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.Batches.Remove(batch);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Rejects a write to a batch that already has a settlement (تصفية). Mirrors the
    /// <c>batch_already_settled</c> / <c>batch_settled</c> guards on settle + invoice issuance, so a
    /// settled batch is immutable end-to-end. Honors the soft-delete query filter, so an admin who
    /// soft-deletes the settlement re-opens the batch (same as the settle slot).
    /// </summary>
    private async Task RequireNotSettledAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (await _db.BatchSettlements.AsNoTracking().AnyAsync(s => s.BatchId == batchId, cancellationToken))
        {
            throw new ConflictException("batch_settled",
                "This batch has been settled (تصفية) and can no longer be edited or deleted.");
        }
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }

    private async Task RequireFarmBelongsToCustomerAsync(Guid farmId, Guid customerId, CancellationToken cancellationToken)
    {
        var farmCustomerId = await _db.Farms
            .Where(f => f.Id == farmId)
            .Select(f => (Guid?)f.CustomerId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("farm", farmId);

        if (farmCustomerId != customerId)
        {
            throw new ConflictException("farm_customer_mismatch",
                "The farm does not belong to the batch's customer.");
        }
    }
}
