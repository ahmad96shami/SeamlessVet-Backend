using Mapster;
using VetSystem.Application.DoctorPartners.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.DoctorPartners.Mapping;

public sealed class DoctorPartnersMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // DoctorName + Balance + LedgerStatus default here and are layered on in the service
        // (DoctorName from the linked user, the ledger state from the partner ledger).
        config.NewConfig<DoctorPartner, DoctorPartnerResponse>();
        config.NewConfig<DoctorPartnerPayment, DoctorPartnerPaymentResponse>();
    }
}
