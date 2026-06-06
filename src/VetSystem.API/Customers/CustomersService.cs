using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Customers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Customers;

/// <summary>
/// Operational customer CRUD (PRD §5.1 / SCHEMA §2). Reads + writes are env-scoped via the
/// global EF query filter; <see cref="CreateAsync"/> also seeds the matching <see cref="Ledger"/>
/// in the same transaction so downstream POS / receipt-voucher flows always have one to post into
/// (M3 task 5).
/// </summary>
public sealed class CustomersService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public CustomersService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<CustomerResponse>> ListAsync(
        string? search,
        string? type,
        Guid? assignedDoctorId,
        string? ledgerStatus,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (type is not null && !CustomerType.All.Contains(type))
        {
            throw new ConflictException("invalid_customer_type", $"type '{type}' is not valid.");
        }

        if (ledgerStatus is not null && !LedgerStatus.All.Contains(ledgerStatus))
        {
            throw new ConflictException("invalid_ledger_status", $"ledgerStatus '{ledgerStatus}' is not valid.");
        }

        var customers = _db.Customers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            customers = customers.Where(c =>
                EF.Functions.ILike(c.FullName, pattern) ||
                (c.PhonePrimary != null && EF.Functions.ILike(c.PhonePrimary, pattern)) ||
                (c.IdNumber != null && EF.Functions.ILike(c.IdNumber, pattern)));
        }

        if (type is not null)
        {
            customers = customers.Where(c => c.Type == type);
        }

        if (assignedDoctorId is { } doctorId)
        {
            customers = customers.Where(c => c.AssignedDoctorId == doctorId);
        }

        // M16 — filter by the customer's settled rollup (must match Aggregate() above). Aggregate
        // balance = Σ over the own ledger (l.CustomerId == c.Id) + every farm ledger whose farm
        // belongs to c.
        //   has_debt = Σ owned balances > 0;
        //   closed   = no debt AND the OWN ledger is closed (a zero-balance open farm doesn't matter);
        //   open     = no debt AND the own ledger is not closed.
        if (ledgerStatus is { } status)
        {
            customers = status switch
            {
                LedgerStatus.HasDebt => customers.Where(c =>
                    _db.Ledgers
                        .Where(l => l.CustomerId == c.Id || _db.Farms.Any(f => f.Id == l.FarmId && f.CustomerId == c.Id))
                        .Sum(l => l.Balance) > 0m),
                LedgerStatus.Closed => customers.Where(c =>
                    _db.Ledgers.Any(l => l.CustomerId == c.Id && l.Status == LedgerStatus.Closed)
                    && _db.Ledgers
                        .Where(l => l.CustomerId == c.Id || _db.Farms.Any(f => f.Id == l.FarmId && f.CustomerId == c.Id))
                        .Sum(l => l.Balance) <= 0m),
                _ => customers.Where(c =>
                    !_db.Ledgers.Any(l => l.CustomerId == c.Id && l.Status == LedgerStatus.Closed)
                    && _db.Ledgers
                        .Where(l => l.CustomerId == c.Id || _db.Farms.Any(f => f.Id == l.FarmId && f.CustomerId == c.Id))
                        .Sum(l => l.Balance) <= 0m),
            };
        }

        var page = await customers
            .OrderBy(c => c.FullName)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return await EnrichAsync(page, cancellationToken);
    }

    public async Task<CustomerResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("customer", id);

        var own = await _db.Ledgers.AsNoTracking()
            .Where(l => l.CustomerId == id)
            .Select(l => new OwnerLedger(l.Balance, l.Status))
            .FirstOrDefaultAsync(cancellationToken);

        // Per-farm breakdown (detail only): each active farm's ledger, owner-joined.
        var farmLedgers = (await _db.Ledgers.AsNoTracking()
                .Where(l => l.FarmId != null)
                .Join(
                    _db.Farms.AsNoTracking().Where(f => f.CustomerId == id),
                    l => l.FarmId, f => f.Id,
                    (l, f) => new { FarmId = f.Id, FarmName = f.Name, LedgerId = l.Id, l.Balance, l.Status, l.ClosedAt })
                .ToListAsync(cancellationToken))
            .Select(x => new CustomerFarmLedger(x.FarmId, x.FarmName, x.LedgerId, x.Balance, x.Status, x.ClosedAt))
            .OrderBy(fl => fl.FarmName)
            .ToList();

        var ownBalance = own?.Balance ?? 0m;
        var (aggregate, aggregateStatus) = Aggregate(
            ownBalance, own?.Status, farmLedgers.Select(f => new OwnerLedger(f.Balance, f.Status)).ToList());

        return ToResponse(customer, aggregate, aggregateStatus, ownBalance, farmLedgers, own?.Status);
    }

    /// <summary>
    /// Loads each customer's aggregate ledger state (own ledger + all its farm ledgers) for the page
    /// in two queries (own ledgers, then farm ledgers owner-joined), then folds them per customer.
    /// </summary>
    private async Task<IReadOnlyList<CustomerResponse>> EnrichAsync(
        IReadOnlyList<Customer> customers,
        CancellationToken cancellationToken)
    {
        if (customers.Count == 0)
        {
            return [];
        }

        var ids = customers.Select(c => c.Id).ToList();

        var ownByCustomer = await _db.Ledgers.AsNoTracking()
            .Where(l => l.CustomerId != null && ids.Contains(l.CustomerId.Value))
            .Select(l => new { CustomerId = l.CustomerId!.Value, l.Balance, l.Status })
            .ToDictionaryAsync(o => o.CustomerId, o => new OwnerLedger(o.Balance, o.Status), cancellationToken);

        var farmRows = await _db.Ledgers.AsNoTracking()
            .Where(l => l.FarmId != null)
            .Join(
                _db.Farms.AsNoTracking().Where(f => ids.Contains(f.CustomerId)),
                l => l.FarmId, f => f.Id,
                (l, f) => new { f.CustomerId, l.Balance, l.Status })
            .ToListAsync(cancellationToken);
        var farmsByCustomer = farmRows
            .GroupBy(f => f.CustomerId)
            .ToDictionary(g => g.Key, g => g.Select(x => new OwnerLedger(x.Balance, x.Status)).ToList());

        return customers.Select(c =>
        {
            ownByCustomer.TryGetValue(c.Id, out var own);
            farmsByCustomer.TryGetValue(c.Id, out var farms);
            var ownBalance = own?.Balance ?? 0m;
            var (aggregate, aggregateStatus) = Aggregate(ownBalance, own?.Status, farms ?? []);
            return ToResponse(c, aggregate, aggregateStatus, ownBalance, farmLedgers: null, own?.Status);
        }).ToList();
    }

    /// <summary>
    /// Folds an owner's own ledger + its farm ledgers into the aggregate balance and status. Balance
    /// is the simple sum. Status is a <b>settled rollup</b>: outstanding debt anywhere wins
    /// (<c>has_debt</c>); otherwise the customer reads <c>closed</c> once their <b>own</b> ledger is
    /// closed — a zero-balance farm ledger left open does not keep them open — else <c>open</c>.
    /// (The per-ledger <c>status</c> stays authoritative for settlement; this is a display rollup, so
    /// <c>closed</c> here doesn't imply every farm ledger was individually closed.)
    /// </summary>
    private static (decimal Balance, string Status) Aggregate(
        decimal ownBalance, string? ownStatus, IReadOnlyList<OwnerLedger> farmLedgers)
    {
        var aggregate = ownBalance + farmLedgers.Sum(f => f.Balance);

        var status = aggregate > 0m ? LedgerStatus.HasDebt
            : ownStatus == LedgerStatus.Closed ? LedgerStatus.Closed
            : LedgerStatus.Open;

        return (aggregate, status);
    }

    /// <summary>Maps the base customer fields via Mapster, then layers on the aggregated ledger state.</summary>
    private CustomerResponse ToResponse(
        Customer customer,
        decimal aggregateBalance,
        string aggregateStatus,
        decimal ownBalance,
        IReadOnlyList<CustomerFarmLedger>? farmLedgers,
        string? ownLedgerStatus = null) =>
        _mapper.Map<CustomerResponse>(customer) with
        {
            Balance = aggregateBalance,
            LedgerStatus = aggregateStatus,
            OwnBalance = ownBalance,
            FarmLedgers = farmLedgers,
            OwnLedgerStatus = ownLedgerStatus,
        };

    private sealed record OwnerLedger(decimal Balance, string Status);

    public async Task<CustomerResponse> CreateAsync(CustomerRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Customers
                .IgnoreQueryFilters()
                .AnyAsync(c => c.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("customer_id_collision", $"A customer with id '{id}' already exists.");
            }
        }

        var entity = new Customer
        {
            Id = request.Id ?? Guid.Empty,
            Type = request.Type,
            FullName = request.FullName,
            PhonePrimary = request.PhonePrimary,
            PhoneSecondary = request.PhoneSecondary,
            Address = request.Address,
            Email = request.Email,
            IdNumber = request.IdNumber,
            Notes = request.Notes,
            AssignedDoctorId = request.AssignedDoctorId,
        };

        _db.Customers.Add(entity);

        // M3 task 5: ledger row is created in the same DbContext transaction. AuditingInterceptor
        // stamps id + environment_id + timestamps so we leave those at default.
        var ledger = new Ledger
        {
            CustomerId = entity.Id,
            Balance = 0m,
            Status = LedgerStatus.Open,
        };
        _db.Ledgers.Add(ledger);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPhoneViolation(ex))
        {
            throw new ConflictException("customer_phone_taken",
                "A customer with this primary phone already exists in this environment.");
        }

        // The ledger was created in the same transaction above — balance 0, status open; a new
        // customer has no farms yet, so aggregate == own.
        return ToResponse(entity, ledger.Balance, ledger.Status, ledger.Balance, farmLedgers: null, ledger.Status);
    }

    public async Task<CustomerResponse> UpdateAsync(
        Guid id,
        CustomerPatchRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                     ?? throw new NotFoundException("customer", id);

        if (request.Type is not null) entity.Type = request.Type;
        if (request.FullName is not null) entity.FullName = request.FullName;
        if (request.PhonePrimary is not null) entity.PhonePrimary = request.PhonePrimary;
        if (request.PhoneSecondary is not null) entity.PhoneSecondary = request.PhoneSecondary;
        if (request.Address is not null) entity.Address = request.Address;
        if (request.Email is not null) entity.Email = request.Email;
        if (request.IdNumber is not null) entity.IdNumber = request.IdNumber;
        if (request.Notes is not null) entity.Notes = request.Notes;
        if (request.AssignedDoctorId.HasValue) entity.AssignedDoctorId = request.AssignedDoctorId;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPhoneViolation(ex))
        {
            throw new ConflictException("customer_phone_taken",
                "A customer with this primary phone already exists in this environment.");
        }

        // The caller (PATCH endpoint) only reads the id; return the own ledger as the balance — a CRUD
        // update never changes ledger state, and the aggregate read lives on GET.
        var ledger = await _db.Ledgers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CustomerId == entity.Id, cancellationToken);
        var ownBalance = ledger?.Balance ?? 0m;
        var ownStatus = ledger?.Status ?? LedgerStatus.Open;
        return ToResponse(entity, ownBalance, ownStatus, ownBalance, farmLedgers: null, ownStatus);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Customers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
                     ?? throw new NotFoundException("customer", id);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.Customers.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static bool IsPhoneViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException pg
        && pg.SqlState == "23505"
        && pg.ConstraintName == "ux_customers_env_phone";
}
