using FluentValidation;
using VetSystem.Application.Contracts.Contracts;

namespace VetSystem.Application.Contracts.Validators;

public sealed class ContractMedicationPriceCreateRequestValidator : AbstractValidator<ContractMedicationPriceCreateRequest>
{
    public ContractMedicationPriceCreateRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.ContractPrice).GreaterThanOrEqualTo(0m);
    }
}

public sealed class ContractMedicationPricePatchRequestValidator : AbstractValidator<ContractMedicationPricePatchRequest>
{
    public ContractMedicationPricePatchRequestValidator()
    {
        RuleFor(r => r.ContractPrice!.Value).GreaterThanOrEqualTo(0m).When(r => r.ContractPrice.HasValue);
    }
}
