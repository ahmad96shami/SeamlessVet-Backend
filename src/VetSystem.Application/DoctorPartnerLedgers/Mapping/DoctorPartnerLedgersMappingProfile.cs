using Mapster;
using VetSystem.Application.DoctorPartnerLedgers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.DoctorPartnerLedgers.Mapping;

public sealed class DoctorPartnerLedgersMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<DoctorPartnerLedgerEntry, DoctorPartnerLedgerEntryResponse>();
    }
}
