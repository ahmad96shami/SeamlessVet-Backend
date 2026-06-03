using Mapster;
using VetSystem.Application.SupplierLedgers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.SupplierLedgers.Mapping;

public sealed class SupplierLedgersMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<SupplierLedgerEntry, SupplierLedgerEntryResponse>();
    }
}
