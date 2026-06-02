using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §2 — a physical farm/site owned by a customer, attached the way <see cref="Pet"/> is
/// attached (M15). A customer owns 1–N farms; contracts, batches, visits and invoices attribute to a
/// farm. A farm carries <b>no</b> <c>assigned_doctor_id</c> — it inherits the owning customer's, so it
/// streams to the field device through the existing <c>by_customer</c> scope unchanged. <c>location</c>
/// is free-text only (no PostGIS). Ledger ownership stays single-ledger (the customer's) this milestone;
/// per-farm ledgers land in M16.
/// </summary>
public sealed class Farm : Entity
{
    public Guid CustomerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = FarmKind.Other;

    /// <summary>Free-text location / address (no geospatial type — PRD: no PostGIS).</summary>
    public string? Location { get; set; }

    public string? AnimalType { get; set; }

    public int? HeadCount { get; set; }

    public string? Notes { get; set; }
}

public static class FarmKind
{
    public const string Poultry = "poultry";
    public const string Cattle = "cattle";
    public const string Mixed = "mixed";
    public const string Other = "other";

    public static readonly IReadOnlyCollection<string> All = [Poultry, Cattle, Mixed, Other];
}
