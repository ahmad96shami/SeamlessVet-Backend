using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/prescriptions</c> (M5 task 19). Persists the device's prescription record as-is and
/// performs <b>no</b> inventory deduction or POS event. This is deliberate: in the offline field
/// flow the device already deducted its local stock and syncs that as a separate
/// <c>/sync/inventory_movements</c> delta — re-deducting here would double-count. The deduction /
/// POS-event side effects live only on the online <c>POST /prescriptions</c> path (M5 task 9/10).
/// </summary>
public sealed class PrescriptionsSyncHandler : ISyncTableHandler
{
    public const string TableName = "prescriptions";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public PrescriptionsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        if (await _db.Prescriptions.IgnoreQueryFilters().AnyAsync(p => p.Id == id, cancellationToken))
        {
            throw new ConflictException("prescription_already_exists", $"Prescription '{id}' already exists; use PATCH.");
        }

        var visitId = SyncBody.RequireGuid(body, "visit_id");
        await EnsureExistsAsync(_db.Visits.AnyAsync(v => v.Id == visitId, cancellationToken), "visit", visitId);

        var productId = SyncBody.RequireGuid(body, "product_id");
        await EnsureExistsAsync(_db.Products.AnyAsync(p => p.Id == productId, cancellationToken), "product", productId);

        var prescription = new Prescription
        {
            Id = id,
            VisitId = visitId,
            ProductId = productId,
            Dosage = SyncBody.OptionalString(body, "dosage"),
            Frequency = SyncBody.OptionalString(body, "frequency"),
            Duration = SyncBody.OptionalString(body, "duration"),
            Notes = SyncBody.OptionalString(body, "notes"),
            DispenseType = SyncBody.RequireString(body, "dispense_type", DispenseType.All, TableName),
            Quantity = SyncBody.OptionalDecimal(body, "quantity"),
        };

        _db.Prescriptions.Add(prescription);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(prescription.Id, prescription.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var prescription = await _db.Prescriptions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                           ?? throw new NotFoundException(TableName, id);

        // Advisory text only — product/quantity/dispense_type are immutable post-create (they carry
        // inventory/billing meaning that already flowed through separate sync writes).
        if (SyncBody.TryGetString(body, "dosage", out var dosage)) prescription.Dosage = dosage;
        if (SyncBody.TryGetString(body, "frequency", out var freq)) prescription.Frequency = freq;
        if (SyncBody.TryGetString(body, "duration", out var dur)) prescription.Duration = dur;
        if (SyncBody.TryGetString(body, "notes", out var notes)) prescription.Notes = notes;

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(prescription.Id, prescription.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var prescription = await _db.Prescriptions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                           ?? throw new NotFoundException(TableName, id);

        _db.Prescriptions.Remove(prescription);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(prescription.Id, prescription.UpdatedAt);
    }

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
}
