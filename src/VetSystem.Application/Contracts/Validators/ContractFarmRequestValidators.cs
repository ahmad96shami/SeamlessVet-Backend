using FluentValidation;
using VetSystem.Application.Contracts.Contracts;

namespace VetSystem.Application.Contracts.Validators;

public sealed class ContractFarmAttachRequestValidator : AbstractValidator<ContractFarmAttachRequest>
{
    public ContractFarmAttachRequestValidator()
    {
        RuleFor(r => r.FarmId).NotEmpty();
    }
}
