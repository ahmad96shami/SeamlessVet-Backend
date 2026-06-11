using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M30 (SCHEMA §4) — append-only history of a doctor-partner account, mirroring
/// <see cref="SupplierLedgerEntry"/>. UPDATE/DELETE is never accepted; corrections are new
/// <c>adjustment</c> rows. <see cref="Amount"/> is signed: positive increases the payable (a batch
/// entitlement credit), negative reduces it (a payment). <see cref="BalanceAfter"/> stores the running
/// balance immediately after this entry applied. The polymorphic source FKs are nullable: an
/// <c>entitlement</c> entry carries <see cref="DoctorEntitlementId"/> (and <see cref="BatchId"/> for
/// reference), a <c>payment</c> entry carries <see cref="DoctorPartnerPaymentId"/>, an adjustment has
/// neither.
/// </summary>
public sealed class DoctorPartnerLedgerEntry : Entity
{
    public Guid DoctorPartnerLedgerId { get; set; }

    public string EntryType { get; set; } = DoctorPartnerLedgerEntryType.Adjustment;

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public Guid? DoctorEntitlementId { get; set; }

    public Guid? BatchId { get; set; }

    public Guid? DoctorPartnerPaymentId { get; set; }

    public string? Description { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}

public static class DoctorPartnerLedgerEntryType
{
    /// <summary>A batch supervision-fee entitlement released on settlement — increases the payable.</summary>
    public const string Entitlement = "entitlement";

    /// <summary>A payment made to the doctor — reduces the payable.</summary>
    public const string Payment = "payment";

    /// <summary>Manual correction — signed either way.</summary>
    public const string Adjustment = "adjustment";

    public static readonly IReadOnlyCollection<string> All = [Entitlement, Payment, Adjustment];
}
