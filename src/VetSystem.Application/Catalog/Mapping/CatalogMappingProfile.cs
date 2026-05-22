using Mapster;
using VetSystem.Application.Catalog.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Catalog.Mapping;

/// <summary>
/// Mapster profile for products and services. Loaded by assembly scan via
/// <see cref="VetSystem.Application.DependencyInjection.AddApplication"/>.
/// </summary>
public sealed class CatalogMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Product, ProductResponse>();
        config.NewConfig<Service, ServiceResponse>();
    }
}
