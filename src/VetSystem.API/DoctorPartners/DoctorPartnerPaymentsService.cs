using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.DoctorPartnerLedgers;
using VetSystem.Application.DoctorPartnerLedgers.Contracts;
using VetSystem.Application.DoctorPartners.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.DoctorPartners;

/// <summary>
/// Doctor-partner payments (M30) — the doctor-AP mirror of supplier payments / receipt vouchers.
/// Issuing one records the payment and posts a negative <c>payment</c> entry that reduces the
/// doctor-partner ledger balance, in one transaction. Append-only and idempotent per environment so a
/// retried payment never double-debits.
/// </summary>
public sealed class DoctorPartnerPaymentsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IDoctorPartnerLedgerService _ledgers;

    public DoctorPartnerPaymentsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        IDoctorPartnerLedgerService ledgers)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _ledgers = ledgers;
    }

    public async Task<DoctorPartnerPaymentResponse> IssueAsync(
        Guid doctorPartnerId, DoctorPartnerPaymentRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var replay = await _db.DoctorPartnerPayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EnvironmentId == envId && p.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return _mapper.Map<DoctorPartnerPaymentResponse>(replay);
        }

        if (!await _db.DoctorPartners.AnyAsync(p => p.Id == doctorPartnerId, cancellationToken))
        {
            throw new NotFoundException("doctor_partner", doctorPartnerId);
        }

        var ledgerId = await _db.DoctorPartnerLedgers
            .Where(l => l.DoctorPartnerId == doctorPartnerId)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("doctor_partner_ledger", doctorPartnerId);

        if (request.Id is { } rid && rid != Guid.Empty
            && await _db.DoctorPartnerPayments.IgnoreQueryFilters().AnyAsync(p => p.Id == rid, cancellationToken))
        {
            throw new ConflictException("doctor_partner_payment_id_collision",
                $"A doctor-partner payment with id '{rid}' already exists.");
        }

        var payment = new DoctorPartnerPayment
        {
            Id = request.Id ?? Guid.Empty,
            DoctorPartnerId = doctorPartnerId,
            Amount = Money(request.Amount),
            Method = request.Method,
            PaidBy = userId,
            PaidAt = _clock.UtcNow,
            Notes = request.Notes,
            ChequeNumber = request.ChequeNumber,
            ChequeBank = request.ChequeBank,
            ChequeDueDate = request.ChequeDueDate,
            IdempotencyKey = request.IdempotencyKey,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.DoctorPartnerPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken); // assigns payment.Id

        // A payment reduces the payable: post a negative entry referencing this payment.
        await _ledgers.AppendEntryAsync(
            new DoctorPartnerLedgerEntryRequest(
                Id: null,
                DoctorPartnerLedgerId: ledgerId,
                EntryType: DoctorPartnerLedgerEntryType.Payment,
                Amount: -payment.Amount,
                DoctorEntitlementId: null,
                BatchId: null,
                DoctorPartnerPaymentId: payment.Id,
                Description: payment.Notes ?? "Doctor-partner payment",
                IdempotencyKey: $"doctor-partner-payment-{payment.Id}"),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return _mapper.Map<DoctorPartnerPaymentResponse>(payment);
    }

    public async Task<IReadOnlyList<DoctorPartnerPaymentResponse>> ListAsync(
        Guid doctorPartnerId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var rows = await _db.DoctorPartnerPayments.AsNoTracking()
            .Where(p => p.DoctorPartnerId == doctorPartnerId)
            .OrderByDescending(p => p.PaidAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<DoctorPartnerPaymentResponse>).ToList();
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
