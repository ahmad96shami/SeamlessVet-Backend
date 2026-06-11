using Mapster;
using VetSystem.Application.EmployeeLedgers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.EmployeeLedgers.Mapping;

public sealed class EmployeeLedgersMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<EmployeeLedgerEntry, EmployeeLedgerEntryResponse>();
    }
}
