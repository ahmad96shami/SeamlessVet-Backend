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

        // Filter by the 1:1 ledger's status via a correlated subquery, so ordering + paging stay on
        // the customer entity (which EF translates cleanly — projecting the entity into a wrapper and
        // then composing OrderBy/Skip/Take on top does not).
        if (ledgerStatus is { } status)
        {
            customers = customers.Where(c => _db.Ledgers.Any(l => l.CustomerId == c.Id && l.Status == status));
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

        var ledger = await _db.Ledgers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CustomerId == id, cancellationToken);

        return ToResponse(customer, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
    }

    /// <summary>Loads the 1:1 ledger balance + status for a page of customers in a single query.</summary>
    private async Task<IReadOnlyList<CustomerResponse>> EnrichAsync(
        IReadOnlyList<Customer> customers,
        CancellationToken cancellationToken)
    {
        if (customers.Count == 0)
        {
            return [];
        }

        // M16: still the customer's own ledger here; aggregate (own + Σ farm ledgers) lands in SC8.
        var ids = customers.Select(c => c.Id).ToList();
        var ledgers = await _db.Ledgers.AsNoTracking()
            .Where(l => l.CustomerId != null && ids.Contains(l.CustomerId.Value))
            .ToDictionaryAsync(l => l.CustomerId!.Value, cancellationToken);

        return customers.Select(c =>
        {
            ledgers.TryGetValue(c.Id, out var ledger);
            return ToResponse(c, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
        }).ToList();
    }

    /// <summary>Maps the base customer fields via Mapster, then layers on the joined ledger state.</summary>
    private CustomerResponse ToResponse(Customer customer, decimal balance, string ledgerStatus) =>
        _mapper.Map<CustomerResponse>(customer) with { Balance = balance, LedgerStatus = ledgerStatus };

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

        // The ledger was created in the same transaction above — balance 0, status open.
        return ToResponse(entity, ledger.Balance, ledger.Status);
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

        var ledger = await _db.Ledgers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.CustomerId == entity.Id, cancellationToken);
        return ToResponse(entity, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
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
