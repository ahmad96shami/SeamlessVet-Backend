using Mapster;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Inventory.Mapping;

public sealed class InventoryMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<StockItem, StockItemResponse>();
        config.NewConfig<InventoryMovement, InventoryMovementResponse>();
    }
}
