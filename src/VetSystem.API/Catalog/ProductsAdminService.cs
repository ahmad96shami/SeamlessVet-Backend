using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Catalog.Contracts;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.API.Catalog;

/// <summary>
/// Admin operations on the products catalog. Writes scope to the current environment via the
/// global EF query filter; reads paginate with offset (admin tables, TECH_STACK API Design Notes).
/// </summary>
public sealed class ProductsAdminService
{
    private const int MaxPageSize = 200;

    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IMapper _mapper;

    public ProductsAdminService(ApplicationDbContext db, ICurrentUserAccessor currentUser, IMapper mapper)
    {
        _db = db;
        _currentUser = currentUser;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ProductResponse>> ListAsync(
        string? search,
        string? category,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        if (category is not null && !ProductCategory.All.Contains(category))
        {
            throw new ConflictException("invalid_category", $"category '{category}' is not valid.");
        }

        var query = _db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(p =>
                EF.Functions.ILike(p.NameAr, pattern) ||
                (p.NameLatin != null && EF.Functions.ILike(p.NameLatin, pattern)) ||
                (p.Barcode != null && EF.Functions.ILike(p.Barcode, pattern)));
        }

        if (category is not null)
        {
            query = query.Where(p => p.Category == category);
        }

        var rows = await query
            .OrderBy(p => p.NameAr)
            .Skip(Math.Max(0, skip ?? 0))
            .Take(Math.Clamp(take ?? 50, 1, MaxPageSize))
            .ToListAsync(cancellationToken);

        return rows.Select(_mapper.Map<ProductResponse>).ToList();
    }

    public async Task<ProductResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new NotFoundException("product", id);

        return _mapper.Map<ProductResponse>(product);
    }

    public async Task<ProductResponse> CreateAsync(ProductRequest request, CancellationToken cancellationToken)
    {
        RequireEnvironment();

        if (request.Id is { } id && id != Guid.Empty)
        {
            var collision = await _db.Products
                .IgnoreQueryFilters()
                .AnyAsync(p => p.Id == id, cancellationToken);
            if (collision)
            {
                throw new ConflictException("product_id_collision", $"A product with id '{id}' already exists.");
            }
        }

        var entity = new Product
        {
            Id = request.Id ?? Guid.Empty,
            NameAr = request.NameAr,
            NameLatin = request.NameLatin,
            Barcode = request.Barcode,
            Category = request.Category,
            Manufacturer = request.Manufacturer,
            Supplier = request.Supplier,
            PurchasePrice = request.PurchasePrice,
            SellingPrice = request.SellingPrice,
            UnitOfMeasure = request.UnitOfMeasure,
            ExpirationDate = request.ExpirationDate,
            ReorderPoint = request.ReorderPoint,
            IsConsumable = request.IsConsumable,
        };

        _db.Products.Add(entity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsBarcodeViolation(ex))
        {
            throw new ConflictException("product_barcode_taken", "A product with this barcode already exists.");
        }

        return _mapper.Map<ProductResponse>(entity);
    }

    public async Task<ProductResponse> UpdateAsync(
        Guid id,
        ProductPatchRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new NotFoundException("product", id);

        if (request.NameAr is not null) entity.NameAr = request.NameAr;
        if (request.NameLatin is not null) entity.NameLatin = request.NameLatin;
        if (request.Barcode is not null) entity.Barcode = request.Barcode;
        if (request.Category is not null) entity.Category = request.Category;
        if (request.Manufacturer is not null) entity.Manufacturer = request.Manufacturer;
        if (request.Supplier is not null) entity.Supplier = request.Supplier;
        if (request.PurchasePrice.HasValue) entity.PurchasePrice = request.PurchasePrice.Value;
        if (request.SellingPrice.HasValue) entity.SellingPrice = request.SellingPrice.Value;
        if (request.UnitOfMeasure is not null) entity.UnitOfMeasure = request.UnitOfMeasure;
        if (request.ExpirationDate.HasValue) entity.ExpirationDate = request.ExpirationDate;
        if (request.ReorderPoint.HasValue) entity.ReorderPoint = request.ReorderPoint.Value;
        if (request.IsConsumable.HasValue) entity.IsConsumable = request.IsConsumable.Value;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsBarcodeViolation(ex))
        {
            throw new ConflictException("product_barcode_taken", "A product with this barcode already exists.");
        }

        return _mapper.Map<ProductResponse>(entity);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
                     ?? throw new NotFoundException("product", id);

        // The auditing interceptor converts EntityState.Deleted into a soft-delete (sets deleted_at).
        _db.Products.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void RequireEnvironment()
    {
        if (_currentUser.EnvironmentId is null)
        {
            throw new ForbiddenException("unauthenticated", "Authentication required.");
        }
    }

    private static bool IsBarcodeViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException pg
        && pg.SqlState == "23505"
        && pg.ConstraintName == "ux_products_env_barcode";
}
