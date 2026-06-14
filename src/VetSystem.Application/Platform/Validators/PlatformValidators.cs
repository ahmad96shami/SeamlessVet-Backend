using FluentValidation;
using VetSystem.Application.Platform.Contracts;
using VetSystem.Application.Provisioning;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Platform.Validators;

public sealed class PlatformLoginRequestValidator : AbstractValidator<PlatformLoginRequest>
{
    public PlatformLoginRequestValidator()
    {
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(32);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
    }
}

/// <summary>
/// M35 — validates a platform-console center-provisioning request (POST /platform/tenants). The code
/// is the globally-unique support handle; mode defaults to <c>solo</c> when blank (the service
/// applies the same default), so empty is allowed but a non-empty value must be a known mode.
/// </summary>
public sealed class ProvisionEnvironmentRequestValidator : AbstractValidator<ProvisionEnvironmentRequest>
{
    public ProvisionEnvironmentRequestValidator()
    {
        RuleFor(r => r.CenterName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Code).NotEmpty().MaximumLength(32)
            .Matches(@"^[A-Za-z0-9][A-Za-z0-9\-]*$")
            .WithMessage("Code must be alphanumeric with dashes (no spaces).");
        RuleFor(r => r.Mode).Must(m => EnvironmentMode.All.Contains(m))
            .When(r => !string.IsNullOrWhiteSpace(r.Mode))
            .WithMessage("Mode must be 'solo' or 'partnership'.");
        RuleFor(r => r.AdminFullName).NotEmpty().MaximumLength(128);
        RuleFor(r => r.AdminPhone).NotEmpty().MaximumLength(32)
            .Matches(@"^\+?[0-9\- ]{7,32}$")
            .WithMessage("AdminPhone must be 7–32 digits, optionally with + - or spaces.");
        RuleFor(r => r.AdminPassword).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(r => r.AdminEmail).EmailAddress().MaximumLength(255)
            .When(r => !string.IsNullOrWhiteSpace(r.AdminEmail));
    }
}
