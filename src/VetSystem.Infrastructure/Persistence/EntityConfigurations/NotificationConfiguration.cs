using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(n => n.Type).HasColumnName("type").IsRequired().HasMaxLength(48);
        builder.Property(n => n.Title).HasColumnName("title");
        builder.Property(n => n.Body).HasColumnName("body");
        builder.Property(n => n.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(n => n.ReadAt).HasColumnName("read_at");

        // SCHEMA §9 — feed lookup is "this user's notifications, newest first".
        builder.HasIndex(n => new { n.UserId, n.CreatedAt })
            .HasDatabaseName("ix_notifications_user")
            .IsDescending(false, true);

        var allowedTypes = string.Join(",", NotificationType.All.Select(t => $"'{t}'"));
        builder.ToTable(t => t.HasCheckConstraint("ck_notifications_type", $"type IN ({allowedTypes})"));

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
