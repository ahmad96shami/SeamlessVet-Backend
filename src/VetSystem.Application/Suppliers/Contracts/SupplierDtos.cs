namespace VetSystem.Application.Suppliers.Contracts;

/// <summary>
/// M19 (SCHEMA §4) supplier payload. <c>Id</c> may be supplied as a Guid v7 (CRUD is online-only —
/// suppliers are not part of any sync scope — but the client-id convention is kept for consistency).
/// </summary>
public sealed record SupplierRequest(
    Guid? Id,
    string Name,
    string? PhonePrimary,
    string? PhoneSecondary,
    string? Address,
    string? Email,
    string? TaxNumber,
    string? Notes);

public sealed record SupplierPatchRequest(
    string? Name,
    string? PhonePrimary,
    string? PhoneSecondary,
    string? Address,
    string? Email,
    string? TaxNumber,
    string? Notes);

/// <summary>
/// Supplier read projection. <see cref="Balance"/> and <see cref="LedgerStatus"/> are the supplier's
/// ledger state — positive balance = the clinic owes the supplier. Read-only (the ledger is
/// server-authoritative).
/// </summary>
public sealed record SupplierResponse(
    Guid Id,
    string Name,
    string? PhonePrimary,
    string? PhoneSecondary,
    string? Address,
    string? Email,
    string? TaxNumber,
    string? Notes,
    decimal Balance,
    string LedgerStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
