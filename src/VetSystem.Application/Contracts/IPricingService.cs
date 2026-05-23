namespace VetSystem.Application.Contracts;

/// <summary>
/// Resolves the sale unit price for a product at a point in time (M8 task 9, SCHEMA "Key invariants"
/// #8): the contract-overridden price when an <c>active</c> contract for the customer — whose period
/// covers <paramref name="asOf"/> — carries a <c>contract_medication_prices</c> row for the product;
/// otherwise <c>products.selling_price</c>. While a contract is still <c>draft</c> it does not apply,
/// so the same product bills at catalog price (PRD §6.6). Consumed by field-visit invoice creation
/// (M7) and by M9's System-A drug-profit <c>sale_value</c> resolution.
/// </summary>
public interface IPricingService
{
    Task<ResolvedUnitPrice> ResolveUnitPriceAsync(
        Guid productId, Guid? customerId, DateOnly asOf, CancellationToken cancellationToken);
}

/// <summary>
/// The resolved price plus its provenance. <see cref="IsContractPrice"/> is true (and
/// <see cref="ContractId"/> set) when an active-contract override applied; false when the catalog
/// selling price was used.
/// </summary>
public sealed record ResolvedUnitPrice(decimal UnitPrice, bool IsContractPrice, Guid? ContractId);
