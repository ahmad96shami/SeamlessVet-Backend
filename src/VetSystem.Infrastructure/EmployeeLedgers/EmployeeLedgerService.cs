using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.EmployeeLedgers;
using VetSystem.Application.EmployeeLedgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.EmployeeLedgers;

/// <summary>
/// Append-only implementation of <see cref="IEmployeeLedgerService"/>, mirroring
/// <c>SupplierLedgerService</c> / <c>DoctorPartnerLedgerService</c>. Every entry runs inside the ambient
/// transaction so the new <c>employee_ledger_entries</c> row, the bumped <c>employee_ledgers.balance</c>,
/// and the <c>open ⇄ has_debt</c> status transition all commit (or none do). The unique
/// <c>(environment_id, idempotency_key)</c> index converts retried writes into idempotent replays.
/// </summary>
public sealed class EmployeeLedgerService : IEmployeeLedgerService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public EmployeeLedgerService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<EmployeeLedgerEntryResponse> AppendEntryAsync(
        EmployeeLedgerEntryRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var replay = await _db.EmployeeLedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
        if (replay is not null)
        {
            return _mapper.Map<EmployeeLedgerEntryResponse>(replay);
        }

        var ledger = await _db.EmployeeLedgers.FirstOrDefaultAsync(
                         l => l.Id == request.EmployeeLedgerId, cancellationToken)
                     ?? throw new NotFoundException("employee_ledger", request.EmployeeLedgerId);

        if (ledger.Status == LedgerStatus.Closed)
        {
            throw new ConflictException("employee_ledger_closed",
                "Cannot append to a closed employee ledger. Create an adjustment entry instead.");
        }

        var newBalance = ledger.Balance + request.Amount;

        var entry = new EmployeeLedgerEntry
        {
            Id = request.Id ?? Guid.Empty,
            EmployeeLedgerId = ledger.Id,
            EntryType = request.EntryType,
            Amount = request.Amount,
            BalanceAfter = newBalance,
            EmployeePaymentId = request.EmployeePaymentId,
            Description = request.Description,
            IdempotencyKey = request.IdempotencyKey,
        };

        _db.EmployeeLedgerEntries.Add(entry);

        ledger.Balance = newBalance;
        ledger.Status = newBalance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            var winning = await _db.EmployeeLedgerEntries
                .AsNoTracking()
                .FirstAsync(
                    e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);
            return _mapper.Map<EmployeeLedgerEntryResponse>(winning);
        }

        return _mapper.Map<EmployeeLedgerEntryResponse>(entry);
    }

    public async Task<EmployeeStatementResponse> GetStatementAsync(
        Guid employeeId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to)
        {
            throw new ConflictException("statement_invalid_window", "'from' must be on or before 'to'.");
        }

        var employee = await _db.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken)
            ?? throw new NotFoundException("employee", employeeId);

        var ledger = await _db.EmployeeLedgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.EmployeeId == employeeId, cancellationToken)
            ?? throw new NotFoundException("employee_ledger", employeeId);

        var openingBalance = 0m;
        if (from is { } fromValue)
        {
            var prior = await _db.EmployeeLedgerEntries
                .AsNoTracking()
                .Where(e => e.EmployeeLedgerId == ledger.Id && e.CreatedAt < fromValue)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => (decimal?)e.BalanceAfter)
                .FirstOrDefaultAsync(cancellationToken);
            openingBalance = prior ?? 0m;
        }

        var query = _db.EmployeeLedgerEntries
            .AsNoTracking()
            .Where(e => e.EmployeeLedgerId == ledger.Id);

        if (from is { } f) query = query.Where(e => e.CreatedAt >= f);
        if (to is { } t) query = query.Where(e => e.CreatedAt <= t);

        var entries = await query
            .OrderBy(e => e.CreatedAt)
            .Select(e => _mapper.Map<EmployeeLedgerEntryResponse>(e))
            .ToListAsync(cancellationToken);

        var closingBalance = entries.Count > 0 ? entries[^1].BalanceAfter : openingBalance;

        return new EmployeeStatementResponse(
            EmployeeId: employee.Id,
            FullName: employee.FullName,
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
        && pg.ConstraintName == "ux_employee_ledger_entries_env_idempotency";
}
