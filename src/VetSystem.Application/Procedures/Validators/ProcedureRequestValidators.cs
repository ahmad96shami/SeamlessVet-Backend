using FluentValidation;
using VetSystem.Application.Procedures.Contracts;

namespace VetSystem.Application.Procedures.Validators;

public sealed class ProcedureCreateRequestValidator : AbstractValidator<ProcedureCreateRequest>
{
    public ProcedureCreateRequestValidator()
    {
        RuleFor(r => r.VisitId).NotEmpty();
        RuleFor(r => r.Price!.Value).GreaterThanOrEqualTo(0).When(r => r.Price.HasValue);
    }
}

public sealed class ProcedurePatchRequestValidator : AbstractValidator<ProcedurePatchRequest>
{
    public ProcedurePatchRequestValidator()
    {
        RuleFor(r => r.Price!.Value).GreaterThanOrEqualTo(0).When(r => r.Price.HasValue);
    }
}
