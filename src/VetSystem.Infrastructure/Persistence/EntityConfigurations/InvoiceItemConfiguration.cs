using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("invoice_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.InvoiceId).HasColumnName("invoice_id").IsRequired();
        builder.Property(i => i.ProductId).HasColumnName("product_id");
        builder.Property(i => i.ServiceId).HasColumnName("service_id");
        builder.Property(i => i.Description).HasColumnName("description");
        builder.Property(i => i.Quantity).HasColumnName("quantity").HasColumnType("numeric(14,3)");
        builder.Property(i => i.UnitPrice).HasColumnName("unit_price").HasColumnType("numeric(14,2)");
        builder.Property(i => i.CostPrice).HasColumnName("cost_price").HasColumnType("numeric(14,2)");
        builder.Property(i => i.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(14,2)");
        builder.Property(i => i.LineTotal).HasColumnName("line_total").HasColumnType("numeric(14,2)");
        builder.Property(i => i.PrescriptionId).HasColumnName("prescription_id");
        builder.Property(i => i.ProcedureId).HasColumnName("procedure_id");

        // SCHEMA §8 — a line targets exactly one of product / service.
        builder.ToTable(t => t.HasCheckConstraint(
            "chk_item_target",
            "(product_id IS NOT NULL AND service_id IS NULL) OR (product_id IS NULL AND service_id IS NOT NULL)"));

        builder.HasIndex(i => i.InvoiceId).HasDatabaseName("ix_invoice_items_invoice");

        // M14 — denormalized scope keys for PowerSync's by_customer / by_visit buckets (single-table
        // data queries can't JOIN to invoices). Shadow properties kept off the Domain entity; a BEFORE
        // INSERT trigger copies the parent invoice's customer_id + visit_id (migration
        // M14_SyncScopeDenormalization). invoice_items are append-only — insert-time population suffices.
        builder.Property<Guid?>("CustomerId").HasColumnName("customer_id");
        builder.Property<Guid?>("VisitId").HasColumnName("visit_id");
        builder.HasIndex("CustomerId").HasDatabaseName("ix_invoice_items_customer");
        builder.HasIndex("VisitId").HasDatabaseName("ix_invoice_items_visit");

        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(i => i.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Service>()
            .WithMany()
            .HasForeignKey(i => i.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Prescription>()
            .WithMany()
            .HasForeignKey(i => i.PrescriptionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Procedure>()
            .WithMany()
            .HasForeignKey(i => i.ProcedureId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
