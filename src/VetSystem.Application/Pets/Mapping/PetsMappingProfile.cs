using Mapster;
using VetSystem.Application.Pets.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Pets.Mapping;

public sealed class PetsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Pet, PetResponse>();
    }
}
