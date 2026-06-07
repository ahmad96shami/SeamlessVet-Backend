using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Contracts;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Application.Entitlements;
using VetSystem.Application.Ledgers;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Financial;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Contracts;

/// <summary>
/// M24 batch settlement (تصفية الدورة) — SCHEMA §5a, invariant #11. The end-of-cycle renegotiation:
/// one settled unit price per product across ALL the batch's effective invoices, plus an optional
/// batch-level discount, then the cycle closes and the doctor's entitlement is computed on the
/// settled numbers. Invoices are never mutated (invariant #3): the settlement document is inserted
/// append-only and the money moves as ledger <c>adjustment</c> entries on the batch's owner ledger
/// (farm ledger when the batch is farm-scoped — M16 routing).
///
/// <para>Guards: one settlement per batch (partial-unique index is the race backstop), the owner
/// ledger must not be <c>closed</c> (the adjustments must still be postable), and no entitlement for
/// the batch may be <c>approved</c>/<c>paid</c> (those figures are frozen — re-pricing under them
/// would create a reconciliation gap). The batch itself may be <c>open</c> or already-closed-but-
/// unsettled (closing via PATCH first is harmless; settling re-runs the idempotent compute).</para>
/// </summary>
public sealed class BatchSettlementService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IClock _clock;
    private readonly ILedgerService _ledgers;
    private readonly IOwnerLedgerResolver _ownerLedgers;
    private readonly IPricingService _pricing;
    private readonly IEntitlementService _entitlements;

    public BatchSettlementService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IClock clock,
        ILedgerService ledgers,
        IOwnerLedgerResolver ownerLedgers,
        IPricingService pricing,
        IEntitlementService entitlements)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _ledgers = ledgers;
        _ownerLedgers = ownerLedgers;
        _pricing = pricing;
        _entitlements = entitlements;
    }

    public async Task<BatchSettlementPreviewResponse> PreviewAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await _db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
                    ?? throw new NotFoundException("batch", batchId);

        var (effectiveInvoices, productItems) = await EffectiveInvoices.LoadAsync(
            _db, i => i.BatchId == batchId, cancellationToken);
        var originalTotal = effectiveInvoices.Values.Sum(i => i.Total);

        var settlement = await _db.BatchSettlements.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BatchId == batchId, cancellationToken);

        var entitlementFrozen = await _db.DoctorEntitlements.AsNoTracking()
            .AnyAsync(e => e.BatchId == batchId && e.Status != EntitlementStatus.Pending, cancellationToken);

        var ledgerId = await _ownerLedgers.ResolveAsync(batch.CustomerId, batch.FarmId, cancellationToken);
        var ledger = ledgerId is { } lid
            ? await _db.Ledgers.AsNoTracking().FirstAsync(l => l.Id == lid, cancellationToken)
            : null;

        var customerName = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == batch.CustomerId).Select(c => c.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var farmName = batch.FarmId is { } fid
            ? await _db.Farms.AsNoTracking().Where(f => f.Id == fid).Select(f => f.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var doctorName = await _db.Users.AsNoTracking()
            .Where(u => u.Id == batch.ResponsibleDoctorId).Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        var products = await BuildProductRowsAsync(batch, productItems, cancellationToken);

        var invoices = effectiveInvoices.Values
            .OrderBy(i => i.IssuedAt)
            .Select(i => new SettlementPreviewInvoice(i.Id, i.Number, i.InvoiceType, i.IssuedAt, i.Total))
            .ToList();

        return new BatchSettlementPreviewResponse(
            batch.Id,
            batch.Status,
            batch.CustomerId,
            customerName,
            batch.FarmId,
            farmName,
            batch.ResponsibleDoctorId,
            doctorName,
            batch.AnimalCount,
            batch.StartDate,
            batch.EndDate,
            batch.SupervisionFeeModel,
            batch.SupervisionFeeValue,
            batch.EntitlementEnabled,
            batch.EntitlementSystem,
            batch.DoctorSharePercent,
            batch.DoctorShareCeiling,
            originalTotal,
            ledgerId,
            ledger?.Balance ?? 0m,
            ledger?.Status ?? LedgerStatus.Open,
            AlreadySettled: settlement is not null,
            SettledAt: settlement?.SettledAt,
            LedgerClosed: ledger?.Status == LedgerStatus.Closed,
            EntitlementFrozen: entitlementFrozen,
            products,
            invoices);
    }

    public async Task<BatchSettlementResponse> GetAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var settlement = await _db.BatchSettlements.AsNoTracking()
            .FirstOrDefaultAsync(s => s.BatchId == batchId, cancellationToken)
            ?? throw new NotFoundException("batch_settlement", batchId);

        var lines = await _db.BatchSettlementLines.AsNoTracking()
            .Where(l => l.SettlementId == settlement.Id)
            .ToListAsync(cancellationToken);

        return ToResponse(settlement, lines);
    }

    public async Task<BatchSettlementResponse> SettleAsync(
        Guid batchId, BatchSettlementRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId || _currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        // Body-level replay (the endpoint's Idempotency-Key header filter is the first line; the
        // unique (env, idempotency_key) index below is the concurrent backstop).
        var replay = await _db.BatchSettlements.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.EnvironmentId == envId && s.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            return await GetAsync(replay.BatchId, cancellationToken);
        }

        var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
                    ?? throw new NotFoundException("batch", batchId);

        if (await _db.BatchSettlements.AnyAsync(s => s.BatchId == batchId, cancellationToken))
        {
            throw new ConflictException("batch_already_settled", "This batch already has a settlement.");
        }

        // Approved/paid figures are frozen (M9) — re-pricing underneath them is forbidden.
        if (await _db.DoctorEntitlements.AnyAsync(
                e => e.BatchId == batchId && e.Status != EntitlementStatus.Pending, cancellationToken))
        {
            throw new ConflictException("entitlement_frozen",
                "The doctor's entitlement for this batch is already approved/paid; the figures are frozen.");
        }

        var ledgerId = await _ownerLedgers.ResolveAsync(batch.CustomerId, batch.FarmId, cancellationToken)
                       ?? throw new ConflictException("ledger_missing", "The batch has no owner ledger to settle against.");
        var ledgerStatus = await _db.Ledgers.AsNoTracking()
            .Where(l => l.Id == ledgerId).Select(l => l.Status).FirstAsync(cancellationToken);
        if (ledgerStatus == LedgerStatus.Closed)
        {
            throw new ConflictException("owner_ledger_closed",
                "The owner account is closed. Re-open it before settling the batch.");
        }

        var (effectiveInvoices, productItems) = await EffectiveInvoices.LoadAsync(
            _db, i => i.BatchId == batchId, cancellationToken);
        var originalTotal = effectiveInvoices.Values.Sum(i => i.Total);

        if (effectiveInvoices.Count == 0 && (request.Lines.Count > 0 || request.DiscountAmount > 0m))
        {
            throw new ConflictException("settlement_no_basis",
                "The batch has no effective invoices — there is nothing to re-price or discount.");
        }

        // Per-product deltas against what was actually billed. A product the client sends that is
        // not on the batch's invoices means their preview drifted (e.g. a void landed in between).
        var byProduct = productItems.GroupBy(it => it.ProductId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var lines = new List<BatchSettlementLine>(request.Lines.Count);
        var repricingDelta = 0m;
        foreach (var input in request.Lines)
        {
            if (!byProduct.TryGetValue(input.ProductId, out var items))
            {
                throw new ConflictException("settlement_unknown_product",
                    $"Product '{input.ProductId}' has no effective invoice lines on this batch — reload the preview.");
            }

            var quantity = items.Sum(it => it.Quantity);
            var originalAmount = items.Sum(it => it.LineTotal);
            var delta = Money(items.Sum(it => (input.SettledUnitPrice - it.UnitPrice) * it.Quantity));
            repricingDelta += delta;

            lines.Add(new BatchSettlementLine
            {
                Id = Guid.Empty,
                ProductId = input.ProductId,
                SettledUnitPrice = Money(input.SettledUnitPrice),
                OriginalQuantity = quantity,
                OriginalAmount = originalAmount,
                Delta = delta,
            });
        }

        var discount = Money(request.DiscountAmount);
        var settlement = new BatchSettlement
        {
            Id = request.Id ?? Guid.Empty,
            BatchId = batchId,
            RepricingDelta = Money(repricingDelta),
            DiscountAmount = discount,
            OriginalTotal = originalTotal,
            SettledTotal = Money(originalTotal + repricingDelta - discount),
            Notes = request.Notes,
            SettledBy = userId,
            SettledAt = _clock.UtcNow,
            IdempotencyKey = request.IdempotencyKey,
        };

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.BatchSettlements.Add(settlement);
        try
        {
            await _db.SaveChangesAsync(cancellationToken); // assigns settlement.Id
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_batch_settlements_batch"))
        {
            throw new ConflictException("batch_already_settled", "This batch already has a settlement.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "ux_batch_settlements_env_idempotency"))
        {
            await tx.RollbackAsync(cancellationToken);
            return await GetAsync(batchId, cancellationToken);
        }

        foreach (var line in lines)
        {
            line.SettlementId = settlement.Id;
        }
        _db.BatchSettlementLines.AddRange(lines);

        // The money: up to two adjustment entries on the owner ledger (invariant #3 — the invoices
        // stay as issued; the SOA shows them plus these signed correction rows). Deterministic keys
        // derived from the settlement id make a mid-transaction retry collapse instead of double-post.
        if (settlement.RepricingDelta != 0m)
        {
            await _ledgers.AppendEntryAsync(
                new LedgerEntryRequest(
                    Id: null,
                    LedgerId: ledgerId,
                    EntryType: LedgerEntryType.Adjustment,
                    Amount: settlement.RepricingDelta,
                    InvoiceId: null,
                    ReceiptVoucherId: null,
                    Description: "تسوية أسعار الدورة",
                    IdempotencyKey: $"settle-reprice-{settlement.Id:N}"),
                cancellationToken);
        }

        if (discount > 0m)
        {
            await _ledgers.AppendEntryAsync(
                new LedgerEntryRequest(
                    Id: null,
                    LedgerId: ledgerId,
                    EntryType: LedgerEntryType.Adjustment,
                    Amount: -discount,
                    InvoiceId: null,
                    ReceiptVoucherId: null,
                    Description: "خصم تصفية الدورة",
                    IdempotencyKey: $"settle-discount-{settlement.Id:N}"),
                cancellationToken);
        }

        // Close the cycle and compute the doctor's pending entitlement on the settled numbers,
        // atomically with the settlement itself (mirrors BatchesService.UpdateAsync's close path).
        batch.Status = BatchStatus.Closed;
        await _db.SaveChangesAsync(cancellationToken);
        await _entitlements.ComputeForBatchAsync(batchId, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return ToResponse(settlement, lines);
    }

    private async Task<List<SettlementPreviewProduct>> BuildProductRowsAsync(
        Batch batch, List<InvoiceItem> productItems, CancellationToken cancellationToken)
    {
        if (productItems.Count == 0)
        {
            return [];
        }

        var productIds = productItems.Select(it => it.ProductId!.Value).Distinct().ToList();
        var names = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.NameAr, cancellationToken);

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var rows = new List<SettlementPreviewProduct>(productIds.Count);
        foreach (var group in productItems.GroupBy(it => it.ProductId!.Value).OrderBy(g => names.GetValueOrDefault(g.Key)))
        {
            var quantity = group.Sum(it => it.Quantity);
            var weighted = quantity == 0m ? 0m : Money(group.Sum(it => it.UnitPrice * it.Quantity) / quantity);

            // Display hint: the active contract override for this product today, if any.
            var resolved = await _pricing.ResolveUnitPriceAsync(group.Key, batch.CustomerId, today, cancellationToken);

            rows.Add(new SettlementPreviewProduct(
                group.Key,
                names.GetValueOrDefault(group.Key, string.Empty),
                quantity,
                group.Select(it => it.UnitPrice).Distinct().OrderBy(p => p).ToList(),
                weighted,
                resolved.IsContractPrice ? resolved.UnitPrice : null,
                group.Sum(it => it.LineTotal)));
        }

        return rows;
    }

    private static BatchSettlementResponse ToResponse(BatchSettlement settlement, IReadOnlyList<BatchSettlementLine> lines)
        => new(
            settlement.Id,
            settlement.BatchId,
            settlement.RepricingDelta,
            settlement.DiscountAmount,
            settlement.OriginalTotal,
            settlement.SettledTotal,
            settlement.Notes,
            settlement.SettledBy,
            settlement.SettledAt,
            lines.Select(l => new BatchSettlementLineResponse(
                l.ProductId, l.SettledUnitPrice, l.OriginalQuantity, l.OriginalAmount, l.Delta)).ToList());

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName)
        => ex.InnerException is Npgsql.PostgresException pg
           && pg.SqlState == "23505"
           && pg.ConstraintName == constraintName;

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
