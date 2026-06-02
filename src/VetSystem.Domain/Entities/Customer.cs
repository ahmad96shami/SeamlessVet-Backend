using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §2 — unified household + farm owner. A <c>home</c> customer is a household pet owner; a
/// <c>clinic_customer</c> is an in-clinic walk-in/account owner; farm types own one-or-more
/// <see cref="Farm"/>s (M15). All share one <see cref="Ledger"/> this milestone.
/// <see cref="AssignedDoctorId"/> drives the field-app sync scope (PRD §8.6).
/// </summary>
public sealed class Customer : Entity
{
    public string Type { get; set; } = CustomerType.Home;

    public string FullName { get; set; } = string.Empty;

    public string? PhonePrimary { get; set; }

    public string? PhoneSecondary { get; set; }

    public string? Address { get; set; }

    public string? Email { get; set; }

    public string? IdNumber { get; set; }

    public string? Notes { get; set; }

    public Guid? AssignedDoctorId { get; set; }
}

public static class CustomerType
{
    public const string RegularFarm = "regular_farm";
    public const string Home = "home";
    public const string CattleFarm = "cattle_farm";
    public const string PoultryFarm = "poultry_farm";

    /// <summary>An in-clinic account owner (M15) — "زبون عيادة".</summary>
    public const string ClinicCustomer = "clinic_customer";

    public static readonly IReadOnlyCollection<string> All =
    [
        RegularFarm, Home, CattleFarm, PoultryFarm, ClinicCustomer,
    ];

    /// <summary>
    /// Types that own farms (M15 default-farm backfill targets these). <c>home</c> and
    /// <c>clinic_customer</c> get no default farm.
    /// </summary>
    public static readonly IReadOnlyCollection<string> FarmOwning =
    [
        RegularFarm, CattleFarm, PoultryFarm,
    ];
}
