namespace VetSystem.Application.Contracts.Contracts;

/// <summary>
/// SCHEMA §5 per-medication price override (PRD §6.6). Nested under a contract — the parent
/// <c>contractId</c> comes from the route, not the body. Created/edited only while the parent
/// contract is <c>draft</c> (M8 task 7); once the contract is active its terms are locked.
/// </summary>
public sealed record ContractMedicationPriceCreateRequest(
    Guid? Id,
    Guid ProductId,
    decimal ContractPrice);

public sealed record ContractMedicationPricePatchRequest(
    decimal? ContractPrice);

public sealed record ContractMedicationPriceResponse(
    Guid Id,
    Guid ContractId,
    Guid ProductId,
    decimal ContractPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
