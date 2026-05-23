namespace VetSystem.Application.Procedures.Contracts;

/// <summary>
/// SCHEMA §6 procedure payload (PRD §5.2-C). Linked to a catalog <c>service_id</c>; <c>Price</c>
/// is snapshotted at create time — if omitted, it defaults to the linked service's
/// <c>default_price</c>. <c>Id</c> is client-generated so CRUD and sync converge.
/// </summary>
public sealed record ProcedureCreateRequest(
    Guid? Id,
    Guid VisitId,
    Guid? ServiceId,
    string? ResultText,
    string? ResultFileUrl,
    decimal? Price);

public sealed record ProcedurePatchRequest(
    Guid? ServiceId,
    string? ResultText,
    string? ResultFileUrl,
    decimal? Price);

public sealed record ProcedureResponse(
    Guid Id,
    Guid VisitId,
    Guid? ServiceId,
    string? ResultText,
    string? ResultFileUrl,
    decimal Price,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
