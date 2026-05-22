namespace VetSystem.Application.Catalog.Contracts;

/// <summary>
/// Admin request for creating or updating a <c>products</c> row. <c>Id</c> is supplied by the
/// admin web UI as a client-generated Guid v7 (matches the sync convention; the same record
/// will later flow to mobile via the PowerSync <c>reference</c> bucket).
/// </summary>
public sealed record ProductRequest(
    Guid? Id,
    string NameAr,
    string? NameLatin,
    string? Barcode,
    string Category,
    string? Manufacturer,
    string? Supplier,
    decimal PurchasePrice,
    decimal SellingPrice,
    string? UnitOfMeasure,
    DateOnly? ExpirationDate,
    decimal ReorderPoint);

public sealed record ProductPatchRequest(
    string? NameAr,
    string? NameLatin,
    string? Barcode,
    string? Category,
    string? Manufacturer,
    string? Supplier,
    decimal? PurchasePrice,
    decimal? SellingPrice,
    string? UnitOfMeasure,
    DateOnly? ExpirationDate,
    decimal? ReorderPoint);

public sealed record ProductResponse(
    Guid Id,
    string NameAr,
    string? NameLatin,
    string? Barcode,
    string Category,
    string? Manufacturer,
    string? Supplier,
    decimal PurchasePrice,
    decimal SellingPrice,
    string? UnitOfMeasure,
    DateOnly? ExpirationDate,
    decimal ReorderPoint,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
