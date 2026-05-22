using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VetSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M3_CustomersPetsLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    full_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    phone_primary = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    phone_secondary = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    id_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    assigned_doctor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.CheckConstraint("ck_customers_type", "type IN ('regular_farm','home','cattle_farm','poultry_farm')");
                    table.ForeignKey(
                        name: "fk_customers_users_assigned_doctor_id",
                        column: x => x.assigned_doctor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledgers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledgers", x => x.id);
                    table.CheckConstraint("ck_ledgers_status", "status IN ('open','has_debt','closed')");
                    table.ForeignKey(
                        name: "fk_ledgers_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    species = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    breed = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sex = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    color_marks = table.Column<string>(type: "text", nullable: true),
                    weight_latest = table.Column<decimal>(type: "numeric(8,3)", nullable: true),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    microchip_no = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    health_notes = table.Column<string>(type: "text", nullable: true),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pets", x => x.id);
                    table.CheckConstraint("ck_pets_sex", "sex IS NULL OR sex IN ('male','female','unknown')");
                    table.ForeignKey(
                        name: "fk_pets_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ledger_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    receipt_voucher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    environment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_type", "entry_type IN ('invoice','service_fee','exam_fee','receipt_voucher','adjustment')");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledgers_ledger_id",
                        column: x => x.ledger_id,
                        principalTable: "ledgers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_customers_assigned_doctor",
                table: "customers",
                columns: new[] { "environment_id", "assigned_doctor_id" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_assigned_doctor_id",
                table: "customers",
                column: "assigned_doctor_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_environment_id_deleted_at",
                table: "customers",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_environment_id_updated_at",
                table: "customers",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_customers_env_phone",
                table: "customers",
                columns: new[] { "environment_id", "phone_primary" },
                unique: true,
                filter: "phone_primary IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_environment_id_deleted_at",
                table: "ledger_entries",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_environment_id_updated_at",
                table: "ledger_entries",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ledger",
                table: "ledger_entries",
                columns: new[] { "ledger_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_ledger_entries_env_idempotency",
                table: "ledger_entries",
                columns: new[] { "environment_id", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_environment_id_deleted_at",
                table: "ledgers",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ledgers_environment_id_updated_at",
                table: "ledgers",
                columns: new[] { "environment_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_ledgers_customer",
                table: "ledgers",
                column: "customer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_pets_customer",
                table: "pets",
                columns: new[] { "environment_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pets_customer_id",
                table: "pets",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_pets_environment_id_deleted_at",
                table: "pets",
                columns: new[] { "environment_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pets_environment_id_updated_at",
                table: "pets",
                columns: new[] { "environment_id", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "pets");

            migrationBuilder.DropTable(
                name: "ledgers");

            migrationBuilder.DropTable(
                name: "customers");
        }
    }
}
