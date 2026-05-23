using Mapster;
using VetSystem.Application.Visits.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Visits.Mapping;

public sealed class VisitsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Visit, VisitResponse>();
    }
}
