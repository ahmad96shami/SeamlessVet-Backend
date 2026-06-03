using Mapster;
using VetSystem.Application.Purchasing.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Purchasing.Mapping;

/// <summary>
/// Leaf-row mappings. <see cref="PurchaseInvoiceResponse"/> carries its item list and is composed by
/// hand in the service (mirrors the sales <c>FinancialMappingProfile</c>).
/// </summary>
public sealed class PurchasingMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<PurchaseInvoiceItem, PurchaseInvoiceItemResponse>();
        config.NewConfig<SupplierPayment, SupplierPaymentResponse>();
    }
}
