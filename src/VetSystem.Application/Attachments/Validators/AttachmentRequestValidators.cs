using FluentValidation;
using VetSystem.Application.Attachments.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Attachments.Validators;

public sealed class PresignedUploadRequestValidator : AbstractValidator<PresignedUploadRequest>
{
    public PresignedUploadRequestValidator()
    {
        RuleFor(r => r.VisitId).NotEmpty();
        RuleFor(r => r.FileType)
            .Must(AttachmentType.All.Contains)
            .WithMessage($"FileType must be one of: {string.Join(", ", AttachmentType.All)}.");
        RuleFor(r => r.Title).MaximumLength(256);
    }
}

public sealed class AttachmentConfirmRequestValidator : AbstractValidator<AttachmentConfirmRequest>
{
    private static readonly string[] Confirmable = [UploadStatus.Uploaded, UploadStatus.Failed];

    public AttachmentConfirmRequestValidator()
    {
        RuleFor(r => r.UploadStatus!)
            .Must(Confirmable.Contains)
            .WithMessage($"UploadStatus must be one of: {string.Join(", ", Confirmable)}.")
            .When(r => r.UploadStatus is not null);
    }
}
