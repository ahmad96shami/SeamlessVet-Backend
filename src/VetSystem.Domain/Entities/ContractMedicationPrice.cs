using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §5 — a per-medication price override within a contract (PRD §6.6). When the parent
/// contract is <c>active</c>, the pricing service (M8) uses <see cref="ContractPrice"/> as the sale
/// value for the product on field invoices — and as M9's System-A sale value, so that line's drug
/// profit is <c>(contract price − cost)</c>. Editable by the authoring field doctor only while the
/// parent contract is still <c>draft</c>; UNIQUE per (contract, product).
/// </summary>
public sealed class ContractMedicationPrice : Entity
{
    public Guid ContractId { get; set; }

    public Guid ProductId { get; set; }

    public decimal ContractPrice { get; set; }
}
