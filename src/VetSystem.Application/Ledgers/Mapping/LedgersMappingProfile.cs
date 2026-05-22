using Mapster;
using VetSystem.Application.Ledgers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Ledgers.Mapping;

public sealed class LedgersMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Ledger, LedgerResponse>();
        config.NewConfig<LedgerEntry, LedgerEntryResponse>();
    }
}
