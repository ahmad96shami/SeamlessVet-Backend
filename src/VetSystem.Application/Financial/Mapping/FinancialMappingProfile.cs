using Mapster;
using VetSystem.Application.Financial.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Financial.Mapping;

/// <summary>
/// Child-row mappings. <see cref="InvoiceResponse"/> is composed by hand in the service (it carries
/// its item + payment lists), so only the leaf rows are registered here.
/// </summary>
public sealed class FinancialMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<InvoiceItem, InvoiceItemResponse>();
        config.NewConfig<Payment, PaymentResponse>();
        config.NewConfig<ReceiptVoucher, ReceiptVoucherResponse>();
    }
}
