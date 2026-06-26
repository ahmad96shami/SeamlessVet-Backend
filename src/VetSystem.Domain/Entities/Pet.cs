using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §2 — master medical profile (permanent health info). Per-visit detail lives on
/// <c>visits</c>. Pets always belong to a customer; ownership transfers move <see cref="CustomerId"/>.
/// </summary>
public sealed class Pet : Entity
{
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Optional farm the pet belongs to (M15). Backward-compatible: existing pets keep
    /// <see cref="CustomerId"/> with <c>FarmId</c> null. Always within the same customer.
    /// </summary>
    public Guid? FarmId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Species { get; set; }

    public string? Breed { get; set; }

    public string? Sex { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? ColorMarks { get; set; }

    public decimal? WeightLatest { get; set; }

    public string? PhotoUrl { get; set; }

    public string? MicrochipNo { get; set; }

    public string? HealthNotes { get; set; }

    /// <summary>Spayed/castrated (معقم/مخصي). Null = unrecorded.</summary>
    public bool? IsNeutered { get; set; }
}

public static class PetSex
{
    public const string Male = "male";
    public const string Female = "female";
    public const string Unknown = "unknown";

    public static readonly IReadOnlyCollection<string> All = [Male, Female, Unknown];
}
