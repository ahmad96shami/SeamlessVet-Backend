using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.DoctorPartnerLedgers;
using VetSystem.Application.DoctorPartnerLedgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure.DoctorPartnerLedgers;

/// <summary>
/// Append-only implementation of <see cref="IDoctorPartnerLedgerService"/>, mirroring
/// <c>SupplierLedgerService</c>. Every entry runs inside the ambient transaction so the new
/// <c>doctor_partner_ledger_entries</c> row, the bumped <c>doctor_partner_ledgers.balance</c>, and the
/// <c>open ⇄ has_debt</c> status transition all commit (or none do). The unique
/// <c>(environment_id, idempotency_key)</c> index converts retried writes into idempotent replays.
/// </summary>
public sealed class DoctorPartnerLedgerService : IDoctorPartnerLedgerService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public DoctorPartnerLedgerService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<DoctorPartnerLedgerEntryResponse> AppendEntryAsync(
        DoctorPartnerLedgerEntryRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var replay = await _db.DoctorPartnerLedgerEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                cancellationToken);
        if (replay is not null)
        {
            return _mapper.Map<DoctorPartnerLedgerEntryResponse>(replay);
        }

        var ledger = await _db.DoctorPartnerLedgers.FirstOrDefaultAsync(
                         l => l.Id == request.DoctorPartnerLedgerId, cancellationToken)
                     ?? throw new NotFoundException("doctor_partner_ledger", request.DoctorPartnerLedgerId);

        if (ledger.Status == LedgerStatus.Closed)
        {
            throw new ConflictException("doctor_partner_ledger_closed",
                "Cannot append to a closed doctor-partner ledger. Create an adjustment entry instead.");
        }

        var newBalance = ledger.Balance + request.Amount;

        var entry = new DoctorPartnerLedgerEntry
        {
            Id = request.Id ?? Guid.Empty,
            DoctorPartnerLedgerId = ledger.Id,
            EntryType = request.EntryType,
            Amount = request.Amount,
            BalanceAfter = newBalance,
            DoctorEntitlementId = request.DoctorEntitlementId,
            BatchId = request.BatchId,
            DoctorPartnerPaymentId = request.DoctorPartnerPaymentId,
            Description = request.Description,
            IdempotencyKey = request.IdempotencyKey,
        };

        _db.DoctorPartnerLedgerEntries.Add(entry);

        ledger.Balance = newBalance;
        ledger.Status = newBalance > 0m ? LedgerStatus.HasDebt : LedgerStatus.Open;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            var winning = await _db.DoctorPartnerLedgerEntries
                .AsNoTracking()
                .FirstAsync(
                    e => e.EnvironmentId == envId && e.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);
            return _mapper.Map<DoctorPartnerLedgerEntryResponse>(winning);
        }

        return _mapper.Map<DoctorPartnerLedgerEntryResponse>(entry);
    }

    public async Task<DoctorPartnerStatementResponse> GetStatementAsync(
        Guid doctorPartnerId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        if (from is not null && to is not null && from > to)
        {
            throw new ConflictException("statement_invalid_window", "'from' must be on or before 'to'.");
        }

        var partner = await _db.DoctorPartners
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == doctorPartnerId, cancellationToken)
            ?? throw new NotFoundException("doctor_partner", doctorPartnerId);

        var doctorName = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == partner.UserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var ledger = await _db.DoctorPartnerLedgers
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.DoctorPartnerId == doctorPartnerId, cancellationToken)
            ?? throw new NotFoundException("doctor_partner_ledger", doctorPartnerId);

        var openingBalance = 0m;
        if (from is { } fromValue)
        {
            var prior = await _db.DoctorPartnerLedgerEntries
                .AsNoTracking()
                .Where(e => e.DoctorPartnerLedgerId == ledger.Id && e.CreatedAt < fromValue)
                .OrderByDescending(e => e.CreatedAt)
                .Select(e => (decimal?)e.BalanceAfter)
                .FirstOrDefaultAsync(cancellationToken);
            openingBalance = prior ?? 0m;
        }

        var query = _db.DoctorPartnerLedgerEntries
            .AsNoTracking()
            .Where(e => e.DoctorPartnerLedgerId == ledger.Id);

        if (from is { } f) query = query.Where(e => e.CreatedAt >= f);
        if (to is { } t) query = query.Where(e => e.CreatedAt <= t);

        var entries = await query
            .OrderBy(e => e.CreatedAt)
            .Select(e => _mapper.Map<DoctorPartnerLedgerEntryResponse>(e))
            .ToListAsync(cancellationToken);

        var closingBalance = entries.Count > 0 ? entries[^1].BalanceAfter : openingBalance;

        return new DoctorPartnerStatementResponse(
            DoctorPartnerId: partner.Id,
            DoctorName: doctorName,
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
        && pg.ConstraintName == "ux_doctor_partner_ledger_entries_env_idempotency";
}
