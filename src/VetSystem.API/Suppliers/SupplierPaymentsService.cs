using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Purchasing.Contracts;
using VetSystem.Application.SupplierLedgers;
using VetSystem.Application.SupplierLedgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Suppliers;

/// <summary>
/// Supplier payments (M19 task 6) — the AP mirror of receipt vouchers. Issuing one records the payment
/// and posts a negative <c>payment</c> entry that reduces the supplier ledger balance, in one
/// transaction. Append-only and idempotent per environment so a retried payment never double-debits.
/// </summary>
public sealed class SupplierPaymentsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly ISupplierLedgerService _supplierLedgers;

    public SupplierPaymentsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        ISupplierLedgerService supplierLedgers)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _supplierLedgers = supplierLedgers;
    }

    public async Task<SupplierPaymentResponse> IssueAsync(
        Guid supplierId, SupplierPaymentRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var replay = await _db.SupplierPayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EnvironmentId == envId && p.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return _mapper.Map<SupplierPaymentResponse>(replay);
        }

        if (!await _db.Suppliers.AnyAsync(s => s.Id == supplierId, cancellationToken))
        {
            throw new NotFoundException("supplier", supplierId);
        }

        var supplierLedgerId = await _db.SupplierLedgers
            .Where(l => l.SupplierId == supplierId)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("supplier_ledger", supplierId);

        if (request.Id is { } rid && rid != Guid.Empty
            && await _db.SupplierPayments.IgnoreQueryFilters().AnyAsync(p => p.Id == rid, cancellationToken))
        {
            throw new ConflictException("supplier_payment_id_collision",
                $"A supplier payment with id '{rid}' already exists.");
        }

        var payment = new SupplierPayment
        {
            Id = request.Id ?? Guid.Empty,
            SupplierId = supplierId,
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

        _db.SupplierPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken); // assigns payment.Id

        // A payment reduces the payable: post a negative entry referencing this payment.
        await _supplierLedgers.AppendEntryAsync(
            new SupplierLedgerEntryRequest(
                Id: null,
                SupplierLedgerId: supplierLedgerId,
                EntryType: SupplierLedgerEntryType.Payment,
                Amount: -payment.Amount,
                PurchaseInvoiceId: null,
                SupplierPaymentId: payment.Id,
                Description: payment.Notes ?? "Supplier payment",
                IdempotencyKey: $"supplier-payment-{payment.Id}"),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return _mapper.Map<SupplierPaymentResponse>(payment);
    }

    public async Task<IReadOnlyList<SupplierPaymentResponse>> ListAsync(
        Guid supplierId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var rows = await _db.SupplierPayments.AsNoTracking()
            .Where(p => p.SupplierId == supplierId)
            .OrderByDescending(p => p.PaidAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<SupplierPaymentResponse>).ToList();
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
