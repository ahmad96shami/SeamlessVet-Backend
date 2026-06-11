using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Application.Vaccinations.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Vaccinations;

/// <summary>
/// Vaccination CRUD (PRD §5.2, §6.7, M5 task 12). Targets a single pet or a farm group (customer);
/// <c>NextDueDate</c> is what the M11 reminder job scans. Existence of any referenced pet/customer/
/// visit is validated within the caller's environment via the global query filter.
/// <para>
/// M26 — a catalog-linked vaccination (<c>ProductId</c> set, a stock product of category
/// <c>vaccine</c>) is <b>administered</b> on create: an atomic <c>sale_deduct</c> via
/// <see cref="IInventoryService"/> moves stock FEFO from the clinic warehouse (or the field doctor's
/// inventory for a field visit) and the lot's weighted-average cost is captured onto
/// <see cref="Vaccination.ResolvedUnitCost"/>. The issuance assembler then bills it as a product line
/// without re-deducting (the stock — and COGS — already moved; mirrors a billable in-clinic med).
/// Free-text rows (<c>ProductId</c> null) are clinical records only — no stock, no bill.
/// </para>
/// </summary>
public sealed class VaccinationsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IClock _clock;
    private readonly IInventoryService _inventory;

    public VaccinationsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IClock clock,
        IInventoryService inventory)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _clock = clock;
        _inventory = inventory;
    }

    public async Task<IReadOnlyList<VaccinationResponse>> ListAsync(
        Guid? petId, Guid? customerId, Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.Vaccinations.AsNoTracking();
        if (petId is { } pid) query = query.Where(v => v.PetId == pid);
        if (customerId is { } cid) query = query.Where(v => v.CustomerId == cid);
        if (visitId is { } vid) query = query.Where(v => v.VisitId == vid);

        var rows = await query
            .OrderByDescending(v => v.DateGiven)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<VaccinationResponse>).ToList();
    }

    /// <summary>
    /// M18 task 6 — the upcoming-vaccination calendar query (PRD §6.7). Lists vaccinations whose
    /// <c>next_due_date</c> falls in the requested range, soonest first. <paramref name="from"/> defaults
    /// to today (genuinely "upcoming"); pass an explicit range to drive a calendar view of any window.
    /// Environment-scoped via the global query filter and auth-only — a field doctor can see the schedule
    /// without the admin-gated <c>/reports/upcoming-vaccinations</c>.
    /// </summary>
    public async Task<IReadOnlyList<VaccinationResponse>> ListUpcomingAsync(
        DateOnly? from, DateOnly? to, Guid? petId, Guid? customerId, int? skip, int? take,
        CancellationToken cancellationToken)
    {
        var fromDate = from ?? DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        if (to is { } upper && upper < fromDate)
        {
            throw new ConflictException("invalid_period", "'to' must be on or after 'from'.");
        }

        var query = _db.Vaccinations.AsNoTracking().Where(v => v.NextDueDate != null && v.NextDueDate >= fromDate);
        if (to is { } t) query = query.Where(v => v.NextDueDate <= t);
        if (petId is { } pid) query = query.Where(v => v.PetId == pid);
        if (customerId is { } cid) query = query.Where(v => v.CustomerId == cid);

        var rows = await query
            .OrderBy(v => v.NextDueDate)
            .ThenBy(v => v.Id)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<VaccinationResponse>).ToList();
    }

    public async Task<VaccinationResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await _db.Vaccinations.AsNoTracking().FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                  ?? throw new NotFoundException("vaccination", id);
        return _mapper.Map<VaccinationResponse>(row);
    }

    public async Task<VaccinationResponse> CreateAsync(
        VaccinationCreateRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.PetId is { } petId)
        {
            await RequireExistsAsync(_db.Pets.AnyAsync(p => p.Id == petId, cancellationToken), "pet", petId);
        }

        if (request.CustomerId is { } customerId)
        {
            await RequireExistsAsync(
                _db.Customers.AnyAsync(c => c.Id == customerId, cancellationToken), "customer", customerId);
        }

        Visit? visit = null;
        if (request.VisitId is { } visitId)
        {
            visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == visitId, cancellationToken)
                    ?? throw new NotFoundException("visit", visitId);
        }

        // M26 — catalog-linked (billable) vaccination: the catalog vaccine is a stock product
        // (category vaccine). Price snapshots at recording time, defaulting to the product's selling
        // price (mirrors ProceduresService.CreateAsync). Existence is checked through the env-scoped
        // query filter even when the request carries its own price.
        Product? vaccineProduct = null;
        if (request.ProductId is { } productId)
        {
            vaccineProduct = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
                             ?? throw new NotFoundException("product", productId);
            if (vaccineProduct.Category != ProductCategory.Vaccine)
            {
                throw new ConflictException("product_not_vaccine",
                    "A vaccination must link a product of category 'vaccine'.");
            }
        }

        var price = request.Price ?? vaccineProduct?.SellingPrice;

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Vaccinations.IgnoreQueryFilters().AnyAsync(v => v.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("vaccination_id_collision",
                    $"A vaccination with id '{id}' already exists.");
            }
        }

        var vaccination = new Vaccination
        {
            Id = request.Id ?? Guid.Empty,
            PetId = request.PetId,
            CustomerId = request.CustomerId,
            VisitId = request.VisitId,
            ProductId = request.ProductId,
            VaccineType = request.VaccineType,
            Price = price,
            DateGiven = request.DateGiven,
            NextDueDate = request.NextDueDate,
            CertificateUrl = request.CertificateUrl,
        };

        if (vaccineProduct is not null)
        {
            await AdministerAsync(vaccination, visit, cancellationToken);
        }
        else
        {
            _db.Vaccinations.Add(vaccination);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return _mapper.Map<VaccinationResponse>(vaccination);
    }

    /// <summary>
    /// M26 — persists the vaccination and its <c>sale_deduct</c> movement in one transaction (mirrors
    /// <c>PrescriptionsService.AdministerAsync</c>). The movement's idempotency key is derived from the
    /// vaccination id, so a retried apply (or the HTTP-level idempotency replay) never double-deducts;
    /// the consumed lots' FEFO weighted-average cost is captured for the issuance assembler.
    /// </summary>
    private async Task AdministerAsync(Vaccination vaccination, Visit? visit, CancellationToken cancellationToken)
    {
        var (locationType, locationId) = await ResolveDeductionLocationAsync(visit, cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Vaccinations.Add(vaccination);
        await _db.SaveChangesAsync(cancellationToken); // assigns vaccination.Id for the movement key below

        var movement = await _inventory.ApplyMovementAsync(
            new MovementIntent(
                Id: null,
                MovementType: MovementType.SaleDeduct,
                ProductId: vaccination.ProductId!.Value,
                Quantity: 1m, // a vaccination administers a single dose
                FromLocationType: locationType,
                FromLocationId: locationId,
                ToLocationType: null,
                ToLocationId: null,
                IdempotencyKey: $"vax-{vaccination.Id}",
                Reason: "vaccination administered",
                VisitId: vaccination.VisitId),
            cancellationToken);

        // Capture the FEFO cost of the deducted lot so the issuance assembler snapshots it as COGS
        // (stock has already moved, so issuance won't re-resolve it).
        vaccination.ResolvedUnitCost = Money(movement.ResolvedUnitCost);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<VaccinationResponse> UpdateAsync(
        Guid id, VaccinationPatchRequest request, CancellationToken cancellationToken)
    {
        var vaccination = await _db.Vaccinations.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                          ?? throw new NotFoundException("vaccination", id);

        // M26 — the catalog link drives an inventory deduction at recording; re-pointing it would
        // desync the stock already moved. Block it (mirrors BilledChargeGuard's identity freeze) and
        // require a delete + re-record instead. Only metadata patches (date/certificate/next-due) flow.
        if (request.ProductId.HasValue && request.ProductId != vaccination.ProductId)
        {
            throw new ConflictException("vaccination_product_immutable",
                "A vaccination's catalog vaccine cannot be changed after recording (stock already moved); delete and re-record.");
        }

        // Once an invoice line bills this vaccination, its price is frozen too (change-detected so
        // date/certificate-only patches still apply).
        if (request.Price.HasValue && request.Price != vaccination.Price)
        {
            await BilledChargeGuard.EnsureVaccinationNotBilledAsync(_db, id, cancellationToken);
            vaccination.Price = request.Price;
        }

        if (request.VaccineType is not null) vaccination.VaccineType = request.VaccineType;
        if (request.DateGiven.HasValue) vaccination.DateGiven = request.DateGiven.Value;
        if (request.NextDueDate.HasValue) vaccination.NextDueDate = request.NextDueDate;
        if (request.CertificateUrl is not null) vaccination.CertificateUrl = request.CertificateUrl;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<VaccinationResponse>(vaccination);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var vaccination = await _db.Vaccinations.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                          ?? throw new NotFoundException("vaccination", id);

        // M22 — a billed vaccination backs an invoice line (BilledChargeGuard). Inventory is
        // append-only: a deduction already applied for an administered vaccine is not auto-reversed —
        // post a return_add movement to correct stock.
        await BilledChargeGuard.EnsureVaccinationNotBilledAsync(_db, id, cancellationToken);

        _db.Vaccinations.Remove(vaccination);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// M26 — where an administered vaccine's stock is deducted: the field doctor's inventory for a
    /// field visit, else the central warehouse (an in-clinic visit or a stand-alone recording).
    /// </summary>
    private async Task<(string LocationType, Guid LocationId)> ResolveDeductionLocationAsync(
        Visit? visit, CancellationToken cancellationToken)
    {
        if (visit is { VisitType: VisitType.Field })
        {
            var fieldId = await _db.FieldInventories
                .Where(fi => fi.DoctorId == visit.DoctorId)
                .Select(fi => (Guid?)fi.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (fieldId is not { } fid)
            {
                throw new ConflictException("no_field_inventory",
                    "The visit's field doctor has no field inventory to deduct the administered vaccine from.");
            }

            return (StockLocation.Field, fid);
        }

        var warehouseId = await _db.Warehouses
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouseId is not { } wid)
        {
            throw new ConflictException("no_warehouse",
                "No warehouse exists in this environment to deduct the administered vaccine from.");
        }

        return (StockLocation.Warehouse, wid);
    }

    private static async Task RequireExistsAsync(Task<bool> existsQuery, string entity, Guid id)
    {
        if (!await existsQuery)
        {
            throw new NotFoundException(entity, id);
        }
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
