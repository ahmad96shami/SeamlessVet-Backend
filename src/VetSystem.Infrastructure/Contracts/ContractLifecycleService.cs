using VetSystem.Application.Contracts;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Contracts;

/// <inheritdoc cref="IContractLifecycleService"/>
public sealed class ContractLifecycleService : IContractLifecycleService
{
    public void EnsureCanTransition(string from, string to)
    {
        if (!ContractStatus.CanTransition(from, to))
        {
            throw new ConflictException(
                "invalid_contract_transition",
                $"Cannot transition a contract from '{from}' to '{to}'.");
        }
    }
}
