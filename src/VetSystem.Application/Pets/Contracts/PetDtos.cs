namespace VetSystem.Application.Pets.Contracts;

/// <summary>
/// SCHEMA §2 pet payload. <c>Id</c> is supplied by the client (Guid v7) so CRUD and sync writes
/// converge. Pets always belong to a customer; ownership transfers use a dedicated endpoint
/// rather than a generic PATCH (M3 task 4).
/// </summary>
public sealed record PetRequest(
    Guid? Id,
    Guid CustomerId,
    string Name,
    string? Species,
    string? Breed,
    string? Sex,
    DateOnly? DateOfBirth,
    string? ColorMarks,
    decimal? WeightLatest,
    string? PhotoUrl,
    string? MicrochipNo,
    string? HealthNotes,
    bool? IsNeutered);

public sealed record PetPatchRequest(
    string? Name,
    string? Species,
    string? Breed,
    string? Sex,
    DateOnly? DateOfBirth,
    string? ColorMarks,
    decimal? WeightLatest,
    string? PhotoUrl,
    string? MicrochipNo,
    string? HealthNotes,
    bool? IsNeutered);

public sealed record PetTransferRequest(Guid TargetCustomerId);

public sealed record PetResponse(
    Guid Id,
    Guid CustomerId,
    string Name,
    string? Species,
    string? Breed,
    string? Sex,
    DateOnly? DateOfBirth,
    string? ColorMarks,
    decimal? WeightLatest,
    string? PhotoUrl,
    string? MicrochipNo,
    string? HealthNotes,
    bool? IsNeutered,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
