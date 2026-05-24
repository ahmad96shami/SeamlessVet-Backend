using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M14_SyncScopeDenormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "visit_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                table: "invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "visit_id",
                table: "invoice_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_customer",
                table: "payments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_visit",
                table: "payments",
                column: "visit_id");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_customer",
                table: "ledger_entries",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_customer",
                table: "invoice_items",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_items_visit",
                table: "invoice_items",
                column: "visit_id");

            // M14 — PowerSync sync rules can't JOIN, so child tables expose their per-doctor scope key
            // (customer_id / visit_id) directly. These keys are derived server-side from the immutable
            // parent FK by triggers below, so bucketing never trusts a client-supplied value and a
            // customer reassignment moves the whole subtree automatically (only the parent FK matters).
            // Backfill existing rows first.
            migrationBuilder.Sql(
                "UPDATE ledger_entries le SET customer_id = l.customer_id " +
                "FROM ledgers l WHERE le.ledger_id = l.id;");
            migrationBuilder.Sql(
                "UPDATE invoice_items ii SET customer_id = i.customer_id, visit_id = i.visit_id " +
                "FROM invoices i WHERE ii.invoice_id = i.id;");
            migrationBuilder.Sql(
                "UPDATE payments p SET customer_id = i.customer_id, visit_id = i.visit_id " +
                "FROM invoices i WHERE p.invoice_id = i.id;");
            migrationBuilder.Sql(
                "UPDATE vaccinations v SET customer_id = p.customer_id " +
                "FROM pets p WHERE v.pet_id = p.id;");

            // ledger_entries / invoice_items / payments are append-only → BEFORE INSERT is enough.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION vet_set_ledger_entry_scope() RETURNS trigger AS $$
                BEGIN
                    SELECT l.customer_id INTO NEW.customer_id FROM ledgers l WHERE l.id = NEW.ledger_id;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_ledger_entries_scope BEFORE INSERT ON ledger_entries
                    FOR EACH ROW EXECUTE FUNCTION vet_set_ledger_entry_scope();
                """);
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION vet_set_invoice_child_scope() RETURNS trigger AS $$
                BEGIN
                    SELECT i.customer_id, i.visit_id INTO NEW.customer_id, NEW.visit_id
                    FROM invoices i WHERE i.id = NEW.invoice_id;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_invoice_items_scope BEFORE INSERT ON invoice_items
                    FOR EACH ROW EXECUTE FUNCTION vet_set_invoice_child_scope();
                CREATE TRIGGER trg_payments_scope BEFORE INSERT ON payments
                    FOR EACH ROW EXECUTE FUNCTION vet_set_invoice_child_scope();
                """);
            // vaccinations.customer_id is a pre-existing column the app reads; the trigger keeps it equal
            // to the pet's owner (when pet_id is set) on INSERT and UPDATE so scoping is server-authoritative.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION vet_set_vaccination_scope() RETURNS trigger AS $$
                BEGIN
                    IF NEW.pet_id IS NOT NULL THEN
                        SELECT p.customer_id INTO NEW.customer_id FROM pets p WHERE p.id = NEW.pet_id;
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER trg_vaccinations_scope BEFORE INSERT OR UPDATE ON vaccinations
                    FOR EACH ROW EXECUTE FUNCTION vet_set_vaccination_scope();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_vaccinations_scope ON vaccinations;
                DROP TRIGGER IF EXISTS trg_payments_scope ON payments;
                DROP TRIGGER IF EXISTS trg_invoice_items_scope ON invoice_items;
                DROP TRIGGER IF EXISTS trg_ledger_entries_scope ON ledger_entries;
                DROP FUNCTION IF EXISTS vet_set_vaccination_scope();
                DROP FUNCTION IF EXISTS vet_set_invoice_child_scope();
                DROP FUNCTION IF EXISTS vet_set_ledger_entry_scope();
                """);

            migrationBuilder.DropIndex(
                name: "ix_payments_customer",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_visit",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_customer",
                table: "ledger_entries");

            migrationBuilder.DropIndex(
                name: "ix_invoice_items_customer",
                table: "invoice_items");

            migrationBuilder.DropIndex(
                name: "ix_invoice_items_visit",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "customer_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "visit_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "customer_id",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "customer_id",
                table: "invoice_items");

            migrationBuilder.DropColumn(
                name: "visit_id",
                table: "invoice_items");
        }
    }
}
