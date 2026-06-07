namespace VetSystem.Application.Contracts.Contracts;

/// <summary>
/// SCHEMA §5 batch (Dawra/Cycle) payload (PRD §7.2). <c>EntitlementEnabled</c> is a tri-state:
/// <c>null</c> inherits <c>system_settings.entitlement_enabled_global</c>, <c>true</c>/<c>false</c>
/// override per batch (SCHEMA "Key invariants" #4). The fee model + share fields feed M9's
/// entitlement calculation. Batch financial configuration is an Admin/Accountant operation
/// (PRD §7, §8.9).
/// </summary>
public sealed record BatchCreateRequest(
    Guid? Id,
    Guid? ContractId,
    Guid CustomerId,
    Guid? FarmId,
    Guid ResponsibleDoctorId,
    int AnimalCount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string SupervisionFeeModel,
    decimal SupervisionFeeValue,
    bool? EntitlementEnabled,
    string? EntitlementSystem,
    decimal? DoctorSharePercent,
    decimal? DoctorShareCeiling,
    string? Status);

/// <summary>
/// Batch edit. Every field optional — only supplied ones change. Note: like the rest of the codebase,
/// a tri-state nullable (<c>EntitlementEnabled</c>) can be set to true/false via PATCH but cannot be
/// reverted to "inherit" (null) — recreate the batch for that.
/// </summary>
public sealed record BatchPatchRequest(
    Guid? ContractId,
    Guid? FarmId,
    Guid? ResponsibleDoctorId,
    int? AnimalCount,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? SupervisionFeeModel,
    decimal? SupervisionFeeValue,
    bool? EntitlementEnabled,
    string? EntitlementSystem,
    decimal? DoctorSharePercent,
    decimal? DoctorShareCeiling,
    string? Status);

public sealed record BatchResponse(
    Guid Id,
    Guid? ContractId,
    Guid CustomerId,
    Guid? FarmId,
    Guid ResponsibleDoctorId,
    int AnimalCount,
    DateOnly StartDate,
    DateOnly? EndDate,
    string SupervisionFeeModel,
    decimal SupervisionFeeValue,
    bool? EntitlementEnabled,
    string? EntitlementSystem,
    decimal? DoctorSharePercent,
    decimal? DoctorShareCeiling,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SettledAt = null);
