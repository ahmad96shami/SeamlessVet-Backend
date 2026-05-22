using FluentValidation;
using VetSystem.Application.Catalog.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Catalog.Validators;

public sealed class ProductRequestValidator : AbstractValidator<ProductRequest>
{
    public ProductRequestValidator()
    {
        RuleFor(r => r.NameAr).NotEmpty().MaximumLength(256);
        RuleFor(r => r.NameLatin).MaximumLength(256);
        RuleFor(r => r.Barcode).MaximumLength(64);
        RuleFor(r => r.Category)
            .NotEmpty()
            .Must(c => ProductCategory.All.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", ProductCategory.All)}.");
        RuleFor(r => r.Manufacturer).MaximumLength(128);
        RuleFor(r => r.Supplier).MaximumLength(128);
        RuleFor(r => r.PurchasePrice).GreaterThanOrEqualTo(0).WithMessage("PurchasePrice must be ≥ 0.");
        RuleFor(r => r.SellingPrice).GreaterThanOrEqualTo(0).WithMessage("SellingPrice must be ≥ 0.");
        RuleFor(r => r.UnitOfMeasure).MaximumLength(32);
        RuleFor(r => r.ReorderPoint).GreaterThanOrEqualTo(0).WithMessage("ReorderPoint must be ≥ 0.");
    }
}

public sealed class ProductPatchRequestValidator : AbstractValidator<ProductPatchRequest>
{
    public ProductPatchRequestValidator()
    {
        RuleFor(r => r.NameAr!).NotEmpty().MaximumLength(256).When(r => r.NameAr is not null);
        RuleFor(r => r.NameLatin).MaximumLength(256);
        RuleFor(r => r.Barcode).MaximumLength(64);
        RuleFor(r => r.Category!)
            .Must(c => ProductCategory.All.Contains(c))
            .WithMessage($"Category must be one of: {string.Join(", ", ProductCategory.All)}.")
            .When(r => r.Category is not null);
        RuleFor(r => r.Manufacturer).MaximumLength(128);
        RuleFor(r => r.Supplier).MaximumLength(128);
        RuleFor(r => r.PurchasePrice!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("PurchasePrice must be ≥ 0.")
            .When(r => r.PurchasePrice.HasValue);
        RuleFor(r => r.SellingPrice!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("SellingPrice must be ≥ 0.")
            .When(r => r.SellingPrice.HasValue);
        RuleFor(r => r.UnitOfMeasure).MaximumLength(32);
        RuleFor(r => r.ReorderPoint!.Value)
            .GreaterThanOrEqualTo(0).WithMessage("ReorderPoint must be ≥ 0.")
            .When(r => r.ReorderPoint.HasValue);
    }
}
