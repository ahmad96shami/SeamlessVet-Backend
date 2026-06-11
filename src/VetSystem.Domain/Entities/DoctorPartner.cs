using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M30 (SCHEMA §4) — an entitlement-earning field doctor on the accounts-payable side. <b>Distinct</b>
/// from the M10 investor <see cref="Partner"/> (profit-split): a doctor-partner is the payee for the
/// batch supervision fees they earn. Each one carries a <b>mandatory</b> <see cref="UserId"/> (the
/// staff account it pays) and owns exactly one <see cref="DoctorPartnerLedger"/> (created with the
/// partner) whose balance is what the clinic owes them. Mirrors <see cref="Supplier"/> on the AP side;
/// the display name is resolved from the linked <see cref="User"/>, not stored here. Center-web only
/// (admin/accountant) — not part of any field-doctor sync scope, so there is no <c>/sync</c> path.
/// </summary>
public sealed class DoctorPartner : Entity
{
    public Guid UserId { get; set; }

    public string? Notes { get; set; }
}
