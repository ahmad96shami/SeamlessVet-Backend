using Mapster;
using VetSystem.Application.Prescriptions.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Prescriptions.Mapping;

public sealed class PrescriptionsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Prescription, PrescriptionResponse>();
    }
}
