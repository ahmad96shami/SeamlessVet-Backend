using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Application.Entitlements;
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
/// <para>M9: closing a batch (status → <c>closed</c>) is the cycle-finalization trigger that computes
/// the responsible doctor's entitlement into a <c>pending</c> row (PRD §7.8 "Calculated, awaiting
/// account closure"). It stays locked until the customer account is closed in full (settlement lock).</para>
/// </summary>
public sealed class BatchesService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IEntitlementService _entitlements;
    private readonly IMapper _mapper;

    public BatchesService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IEntitlementService entitlements,
        IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _entitlements = entitlements;
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

        var wasClosed = batch.Status == BatchStatus.Closed;
        if (request.Status is not null) batch.Status = request.Status;
        var nowClosing = !wasClosed && batch.Status == BatchStatus.Closed;

        // Closing the cycle finalizes its accounting: compute the responsible doctor's entitlement
        // into a pending row, atomically with the status flip (PRD §7.8). It remains locked until the
        // customer account closes in full. Idempotent — re-closing/recompute refreshes the pending row.
        if (nowClosing)
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _entitlements.ComputeForBatchAsync(batch.Id, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        else
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return _mapper.Map<BatchResponse>(batch);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
                    ?? throw new NotFoundException("batch", id);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.Batches.Remove(batch);
        await _db.SaveChangesAsync(cancellationToken);
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
