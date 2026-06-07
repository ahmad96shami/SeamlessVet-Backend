using FluentValidation;
using VetSystem.Application.Contracts.Contracts;

namespace VetSystem.Application.Contracts.Validators;

/// <summary>
/// Static-shape validation for the settle request. Existence/consistency checks (batch, products
/// actually on the batch's invoices, ledger/entitlement guards) run in the service. An empty
/// <c>Lines</c> list is valid — settling with no re-pricing is the plain "close the cycle" path.
/// </summary>
public sealed class BatchSettlementRequestValidator : AbstractValidator<BatchSettlementRequest>
{
    public BatchSettlementRequestValidator()
    {
        RuleFor(r => r.Lines).NotNull();

        RuleForEach(r => r.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.SettledUnitPrice).GreaterThanOrEqualTo(0m);
        });

        RuleFor(r => r.Lines)
            .Must(lines => lines.Select(l => l.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("lines must not repeat a product.")
            .When(r => r.Lines is not null);

        RuleFor(r => r.DiscountAmount).GreaterThanOrEqualTo(0m);

        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}
