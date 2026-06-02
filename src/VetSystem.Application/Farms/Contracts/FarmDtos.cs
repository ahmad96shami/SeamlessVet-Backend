namespace VetSystem.Application.Farms.Contracts;

/// <summary>
/// SCHEMA §2 farm payload (M15). <c>Id</c> is supplied by the client (Guid v7) so CRUD and sync
/// writes converge. A farm always belongs to a customer; it inherits the customer's assigned doctor
/// (no <c>assigned_doctor_id</c> of its own) and streams through the existing <c>by_customer</c> scope.
/// </summary>
public sealed record FarmRequest(
    Guid? Id,
    Guid CustomerId,
    string Name,
    string Kind,
    string? Location,
    string? AnimalType,
    int? HeadCount,
    string? Notes);

public sealed record FarmPatchRequest(
    string? Name,
    string? Kind,
    string? Location,
    string? AnimalType,
    int? HeadCount,
    string? Notes);

public sealed record FarmResponse(
    Guid Id,
    Guid CustomerId,
    string Name,
    string Kind,
    string? Location,
    string? AnimalType,
    int? HeadCount,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
