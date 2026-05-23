using FluentValidation;

namespace VetSystem.Application.Partnership.Validators;

/// <summary>
/// Static-shape validation for partnership shares: per-row percent bounds (0–100) and date sanity.
/// The cross-row "active shares ≤ 100% on every date" invariant is enforced by
/// <see cref="IPartnershipValidator"/> in the service (it needs the environment's other shares).
/// </summary>
public sealed class PartnershipShareCreateRequestValidator : AbstractValidator<PartnershipShareCreateRequest>
{
    public PartnershipShareCreateRequestValidator()
    {
        RuleFor(r => r.PartnerId).NotEmpty();
        RuleFor(r => r.SharePercent).InclusiveBetween(0m, 100m);

        RuleFor(r => r.EffectiveTo!.Value)
            .GreaterThanOrEqualTo(r => r.EffectiveFrom)
            .WithMessage("effective_to must be on or after effective_from.")
            .When(r => r.EffectiveTo.HasValue);
    }
}

public sealed class PartnershipSharePatchRequestValidator : AbstractValidator<PartnershipSharePatchRequest>
{
    public PartnershipSharePatchRequestValidator()
    {
        RuleFor(r => r.SharePercent!.Value).InclusiveBetween(0m, 100m).When(r => r.SharePercent.HasValue);

        RuleFor(r => r.EffectiveTo!.Value)
            .GreaterThanOrEqualTo(r => r.EffectiveFrom!.Value)
            .WithMessage("effective_to must be on or after effective_from.")
            .When(r => r.EffectiveTo.HasValue && r.EffectiveFrom.HasValue);
    }
}
