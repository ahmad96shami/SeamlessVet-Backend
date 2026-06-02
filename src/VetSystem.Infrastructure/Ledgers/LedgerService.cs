using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.Ledgers;

/// <summary>
/// Append-only implementation of <see cref="ILedgerService"/>. Every entry runs inside an EF
/// transaction so the new <c>ledger_entries</c> row, the bumped <c>ledgers.balance</c>, and the
/// <c>open ⇄ has_debt</c> status transition all commit (or none do). The unique
/// <c>(environment_id, idempotency_key)</c> index converts retried offline writes into idempotent
/// replays via the catch-block below.
/// </summary>
public sealed class LedgerService : ILedgerService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IDomainEventPublisher _events;

    public LedgerService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IDomainEventPublisher events)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _events = events;
    }

    public async Task<LedgerEntryResponse> AppendEntryAsync(
        LedgerEntryRequest request,
        CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        // Replay path: same idempotency key returns the original row. Cheaper than relying on
        // the unique-violation catch and gives the caller a stable response.
        var replay = await _db.LedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);

        if (replay is not null)
        {
            return _mapper.Map<LedgerEntryResponse>(replay);
        }

        var ledger = await _db.Ledgers.FirstOrDefaultAsync(l => l.Id == request.LedgerId, cancellationToken)
                     ?? throw new NotFoundException("ledger", request.LedgerId);

        if (ledger.Status == LedgerStatus.Closed)
        {
            throw new ConflictException("ledger_closed",
                "Cannot append to a closed ledger. Re-open the account or create an adjustment entry.");
        }

        var previousBalance = ledger.Balance;
        var newBalance = ledger.Balance + request.Amount;

        var entry = new LedgerEntry
        {
            Id = request.Id ?? Guid.Empty,
            LedgerId = ledger.Id,
            EntryType = request.EntryType,
            Amount = request.Amount,
            BalanceAfter = newBalance,
            InvoiceId = request.InvoiceId,
            ReceiptVoucherId = request.ReceiptVoucherId,
            Description = request.Description,
            IdempotencyKey = request.IdempotencyKey,
        };

        _db.LedgerEntries.Add(entry);

        ledger.Balance = newBalance;
        ledger.Status = newBalance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            // Two concurrent posts of the same idempotency key — return the row that won.
            var winning = await _db.LedgerEntries
                .AsNoTracking()
                .FirstAsync(
                    e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);
            return _mapper.Map<LedgerEntryResponse>(winning);
        }

        // The account just transitioned from owing (> 0) to fully settled (0): the last open invoice
        // is paid, so the account is ready to close and release entitlements (M11 task 12). Published
        // after the commit so handlers observe persisted state; a handler failure won't undo the entry.
        // M16: a farm ledger resolves to its owning customer for addressing (notifications target the
        // env's admins/accountants; the customer + farm ids ride in the payload).
        if (previousBalance > 0m && newBalance == 0m)
        {
            var ownerCustomerId = ledger.CustomerId;
            if (ledger.FarmId is { } farmId)
            {
                ownerCustomerId = await _db.Farms
                    .Where(f => f.Id == farmId)
                    .Select(f => (Guid?)f.CustomerId)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            if (ownerCustomerId is { } settledCustomerId)
            {
                await _events.PublishAsync(
                    new AccountReadyForSettlementEvent(
                        ledger.EnvironmentId, settledCustomerId, ledger.FarmId, ledger.Id, previousBalance),
                    cancellationToken);
            }
        }

        return _mapper.Map<LedgerEntryResponse>(entry);
    }

    public async Task<StatementResponse> GetStatementAsync(
        Guid customerId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new NotFoundException("customer", customerId);

        var ledger = await _db.Ledgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.CustomerId == customerId, cancellationToken)
            ?? throw new NotFoundException("ledger", customerId);

        return await BuildStatementAsync(
            ledger, customer.Id, customer.FullName, farmId: null, farmName: null, from, to, cancellationToken);
    }

    public async Task<StatementResponse> GetFarmStatementAsync(
        Guid farmId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        var farm = await _db.Farms
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == farmId, cancellationToken)
            ?? throw new NotFoundException("farm", farmId);

        var customerName = await _db.Customers
            .AsNoTracking()
            .Where(c => c.Id == farm.CustomerId)
            .Select(c => c.FullName)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("customer", farm.CustomerId);

        var ledger = await _db.Ledgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.FarmId == farmId, cancellationToken)
            ?? throw new NotFoundException("ledger", farmId);

        return await BuildStatementAsync(
            ledger, farm.CustomerId, customerName, farm.Id, farm.Name, from, to, cancellationToken);
    }

    /// <summary>Shared statement core (M3 customer + M16 farm): opening/closing balance over the window.</summary>
    private async Task<StatementResponse> BuildStatementAsync(
        Ledger ledger,
        Guid customerId,
        string customerName,
        Guid? farmId,
        string? farmName,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to)
        {
            throw new ConflictException("statement_invalid_window", "'from' must be on or before 'to'.");
        }

        // Opening balance = balance of the most recent entry strictly before `from`.
        // If no `from` is given, opening is 0 — the ledger starts with no debt.
        var openingBalance = 0m;
        if (from is { } fromValue)
        {
            var prior = await _db.LedgerEntries
                .AsNoTracking()
                .Where(e => e.LedgerId == ledger.Id && e.CreatedAt < fromValue)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => (decimal?)e.BalanceAfter)
                .FirstOrDefaultAsync(cancellationToken);
            openingBalance = prior ?? 0m;
        }

        var query = _db.LedgerEntries
            .AsNoTracking()
            .Where(e => e.LedgerId == ledger.Id);

        if (from is { } f) query = query.Where(e => e.CreatedAt >= f);
        if (to is { } t) query = query.Where(e => e.CreatedAt <= t);

        var entries = await query
            .OrderBy(e => e.CreatedAt)
            .Select(e => _mapper.Map<LedgerEntryResponse>(e))
            .ToListAsync(cancellationToken);

        var closingBalance = entries.Count > 0 ? entries[^1].BalanceAfter : openingBalance;

        return new StatementResponse(
            CustomerId: customerId,
            CustomerName: customerName,
            FarmId: farmId,
            FarmName: farmName,
            LedgerId: ledger.Id,
            OpeningBalance: openingBalance,
            ClosingBalance: closingBalance,
            Status: ledger.Status,
            From: from,
            To: to,
            Entries: entries);
    }

    private static bool IsIdempotencyViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException pg
        && pg.SqlState == "23505"
        && pg.ConstraintName == "ux_ledger_entries_env_idempotency";
}
