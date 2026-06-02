using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ContractFarmConfiguration : IEntityTypeConfiguration<ContractFarm>
{
    public void Configure(EntityTypeBuilder<ContractFarm> builder)
    {
        builder.ToTable("contract_farms");

        builder.HasKey(cf => cf.Id);
        builder.Property(cf => cf.ContractId).HasColumnName("contract_id").IsRequired();
        builder.Property(cf => cf.FarmId).HasColumnName("farm_id").IsRequired();

        // SCHEMA §5 (M15) — one attachment per farm within a contract.
        builder.HasIndex(cf => new { cf.ContractId, cf.FarmId })
            .HasDatabaseName("ux_contract_farms_contract_farm")
            .IsUnique();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(cf => cf.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Farm>()
            .WithMany()
            .HasForeignKey(cf => cf.FarmId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
