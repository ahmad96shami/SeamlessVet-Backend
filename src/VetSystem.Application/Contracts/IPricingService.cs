namespace VetSystem.Application.Contracts;

/// <summary>
/// Resolves the sale unit price for a product at a point in time. Since M29 removed per-contract
/// medication pricing, this is always <c>products.selling_price</c> — there is no longer a contract
/// tier (negotiated overrides happen only at batch settlement, M24). The signature is preserved so
/// the consumers (field-visit invoice creation, settlement preview) need not change; <paramref
/// name="customerId"/> and <paramref name="asOf"/> no longer affect the result. Consumed by
/// field-visit invoice creation (M7).
/// </summary>
public interface IPricingService
{
    Task<ResolvedUnitPrice> ResolveUnitPriceAsync(
        Guid productId, Guid? customerId, DateOnly asOf, CancellationToken cancellationToken);
}

/// <summary>
/// The resolved price plus its provenance. Since M29 (per-contract pricing removed)
/// <see cref="IsContractPrice"/> is always false and <see cref="ContractId"/> always null — the
/// fields are retained so the call sites that branch on them keep compiling and provably bill catalog.
/// </summary>
public sealed record ResolvedUnitPrice(decimal UnitPrice, bool IsContractPrice, Guid? ContractId);
