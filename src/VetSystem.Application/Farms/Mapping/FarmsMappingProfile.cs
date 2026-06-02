using Mapster;
using VetSystem.Application.Farms.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Farms.Mapping;

public sealed class FarmsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Farm, FarmResponse>();
    }
}
