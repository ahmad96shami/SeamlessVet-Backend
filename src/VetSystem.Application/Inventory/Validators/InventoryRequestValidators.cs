using FluentValidation;
using VetSystem.Application.Inventory.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Inventory.Validators;

public sealed class ReceiveStockRequestValidator : AbstractValidator<ReceiveStockRequest>
{
    public ReceiveStockRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.Quantity).GreaterThan(0m).WithMessage("Quantity must be greater than zero.");
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Reason).MaximumLength(512);
        RuleFor(r => r.UnitCost!.Value).GreaterThanOrEqualTo(0m).When(r => r.UnitCost.HasValue);
        RuleFor(r => r.LotNumber).MaximumLength(64);
    }
}

public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.LocationType)
            .Must(t => StockLocation.All.Contains(t))
            .WithMessage($"LocationType must be one of: {string.Join(", ", StockLocation.All)}.");
        RuleFor(r => r.LocationId).NotEmpty();
        RuleFor(r => r.QuantityDelta).NotEqual(0m).WithMessage("QuantityDelta must be non-zero (signed).");
        RuleFor(r => r.Reason).NotEmpty().WithMessage("Adjustments require a reason.").MaximumLength(512);
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
    }
}

public sealed class LoadFieldRequestValidator : AbstractValidator<LoadFieldRequest>
{
    public LoadFieldRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.FieldInventoryId).NotEmpty();
        RuleFor(r => r.Quantity).GreaterThan(0m).WithMessage("Quantity must be greater than zero.");
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Reason).MaximumLength(512);
    }
}

public sealed class UnloadFieldRequestValidator : AbstractValidator<UnloadFieldRequest>
{
    public UnloadFieldRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.FieldInventoryId).NotEmpty();
        RuleFor(r => r.Quantity).GreaterThan(0m).WithMessage("Quantity must be greater than zero.");
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(r => r.Reason).MaximumLength(512);
    }
}

public sealed class ConsumeStockRequestValidator : AbstractValidator<ConsumeStockRequest>
{
    public ConsumeStockRequestValidator()
    {
        RuleFor(r => r.ProductId).NotEmpty();
        RuleFor(r => r.Quantity).GreaterThan(0m).WithMessage("Quantity must be greater than zero.");
        RuleFor(r => r.Reason).NotEmpty().WithMessage("Consumption requires a reason.").MaximumLength(512);
        RuleFor(r => r.IdempotencyKey).NotEmpty().MaximumLength(128);

        // Location is optional (omit both ⇒ central warehouse), but both parts must come together.
        RuleFor(r => r.LocationType!)
            .Must(t => StockLocation.All.Contains(t))
            .WithMessage($"LocationType must be one of: {string.Join(", ", StockLocation.All)}.")
            .When(r => r.LocationType is not null);
        RuleFor(r => r.LocationId)
            .NotEmpty().WithMessage("LocationId is required when LocationType is set.")
            .When(r => r.LocationType is not null);
        RuleFor(r => r.LocationType)
            .NotEmpty().WithMessage("LocationType is required when LocationId is set.")
            .When(r => r.LocationId is not null);
    }
}
