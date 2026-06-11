using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/inventory_movements</c> (M4 task 10) — the offline write path for stock changes. PUT
/// accepts a client <b>intent</b> (movement type + product + location + a signed
/// <c>quantity_delta</c>) and delegates to <see cref="IInventoryService"/>, which translates it to
/// signed row(s) and recomputes <c>stock_items</c> server-side. The server is authoritative on the
/// sign: every type except <c>adjust</c> takes the delta's magnitude and applies the type's
/// canonical direction, so a buggy client cannot turn a sale into a credit. PATCH/DELETE are
/// rejected — movements are append-only (SCHEMA "Key invariants" #2/#3).
/// </summary>
public sealed class InventoryMovementsSyncHandler : ISyncTableHandler
{
    public const string TableName = "inventory_movements";

    private readonly IInventoryService _inventory;
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public InventoryMovementsSyncHandler(
        IInventoryService inventory,
        ApplicationDbContext db,
        ICurrentUserAccessor user)
    {
        _inventory = inventory;
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        if (_user.EnvironmentId is null || _user.UserId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var movementType = SyncBody.RequireString(body, "movement_type", MovementType.All, TableName);
        var productId = SyncBody.RequireGuid(body, "product_id");
        var rawDelta = SyncBody.RequireDecimal(body, "quantity_delta");

        var fromType = SyncBody.OptionalString(body, "from_location_type");
        var fromId = SyncBody.OptionalGuid(body, "from_location_id");
        var toType = SyncBody.OptionalString(body, "to_location_type");
        var toId = SyncBody.OptionalGuid(body, "to_location_id");

        decimal quantity;
        if (movementType == MovementType.Adjust)
        {
            // Adjust is inherently signed; the affected location may arrive in either slot. Collapse
            // it into 'to' (where the translator reads it) and keep the client's sign.
            (toType, toId) = toType is not null ? (toType, toId) : (fromType, fromId);
            fromType = null;
            fromId = null;
            quantity = rawDelta;
        }
        else
        {
            quantity = Math.Abs(rawDelta);
        }

        await EnforceFieldScopeAsync(fromType, fromId, toType, toId, cancellationToken);

        var intent = new MovementIntent(
            Id: id,
            MovementType: movementType,
            ProductId: productId,
            Quantity: quantity,
            FromLocationType: fromType,
            FromLocationId: fromId,
            ToLocationType: toType,
            ToLocationId: toId,
            IdempotencyKey: SyncBody.RequireString(body, "idempotency_key"),
            Reason: SyncBody.OptionalString(body, "reason"),
            VisitId: SyncBody.OptionalGuid(body, "visit_id"),
            InvoiceId: SyncBody.OptionalGuid(body, "invoice_id"),
            // M25 — cost + expiry ride along on a stock-arriving movement (receive / return_add /
            // positive adjust) so the device can seed a lot's FEFO basis; ignored by deductions/transfers.
            UnitCost: SyncBody.OptionalDecimal(body, "unit_cost"),
            ExpirationDate: SyncBody.OptionalDate(body, "expiration_date"),
            LotNumber: SyncBody.OptionalString(body, "lot_number"));

        var result = await _inventory.ApplyMovementAsync(intent, cancellationToken);
        return new SyncWriteResult(result.MovementId, DateTimeOffset.UtcNow);
    }

    public Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
        => throw AppendOnly();

    public Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
        => throw AppendOnly();

    private static ConflictException AppendOnly() => new(
        "inventory_movements_append_only",
        "inventory_movements are append-only. Post a compensating movement (e.g. return_add or "
        + "adjust) to correct stock — never update or delete an existing movement.");

    /// <summary>
    /// PRD §8.6 — a field doctor may only move stock in their own field inventory. Admin / inventory
    /// staff (who don't go through the sync path for transfers) are unrestricted here.
    /// </summary>
    private async Task EnforceFieldScopeAsync(
        string? fromType, Guid? fromId, string? toType, Guid? toId, CancellationToken cancellationToken)
    {
        if (_user.Role != RoleKey.VetField && _user.Role != RoleKey.VetBoth)
        {
            return;
        }

        var referencedFieldIds = new List<Guid>(2);
        if (fromType == StockLocation.Field && fromId is { } f) referencedFieldIds.Add(f);
        if (toType == StockLocation.Field && toId is { } t) referencedFieldIds.Add(t);
        if (referencedFieldIds.Count == 0)
        {
            return;
        }

        var ownFieldInventoryId = await _db.FieldInventories
            .Where(fi => fi.DoctorId == _user.UserId)
            .Select(fi => (Guid?)fi.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (referencedFieldIds.Any(fid => fid != ownFieldInventoryId))
        {
            throw new ForbiddenException("field_inventory_forbidden",
                "Field doctors may only move stock in their own field inventory.");
        }
    }
}
