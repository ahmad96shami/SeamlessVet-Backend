using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VetSystem.Domain.Entities;

namespace VetSystem.Infrastructure.Persistence.EntityConfigurations;

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.InvoiceType).HasColumnName("invoice_type").IsRequired().HasMaxLength(16);
        builder.Property(i => i.CustomerId).HasColumnName("customer_id");
        builder.Property(i => i.VisitId).HasColumnName("visit_id");
        builder.Property(i => i.BatchId).HasColumnName("batch_id");
        builder.Property(i => i.Number).HasColumnName("number").HasMaxLength(64);
        builder.Property(i => i.Subtotal).HasColumnName("subtotal").HasColumnType("numeric(14,2)");
        builder.Property(i => i.DiscountAmount).HasColumnName("discount_amount").HasColumnType("numeric(14,2)");
        builder.Property(i => i.TaxAmount).HasColumnName("tax_amount").HasColumnType("numeric(14,2)");
        builder.Property(i => i.Total).HasColumnName("total").HasColumnType("numeric(14,2)");
        builder.Property(i => i.Status).HasColumnName("status").IsRequired().HasMaxLength(16);
        builder.Property(i => i.IssuedBy).HasColumnName("issued_by").IsRequired();
        builder.Property(i => i.IssuedAt).HasColumnName("issued_at");
        builder.Property(i => i.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(128);
        builder.Property(i => i.VoidOfInvoiceId).HasColumnName("void_of_invoice_id");

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_invoices_type", "invoice_type IN ('pos','field','exam_fee')"));
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_invoices_status", "status IN ('issued','flagged','void')"));

        // SCHEMA §8 — dedupe + offline-safe numbering, both UNIQUE per environment. NULL number rows
        // (void markers) don't collide since Postgres treats NULLs as distinct.
        builder.HasIndex(i => new { i.EnvironmentId, i.IdempotencyKey })
            .HasDatabaseName("ux_invoices_env_idempotency")
            .IsUnique();
        builder.HasIndex(i => new { i.EnvironmentId, i.Number })
            .HasDatabaseName("ux_invoices_env_number")
            .IsUnique();

        builder.HasIndex(i => new { i.EnvironmentId, i.CustomerId, i.IssuedAt })
            .HasDatabaseName("ix_invoices_customer")
            .IsDescending(false, false, true);
        builder.HasIndex(i => new { i.EnvironmentId, i.BatchId })
            .HasDatabaseName("ix_invoices_batch");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Visit>()
            .WithMany()
            .HasForeignKey(i => i.VisitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(i => i.IssuedBy)
            .OnDelete(DeleteBehavior.Restrict);

        // A void row points back to the invoice it reverses.
        builder.HasOne<Invoice>()
            .WithMany()
            .HasForeignKey(i => i.VoidOfInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        // batch_id FK target lands in M8; the column exists now so field invoices can carry it.
    }
}
