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
