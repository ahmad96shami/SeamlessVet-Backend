using Mapster;
using VetSystem.Application.Vaccinations.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Vaccinations.Mapping;

public sealed class VaccinationsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Vaccination, VaccinationResponse>();
    }
}
