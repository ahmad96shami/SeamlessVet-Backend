namespace VetSystem.Application.Contracts.Contracts;

/// <summary>
/// SCHEMA §5 contract↔farm attachment (M15). Nested under a contract — the parent <c>contractId</c>
/// comes from the route, not the body. A contract covers one-or-more farms of the <b>same</b>
/// owning customer; attaching a farm of a different customer is rejected. Attached/detached only
/// while the parent contract is <c>draft</c> (M15 task 7); once active its terms are locked.
/// </summary>
public sealed record ContractFarmAttachRequest(
    Guid? Id,
    Guid FarmId);

public sealed record ContractFarmResponse(
    Guid Id,
    Guid ContractId,
    Guid FarmId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
