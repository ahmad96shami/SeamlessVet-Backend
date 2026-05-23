using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §6 — medication prescribed within a <see cref="Visit"/> (PRD §5.2-D).
/// <see cref="DispenseType"/> discriminates the fulfilment path: <c>administered_in_clinic</c>
/// deducts from inventory at create time (M5 task 9); <c>dispensed_to_owner</c> is queued for the
/// POS invoice (M7, M5 task 10).
/// </summary>
public sealed class Prescription : Entity
{
    public Guid VisitId { get; set; }

    public Guid ProductId { get; set; }

    public string? Dosage { get; set; }

    public string? Frequency { get; set; }

    public string? Duration { get; set; }

    public string? Notes { get; set; }

    public string DispenseType { get; set; } = Entities.DispenseType.AdministeredInClinic;

    public decimal? Quantity { get; set; }
}

public static class DispenseType
{
    public const string AdministeredInClinic = "administered_in_clinic";
    public const string DispensedToOwner = "dispensed_to_owner";

    public static readonly IReadOnlyCollection<string> All = [AdministeredInClinic, DispensedToOwner];
}
