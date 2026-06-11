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
/// M26: accepts the catalog link (<c>product_id</c> — a stock product of category <c>vaccine</c>,
/// billable) + <c>price</c> snapshot; billed vaccinations are frozen like billed procedures
/// (BilledChargeGuard). This write path performs <b>no</b> inventory deduction: in the offline field
/// flow the device deducts its local stock and syncs that as a separate <c>/sync/inventory_movements</c>
/// delta (mirrors <c>PrescriptionsSyncHandler</c>) — re-deducting here would double-count.
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

        // M26 — optional catalog link; the vaccine is a stock product (category vaccine), price
        // defaults to its selling price when omitted.
        var productId = SyncBody.OptionalGuid(body, "product_id");
        var price = SyncBody.OptionalDecimal(body, "price");
        if (productId is { } prod)
        {
            var product = await _db.Products.AsNoTracking()
                              .Where(p => p.Id == prod).Select(p => new { p.Category, p.SellingPrice })
                              .FirstOrDefaultAsync(cancellationToken)
                          ?? throw new NotFoundException("product", prod);
            if (product.Category != ProductCategory.Vaccine)
            {
                throw new ConflictException("product_not_vaccine",
                    "A vaccination must link a product of category 'vaccine'.");
            }
            price ??= product.SellingPrice;
        }

        var vaccination = new Vaccination
        {
            Id = id,
            PetId = petId,
            CustomerId = customerId,
            VisitId = visitId,
            ProductId = productId,
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
        var incomingProductId = body.TryGetProperty("product_id", out _)
            ? SyncBody.OptionalGuid(body, "product_id")
            : vaccination.ProductId;
        var incomingPrice = body.TryGetProperty("price", out _)
            ? SyncBody.OptionalDecimal(body, "price")
            : vaccination.Price;
        if (incomingProductId != vaccination.ProductId || incomingPrice != vaccination.Price)
        {
            await BilledChargeGuard.EnsureVaccinationNotBilledAsync(_db, id, cancellationToken);
        }

        if (body.TryGetProperty("product_id", out _))
        {
            var productId = SyncBody.OptionalGuid(body, "product_id");
            if (productId is { } pid)
            {
                var category = await _db.Products.AsNoTracking()
                    .Where(p => p.Id == pid).Select(p => (string?)p.Category)
                    .FirstOrDefaultAsync(cancellationToken)
                    ?? throw new NotFoundException("product", pid);
                if (category != ProductCategory.Vaccine)
                {
                    throw new ConflictException("product_not_vaccine",
                        "A vaccination must link a product of category 'vaccine'.");
                }
            }
            vaccination.ProductId = productId;
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
