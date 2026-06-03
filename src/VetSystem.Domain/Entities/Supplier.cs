using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// M19 (SCHEMA §4) — a goods/medication supplier the clinic buys from. Mirrors <see cref="Customer"/>
/// on the accounts-payable side: each supplier owns exactly one <see cref="SupplierLedger"/> (created
/// with the supplier) whose balance is what the clinic owes them. Center-web only (admin/accountant);
/// suppliers are not part of any field-doctor sync scope, so there is no <c>/sync/suppliers</c> path.
/// </summary>
public sealed class Supplier : Entity
{
    public string Name { get; set; } = string.Empty;

    public string? PhonePrimary { get; set; }

    public string? PhoneSecondary { get; set; }

    public string? Address { get; set; }

    public string? Email { get; set; }

    /// <summary>Tax / commercial-registration number (الرقم الضريبي).</summary>
    public string? TaxNumber { get; set; }

    public string? Notes { get; set; }
}
