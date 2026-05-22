using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class UserPermissionOverrideConfiguration : IEntityTypeConfiguration<UserPermissionOverride>
{
    public void Configure(EntityTypeBuilder<UserPermissionOverride> builder)
    {
        builder.ToTable("user_permission_overrides");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.UserId).HasColumnName("user_id");
        builder.Property(o => o.PermissionId).HasColumnName("permission_id");
        builder.Property(o => o.Effect).HasColumnName("effect").IsRequired().HasMaxLength(8);

        builder.HasOne<User>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Permission>().WithMany().HasForeignKey(o => o.PermissionId).OnDelete(DeleteBehavior.Cascade);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_user_permission_overrides_effect",
            "effect IN ('grant','deny')"));

        builder.HasIndex(o => new { o.UserId, o.PermissionId })
            .HasDatabaseName("ux_user_permission_overrides_user_perm")
            .IsUnique();
    }
}
