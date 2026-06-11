using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §5 — join row attaching a <see cref="Contract"/> to one of the owning customer's
/// <see cref="Farm"/>s (M15). A contract covers one-or-more farms of the <b>same</b> customer
/// (<see cref="Contract.CustomerId"/> stays the owning customer). The unique
/// <c>(contract_id, farm_id)</c> pair is enforced. Authority follows the parent contract: writable
/// while the contract is <c>draft</c> (doctor-device authoritative); locked once <c>active</c>+.
/// </summary>
public sealed class ContractFarm : Entity
{
    public Guid ContractId { get; set; }

    public Guid FarmId { get; set; }
}
