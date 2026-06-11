using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.API.Financial;
using VetSystem.Application.Common;
using VetSystem.Application.Inventory;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Application.Prescriptions.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Domain.Events;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Prescriptions;

/// <summary>
/// Prescription CRUD + fulfilment wiring (PRD §5.2-D, M5 tasks 8–10). On create the
/// <c>dispense_type</c> decides what happens beyond the row:
/// <list type="bullet">
/// <item><c>administered_in_clinic</c> → an atomic <c>sale_deduct</c> via <see cref="IInventoryService"/>
/// from the clinic warehouse (in-clinic visit) or the doctor's field inventory (field visit). The
/// prescription insert and the inventory movement commit together in one transaction, so a
/// negative-stock rejection rolls the prescription back too.</item>
/// <item><c>dispensed_to_owner</c> → a <see cref="PrescriptionDispensedEvent"/> for M7 to bill on the
/// visit's POS invoice; no inventory move here.</item>
/// </list>
/// </summary>
public sealed class PrescriptionsService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;
    private readonly IInventoryService _inventory;
    private readonly IDomainEventPublisher _events;

    public PrescriptionsService(
        ApplicationDbContext db,
        ICurrentUserAccessor currentUser,
        IMapper mapper,
        IInventoryService inventory,
        IDomainEventPublisher events)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
        _inventory = inventory;
        _events = events;
    }

    public async Task<IReadOnlyList<PrescriptionResponse>> ListAsync(
        Guid? visitId, int? skip, int? take, CancellationToken cancellationToken)
    {
        var query = _db.Prescriptions.AsNoTracking();
        if (visitId is { } vid) query = query.Where(p => p.VisitId == vid);

        var rows = await query
            .OrderBy(p => p.CreatedAt)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<PrescriptionResponse>).ToList();
    }

    public async Task<PrescriptionResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var rx = await _db.Prescriptions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                 ?? throw new NotFoundException("prescription", id);
        return _mapper.Map<PrescriptionResponse>(rx);
    }

    public async Task<PrescriptionResponse> CreateAsync(
        PrescriptionCreateRequest request, CancellationToken cancellationToken)
    {
        if (_currentUser.EnvironmentId is not { } envId)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }

        var visit = await _db.Visits.FirstOrDefaultAsync(v => v.Id == request.VisitId, cancellationToken)
                    ?? throw new NotFoundException("visit", request.VisitId);

        if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, cancellationToken))
        {
            throw new NotFoundException("product", request.ProductId);
        }

        if (request.Id is { } reqId && reqId != Guid.Empty)
        {
            var collision = await _db.Prescriptions.IgnoreQueryFilters().AnyAsync(p => p.Id == reqId, cancellationToken);
            if (collision)
            {
                throw new ConflictException("prescription_id_collision",
                    $"A prescription with id '{reqId}' already exists.");
            }
        }

        var quantity = request.Quantity!.Value; // required + positive (validator)
        var rx = new Prescription
        {
            Id = request.Id ?? Guid.Empty,
            VisitId = request.VisitId,
            ProductId = request.ProductId,
            Dosage = request.Dosage,
            Frequency = request.Frequency,
            Duration = request.Duration,
            Notes = request.Notes,
            DispenseType = request.DispenseType,
            Quantity = quantity,
            // M23 — only meaningful for administered_in_clinic; dispensed_to_owner always bills.
            Billable = request.DispenseType == DispenseType.AdministeredInClinic && request.Billable,
            ReminderEnabled = request.ReminderEnabled,
            IntervalMinutes = request.IntervalMinutes,
            LeadMinutes = request.LeadMinutes,
            StartAt = request.StartAt,
            EndAt = request.EndAt,
            DosesCount = request.DosesCount,
        };

        if (request.DispenseType == DispenseType.AdministeredInClinic)
        {
            await AdministerAsync(rx, visit, quantity, cancellationToken);
        }
        else
        {
            _db.Prescriptions.Add(rx);
            await _db.SaveChangesAsync(cancellationToken);

            await _events.PublishAsync(
                new PrescriptionDispensedEvent(
                    envId, rx.Id, visit.Id, visit.CustomerId, rx.ProductId, quantity, visit.DoctorId),
                cancellationToken);
        }

        return _mapper.Map<PrescriptionResponse>(rx);
    }

    /// <summary>
    /// Persists the prescription and its <c>sale_deduct</c> movement in one transaction. The movement's
    /// idempotency key is derived from the prescription id, so a retried apply (or the HTTP-level
    /// idempotency replay) never double-deducts.
    /// </summary>
    private async Task AdministerAsync(Prescription rx, Visit visit, decimal quantity, CancellationToken cancellationToken)
    {
        var (locationType, locationId) = await ResolveDeductionLocationAsync(visit, cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Prescriptions.Add(rx);
        await _db.SaveChangesAsync(cancellationToken); // assigns rx.Id for the movement key below

        var movement = await _inventory.ApplyMovementAsync(
            new MovementIntent(
                Id: null,
                MovementType: MovementType.SaleDeduct,
                ProductId: rx.ProductId,
                Quantity: quantity,
                FromLocationType: locationType,
                FromLocationId: locationId,
                ToLocationType: null,
                ToLocationId: null,
                IdempotencyKey: $"rx-{rx.Id}",
                Reason: "administered_in_clinic",
                VisitId: visit.Id),
            cancellationToken);

        // M25 — capture the FEFO cost of the deducted lots so a billable in-clinic med (M23) snapshots
        // it as COGS at issuance (stock has already moved, so issuance won't re-resolve it).
        rx.ResolvedUnitCost = Money(movement.ResolvedUnitCost);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<PrescriptionResponse> UpdateAsync(
        Guid id, PrescriptionPatchRequest request, CancellationToken cancellationToken)
    {
        var rx = await _db.Prescriptions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                 ?? throw new NotFoundException("prescription", id);

        if (request.Dosage is not null) rx.Dosage = request.Dosage;
        if (request.Frequency is not null) rx.Frequency = request.Frequency;
        if (request.Duration is not null) rx.Duration = request.Duration;
        if (request.Notes is not null) rx.Notes = request.Notes;

        // M23 — the in-clinic billable toggle is free until an invoice line bills the row
        // (then flipping it would disagree with the issued, append-only invoice).
        if (request.Billable is { } billable && billable != rx.Billable)
        {
            if (rx.DispenseType != DispenseType.AdministeredInClinic)
            {
                throw new ConflictException("billable_in_clinic_only",
                    "Only administered_in_clinic prescriptions carry the billable toggle.");
            }

            await BilledChargeGuard.EnsurePrescriptionNotBilledAsync(_db, id, cancellationToken);
            rx.Billable = billable;
        }

        // M18 reminder schedule — toggle/retune. Dose numbering anchors (start/interval) can change too;
        // the job's high-water mark only advances, so a mid-course edit never re-fires a past dose.
        if (request.ReminderEnabled.HasValue) rx.ReminderEnabled = request.ReminderEnabled.Value;
        if (request.IntervalMinutes.HasValue) rx.IntervalMinutes = request.IntervalMinutes;
        if (request.LeadMinutes.HasValue) rx.LeadMinutes = request.LeadMinutes;
        if (request.StartAt.HasValue) rx.StartAt = request.StartAt;
        if (request.EndAt.HasValue) rx.EndAt = request.EndAt;
        if (request.DosesCount.HasValue) rx.DosesCount = request.DosesCount;

        await _db.SaveChangesAsync(cancellationToken);
        return _mapper.Map<PrescriptionResponse>(rx);
    }

    /// <summary>
    /// Soft-deletes the prescription record — refused once an invoice line bills it
    /// (<see cref="BilledChargeGuard"/>): the clinical row backs the issued invoice. Inventory is
    /// append-only: a deduction already applied for an <c>administered_in_clinic</c> item is not
    /// auto-reversed — post a <c>return_add</c> movement to correct stock.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var rx = await _db.Prescriptions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                 ?? throw new NotFoundException("prescription", id);

        await BilledChargeGuard.EnsurePrescriptionNotBilledAsync(_db, id, cancellationToken);

        _db.Prescriptions.Remove(rx);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string LocationType, Guid LocationId)> ResolveDeductionLocationAsync(
        Visit visit, CancellationToken cancellationToken)
    {
        if (visit.VisitType == VisitType.Field)
        {
            var fieldId = await _db.FieldInventories
                .Where(fi => fi.DoctorId == visit.DoctorId)
                .Select(fi => (Guid?)fi.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (fieldId is not { } fid)
            {
                throw new ConflictException("no_field_inventory",
                    "The visit's field doctor has no field inventory to deduct administered medication from.");
            }

            return (StockLocation.Field, fid);
        }

        var warehouseId = await _db.Warehouses
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (warehouseId is not { } wid)
        {
            throw new ConflictException("no_warehouse",
                "No warehouse exists in this environment to deduct administered medication from.");
        }

        return (StockLocation.Warehouse, wid);
    }

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
