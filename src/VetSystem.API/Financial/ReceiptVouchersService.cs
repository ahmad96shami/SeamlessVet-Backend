using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Financial.Contracts;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Financial;

/// <summary>
/// Receipt vouchers (Sanad Qabd, M7 task 9). Issuing one records the payment received from a
/// customer and posts a <c>receipt_voucher</c> ledger entry that reduces their balance (negative
/// amount). Append-only and idempotent per environment, in one transaction with the ledger post.
/// </summary>
public sealed class ReceiptVouchersService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly ILedgerService _ledgers;

    public ReceiptVouchersService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        ILedgerService ledgers)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _ledgers = ledgers;
    }

    public async Task<ReceiptVoucherResponse> IssueAsync(ReceiptVoucherRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var replay = await _db.ReceiptVouchers.AsNoTracking()
            .FirstOrDefaultAsync(v => v.EnvironmentId == envId && v.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return _mapper.Map<ReceiptVoucherResponse>(replay);
        }

        if (!await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, cancellationToken))
        {
            throw new NotFoundException("customer", request.CustomerId);
        }

        if (request.Id is { } rid && rid != Guid.Empty
            && await _db.ReceiptVouchers.IgnoreQueryFilters().AnyAsync(v => v.Id == rid, cancellationToken))
        {
            throw new ConflictException("receipt_voucher_id_collision", $"A receipt voucher with id '{rid}' already exists.");
        }

        var ledgerId = await _db.Ledgers
            .Where(l => l.CustomerId == request.CustomerId)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("ledger", request.CustomerId);

        var voucher = new ReceiptVoucher
        {
            Id = request.Id ?? Guid.Empty,
            CustomerId = request.CustomerId,
            Amount = Money(request.Amount),
            Method = request.Method,
            IssuedBy = userId,
            IssuedAt = _clock.UtcNow,
            Notes = request.Notes,
            IdempotencyKey = request.IdempotencyKey,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.ReceiptVouchers.Add(voucher);
        await _db.SaveChangesAsync(cancellationToken); // assigns voucher.Id

        // A payment reduces the customer's debt: post a negative ledger entry referencing the voucher.
        await _ledgers.AppendEntryAsync(
            new LedgerEntryRequest(
                Id: null,
                LedgerId: ledgerId,
                EntryType: LedgerEntryType.ReceiptVoucher,
                Amount: -voucher.Amount,
                InvoiceId: null,
                ReceiptVoucherId: voucher.Id,
                Description: voucher.Notes ?? "Receipt voucher",
                IdempotencyKey: $"voucher-{voucher.Id}"),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return _mapper.Map<ReceiptVoucherResponse>(voucher);
    }

    public async Task<ReceiptVoucherResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var voucher = await _db.ReceiptVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                      ?? throw new NotFoundException("receipt_voucher", id);
        return _mapper.Map<ReceiptVoucherResponse>(voucher);
    }

    public async Task<IReadOnlyList<ReceiptVoucherResponse>> ListAsync(
        Guid? customerId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.ReceiptVouchers.AsNoTracking();
        if (customerId is { } cid) query = query.Where(v => v.CustomerId == cid);

        var rows = await query
            .OrderByDescending(v => v.IssuedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<ReceiptVoucherResponse>).ToList();
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
