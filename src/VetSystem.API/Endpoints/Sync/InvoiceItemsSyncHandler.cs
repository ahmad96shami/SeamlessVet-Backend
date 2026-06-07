using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/invoice_items</c> (M7 tasks 11, 13) — append-only, server-wins. PUT persists a device's
/// line, validating the product-XOR-service target and that <c>line_total</c> reconciles to
/// <c>quantity × unit_price − discount_amount</c> (the offline-snapshotted <c>cost_price</c> is kept
/// as-is — SCHEMA invariant #8). PATCH/DELETE are rejected.
/// </summary>
public sealed class InvoiceItemsSyncHandler : ISyncTableHandler
{
    public const string TableName = "invoice_items";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public InvoiceItemsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        var existing = await _db.InvoiceItems.AsNoTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(it => it.Id == id, cancellationToken);
        if (existing is not null)
        {
            return new SyncWriteResult(existing.Id, existing.UpdatedAt); // idempotent: line already landed
        }

        var invoiceId = SyncBody.RequireGuid(body, "invoice_id");
        var invoiceBatchId = await _db.Invoices.AsNoTracking()
            .Where(i => i.Id == invoiceId)
            .Select(i => new { i.BatchId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("invoice", invoiceId);

        // M24 invariant #11 — a line landing after the batch's تصفية could never be priced into the
        // settlement (its snapshots/adjustments are already posted), so it is rejected, not silently
        // skewed. Covers the partial-queue window where the header synced before settlement.
        if (invoiceBatchId.BatchId is { } itemBatchId
            && await _db.BatchSettlements.AsNoTracking().AnyAsync(s => s.BatchId == itemBatchId, cancellationToken))
        {
            throw new ConflictException("batch_settled",
                "The batch has been settled (تصفية) — its invoices are frozen. Correct via a manual ledger adjustment.");
        }

        var productId = SyncBody.OptionalGuid(body, "product_id");
        var serviceId = SyncBody.OptionalGuid(body, "service_id");
        if ((productId is null) == (serviceId is null))
        {
            throw new ConflictException("invalid_item_target",
                "An invoice line must reference exactly one of product_id or service_id.");
        }

        if (productId is { } pid)
        {
            await EnsureExistsAsync(_db.Products.AnyAsync(p => p.Id == pid, cancellationToken), "product", pid);
        }
        if (serviceId is { } sid)
        {
            await EnsureExistsAsync(_db.Services.AnyAsync(s => s.Id == sid, cancellationToken), "service", sid);
        }

        var quantity = SyncBody.RequireDecimal(body, "quantity");
        var unitPrice = SyncBody.RequireDecimal(body, "unit_price");
        var discount = SyncBody.OptionalDecimal(body, "discount_amount") ?? 0m;
        var lineTotal = SyncBody.RequireDecimal(body, "line_total");
        if (Round(quantity * unitPrice - discount) != Round(lineTotal))
        {
            throw new ConflictException("line_total_mismatch",
                "line_total must equal quantity × unit_price − discount_amount.");
        }

        var item = new InvoiceItem
        {
            Id = id,
            InvoiceId = invoiceId,
            ProductId = productId,
            ServiceId = serviceId,
            Description = SyncBody.OptionalString(body, "description"),
            Quantity = quantity,
            UnitPrice = unitPrice,
            CostPrice = SyncBody.OptionalDecimal(body, "cost_price") ?? 0m,
            DiscountAmount = discount,
            LineTotal = lineTotal,
            PrescriptionId = SyncBody.OptionalGuid(body, "prescription_id"),
            ProcedureId = SyncBody.OptionalGuid(body, "procedure_id"),
        };

        _db.InvoiceItems.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(item.Id, item.UpdatedAt);
    }

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw AppendOnly();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw AppendOnly();

    private static ConflictException AppendOnly() => new(
        "invoice_items_append_only", "invoice_items are append-only; void the invoice to reverse a sale.");

    private void RequireAuthenticated()
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static async Task EnsureExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
