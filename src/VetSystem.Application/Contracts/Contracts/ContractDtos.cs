namespace VetSystem.Application.Contracts.Contracts;

/// <summary>
/// SCHEMA §5 contract payload. <c>Id</c> is client-generated (Guid v7) so dedicated and sync writes
/// converge offline-safely. A contract is created <c>draft</c> by default; creating it directly
/// <c>active</c> is allowed only for an actor holding <c>contracts.activate</c> (the web Admin/Accountant
/// flow) — the service enforces that gate. <c>ResponsibleDoctorId</c> defaults to the authoring user
/// when omitted (the field-doctor flow). <c>Status</c> may only be <c>draft</c> or <c>active</c> at
/// creation; later transitions go through the lifecycle endpoints.
/// </summary>
public sealed record ContractCreateRequest(
    Guid? Id,
    Guid CustomerId,
    Guid? ResponsibleDoctorId,
    DateOnly PeriodStart,
    DateOnly? PeriodEnd,
    decimal? TotalPrice,
    int? ExpectedVisitCount,
    string? AnimalType,
    int? AnimalCount,
    string? Status);

/// <summary>
/// Term edit (M8 task 3). Every field is optional — only the supplied ones change. <c>Status</c>,
/// <c>CustomerId</c>, and the activation stamps are not patchable here: status moves through the
/// dedicated <c>/activate</c>, <c>/complete</c>, <c>/cancel</c> endpoints. Editing a <c>draft</c> needs
/// only <c>contracts.write</c>; editing an <c>active</c> contract's binding terms additionally needs
/// <c>contracts.activate</c> (Admin/Accountant); <c>completed</c>/<c>cancelled</c> contracts are locked.
/// </summary>
public sealed record ContractPatchRequest(
    Guid? ResponsibleDoctorId,
    DateOnly? PeriodStart,
    DateOnly? PeriodEnd,
    decimal? TotalPrice,
    int? ExpectedVisitCount,
    string? AnimalType,
    int? AnimalCount);

public sealed record ContractResponse(
    Guid Id,
    Guid CustomerId,
    Guid? ResponsibleDoctorId,
    DateOnly PeriodStart,
    DateOnly? PeriodEnd,
    decimal? TotalPrice,
    int? ExpectedVisitCount,
    string? AnimalType,
    int? AnimalCount,
    string Status,
    Guid? CreatedBy,
    Guid? ActivatedBy,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
