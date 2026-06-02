using Mapster;
using VetSystem.Application.NightStays.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.NightStays.Mapping;

public sealed class NightStaysMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<NightStay, NightStayResponse>();
    }
}
