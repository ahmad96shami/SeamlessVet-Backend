using FluentValidation;
using VetSystem.Application.Devices.Contracts;

namespace VetSystem.Application.Devices.Validators;

public sealed class RegisterPushTokenRequestValidator : AbstractValidator<RegisterPushTokenRequest>
{
    public RegisterPushTokenRequestValidator()
    {
        // 512 matches the column; Expo tokens are ~40 chars but the bound is the schema's, not Expo's.
        RuleFor(r => r.Token).NotEmpty().MaximumLength(512);
        RuleFor(r => r.Platform)
            .Must(p => p is "android" or "ios")
            .WithMessage("Platform must be 'android' or 'ios'.");
    }
}

public sealed class UnregisterPushTokenRequestValidator : AbstractValidator<UnregisterPushTokenRequest>
{
    public UnregisterPushTokenRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty().MaximumLength(512);
    }
}
