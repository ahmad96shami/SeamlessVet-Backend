using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Application.Purchasing.Contracts;
using VetSystem.Application.SupplierLedgers;
using VetSystem.Application.SupplierLedgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Suppliers;

/// <summary>
/// Purchase-invoice issuance (M19 task 5). One transaction does three things, all-or-nothing: writes a
/// signed <c>receive</c> <see cref="InventoryMovement"/> per line (delta-only, via
/// <see cref="IInventoryService"/>), snapshots each line's unit cost on the
/// <see cref="PurchaseInvoiceItem"/>, and posts the full <see cref="PurchaseInvoice.Total"/> as a
/// <c>purchase_invoice</c> payable to the supplier ledger. Row-level idempotent: a retried key returns
/// the original invoice without re-receiving or re-posting. Append-only — a wrong invoice is corrected
/// with a manual supplier-ledger adjustment (and a compensating stock movement).
/// </summary>
public sealed class PurchaseInvoicesService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IInventoryService _inventory;
    private readonly ISupplierLedgerService _supplierLedgers;

    public PurchaseInvoicesService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        IInventoryService inventory,
        ISupplierLedgerService supplierLedgers)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _inventory = inventory;
        _supplierLedgers = supplierLedgers;
    }

    public async Task<PurchaseInvoiceResponse> IssueAsync(
        PurchaseInvoiceRequest request, CancellationToken cancellationToken)
    {
        var (envId, userId) = RequireUser();

        var replayId = await _db.PurchaseInvoices.AsNoTracking()
            .Where(p => p.EnvironmentId == envId && p.IdempotencyKey == request.IdempotencyKey)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (replayId is { } existing)
        {
            return await BuildResponseAsync(existing, cancellationToken);
        }

        if (!await _db.Suppliers.AnyAsync(s => s.Id == request.SupplierId, cancellationToken))
        {
            throw new NotFoundException("supplier", request.SupplierId);
        }

        var supplierLedgerId = await _db.SupplierLedgers
            .Where(l => l.SupplierId == request.SupplierId)
            .Select(l => (Guid?)l.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("supplier_ledger", request.SupplierId);

        var warehouseId = await ResolveWarehouseIdAsync(request.WarehouseId, cancellationToken);

        if (request.Id is { } rid && rid != Guid.Empty
            && await _db.PurchaseInvoices.IgnoreQueryFilters().AnyAsync(p => p.Id == rid, cancellationToken))
        {
            throw new ConflictException("purchase_invoice_id_collision",
                $"A purchase invoice with id '{rid}' already exists.");
        }

        var lines = new List<ResolvedLine>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (!await _db.Products.AnyAsync(p => p.Id == item.ProductId, cancellationToken))
            {
                throw new NotFoundException("product", item.ProductId);
            }

            var lineTotal = Money(item.Quantity * item.UnitCost - item.DiscountAmount);
            if (lineTotal < 0m)
            {
                throw new ConflictException("line_discount_exceeds_total",
                    "A line discount cannot exceed the line's gross cost.");
            }

            lines.Add(new ResolvedLine(item.ProductId, item.Quantity, Money(item.UnitCost), Money(item.DiscountAmount), lineTotal));
        }

        var subtotal = Money(lines.Sum(l => l.LineTotal));
        var discount = Money(request.DiscountAmount);
        var taxable = subtotal - discount;
        if (taxable < 0m)
        {
            throw new ConflictException("discount_exceeds_subtotal", "The invoice discount exceeds the subtotal.");
        }

        var tax = Money(request.TaxAmount ?? 0m);
        var total = taxable + tax;

        var invoice = new PurchaseInvoice
        {
            Id = request.Id ?? Guid.Empty,
            SupplierId = request.SupplierId,
            WarehouseId = warehouseId,
            Number = string.IsNullOrWhiteSpace(request.Number) ? null : request.Number.Trim(),
            Subtotal = subtotal,
            DiscountAmount = discount,
            TaxAmount = tax,
            Total = total,
            ReceivedBy = userId,
            ReceivedAt = _clock.UtcNow,
            Notes = request.Notes,
            IdempotencyKey = request.IdempotencyKey,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.PurchaseInvoices.Add(invoice);
        await _db.SaveChangesAsync(cancellationToken); // assigns invoice.Id

        foreach (var line in lines)
        {
            _db.PurchaseInvoiceItems.Add(new PurchaseInvoiceItem
            {
                Id = Guid.Empty,
                PurchaseInvoiceId = invoice.Id,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitCost = line.UnitCost,
                DiscountAmount = line.DiscountAmount,
                LineTotal = line.LineTotal,
            });
        }

        await _db.SaveChangesAsync(cancellationToken); // assigns item ids (used as movement keys)

        // Receive each line into the warehouse — delta-only signed movements, tagged with the source
        // purchase invoice. Per-item idempotency key keeps a retried issuance from double-receiving.
        var items = await _db.PurchaseInvoiceItems
            .Where(it => it.PurchaseInvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            await _inventory.ApplyMovementAsync(
                new MovementIntent(
                    Id: null,
                    MovementType: MovementType.Receive,
                    ProductId: item.ProductId,
                    Quantity: item.Quantity,
                    FromLocationType: null,
                    FromLocationId: null,
                    ToLocationType: StockLocation.Warehouse,
                    ToLocationId: warehouseId,
                    IdempotencyKey: $"purchase-{item.Id}",
                    Reason: invoice.Number is null ? "Purchase invoice" : $"Purchase invoice {invoice.Number}",
                    VisitId: null,
                    InvoiceId: null,
                    PurchaseInvoiceId: invoice.Id),
                cancellationToken);
        }

        // Post the payable: a purchase invoice increases what the clinic owes the supplier.
        await _supplierLedgers.AppendEntryAsync(
            new SupplierLedgerEntryRequest(
                Id: null,
                SupplierLedgerId: supplierLedgerId,
                EntryType: SupplierLedgerEntryType.PurchaseInvoice,
                Amount: total,
                PurchaseInvoiceId: invoice.Id,
                SupplierPaymentId: null,
                Description: invoice.Number is null ? "Purchase invoice" : $"Purchase invoice {invoice.Number}",
                IdempotencyKey: $"purchase-invoice-{invoice.Id}"),
            cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await BuildResponseAsync(invoice.Id, cancellationToken);
    }

    public async Task<PurchaseInvoiceResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await _db.PurchaseInvoices.AsNoTracking().AnyAsync(p => p.Id == id, cancellationToken))
        {
            throw new NotFoundException("purchase_invoice", id);
        }

        return await BuildResponseAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseInvoiceResponse>> ListAsync(
        Guid? supplierId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.PurchaseInvoices.AsNoTracking();
        if (supplierId is { } sid) query = query.Where(p => p.SupplierId == sid);

        var ids = await query
            .OrderByDescending(p => p.ReceivedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        var results = new List<PurchaseInvoiceResponse>(ids.Count);
        foreach (var id in ids)
        {
            results.Add(await BuildResponseAsync(id, cancellationToken));
        }

        return results;
    }

    private async Task<PurchaseInvoiceResponse> BuildResponseAsync(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await _db.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                      ?? throw new NotFoundException("purchase_invoice", id);

        var items = await _db.PurchaseInvoiceItems.AsNoTracking()
            .Where(it => it.PurchaseInvoiceId == id)
            .OrderBy(it => it.CreatedAt)
            .ToListAsync(cancellationToken);

        return new PurchaseInvoiceResponse(
            invoice.Id,
            invoice.SupplierId,
            invoice.WarehouseId,
            invoice.Number,
            invoice.Subtotal,
            invoice.DiscountAmount,
            invoice.TaxAmount,
            invoice.Total,
            invoice.ReceivedBy,
            invoice.ReceivedAt,
            invoice.Notes,
            items.Select(_mapper.Map<PurchaseInvoiceItemResponse>).ToList(),
            invoice.CreatedAt,
            invoice.UpdatedAt);
    }

    /// <summary>
    /// Resolves the receiving warehouse: an explicit id wins (validated downstream by the inventory
    /// service); otherwise the environment's single central warehouse is used.
    /// </summary>
    private async Task<Guid> ResolveWarehouseIdAsync(Guid? supplied, CancellationToken cancellationToken)
    {
        if (supplied is { } id && id != Guid.Empty)
        {
            return id;
        }

        var warehouseIds = await _db.Warehouses.AsNoTracking()
            .Select(w => w.Id)
            .Take(2)
            .ToListAsync(cancellationToken);

        return warehouseIds.Count switch
        {
            0 => throw new ConflictException("no_warehouse",
                "No warehouse exists in this environment; seed one before receiving stock."),
            1 => warehouseIds[0],
            _ => throw new ConflictException("warehouse_id_required",
                "Multiple warehouses exist in this environment; specify warehouse_id."),
        };
    }

    private (Guid EnvironmentId, Guid UserId) RequireUser()
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        return (envId, userId);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private sealed record ResolvedLine(
        Guid ProductId, decimal Quantity, decimal UnitCost, decimal DiscountAmount, decimal LineTotal);
}
