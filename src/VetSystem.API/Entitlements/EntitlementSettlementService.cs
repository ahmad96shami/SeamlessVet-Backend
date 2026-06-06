using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Entitlements;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Entitlements;

/// <summary>
/// The settlement workflow + entitlement reads/transitions (M9 tasks 9–12). Closing a customer
/// account requires a zero balance (partial payments never release — SCHEMA invariant #1), flips the
/// ledger to <c>closed</c>, and triggers entitlement computation for the customer's batches and
/// eligible visits (PRD §7.7). Approving/paying an entitlement is gated by
/// <see cref="ISettlementLockGuard"/> — the hard guard that refuses release until the ledger is closed.
/// All transitions are idempotent so an offline-replayed call lands once.
/// </summary>
public sealed class EntitlementSettlementService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IEntitlementService _entitlements;
    private readonly ISettlementLockGuard _lock;
    private readonly IClock _clock;
    private readonly IMapper _mapper;
    private readonly IDomainEventPublisher _events;

    public EntitlementSettlementService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IEntitlementService entitlements,
        ISettlementLockGuard settlementLock,
        IClock clock,
        IMapper mapper,
        IDomainEventPublisher events)
    {
        _db = db;
        _currentUser = currentUser;
        _entitlements = entitlements;
        _lock = settlementLock;
        _clock = clock;
        _mapper = mapper;
        _events = events;
    }

    /// <summary>
    /// M9 task 9 / M16 — close a customer's <b>own</b> ledger (pet/clinic charges). Rejected unless the
    /// own-ledger balance is exactly zero; only a full settlement closes it. Then computes entitlements
    /// for the customer's <b>non-farm</b> batches and completed non-batch visits — the ones that route
    /// to this ledger (farm-scoped batches/visits settle via <see cref="CloseFarmAccountAsync"/>). The
    /// customer is only fully settled once its own ledger and every farm ledger are closed.
    /// </summary>
    public async Task<CloseAccountResponse> CloseCustomerAccountAsync(Guid customerId, CancellationToken cancellationToken)
    {
        RequireUser();

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.CustomerId == customerId, cancellationToken)
                     ?? throw new NotFoundException("ledger", customerId);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        await EnsureClosedAsync(ledger, cancellationToken);

        var batchIds = await _db.Batches
            .Where(b => b.CustomerId == customerId && b.FarmId == null)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
        foreach (var batchId in batchIds)
        {
            await _entitlements.ComputeForBatchAsync(batchId, cancellationToken);
        }

        var visitIds = await _db.Visits
            .Where(v => v.CustomerId == customerId && v.FarmId == null && v.BatchId == null
                        && v.Status == VisitStatus.Completed)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        foreach (var visitId in visitIds)
        {
            await _entitlements.ComputeForVisitAsync(visitId, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        var entitlements = await ListEntitlementsAsync(batchIds, visitIds, cancellationToken);
        return new CloseAccountResponse(customerId, FarmId: null, ledger.Id, ledger.Status, ledger.ClosedAt, entitlements);
    }

    /// <summary>
    /// M16 — close a <b>farm</b>'s ledger. Same zero-balance gate as a customer close; then computes
    /// entitlements for that farm's batches and its completed non-batch visits. Closing farm A neither
    /// touches farm B's ledger (so farm B's entitlements stay locked) nor closes the owning customer.
    /// </summary>
    public async Task<CloseAccountResponse> CloseFarmAccountAsync(Guid farmId, CancellationToken cancellationToken)
    {
        RequireUser();

        var farm = await _db.Farms
            .Where(f => f.Id == farmId)
            .Select(f => new { f.Id, f.CustomerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("farm", farmId);

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.FarmId == farmId, cancellationToken)
                     ?? throw new NotFoundException("ledger", farmId);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        await EnsureClosedAsync(ledger, cancellationToken);

        var batchIds = await _db.Batches
            .Where(b => b.FarmId == farmId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
        foreach (var batchId in batchIds)
        {
            await _entitlements.ComputeForBatchAsync(batchId, cancellationToken);
        }

        var visitIds = await _db.Visits
            .Where(v => v.FarmId == farmId && v.BatchId == null && v.Status == VisitStatus.Completed)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        foreach (var visitId in visitIds)
        {
            await _entitlements.ComputeForVisitAsync(visitId, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        var entitlements = await ListEntitlementsAsync(batchIds, visitIds, cancellationToken);
        return new CloseAccountResponse(farm.CustomerId, farm.Id, ledger.Id, ledger.Status, ledger.ClosedAt, entitlements);
    }

    /// <summary>
    /// Re-open a customer's <b>own</b> ledger so a returning customer's new visit can be billed. A
    /// settled account is closed to release entitlements; once the customer is back, the cashier
    /// re-opens it explicitly (charges never auto-reopen). Idempotent: a non-closed ledger is left
    /// untouched. Already-released entitlements stay released — re-opening only lifts the append lock.
    /// </summary>
    public async Task<CloseAccountResponse> ReopenCustomerAccountAsync(Guid customerId, CancellationToken cancellationToken)
    {
        RequireUser();

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.CustomerId == customerId, cancellationToken)
                     ?? throw new NotFoundException("ledger", customerId);

        Reopen(ledger);
        await _db.SaveChangesAsync(cancellationToken);

        return new CloseAccountResponse(
            customerId, FarmId: null, ledger.Id, ledger.Status, ledger.ClosedAt, []);
    }

    /// <summary>M16 — re-open a single <b>farm</b> ledger (mirror of <see cref="CloseFarmAccountAsync"/>).</summary>
    public async Task<CloseAccountResponse> ReopenFarmAccountAsync(Guid farmId, CancellationToken cancellationToken)
    {
        RequireUser();

        var farm = await _db.Farms
            .Where(f => f.Id == farmId)
            .Select(f => new { f.Id, f.CustomerId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("farm", farmId);

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.FarmId == farmId, cancellationToken)
                     ?? throw new NotFoundException("ledger", farmId);

        Reopen(ledger);
        await _db.SaveChangesAsync(cancellationToken);

        return new CloseAccountResponse(
            farm.CustomerId, farm.Id, ledger.Id, ledger.Status, ledger.ClosedAt, []);
    }

    /// <summary>Lifts the closed lock: status recomputes from the balance (open vs. has_debt) and the
    /// close timestamp is cleared. A no-op if the ledger isn't closed.</summary>
    private static void Reopen(Ledger ledger)
    {
        if (ledger.Status != LedgerStatus.Closed)
        {
            return;
        }

        ledger.Status = ledger.Balance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open;
        ledger.ClosedAt = null;
    }

    /// <summary>Closes a ledger once its balance is exactly zero (partial payments never release).</summary>
    private async Task EnsureClosedAsync(Ledger ledger, CancellationToken cancellationToken)
    {
        if (ledger.Status == LedgerStatus.Closed)
        {
            return;
        }

        if (ledger.Balance != 0m)
        {
            throw new ConflictException(
                "account_not_settled",
                $"Cannot close the account: the ledger balance is {ledger.Balance:0.00}. Partial payments do not "
                + "release doctor entitlements — settle the balance to zero first.");
        }

        ledger.Status = LedgerStatus.Closed;
        ledger.ClosedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>M9 task 10 — approve a pending entitlement; blocked unless the related ledger is closed.</summary>
    public async Task<DoctorEntitlementResponse> ApproveAsync(Guid entitlementId, CancellationToken cancellationToken)
    {
        var (_, userId) = RequireUser();

        var entitlement = await _db.DoctorEntitlements.FirstOrDefaultAsync(e => e.Id == entitlementId, cancellationToken)
                          ?? throw new NotFoundException("doctor_entitlement", entitlementId);

        if (entitlement.Status == EntitlementStatus.Approved)
        {
            return _mapper.Map<DoctorEntitlementResponse>(entitlement); // idempotent replay
        }

        if (entitlement.Status == EntitlementStatus.Paid)
        {
            throw new ConflictException("entitlement_already_paid", "A paid entitlement cannot be re-approved.");
        }

        _lock.EnsureReleasable(await ResolveLedgerStatusAsync(entitlement, cancellationToken));

        entitlement.Status = EntitlementStatus.Approved;
        entitlement.ApprovedBy = userId;
        entitlement.ApprovedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Notify the entitled doctor that their accrual cleared the settlement lock (M11 task 13).
        await _events.PublishAsync(
            new EntitlementApprovedEvent(
                entitlement.EnvironmentId, entitlement.Id, entitlement.DoctorId, entitlement.ComputedAmount, userId),
            cancellationToken);

        return _mapper.Map<DoctorEntitlementResponse>(entitlement);
    }

    /// <summary>M9 task 11 — record disbursement. Requires an approved entitlement (so the settlement
    /// lock has already been satisfied); re-checks the ledger as defense in depth.</summary>
    public async Task<DoctorEntitlementResponse> PayAsync(
        Guid entitlementId, PayEntitlementRequest request, CancellationToken cancellationToken)
    {
        RequireUser();

        var entitlement = await _db.DoctorEntitlements.FirstOrDefaultAsync(e => e.Id == entitlementId, cancellationToken)
                          ?? throw new NotFoundException("doctor_entitlement", entitlementId);

        if (entitlement.Status == EntitlementStatus.Paid)
        {
            return _mapper.Map<DoctorEntitlementResponse>(entitlement); // idempotent replay
        }

        if (entitlement.Status != EntitlementStatus.Approved)
        {
            throw new ConflictException("entitlement_not_approved", "Approve the entitlement before paying it.");
        }

        if (!PaymentMethod.All.Contains(request.Method))
        {
            throw new ConflictException("invalid_payment_method", $"Unknown payment method '{request.Method}'.");
        }

        _lock.EnsureReleasable(await ResolveLedgerStatusAsync(entitlement, cancellationToken));

        entitlement.Status = EntitlementStatus.Paid;
        entitlement.PaidAt = _clock.UtcNow;
        entitlement.PaidMethod = request.Method;
        await _db.SaveChangesAsync(cancellationToken);

        return _mapper.Map<DoctorEntitlementResponse>(entitlement);
    }

    public async Task<IReadOnlyList<DoctorEntitlementResponse>> ListAsync(
        Guid? doctorId, string? status, int? skip, int? take, CancellationToken cancellationToken)
    {
        if (status is not null && !EntitlementStatus.All.Contains(status))
        {
            throw new ConflictException("invalid_entitlement_status", $"status '{status}' is not valid.");
        }

        var query = _db.DoctorEntitlements.AsNoTracking();
        if (doctorId is { } did) query = query.Where(e => e.DoctorId == did);
        if (status is not null) query = query.Where(e => e.Status == status);

        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<DoctorEntitlementResponse>).ToList();
    }

    public async Task<DoctorEntitlementResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var entitlement = await _db.DoctorEntitlements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
                          ?? throw new NotFoundException("doctor_entitlement", id);

        return _mapper.Map<DoctorEntitlementResponse>(entitlement);
    }

    /// <summary>
    /// M16 — the status of the ledger that gates an entitlement's release: the farm ledger when the
    /// source (batch or visit) carries a <c>farm_id</c>, else the owning customer's ledger.
    /// </summary>
    private async Task<string> ResolveLedgerStatusAsync(DoctorEntitlement entitlement, CancellationToken cancellationToken)
    {
        var owner = entitlement.BatchId is { } batchId
            ? await _db.Batches.Where(b => b.Id == batchId)
                .Select(b => new LedgerOwner(b.FarmId, b.CustomerId)).FirstOrDefaultAsync(cancellationToken)
            : await _db.Visits.Where(v => v.Id == entitlement.VisitId!.Value)
                .Select(v => new LedgerOwner(v.FarmId, v.CustomerId)).FirstOrDefaultAsync(cancellationToken);

        if (owner is null)
        {
            throw new NotFoundException("entitlement_source", entitlement.Id);
        }

        var status = owner.FarmId is { } farmId
            ? await _db.Ledgers.Where(l => l.FarmId == farmId).Select(l => l.Status).FirstOrDefaultAsync(cancellationToken)
            : await _db.Ledgers.Where(l => l.CustomerId == owner.CustomerId).Select(l => l.Status).FirstOrDefaultAsync(cancellationToken);

        return status ?? throw new NotFoundException("ledger", owner.FarmId ?? owner.CustomerId ?? entitlement.Id);
    }

    private async Task<IReadOnlyList<DoctorEntitlementResponse>> ListEntitlementsAsync(
        IReadOnlyList<Guid> batchIds, IReadOnlyList<Guid> visitIds, CancellationToken cancellationToken)
    {
        var rows = await _db.DoctorEntitlements.AsNoTracking()
            .Where(e => (e.BatchId != null && batchIds.Contains(e.BatchId.Value))
                        || (e.VisitId != null && visitIds.Contains(e.VisitId.Value)))
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<DoctorEntitlementResponse>).ToList();
    }

    private sealed record LedgerOwner(Guid? FarmId, Guid? CustomerId);

    private (Guid EnvironmentId, Guid UserId) RequireUser()
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return (envId, userId);
    }
}
