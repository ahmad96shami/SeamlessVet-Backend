using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §3 — catalog item (medication or general product). Pull-only on clients via the
/// PowerSync <c>reference</c> bucket; writes are admin-only through <c>/admin/products</c>.
/// </summary>
public sealed class Product : Entity
{
    public string NameAr { get; set; } = string.Empty;

    public string? NameLatin { get; set; }

    public string? Barcode { get; set; }

    public string Category { get; set; } = ProductCategory.Medication;

    public string? Manufacturer { get; set; }

    public string? Supplier { get; set; }

    public decimal PurchasePrice { get; set; }

    public decimal SellingPrice { get; set; }

    public string? UnitOfMeasure { get; set; }

    public DateOnly? ExpirationDate { get; set; }

    public decimal ReorderPoint { get; set; }
}

public static class ProductCategory
{
    public const string Medication = "medication";
    public const string Product = "product";

    /// <summary>
    /// M26 — vaccines are stock products (replacing the M22 vaccines-as-services model). The web
    /// اللقاحات tab + POS vaccine chip filter the product catalog on this category; a
    /// <see cref="Vaccination"/> links its <c>product_id</c> and deducts stock FEFO on administration.
    /// </summary>
    public const string Vaccine = "vaccine";

    public static readonly IReadOnlyCollection<string> All = [Medication, Product, Vaccine];
}
