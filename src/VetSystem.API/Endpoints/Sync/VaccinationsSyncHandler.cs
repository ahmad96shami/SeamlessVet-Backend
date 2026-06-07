using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Endpoints.Sync;

/// <summary>
/// <c>/sync/vaccinations</c> (M5 task 19). Targets a pet or a farm-group customer (at least one);
/// persists the device record, validating referenced rows exist in the actor's environment.
/// M22: accepts the catalog link (<c>service_id</c>, billable) + <c>price</c> snapshot; billed
/// vaccinations are frozen like billed procedures (BilledChargeGuard).
/// </summary>
public sealed class VaccinationsSyncHandler : ISyncTableHandler
{
    public const string TableName = "vaccinations";

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _user;

    public VaccinationsSyncHandler(ApplicationDbContext db, ICurrentUserAccessor user)
    {
        _db = db;
        _user = user;
    }

    public string Table => TableName;

    public async Task<SyncWriteResult> PutAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();

        if (await _db.Vaccinations.IgnoreQueryFilters().AnyAsync(v => v.Id == id, cancellationToken))
        {
            throw new ConflictException("vaccination_already_exists", $"Vaccination '{id}' already exists; use PATCH.");
        }

        var petId = SyncBody.OptionalGuid(body, "pet_id");
        var customerId = SyncBody.OptionalGuid(body, "customer_id");
        if (petId is null && customerId is null)
        {
            throw new ConflictException("vaccination_recipient_required",
                "A vaccination must target a pet or a customer (farm group).");
        }

        if (petId is { } pid)
        {
            await EnsureExistsAsync(_db.Pets.AnyAsync(p => p.Id == pid, cancellationToken), "pet", pid);
        }
        if (customerId is { } cid)
        {
            await EnsureExistsAsync(_db.Customers.AnyAsync(c => c.Id == cid, cancellationToken), "customer", cid);
        }

        var visitId = SyncBody.OptionalGuid(body, "visit_id");
        if (visitId is { } vid)
        {
            await EnsureExistsAsync(_db.Visits.AnyAsync(v => v.Id == vid, cancellationToken), "visit", vid);
        }

        var dateGiven = SyncBody.OptionalDate(body, "date_given")
                        ?? throw new ConflictException("invalid_payload", "'date_given' is required and must be a date.");
        var nextDueDate = SyncBody.OptionalDate(body, "next_due_date");
        if (nextDueDate is { } due && due < dateGiven)
        {
            throw new ConflictException("invalid_next_due_date", "next_due_date must be on or after date_given.");
        }

        // M22 — optional catalog link; price defaults to the catalog price when omitted.
        var serviceId = SyncBody.OptionalGuid(body, "service_id");
        var price = SyncBody.OptionalDecimal(body, "price");
        if (serviceId is { } svc)
        {
            var defaultPrice = await _db.Services.AsNoTracking()
                                   .Where(s => s.Id == svc).Select(s => (decimal?)s.DefaultPrice)
                                   .FirstOrDefaultAsync(cancellationToken)
                               ?? throw new NotFoundException("service", svc);
            price ??= defaultPrice;
        }

        var vaccination = new Vaccination
        {
            Id = id,
            PetId = petId,
            CustomerId = customerId,
            VisitId = visitId,
            ServiceId = serviceId,
            VaccineType = SyncBody.RequireString(body, "vaccine_type"),
            Price = price,
            DateGiven = dateGiven,
            NextDueDate = nextDueDate,
            CertificateUrl = SyncBody.OptionalString(body, "certificate_url"),
        };

        _db.Vaccinations.Add(vaccination);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(vaccination.Id, vaccination.UpdatedAt);
    }

    public async Task<SyncWriteResult> PatchAsync(Guid id, JsonElement body, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var vaccination = await _db.Vaccinations.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                          ?? throw new NotFoundException(TableName, id);

        // Mirror the REST rule (BilledChargeGuard): once an invoice line bills this vaccination,
        // its money/identity fields are frozen — change-detected so date/certificate patches apply.
        var incomingServiceId = body.TryGetProperty("service_id", out _)
            ? SyncBody.OptionalGuid(body, "service_id")
            : vaccination.ServiceId;
        var incomingPrice = body.TryGetProperty("price", out _)
            ? SyncBody.OptionalDecimal(body, "price")
            : vaccination.Price;
        if (incomingServiceId != vaccination.ServiceId || incomingPrice != vaccination.Price)
        {
            await BilledChargeGuard.EnsureVaccinationNotBilledAsync(_db, id, cancellationToken);
        }

        if (body.TryGetProperty("service_id", out _))
        {
            var serviceId = SyncBody.OptionalGuid(body, "service_id");
            if (serviceId is { } sid)
            {
                await EnsureExistsAsync(_db.Services.AnyAsync(s => s.Id == sid, cancellationToken), "service", sid);
            }
            vaccination.ServiceId = serviceId;
        }
        if (body.TryGetProperty("price", out _)) vaccination.Price = SyncBody.OptionalDecimal(body, "price");

        if (SyncBody.TryGetString(body, "vaccine_type", out var vt) && vt is not null) vaccination.VaccineType = vt;
        if (body.TryGetProperty("date_given", out _))
        {
            vaccination.DateGiven = SyncBody.OptionalDate(body, "date_given")
                                    ?? throw new ConflictException("invalid_payload", "'date_given' must be a date.");
        }
        if (body.TryGetProperty("next_due_date", out _)) vaccination.NextDueDate = SyncBody.OptionalDate(body, "next_due_date");
        if (SyncBody.TryGetString(body, "certificate_url", out var cu)) vaccination.CertificateUrl = cu;

        if (vaccination.NextDueDate is { } due && due < vaccination.DateGiven)
        {
            throw new ConflictException("invalid_next_due_date", "next_due_date must be on or after date_given.");
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(vaccination.Id, vaccination.UpdatedAt);
    }

    public async Task<SyncWriteResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireAuthenticated();
        var vaccination = await _db.Vaccinations.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                          ?? throw new NotFoundException(TableName, id);

        // Mirror the REST rule (BilledChargeGuard): a billed vaccination backs an invoice line.
        await BilledChargeGuard.EnsureVaccinationNotBilledAsync(_db, id, cancellationToken);

        _db.Vaccinations.Remove(vaccination);
        await _db.SaveChangesAsync(cancellationToken);
        return new SyncWriteResult(vaccination.Id, vaccination.UpdatedAt);
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
