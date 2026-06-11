using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Entitlements;

/// <summary>
/// The account lifecycle + entitlement reads (M9 tasks 9–12; M30). Closing a customer/farm account
/// requires a zero balance and flips the ledger to <c>closed</c> (it no longer locks anything or
/// computes entitlements — M30 removed the settlement lock and the per-visit entitlement). Doctor
/// entitlements are <b>read-only</b> here: they are computed and credited to the doctor-partner ledger
/// when a batch is settled (تصفية). All transitions are idempotent so an offline-replayed call lands once.
/// </summary>
public sealed class EntitlementSettlementService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;
    private readonly IMapper _mapper;

    public EntitlementSettlementService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IClock clock,
        IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _mapper = mapper;
    }

    /// <summary>
    /// M9 task 9 / M16 — close a customer's <b>own</b> ledger (pet/clinic charges). Rejected unless the
    /// own-ledger balance is exactly zero; only a full settlement closes it. M30 — closing no longer
    /// computes or releases entitlements (those settle per batch); it just finalizes the ledger and
    /// returns the entitlements already on record for the customer's non-farm batches.
    /// </summary>
    public async Task<CloseAccountResponse> CloseCustomerAccountAsync(Guid customerId, CancellationToken cancellationToken)
    {
        RequireUser();

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.CustomerId == customerId, cancellationToken)
                     ?? throw new NotFoundException("ledger", customerId);

        await EnsureClosedAsync(ledger, cancellationToken);

        var batchIds = await _db.Batches
            .Where(b => b.CustomerId == customerId && b.FarmId == null)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var entitlements = await ListEntitlementsAsync(batchIds, cancellationToken);
        return new CloseAccountResponse(customerId, FarmId: null, ledger.Id, ledger.Status, ledger.ClosedAt, entitlements);
    }

    /// <summary>
    /// M16 — close a <b>farm</b>'s ledger (same zero-balance gate). Closing farm A neither touches farm
    /// B's ledger nor closes the owning customer. M30 — finalizes the ledger only (no entitlement compute).
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

        await EnsureClosedAsync(ledger, cancellationToken);

        var batchIds = await _db.Batches
            .Where(b => b.FarmId == farmId)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);

        var entitlements = await ListEntitlementsAsync(batchIds, cancellationToken);
        return new CloseAccountResponse(farm.CustomerId, farm.Id, ledger.Id, ledger.Status, ledger.ClosedAt, entitlements);
    }

    /// <summary>
    /// Re-open a customer's <b>own</b> ledger so a returning customer's new visit can be billed.
    /// Idempotent: a non-closed ledger is left untouched. Re-opening only lifts the append lock.
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

    /// <summary>Closes a ledger once its balance is exactly zero (partial payments never close it).</summary>
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
                $"Cannot close the account: the ledger balance is {ledger.Balance:0.00}. Settle the balance "
                + "to zero first.");
        }

        ledger.Status = LedgerStatus.Closed;
        ledger.ClosedAt = _clock.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DoctorEntitlementResponse>> ListAsync(
        Guid? doctorId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.DoctorEntitlements.AsNoTracking();
        if (doctorId is { } did) query = query.Where(e => e.DoctorId == did);

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

    private async Task<IReadOnlyList<DoctorEntitlementResponse>> ListEntitlementsAsync(
        IReadOnlyList<Guid> batchIds, CancellationToken cancellationToken)
    {
        if (batchIds.Count == 0)
        {
            return [];
        }

        var rows = await _db.DoctorEntitlements.AsNoTracking()
            .Where(e => batchIds.Contains(e.BatchId))
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
