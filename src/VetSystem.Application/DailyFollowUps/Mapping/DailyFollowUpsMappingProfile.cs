using Mapster;
using VetSystem.Application.DailyFollowUps.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.DailyFollowUps.Mapping;

public sealed class DailyFollowUpsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<DailyFollowUp, DailyFollowUpResponse>();
    }
}
