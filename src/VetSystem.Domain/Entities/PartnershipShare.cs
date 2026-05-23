using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §1 — a partner's share of net profit over a date window (PRD §6.8). Shares are
/// time-versioned: editing a split historically means closing the old window (<see cref="EffectiveTo"/>)
/// and opening a new one, so a closed batch's distribution always uses the shares effective on its
/// close date. A share is <b>active on a date</b> <c>D</c> when
/// <c>EffectiveFrom &lt;= D &amp;&amp; (EffectiveTo is null || EffectiveTo &gt;= D)</c> — inclusive both
/// ends, matching the contract-period semantics in <c>IPricingService</c>.
///
/// <para><b>Invariant (SCHEMA §1, enforced in the service layer):</b> per environment, the active
/// shares sum to ≤ 100% on every effective date. Because the running sum only rises at an
/// <see cref="EffectiveFrom"/> boundary, checking the sum at each share's start date is sufficient.</para>
/// </summary>
public sealed class PartnershipShare : Entity
{
    public Guid PartnerId { get; set; }

    /// <summary>Percent of net profit, 0..100.</summary>
    public decimal SharePercent { get; set; }

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>Null = open-ended (still in effect).</summary>
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Active on <paramref name="asOf"/> per the inclusive window rule above.</summary>
    public bool IsActiveOn(DateOnly asOf) =>
        EffectiveFrom <= asOf && (EffectiveTo is null || EffectiveTo >= asOf);
}
