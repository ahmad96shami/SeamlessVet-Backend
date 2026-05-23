using Mapster;
using VetSystem.Application.Attachments.Contracts;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Attachments.Mapping;

public sealed class AttachmentsMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        // DownloadUrl / DownloadUrlExpiresAt have no source on the entity — they are the signed GET
        // URL the service fills in on reads. Mapster leaves these constructor args at their default
        // (null), which is exactly what list/non-read mappings want.
        config.NewConfig<Attachment, AttachmentResponse>();
    }
}
