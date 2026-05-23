using Mapster;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Partnership.Mapping;

public sealed class PartnershipMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Partner, PartnerResponse>();
        config.NewConfig<PartnershipShare, PartnershipShareResponse>();
    }
}
