namespace VetSystem.Application.Vaccinations.Contracts;

/// <summary>
/// SCHEMA §6 vaccination payload (PRD §5.2, §6.7). Recipient is either a single pet
/// (<c>PetId</c>) or a farm group (<c>CustomerId</c>); at least one is required. <c>NextDueDate</c>
/// drives the M11 reminder job. <c>Id</c> is client-generated.
/// </summary>
public sealed record VaccinationCreateRequest(
    Guid? Id,
    Guid? PetId,
    Guid? CustomerId,
    Guid? VisitId,
    string VaccineType,
    DateOnly DateGiven,
    DateOnly? NextDueDate,
    string? CertificateUrl);

public sealed record VaccinationPatchRequest(
    string? VaccineType,
    DateOnly? DateGiven,
    DateOnly? NextDueDate,
    string? CertificateUrl);

public sealed record VaccinationResponse(
    Guid Id,
    Guid? PetId,
    Guid? CustomerId,
    Guid? VisitId,
    string VaccineType,
    DateOnly DateGiven,
    DateOnly? NextDueDate,
    string? CertificateUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
