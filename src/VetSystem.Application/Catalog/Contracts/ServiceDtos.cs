namespace VetSystem.Application.Catalog.Contracts;

/// <summary>Admin request for creating or updating a <c>services</c> row.</summary>
public sealed record ServiceRequest(
    Guid? Id,
    string NameAr,
    string? NameLatin,
    string? Category,
    decimal DefaultPrice);

public sealed record ServicePatchRequest(
    string? NameAr,
    string? NameLatin,
    string? Category,
    decimal? DefaultPrice);

public sealed record ServiceResponse(
    Guid Id,
    string NameAr,
    string? NameLatin,
    string? Category,
    decimal DefaultPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
