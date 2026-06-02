using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M15_Farms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_type",
                table: "customers");

            migrationBuilder.AddColumn<Guid>(
                name: "farm_id",
                table: "visits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "farm_id",
                table: "pets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "farm_id",
                table: "invoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "farm_id",
                table: "batches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "farms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    location = table.Column<string>(type: "text", nullable: true),
                    animal_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    head_count = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_farms", x => x.id);
                    table.CheckConstraint("ck_farms_kind", "kind IN ('poultry','cattle','mixed','other')");
                    table.ForeignKey(
                        name: "fk_farms_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contract_farms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    farm_id = table.Column<Guid>(type: "uuid", nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contract_farms", x => x.id);
                    table.ForeignKey(
                        name: "fk_contract_farms_contracts_contract_id",
                        column: x => x.contract_id,
                        principalTable: "contracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contract_farms_farms_farm_id",
                        column: x => x.farm_id,
                        principalTable: "farms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_visits_farm",
                table: "visits",
                columns: new[] { "environment_id", "farm_id" });

            migrationBuilder.CreateIndex(
                name: "ix_visits_farm_id",
                table: "visits",
                column: "farm_id");

            migrationBuilder.CreateIndex(
                name: "ix_pets_farm_id",
                table: "pets",
                column: "farm_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_farm",
                table: "invoices",
                columns: new[] { "environment_id", "farm_id" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_farm_id",
                table: "invoices",
                column: "farm_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_type",
                table: "customers",
                sql: "type IN ('regular_farm','home','cattle_farm','poultry_farm','clinic_customer')");

            migrationBuilder.CreateIndex(
                name: "ix_batches_farm",
                table: "batches",
                columns: new[] { "environment_id", "farm_id" });

            migrationBuilder.CreateIndex(
                name: "ix_batches_farm_id",
                table: "batches",
                column: "farm_id");

            migrationBuilder.CreateIndex(
                name: "ix_contract_farms_environment_id_deleted_at",
                table: "contract_farms",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_farms_environment_id_updated_at",
                table: "contract_farms",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contract_farms_farm_id",
                table: "contract_farms",
                column: "farm_id");

            migrationBuilder.CreateIndex(
                name: "ux_contract_farms_contract_farm",
                table: "contract_farms",
                columns: new[] { "contract_id", "farm_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_farms_customer",
                table: "farms",
                columns: new[] { "environment_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_farms_customer_id",
                table: "farms",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_farms_environment_id_deleted_at",
                table: "farms",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_farms_environment_id_updated_at",
                table: "farms",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_batches_farms_farm_id",
                table: "batches",
                column: "farm_id",
                principalTable: "farms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_invoices_farms_farm_id",
                table: "invoices",
                column: "farm_id",
                principalTable: "farms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_pets_farms_farm_id",
                table: "pets",
                column: "farm_id",
                principalTable: "farms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_visits_farms_farm_id",
                table: "visits",
                column: "farm_id",
                principalTable: "farms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            BackfillDefaultFarms(migrationBuilder);
        }

        /// <summary>
        /// M15 part 2 (data) — give every existing farm-type customer a default farm and re-point
        /// its pre-existing batches / field visits / field invoices and contracts onto it, so the new
        /// farm attribution is populated without any financial change (the ledger stays single-ledger
        /// this milestone). Runs inside the migration transaction (atomic with the schema DDL above).
        ///
        /// Ids use core <c>gen_random_uuid()</c>: these are one-time server-side backfill rows, not
        /// client-minted creations, so the Guid-v7 convention (client-side, time-ordered, sync-
        /// convergent) does not apply and PG17 has no <c>uuidv7()</c>. Scope is active rows only
        /// (<c>deleted_at IS NULL</c>); soft-deleted history keeps its null farm_id.
        /// </summary>
        private static void BackfillDefaultFarms(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- 1) One default farm per farm-owning customer (home / clinic_customer get none). Named from the
--    customer; kind derived from the customer type; location seeded from the customer address.
INSERT INTO farms (id, customer_id, name, kind, location, animal_type, head_count, notes,
                   environment_id, created_at, updated_at)
SELECT gen_random_uuid(),
       c.id,
       c.full_name,
       CASE c.type
           WHEN 'poultry_farm' THEN 'poultry'
           WHEN 'cattle_farm'  THEN 'cattle'
           ELSE 'other'
       END,
       c.address,
       NULL, NULL, NULL,
       c.environment_id,
       now(), now()
FROM customers c
WHERE c.type IN ('regular_farm', 'cattle_farm', 'poultry_farm')
  AND c.deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM farms f WHERE f.customer_id = c.id AND f.deleted_at IS NULL);

-- 2) Re-point pre-existing rows to the owning customer's (single) default farm. At migration time
--    each farm-owning customer has exactly one farm, so the customer_id join is unambiguous; the
--    'farm_id IS NULL' guard makes this idempotent on re-run.
UPDATE batches b
SET farm_id = f.id
FROM farms f
WHERE f.customer_id = b.customer_id
  AND b.farm_id IS NULL
  AND b.deleted_at IS NULL;

UPDATE visits v
SET farm_id = f.id
FROM farms f
WHERE f.customer_id = v.customer_id
  AND v.farm_id IS NULL
  AND v.deleted_at IS NULL;

UPDATE invoices i
SET farm_id = f.id
FROM farms f
WHERE f.customer_id = i.customer_id   -- walk-in invoices (customer_id NULL) never match -> stay null
  AND i.farm_id IS NULL
  AND i.deleted_at IS NULL;

-- 3) Attach every existing contract to its owning customer's default farm (same customer, by design).
INSERT INTO contract_farms (id, contract_id, farm_id, environment_id, created_at, updated_at)
SELECT gen_random_uuid(), ct.id, f.id, ct.environment_id, now(), now()
FROM contracts ct
JOIN farms f ON f.customer_id = ct.customer_id AND f.deleted_at IS NULL
WHERE ct.deleted_at IS NULL
  AND NOT EXISTS (SELECT 1 FROM contract_farms cf
                  WHERE cf.contract_id = ct.id AND cf.farm_id = f.id AND cf.deleted_at IS NULL);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_batches_farms_farm_id",
                table: "batches");

            migrationBuilder.DropForeignKey(
                name: "fk_invoices_farms_farm_id",
                table: "invoices");

            migrationBuilder.DropForeignKey(
                name: "fk_pets_farms_farm_id",
                table: "pets");

            migrationBuilder.DropForeignKey(
                name: "fk_visits_farms_farm_id",
                table: "visits");

            migrationBuilder.DropTable(
                name: "contract_farms");

            migrationBuilder.DropTable(
                name: "farms");

            migrationBuilder.DropIndex(
                name: "ix_visits_farm",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ix_visits_farm_id",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "ix_pets_farm_id",
                table: "pets");

            migrationBuilder.DropIndex(
                name: "ix_invoices_farm",
                table: "invoices");

            migrationBuilder.DropIndex(
                name: "ix_invoices_farm_id",
                table: "invoices");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_type",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_batches_farm",
                table: "batches");

            migrationBuilder.DropIndex(
                name: "ix_batches_farm_id",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "farm_id",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "farm_id",
                table: "pets");

            migrationBuilder.DropColumn(
                name: "farm_id",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "farm_id",
                table: "batches");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_type",
                table: "customers",
                sql: "type IN ('regular_farm','home','cattle_farm','poultry_farm')");
        }
    }
}
