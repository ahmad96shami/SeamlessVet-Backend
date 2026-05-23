using Mapster;
using VetSystem.Application.Entitlements.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Entitlements.Mapping;

public sealed class EntitlementsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<DoctorEntitlement, DoctorEntitlementResponse>();
    }
}
