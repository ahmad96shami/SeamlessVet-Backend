using FluentValidation;
using VetSystem.Application.Identity.Contracts;

namespace VetSystem.Application.Identity.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.EnvironmentId).NotEmpty().WithMessage("A center must be selected.");
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(128);
        RuleFor(r => r.PhonePrimary).NotEmpty().MaximumLength(32)
            .Matches(@"^\+?[0-9\- ]{7,32}$")
            .WithMessage("PhonePrimary must be 7–32 digits, optionally with + - or spaces.");
        RuleFor(r => r.Email).EmailAddress().MaximumLength(255).When(r => !string.IsNullOrWhiteSpace(r.Email));
        RuleFor(r => r.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(r => r.RequestedRoleKey).NotEmpty();
        RuleFor(r => r.LicenseNumber).MaximumLength(64);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(r => r.EnvironmentId).NotEmpty().WithMessage("A center must be selected.");
        RuleFor(r => r.PhonePrimary).NotEmpty().MaximumLength(32);
        RuleFor(r => r.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class CentersLookupRequestValidator : AbstractValidator<CentersLookupRequest>
{
    public CentersLookupRequestValidator()
    {
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(32);
    }
}

public sealed class CenterByCodeRequestValidator : AbstractValidator<CenterByCodeRequest>
{
    public CenterByCodeRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(32);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(r => r.RefreshToken).NotEmpty().MaximumLength(512);
    }
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(r => r.RefreshToken).NotEmpty().MaximumLength(512);
    }
}

public sealed class ApproveRequestValidator : AbstractValidator<ApproveRequest>
{
    public ApproveRequestValidator()
    {
        RuleFor(r => r.Notes).MaximumLength(1024);
    }
}

public sealed class RejectRequestValidator : AbstractValidator<RejectRequest>
{
    public RejectRequestValidator()
    {
        RuleFor(r => r.Notes).NotEmpty().MaximumLength(1024)
            .WithMessage("Reject must include audit notes.");
    }
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(128);
        RuleFor(r => r.PhonePrimary).NotEmpty().MaximumLength(32)
            .Matches(@"^\+?[0-9\- ]{7,32}$")
            .WithMessage("PhonePrimary must be 7–32 digits, optionally with + - or spaces.");
        RuleFor(r => r.Email).EmailAddress().MaximumLength(255).When(r => !string.IsNullOrWhiteSpace(r.Email));
        RuleFor(r => r.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(r => r.RoleKey).NotEmpty();
        RuleFor(r => r.LicenseNumber).MaximumLength(64);
    }
}

public sealed class PermissionOverrideRequestValidator : AbstractValidator<PermissionOverrideRequest>
{
    public PermissionOverrideRequestValidator()
    {
        RuleFor(r => r.PermissionKey).NotEmpty().MaximumLength(64);
        RuleFor(r => r.Effect).NotEmpty().Must(e => e == "grant" || e == "deny")
            .WithMessage("Effect must be 'grant' or 'deny'.");
    }
}
