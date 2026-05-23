using Mapster;
using VetSystem.Application.Appointments.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Appointments.Mapping;

public sealed class AppointmentsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Appointment, AppointmentResponse>();
    }
}
