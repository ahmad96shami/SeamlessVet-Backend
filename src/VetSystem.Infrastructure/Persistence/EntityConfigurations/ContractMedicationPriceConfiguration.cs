using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class ContractMedicationPriceConfiguration : IEntityTypeConfiguration<ContractMedicationPrice>
{
    public void Configure(EntityTypeBuilder<ContractMedicationPrice> builder)
    {
        builder.ToTable("contract_medication_prices");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.ContractId).HasColumnName("contract_id").IsRequired();
        builder.Property(p => p.ProductId).HasColumnName("product_id").IsRequired();
        builder.Property(p => p.ContractPrice).HasColumnName("contract_price").HasColumnType("numeric(14,2)").IsRequired();

        // SCHEMA §5 — one override per product within a contract.
        builder.HasIndex(p => new { p.ContractId, p.ProductId })
            .HasDatabaseName("ux_contract_medication_prices_contract_product")
            .IsUnique();

        builder.HasOne<Contract>()
            .WithMany()
            .HasForeignKey(p => p.ContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
