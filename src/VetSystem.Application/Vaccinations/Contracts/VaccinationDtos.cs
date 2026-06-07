namespace VetSystem.Application.Vaccinations.Contracts;

/// <summary>
/// SCHEMA §6 vaccination payload (PRD §5.2, §6.7). Recipient is either a single pet
/// (<c>PetId</c>) or a farm group (<c>CustomerId</c>); at least one is required. <c>NextDueDate</c>
/// drives the M11 reminder job. <c>Id</c> is client-generated. <c>ServiceId</c> links the catalog
/// vaccine (M22 — billable when set; <c>VaccineType</c> snapshots its name) and <c>Price</c>
/// snapshots the charge at recording time, defaulting to the service's catalog price.
/// </summary>
public sealed record VaccinationCreateRequest(
    Guid? Id,
    Guid? PetId,
    Guid? CustomerId,
    Guid? VisitId,
    Guid? ServiceId,
    string VaccineType,
    decimal? Price,
    DateOnly DateGiven,
    DateOnly? NextDueDate,
    string? CertificateUrl);

public sealed record VaccinationPatchRequest(
    Guid? ServiceId,
    string? VaccineType,
    decimal? Price,
    DateOnly? DateGiven,
    DateOnly? NextDueDate,
    string? CertificateUrl);

public sealed record VaccinationResponse(
    Guid Id,
    Guid? PetId,
    Guid? CustomerId,
    Guid? VisitId,
    Guid? ServiceId,
    string VaccineType,
    decimal? Price,
    DateOnly DateGiven,
    DateOnly? NextDueDate,
    string? CertificateUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
