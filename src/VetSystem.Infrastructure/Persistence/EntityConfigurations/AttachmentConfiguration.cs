using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.VisitId).HasColumnName("visit_id").IsRequired();
        builder.Property(a => a.FileType).HasColumnName("file_type").IsRequired().HasMaxLength(8);
        builder.Property(a => a.Url).HasColumnName("url");
        builder.Property(a => a.Title).HasColumnName("title").HasMaxLength(256);
        builder.Property(a => a.DocDate).HasColumnName("doc_date");
        builder.Property(a => a.Description).HasColumnName("description");
        builder.Property(a => a.UploadStatus).HasColumnName("upload_status").IsRequired().HasMaxLength(12);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_attachments_file_type", "file_type IN ('photo','pdf')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_attachments_upload_status", "upload_status IN ('pending','uploaded','failed')"));

        builder.HasIndex(a => a.VisitId).HasDatabaseName("ix_attachments_visit");

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(a => a.VisitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
