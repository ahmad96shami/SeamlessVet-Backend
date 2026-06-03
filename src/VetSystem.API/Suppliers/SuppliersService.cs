using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Application.Suppliers.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Suppliers;

/// <summary>
/// M19 supplier CRUD (SCHEMA §4). Online-only center-web (admin/accountant). <see cref="CreateAsync"/>
/// seeds the matching <see cref="SupplierLedger"/> in the same transaction so the purchase-invoice and
/// supplier-payment flows always have one to post into (mirrors the customer → ledger auto-create).
/// </summary>
public sealed class SuppliersService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public SuppliersService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<SupplierResponse>> ListAsync(
        string? search,
        string? ledgerStatus,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (ledgerStatus is not null && !LedgerStatus.All.Contains(ledgerStatus))
        {
            throw new ConflictException("invalid_ledger_status", $"ledgerStatus '{ledgerStatus}' is not valid.");
        }

        var suppliers = _db.Suppliers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            suppliers = suppliers.Where(s =>
                EF.Functions.ILike(s.Name, pattern) ||
                (s.PhonePrimary != null && EF.Functions.ILike(s.PhonePrimary, pattern)) ||
                (s.TaxNumber != null && EF.Functions.ILike(s.TaxNumber, pattern)));
        }

        if (ledgerStatus is { } status)
        {
            suppliers = suppliers.Where(s =>
                _db.SupplierLedgers.Any(l => l.SupplierId == s.Id && l.Status == status));
        }

        var page = await suppliers
            .OrderBy(s => s.Name)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        if (page.Count == 0)
        {
            return [];
        }

        var ids = page.Select(s => s.Id).ToList();
        var ledgers = await _db.SupplierLedgers.AsNoTracking()
            .Where(l => ids.Contains(l.SupplierId))
            .ToDictionaryAsync(l => l.SupplierId, l => new { l.Balance, l.Status }, cancellationToken);

        return page.Select(s =>
        {
            ledgers.TryGetValue(s.Id, out var ledger);
            return ToResponse(s, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
        }).ToList();
    }

    public async Task<SupplierResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                       ?? throw new NotFoundException("supplier", id);

        var ledger = await _db.SupplierLedgers.AsNoTracking()
            .Where(l => l.SupplierId == id)
            .Select(l => new { l.Balance, l.Status })
            .FirstOrDefaultAsync(cancellationToken);

        return ToResponse(supplier, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
    }

    public async Task<SupplierResponse> CreateAsync(SupplierRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.Id is { } id && id != Guid.Empty
            && await _db.Suppliers.IgnoreQueryFilters().AnyAsync(s => s.Id == id, cancellationToken))
        {
            throw new ConflictException("supplier_id_collision", $"A supplier with id '{id}' already exists.");
        }

        var entity = new Supplier
        {
            Id = request.Id ?? Guid.Empty,
            Name = request.Name,
            PhonePrimary = request.PhonePrimary,
            PhoneSecondary = request.PhoneSecondary,
            Address = request.Address,
            Email = request.Email,
            TaxNumber = request.TaxNumber,
            Notes = request.Notes,
        };

        _db.Suppliers.Add(entity);

        // One supplier ledger is created in the same transaction (the AuditingInterceptor stamps
        // id + environment_id + timestamps), so AP flows always have a ledger to post into.
        var ledger = new SupplierLedger
        {
            SupplierId = entity.Id,
            Balance = 0m,
            Status = LedgerStatus.Open,
        };
        _db.SupplierLedgers.Add(ledger);

        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(entity, ledger.Balance, ledger.Status);
    }

    public async Task<SupplierResponse> UpdateAsync(
        Guid id, SupplierPatchRequest request, CancellationToken cancellationToken)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                     ?? throw new NotFoundException("supplier", id);

        if (request.Name is not null) entity.Name = request.Name;
        if (request.PhonePrimary is not null) entity.PhonePrimary = request.PhonePrimary;
        if (request.PhoneSecondary is not null) entity.PhoneSecondary = request.PhoneSecondary;
        if (request.Address is not null) entity.Address = request.Address;
        if (request.Email is not null) entity.Email = request.Email;
        if (request.TaxNumber is not null) entity.TaxNumber = request.TaxNumber;
        if (request.Notes is not null) entity.Notes = request.Notes;

        await _db.SaveChangesAsync(cancellationToken);

        var ledger = await _db.SupplierLedgers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.SupplierId == entity.Id, cancellationToken);
        return ToResponse(entity, ledger?.Balance ?? 0m, ledger?.Status ?? LedgerStatus.Open);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                     ?? throw new NotFoundException("supplier", id);

        // AuditingInterceptor converts EntityState.Deleted into a soft-delete.
        _db.Suppliers.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private SupplierResponse ToResponse(Supplier supplier, decimal balance, string ledgerStatus) =>
        _mapper.Map<SupplierResponse>(supplier) with { Balance = balance, LedgerStatus = ledgerStatus };

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }
}
