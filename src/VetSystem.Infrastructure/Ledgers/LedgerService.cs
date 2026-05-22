using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
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

    public LedgerService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
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

        return _mapper.Map<LedgerEntryResponse>(entry);
    }

    public async Task<StatementResponse> GetStatementAsync(
        Guid customerId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to)
        {
            throw new ConflictException("statement_invalid_window", "'from' must be on or before 'to'.");
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken)
            ?? throw new NotFoundException("customer", customerId);

        var ledger = await _db.Ledgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.CustomerId == customerId, cancellationToken)
            ?? throw new NotFoundException("ledger", customerId);

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
            CustomerId: customer.Id,
            CustomerName: customer.FullName,
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
