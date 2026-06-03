using Mapster;
using VetSystem.Application.Suppliers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Suppliers.Mapping;

public sealed class SuppliersMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // Balance + LedgerStatus default here and are layered on in the service from the supplier ledger
        // (mirrors CustomersMappingProfile, where the aggregate ledger state is applied via `with`).
        config.NewConfig<Supplier, SupplierResponse>();
    }
}
