using Mapster;
using VetSystem.Application.Customers.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Customers.Mapping;

public sealed class CustomersMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Customer, CustomerResponse>();
    }
}
