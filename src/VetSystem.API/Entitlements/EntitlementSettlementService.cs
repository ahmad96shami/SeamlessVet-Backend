using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Entitlements;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
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

    public EntitlementSettlementService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IEntitlementService entitlements,
        ISettlementLockGuard settlementLock,
        IClock clock,
        IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _entitlements = entitlements;
        _lock = settlementLock;
        _clock = clock;
        _mapper = mapper;
    }

    /// <summary>
    /// M9 task 9 — close a customer account. Rejected unless the ledger balance is exactly zero;
    /// only a full settlement closes it. Then computes entitlements for the customer's batches and
    /// completed non-batch visits (idempotent), all in one transaction.
    /// </summary>
    public async Task<CloseAccountResponse> CloseCustomerAccountAsync(Guid customerId, CancellationToken cancellationToken)
    {
        RequireUser();

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.CustomerId == customerId, cancellationToken)
                     ?? throw new NotFoundException("ledger", customerId);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (ledger.Status != LedgerStatus.Closed)
        {
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

        // Settlement workflow (PRD §7.7): materialize entitlements for everything this customer owns.
        var batchIds = await _db.Batches
            .Where(b => b.CustomerId == customerId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
        foreach (var batchId in batchIds)
        {
            await _entitlements.ComputeForBatchAsync(batchId, cancellationToken);
        }

        var visitIds = await _db.Visits
            .Where(v => v.CustomerId == customerId && v.BatchId == null && v.Status == VisitStatus.Completed)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
        foreach (var visitId in visitIds)
        {
            await _entitlements.ComputeForVisitAsync(visitId, cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        var entitlements = await ListForCustomerAsync(customerId, cancellationToken);
        return new CloseAccountResponse(customerId, ledger.Id, ledger.Status, ledger.ClosedAt, entitlements);
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

    /// <summary>The ledger status of the customer behind an entitlement's source (batch or visit).</summary>
    private async Task<string> ResolveLedgerStatusAsync(DoctorEntitlement entitlement, CancellationToken cancellationToken)
    {
        var customerId = entitlement.BatchId is { } batchId
            ? await _db.Batches.Where(b => b.Id == batchId).Select(b => (Guid?)b.CustomerId).FirstOrDefaultAsync(cancellationToken)
            : await _db.Visits.Where(v => v.Id == entitlement.VisitId!.Value).Select(v => (Guid?)v.CustomerId).FirstOrDefaultAsync(cancellationToken);

        if (customerId is not { } cid)
        {
            throw new NotFoundException("entitlement_source", entitlement.Id);
        }

        return await _db.Ledgers.Where(l => l.CustomerId == cid).Select(l => l.Status).FirstOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException("ledger", cid);
    }

    private async Task<IReadOnlyList<DoctorEntitlementResponse>> ListForCustomerAsync(
        Guid customerId, CancellationToken cancellationToken)
    {
        var batchIds = await _db.Batches.Where(b => b.CustomerId == customerId).Select(b => b.Id).ToListAsync(cancellationToken);
        var visitIds = await _db.Visits.Where(v => v.CustomerId == customerId).Select(v => v.Id).ToListAsync(cancellationToken);

        var rows = await _db.DoctorEntitlements.AsNoTracking()
            .Where(e => (e.BatchId != null && batchIds.Contains(e.BatchId.Value))
                        || (e.VisitId != null && visitIds.Contains(e.VisitId.Value)))
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<DoctorEntitlementResponse>).ToList();
    }

    private (Guid EnvironmentId, Guid UserId) RequireUser()
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return (envId, userId);
    }
}
