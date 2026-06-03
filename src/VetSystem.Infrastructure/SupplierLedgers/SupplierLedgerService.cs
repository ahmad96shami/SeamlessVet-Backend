using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.SupplierLedgers;
using VetSystem.Application.SupplierLedgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.SupplierLedgers;

/// <summary>
/// Append-only implementation of <see cref="ISupplierLedgerService"/>, mirroring
/// <c>LedgerService</c>. Every entry runs inside the ambient transaction so the new
/// <c>supplier_ledger_entries</c> row, the bumped <c>supplier_ledgers.balance</c>, and the
/// <c>open ⇄ has_debt</c> status transition all commit (or none do). The unique
/// <c>(environment_id, idempotency_key)</c> index converts retried writes into idempotent replays.
/// </summary>
public sealed class SupplierLedgerService : ISupplierLedgerService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public SupplierLedgerService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<SupplierLedgerEntryResponse> AppendEntryAsync(
        SupplierLedgerEntryRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var replay = await _db.SupplierLedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
        if (replay is not null)
        {
            return _mapper.Map<SupplierLedgerEntryResponse>(replay);
        }

        var ledger = await _db.SupplierLedgers.FirstOrDefaultAsync(l => l.Id == request.SupplierLedgerId, cancellationToken)
                     ?? throw new NotFoundException("supplier_ledger", request.SupplierLedgerId);

        if (ledger.Status == LedgerStatus.Closed)
        {
            throw new ConflictException("supplier_ledger_closed",
                "Cannot append to a closed supplier ledger. Create an adjustment entry instead.");
        }

        var newBalance = ledger.Balance + request.Amount;

        var entry = new SupplierLedgerEntry
        {
            Id = request.Id ?? Guid.Empty,
            SupplierLedgerId = ledger.Id,
            EntryType = request.EntryType,
            Amount = request.Amount,
            BalanceAfter = newBalance,
            PurchaseInvoiceId = request.PurchaseInvoiceId,
            SupplierPaymentId = request.SupplierPaymentId,
            Description = request.Description,
            IdempotencyKey = request.IdempotencyKey,
        };

        _db.SupplierLedgerEntries.Add(entry);

        ledger.Balance = newBalance;
        ledger.Status = newBalance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            var winning = await _db.SupplierLedgerEntries
                .AsNoTracking()
                .FirstAsync(
                    e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);
            return _mapper.Map<SupplierLedgerEntryResponse>(winning);
        }

        return _mapper.Map<SupplierLedgerEntryResponse>(entry);
    }

    public async Task<SupplierStatementResponse> GetStatementAsync(
        Guid supplierId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to)
        {
            throw new ConflictException("statement_invalid_window", "'from' must be on or before 'to'.");
        }

        var supplier = await _db.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken)
            ?? throw new NotFoundException("supplier", supplierId);

        var ledger = await _db.SupplierLedgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.SupplierId == supplierId, cancellationToken)
            ?? throw new NotFoundException("supplier_ledger", supplierId);

        var openingBalance = 0m;
        if (from is { } fromValue)
        {
            var prior = await _db.SupplierLedgerEntries
                .AsNoTracking()
                .Where(e => e.SupplierLedgerId == ledger.Id && e.CreatedAt < fromValue)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => (decimal?)e.BalanceAfter)
                .FirstOrDefaultAsync(cancellationToken);
            openingBalance = prior ?? 0m;
        }

        var query = _db.SupplierLedgerEntries
            .AsNoTracking()
            .Where(e => e.SupplierLedgerId == ledger.Id);

        if (from is { } f) query = query.Where(e => e.CreatedAt >= f);
        if (to is { } t) query = query.Where(e => e.CreatedAt <= t);

        var entries = await query
            .OrderBy(e => e.CreatedAt)
            .Select(e => _mapper.Map<SupplierLedgerEntryResponse>(e))
            .ToListAsync(cancellationToken);

        var closingBalance = entries.Count > 0 ? entries[^1].BalanceAfter : openingBalance;

        return new SupplierStatementResponse(
            SupplierId: supplier.Id,
            SupplierName: supplier.Name,
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
        && pg.ConstraintName == "ux_supplier_ledger_entries_env_idempotency";
}
