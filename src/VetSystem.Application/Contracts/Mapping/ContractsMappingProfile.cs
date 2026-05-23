using Mapster;
using VetSystem.Application.Contracts.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Contracts.Mapping;

public sealed class ContractsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Contract, ContractResponse>();
        config.NewConfig<ContractMedicationPrice, ContractMedicationPriceResponse>();
        config.NewConfig<Batch, BatchResponse>();
    }
}
