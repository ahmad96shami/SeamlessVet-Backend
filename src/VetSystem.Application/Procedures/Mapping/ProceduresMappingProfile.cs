using Mapster;
using VetSystem.Application.Procedures.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Procedures.Mapping;

public sealed class ProceduresMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Procedure, ProcedureResponse>();
    }
}
