using Mapster;
using VetSystem.Application.Settings.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Settings.Mapping;

public sealed class SystemSettingsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<SystemSettings, SystemSettingsResponse>();
    }
}
